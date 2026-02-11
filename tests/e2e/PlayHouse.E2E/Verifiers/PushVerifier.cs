using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

using Google.Protobuf;

/// <summary>
/// Push 메시지 순서 보장 검증
/// </summary>
public class PushVerifier : VerifierBase
{
    private readonly List<(long stageId, string stageType, string msgId, byte[] payloadData)> _receivedPushes = new();
    private Action<long, string, IPacket>? _receiveHandler;

    public override string CategoryName => "Push";

    public PushVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 2;

    protected override async Task SetupAsync()
    {
        _receivedPushes.Clear();

        // OnReceive 핸들러 등록
        _receiveHandler = (stageId, stageType, packet) =>
        {
            var msgId = packet.MsgId;
            var payloadData = packet.Payload.DataSpan.ToArray();
            _receivedPushes.Add((stageId, stageType, msgId, payloadData));
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
        // 핸들러 해제
        if (_receiveHandler != null)
        {
            Connector.OnReceive -= _receiveHandler;
            _receiveHandler = null;
        }

        _receivedPushes.Clear();

        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("Push_SingleMessage", Test_Push_SingleMessage);
        await RunTest("Push_OrderPreserved", Test_Push_OrderPreserved);
    }

    private async Task Test_Push_SingleMessage()
    {
        // Given
        _receivedPushes.Clear();

        var trigger = new BroadcastNotify
        {
            EventType = "single_test",
            Data = "Single Push",
            FromAccountId = 0
        };
        using var triggerPacket = new Packet("BroadcastTrigger", trigger.ToByteArray());

        // When
        using var response = await Connector.RequestAsync(triggerPacket);
        await Task.Delay(200);

        // Consume pending callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then
        Assert.Equals(response.MsgId, "BroadcastTriggerReply", "Should receive trigger response");
        Assert.IsTrue(_receivedPushes.Count > 0, "Should receive push message");

        var (stageId, stageType, msgId, payloadData) = _receivedPushes.First();
        Assert.StringContains(msgId, "BroadcastNotify", "Message ID should contain 'BroadcastNotify'");

        var parsed = BroadcastNotify.Parser.ParseFrom(payloadData);
        Assert.Equals(parsed.EventType, "single_test", "Event type should match");
        Assert.Equals(parsed.Data, "Single Push", "Data should match");
    }

    private async Task Test_Push_OrderPreserved()
    {
        // Given
        _receivedPushes.Clear();
        const int messageCount = 5;

        // When - 여러 BroadcastTrigger 요청 전송
        for (int i = 0; i < messageCount; i++)
        {
            var trigger = new BroadcastNotify
            {
                EventType = $"order_test_{i}",
                Data = $"Order {i}",
                FromAccountId = i
            };
            using var triggerPacket = new Packet("BroadcastTrigger", trigger.ToByteArray());

            await Connector.RequestAsync(triggerPacket);
            await Task.Delay(50);
        }

        await Task.Delay(300);

        // Consume pending callbacks
        for (int i = 0; i < 15; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then - Push 메시지 순서 검증
        Assert.IsTrue(_receivedPushes.Count >= messageCount, $"Should receive at least {messageCount} push messages");

        var pushMessages = _receivedPushes
            .Where(m => m.msgId.Contains("BroadcastNotify"))
            .ToList();

        Assert.IsTrue(pushMessages.Count >= messageCount, $"Should receive at least {messageCount} BroadcastNotify messages");

        // 순서 검증 - FromAccountId로 확인
        var receivedOrder = new List<long>();
        foreach (var (stageId, stageType, msgId, payloadData) in pushMessages.Take(messageCount))
        {
            var parsed = BroadcastNotify.Parser.ParseFrom(payloadData);
            receivedOrder.Add(parsed.FromAccountId);
        }

        // 순서가 보장되는지 확인 (0, 1, 2, 3, 4)
        for (int i = 0; i < messageCount; i++)
        {
            Assert.Equals((long)i, receivedOrder[i], $"Message {i} should be in order (FromAccountId={i})");
        }
    }
}
