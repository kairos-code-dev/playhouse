using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

using Google.Protobuf;

/// <summary>
/// GameLoopTimer 기능 검증 (StartGameLoop, StopGameLoop)
///
/// E2E 검증:
/// - 응답 검증: StartGameLoopReply, StopGameLoopReply 수신
/// - Push 메시지 검증: OnReceive 콜백으로 GameLoopTickNotify 수신
/// </summary>
public class GameLoopVerifier : VerifierBase
{
    private readonly List<(int TickNumber, double DeltaTimeMs, double TotalElapsedMs)> _receivedTicks = new();
    private Action<long, string, IPacket>? _receiveHandler;

    public override string CategoryName => "GameLoop";

    public GameLoopVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 4;

    protected override async Task SetupAsync()
    {
        _receivedTicks.Clear();

        // OnReceive 핸들러 등록 (GameLoopTickNotify 수신)
        _receiveHandler = (stageId, stageType, packet) =>
        {
            if (packet.MsgId.EndsWith("GameLoopTickNotify"))
            {
                var notify = GameLoopTickNotify.Parser.ParseFrom(packet.Payload.DataSpan);
                _receivedTicks.Add((notify.TickNumber, notify.DeltaTimeMs, notify.TotalElapsedMs));
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
        await RunTest("GameLoop_ReceivesTicksAtConfiguredRate", Test_ReceivesTicksAtConfiguredRate);
        await RunTest("GameLoop_DeltaTimeMatchesFixedTimestep", Test_DeltaTimeMatchesFixedTimestep);
        await RunTest("GameLoop_StopsOnRequest", Test_StopsOnRequest);
        await RunTest("GameLoop_AutoStopsOnMaxTicks", Test_AutoStopsOnMaxTicks);
    }

    /// <summary>
    /// 50ms 타임스텝으로 1초간 실행 → 약 16~24 tick notify 수신 검증
    /// </summary>
    private async Task Test_ReceivesTicksAtConfiguredRate()
    {
        // Given
        _receivedTicks.Clear();
        var request = new StartGameLoopRequest
        {
            TimestepMs = 50,
            MaxTicks = 0 // 무제한
        };

        // When - 게임 루프 시작
        using var response = await Connector.RequestAsync(new Packet(request));

        Assert.StringContains(response.MsgId, "StartGameLoopReply", "Response should be StartGameLoopReply");
        var reply = StartGameLoopReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Game loop should start successfully");

        // 1초간 대기하면서 MainThreadAction 폴링
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50);
            Connector.MainThreadAction();
        }

        // 게임 루프 중지
        using var stopResponse = await Connector.RequestAsync(new Packet(new StopGameLoopRequest()));
        Connector.MainThreadAction();

        // Then - 약 16~24 tick 수신 (50ms × 20 = 1000ms → ~20 ticks)
        var tickCount = _receivedTicks.Count;
        Assert.GreaterThanOrEqual(tickCount, 12,
            $"Expected at least 12 ticks at 50ms/tick over ~1s, got {tickCount}");
        Assert.LessThanOrEqual(tickCount, 30,
            $"Expected at most 30 ticks at 50ms/tick over ~1s, got {tickCount}");
    }

    /// <summary>
    /// 각 notify의 delta_time_ms가 설정값(50ms)과 일치하는지 검증
    /// </summary>
    private async Task Test_DeltaTimeMatchesFixedTimestep()
    {
        // Given
        _receivedTicks.Clear();
        var request = new StartGameLoopRequest
        {
            TimestepMs = 50,
            MaxTicks = 10 // 10 ticks 후 자동 중단
        };

        // When
        using var response = await Connector.RequestAsync(new Packet(request));

        Assert.StringContains(response.MsgId, "StartGameLoopReply", "Response should be StartGameLoopReply");

        // tick 수신 대기
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50);
            Connector.MainThreadAction();
        }

        // Then - 모든 tick의 deltaTime이 50ms
        Assert.GreaterThan(_receivedTicks.Count, 0, "Should receive at least one tick");
        foreach (var (tickNumber, deltaTimeMs, totalElapsedMs) in _receivedTicks)
        {
            Assert.Equals(50.0, deltaTimeMs,
                $"Tick {tickNumber}: DeltaTime should be 50ms, got {deltaTimeMs}ms");
        }
    }

    /// <summary>
    /// StopGameLoop 호출 후 Push 중단 확인
    /// </summary>
    private async Task Test_StopsOnRequest()
    {
        // Given
        _receivedTicks.Clear();
        var request = new StartGameLoopRequest
        {
            TimestepMs = 50,
            MaxTicks = 0 // 무제한
        };

        // When - 시작
        using var response = await Connector.RequestAsync(new Packet(request));
        Assert.StringContains(response.MsgId, "StartGameLoopReply", "Response should be StartGameLoopReply");

        // 잠시 실행
        for (int i = 0; i < 6; i++)
        {
            await Task.Delay(50);
            Connector.MainThreadAction();
        }

        var ticksBeforeStop = _receivedTicks.Count;
        Assert.GreaterThan(ticksBeforeStop, 0, "Should have received some ticks before stop");

        // 중지 요청
        using var stopResponse = await Connector.RequestAsync(new Packet(new StopGameLoopRequest()));
        Connector.MainThreadAction();

        Assert.StringContains(stopResponse.MsgId, "StopGameLoopReply", "Response should be StopGameLoopReply");
        var stopReply = StopGameLoopReply.Parser.ParseFrom(stopResponse.Payload.DataSpan);
        Assert.IsTrue(stopReply.Success, "StopGameLoop should succeed");
        Assert.GreaterThan(stopReply.TotalTicks, 0, "TotalTicks should be > 0");

        // 중지 후 추가 대기 - 새로운 tick이 오지 않아야 함
        var ticksAtStop = _receivedTicks.Count;
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(50);
            Connector.MainThreadAction();
        }

        // 중지 후 메일박스에 남아있던 소수의 tick은 허용하되, 새로운 tick이 계속 오면 안 됨
        var ticksAfterStop = _receivedTicks.Count;
        var newTicksAfterStop = ticksAfterStop - ticksAtStop;
        Assert.LessThanOrEqual(newTicksAfterStop, 3,
            $"After stop, should receive at most 3 residual ticks, got {newTicksAfterStop}");
    }

    /// <summary>
    /// max_ticks=5 설정 시 정확히 5개 tick 후 자동 중단 검증
    /// </summary>
    private async Task Test_AutoStopsOnMaxTicks()
    {
        // Given
        _receivedTicks.Clear();
        var request = new StartGameLoopRequest
        {
            TimestepMs = 50,
            MaxTicks = 5 // 5 ticks 후 자동 중단
        };

        // When
        using var response = await Connector.RequestAsync(new Packet(request));

        Assert.StringContains(response.MsgId, "StartGameLoopReply", "Response should be StartGameLoopReply");
        var reply = StartGameLoopReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Game loop should start successfully");

        // 충분한 시간 대기 (5 × 50ms = 250ms + 여유)
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50);
            Connector.MainThreadAction();
        }

        // Then - 정확히 5개 tick 수신
        var tickCount = _receivedTicks.Count;
        Assert.Equals(5, tickCount,
            $"Expected exactly 5 ticks with max_ticks=5, got {tickCount}");

        // Tick 번호 순서 확인 (1, 2, 3, 4, 5)
        for (int i = 0; i < 5; i++)
        {
            Assert.Equals(i + 1, _receivedTicks[i].TickNumber,
                $"Tick {i} should have number {i + 1}");
        }
    }
}
