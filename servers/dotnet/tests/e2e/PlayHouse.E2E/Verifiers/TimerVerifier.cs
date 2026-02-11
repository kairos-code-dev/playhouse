using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

using Google.Protobuf;

/// <summary>
/// Timer 기능 검증 (AddRepeatTimer, AddCountTimer)
///
/// E2E 검증:
/// - 응답 검증: StartTimerReply 수신, TimerId 확인
/// - Push 메시지 검증: OnReceive 콜백으로 TimerTickNotify 수신
/// </summary>
public class TimerVerifier : VerifierBase
{
    private readonly List<(int TickNumber, long Timestamp, string TimerType)> _receivedTicks = new();
    private Action<long, string, IPacket>? _receiveHandler;

    public override string CategoryName => "Timer";

    public TimerVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 2;

    protected override async Task SetupAsync()
    {
        _receivedTicks.Clear();

        // OnReceive 핸들러 등록 (TimerTickNotify 수신)
        _receiveHandler = (stageId, stageType, packet) =>
        {
            if (packet.MsgId.EndsWith("TimerTickNotify"))
            {
                var notify = TimerTickNotify.Parser.ParseFrom(packet.Payload.DataSpan);
                _receivedTicks.Add((notify.TickNumber, notify.Timestamp, notify.TimerType));
            }
        };
        Connector.OnReceive += _receiveHandler;

        // 연결 + 인증
        if (!Connector.IsConnected())
        {
            var stageId = GenerateUniqueStageId();
            var connected = await Connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
            Assert.IsTrue(connected, "Should connect to server");
            await Task.Delay(100);
        }

        if (!Connector.IsAuthenticated())
        {
            using var authPacket = Packet.Empty("AuthenticateRequest");
            await Connector.AuthenticateAsync(authPacket);
            Assert.IsTrue(Connector.IsAuthenticated(), "Authentication should succeed");
        }
    }

    protected override Task TeardownAsync()
    {
        if (_receiveHandler != null)
        {
            Connector.OnReceive -= _receiveHandler;
            _receiveHandler = null;
        }

        _receivedTicks.Clear();

        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("AddRepeatTimer_ExecutesRepeatedly_ReceivesMultipleTicks", Test_AddRepeatTimer_ExecutesRepeatedly);
        await RunTest("AddCountTimer_ExecutesExactCount_ReceivesExactTicks", Test_AddCountTimer_ExecutesExactCount);
    }

    /// <summary>
    /// IStageSender.AddRepeatTimer E2E 테스트
    ///
    /// 반복 타이머가 설정한 주기로 실행되는지 검증합니다.
    /// - 응답 검증: StartTimerReply 수신, TimerId 확인
    /// - Push 메시지 검증: 최소 3회 이상 TimerTickNotify 수신
    /// </summary>
    private async Task Test_AddRepeatTimer_ExecutesRepeatedly()
    {
        // Given
        _receivedTicks.Clear();
        var request = new StartRepeatTimerRequest
        {
            IntervalMs = 100,
            InitialDelayMs = 100
        };

        // When
        using var response = await Connector.RequestAsync(new Packet(request));

        // Then - 타이머 시작 응답 확인
        Assert.StringContains(response.MsgId, "StartTimerReply", "Response MsgId should contain 'StartTimerReply'");
        var reply = StartTimerReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Timer should start successfully");
        Assert.GreaterThan(reply.TimerId, 0L, "TimerId should be assigned");

        // MainThreadAction 호출하여 Push 메시지 처리
        // 100ms 초기 대기 + 100ms * 4 = 500ms 대기하여 최소 4회 Tick 예상
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(100);
            Connector.MainThreadAction();
        }

        // 최소 3회 이상 Tick 수신 확인
        var tickCount = _receivedTicks.Count;
        Assert.GreaterThanOrEqual(tickCount, 3,
            $"Expected at least 3 ticks, got {tickCount}");

        // TimerType 확인
        foreach (var (tickNumber, timestamp, timerType) in _receivedTicks)
        {
            Assert.Equals("repeat", timerType, "TimerType should be 'repeat'");
        }

        // TickNumber가 순차적으로 증가하는지 확인
        for (int i = 0; i < _receivedTicks.Count - 1; i++)
        {
            Assert.Equals(_receivedTicks[i].TickNumber + 1, _receivedTicks[i + 1].TickNumber,
                $"TickNumber should increase sequentially");
        }
    }

    /// <summary>
    /// IStageSender.AddCountTimer E2E 테스트
    ///
    /// 카운트 타이머가 지정한 횟수만큼만 실행되는지 검증합니다.
    /// - 응답 검증: StartTimerReply 수신, TimerId 확인
    /// - Push 메시지 검증: 정확히 Count개의 TimerTickNotify 수신
    /// </summary>
    private async Task Test_AddCountTimer_ExecutesExactCount()
    {
        // Given
        _receivedTicks.Clear();
        var request = new StartCountTimerRequest
        {
            IntervalMs = 50,
            Count = 5,
            InitialDelayMs = 50
        };

        // When
        using var response = await Connector.RequestAsync(new Packet(request));

        // Then - 타이머 시작 응답 확인
        Assert.StringContains(response.MsgId, "StartTimerReply", "Response MsgId should contain 'StartTimerReply'");
        var reply = StartTimerReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Timer should start successfully");

        // MainThreadAction 호출하여 Push 메시지 처리
        // Count=5이므로 총 5회 Tick 예상 (50ms 간격 + 50ms 초기 지연 = 300ms)
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(50);
            Connector.MainThreadAction();
        }

        // 'count' 타입 Tick만 필터링 (repeat 타이머는 이전 테스트에서 계속 실행 중)
        var countTicks = _receivedTicks.Where(t => t.TimerType == "count").ToList();

        // 정확히 5회 Tick 수신 확인
        var tickCount = countTicks.Count;
        Assert.Equals(5, tickCount,
            $"Expected exactly 5 ticks, got {tickCount}");

        // Tick 번호 순서 확인 (1, 2, 3, 4, 5)
        for (int i = 0; i < 5; i++)
        {
            Assert.Equals(i + 1, countTicks[i].TickNumber,
                $"Tick {i} should have number {i + 1}");
        }
    }
}
