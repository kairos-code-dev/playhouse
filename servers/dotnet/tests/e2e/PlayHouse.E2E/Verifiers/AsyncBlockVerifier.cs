using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// AsyncBlock E2E 검증
///
/// AsyncBlock은 Stage에서 비동기 작업을 수행하고 완료 후 클라이언트에게 Push 메시지를 보내는 패턴입니다.
/// E2E 검증은 즉시 응답(AsyncBlockAccepted)과 Push 메시지(AsyncBlockReply) 수신으로 이루어집니다.
/// </summary>
public class AsyncBlockVerifier : VerifierBase
{
    private readonly List<(long stageId, string stageType, string msgId, byte[] payloadData)> _receivedPushMessages = new();
    private Action<long, string, IPacket>? _receiveHandler;

    public override string CategoryName => "AsyncBlock";

    public AsyncBlockVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 2;

    protected override async Task SetupAsync()
    {
        _receivedPushMessages.Clear();

        // OnReceive 핸들러 등록
        _receiveHandler = (stageId, stageType, packet) =>
        {
            var msgId = packet.MsgId;
            var payloadData = packet.Payload.DataSpan.ToArray();
            _receivedPushMessages.Add((stageId, stageType, msgId, payloadData));
        };
        Connector.OnReceive += _receiveHandler;

        // 연결 및 인증
        await ConnectAndAuthenticateAsync("asyncblock_");
    }

    protected override async Task TeardownAsync()
    {
        if (_receiveHandler != null)
        {
            Connector.OnReceive -= _receiveHandler;
            _receiveHandler = null;
        }

        _receivedPushMessages.Clear();

        await Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("AsyncBlock_ExecutesPreAndPost_ReceivesResultViaPush", Test_AsyncBlock_ExecutesPreAndPost);
        await RunTest("AsyncBlock_MultipleRequests_AllProcessedCorrectly", Test_AsyncBlock_MultipleRequests);
    }

    /// <summary>
    /// AsyncBlock Pre/Post 콜백 실행 및 Push 메시지 수신 검증
    ///
    /// Given: AsyncBlockRequest 전송 (200ms 지연)
    /// When: 서버가 AsyncBlock을 실행하고 완료 후 Push 메시지 전송
    /// Then:
    ///   - 즉시 AsyncBlockAccepted 응답 수신
    ///   - Push 메시지로 AsyncBlockReply 수신
    ///   - Sequence 일치 확인
    /// </summary>
    private async Task Test_AsyncBlock_ExecutesPreAndPost()
    {
        // Given
        _receivedPushMessages.Clear();
        var request = new AsyncBlockRequest
        {
            Operation = "test",
            DelayMs = 200,
            Sequence = 1
        };

        // When
        using var response = await Connector.RequestAsync(new Packet(request));

        // Then - 즉시 응답 확인
        Assert.StringContains(response.MsgId, "AsyncBlockAccepted", "Should receive AsyncBlockAccepted response");

        // Push 메시지 대기 (DelayMs + 버퍼)
        await Task.Delay(500);

        // Consume pending callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        var asyncReplies = _receivedPushMessages
            .Where(m => m.msgId.Contains("AsyncBlockReply"))
            .Select(m => AsyncBlockReply.Parser.ParseFrom(m.payloadData))
            .ToList();

        Assert.Equals(1, asyncReplies.Count, "Should receive 1 AsyncBlockReply");
        Assert.Equals(1, asyncReplies[0].Sequence, "Sequence should match");
        Assert.StringContains(asyncReplies[0].PreResult, "pre_completed_test", "Pre result should match");
        Assert.StringContains(asyncReplies[0].PostResult, "post_completed", "Post result should match");
    }

    /// <summary>
    /// 여러 AsyncBlock 요청 동시 처리 검증
    ///
    /// Given: 5개의 AsyncBlock 요청 동시 전송
    /// When: 서버가 모든 AsyncBlock을 처리하고 Push 메시지 전송
    /// Then:
    ///   - 모든 요청에 대해 AsyncBlockAccepted 응답
    ///   - 5개의 AsyncBlockReply Push 메시지 수신
    ///   - 모든 Sequence 확인 (1, 2, 3, 4, 5)
    /// </summary>
    private async Task Test_AsyncBlock_MultipleRequests()
    {
        // Given
        _receivedPushMessages.Clear();
        const int requestCount = 5;

        // When - 5개 요청 동시 전송
        for (int i = 0; i < requestCount; i++)
        {
            var request = new AsyncBlockRequest
            {
                Operation = "test",
                DelayMs = 100,
                Sequence = i + 1
            };
            _ = Connector.RequestAsync(new Packet(request)); // fire and forget
        }

        // Then - 모든 Push 메시지 수신 대기
        await Task.Delay(1000);

        // Consume pending callbacks
        for (int i = 0; i < 15; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        var asyncReplies = _receivedPushMessages
            .Where(m => m.msgId.Contains("AsyncBlockReply"))
            .Select(m => AsyncBlockReply.Parser.ParseFrom(m.payloadData))
            .ToList();

        Assert.Equals(requestCount, asyncReplies.Count,
            $"Should receive {requestCount} AsyncBlockReply messages");

        // 모든 Sequence 확인
        var sequences = asyncReplies.Select(r => r.Sequence).OrderBy(s => s).ToList();
        for (int i = 0; i < requestCount; i++)
        {
            Assert.Equals(i + 1, sequences[i], $"Sequence {i + 1} should exist");
        }
    }

    #region Helper Methods

    private async Task ConnectAndAuthenticateAsync(string userPrefix)
    {
        var stageId = GenerateUniqueStageId();
        Connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await Connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        Assert.IsTrue(connected, $"Should connect to server (stageId: {stageId})");
        await Task.Delay(100);

        using var authPacket = Packet.Empty("AuthenticateRequest");
        await Connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);
    }

    #endregion
}
