using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

using Google.Protobuf;

/// <summary>
/// Send, Request, RequestAsync, Push 메시지 검증
/// </summary>
public class MessagingVerifier : VerifierBase
{
    private readonly List<(long stageId, string stageType, string msgId, byte[] payloadData)> _receivedPushes = new();
    private readonly List<(long stageId, string stageType, ushort errorCode)> _receivedErrors = new();
    private Action<long, string, IPacket>? _receiveHandler;
    private Action<long, string, ushort, IPacket>? _errorHandler;

    public override string CategoryName => "Messaging";

    public MessagingVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 10;

    protected override async Task SetupAsync()
    {
        _receivedPushes.Clear();
        _receivedErrors.Clear();

        // OnReceive 핸들러 등록
        _receiveHandler = (stageId, stageType, packet) =>
        {
            var msgId = packet.MsgId;
            var payloadData = packet.Payload.DataSpan.ToArray();
            _receivedPushes.Add((stageId, stageType, msgId, payloadData));
        };
        Connector.OnReceive += _receiveHandler;

        // OnError 핸들러 등록
        _errorHandler = (stageId, stageType, errorCode, request) =>
        {
            _receivedErrors.Add((stageId, stageType, errorCode));
        };
        Connector.OnError += _errorHandler;

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
        if (_errorHandler != null)
        {
            Connector.OnError -= _errorHandler;
            _errorHandler = null;
        }

        _receivedPushes.Clear();
        _receivedErrors.Clear();

        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("Send_ConnectionMaintained", Test_Send_ConnectionMaintained);
        await RunTest("Request_Success_CallbackInvoked", Test_Request_Success_CallbackInvoked);
        await RunTest("Request_ErrorResponse", Test_Request_ErrorResponse);
        await RunTest("RequestAsync_Success", Test_RequestAsync_Success);
        await RunTest("RequestAsync_Timeout", Test_RequestAsync_Timeout);
        await RunTest("RequestAsync_ErrorResponse", Test_RequestAsync_ErrorResponse);
        await RunTest("OnReceive_PushMessage", Test_OnReceive_PushMessage);
        await RunTest("OnReceive_MultiplePushes", Test_OnReceive_MultiplePushes);
        await RunTest("MultipleRequests_Sequential", Test_MultipleRequests_Sequential);
        await RunTest("MultipleRequests_Parallel", Test_MultipleRequests_Parallel);
    }

    private async Task Test_Send_ConnectionMaintained()
    {
        // Given
        using var packet = Packet.Empty("StatusRequest");

        // When - Send (Fire-and-Forget)
        Connector.Send(packet);
        await Task.Delay(100);

        // Then
        Assert.IsTrue(Connector.IsConnected(), "Connection should be maintained after Send");
    }

    private async Task Test_Request_Success_CallbackInvoked()
    {
        // Given
        var echoRequest = new EchoRequest { Content = "Callback Test", Sequence = 42 };
        using var packet = new Packet(echoRequest);

        string? receivedMsgId = null;
        byte[]? receivedPayloadData = null;
        var responseReceived = new ManualResetEventSlim(false);

        // When - Request with callback
        Connector.Request(packet, response =>
        {
            receivedMsgId = response.MsgId;
            receivedPayloadData = response.Payload.DataSpan.ToArray();
            responseReceived.Set();
        });

        // Wait for callback
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!responseReceived.IsSet && DateTime.UtcNow < timeout)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then
        Assert.IsTrue(responseReceived.IsSet, "Callback should be invoked");
        Assert.NotNull(receivedMsgId, "Should receive response packet");
        Assert.StringContains(receivedMsgId!, "EchoReply", "Response MsgId should contain 'EchoReply'");

        Assert.NotNull(receivedPayloadData, "Should receive payload data");
        var echoReply = EchoReply.Parser.ParseFrom(receivedPayloadData!);
        Assert.Equals(echoReply.Content, "Callback Test", "Echo content should match");
        Assert.Equals(42, echoReply.Sequence, "Sequence should match");
    }

    private async Task Test_Request_ErrorResponse()
    {
        // Given
        _receivedErrors.Clear();
        using var failRequest = Packet.Empty("FailRequest");

        // When - Request that fails
        Connector.Request(failRequest, _ => { });

        // Wait for error callback
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_receivedErrors.Count == 0 && DateTime.UtcNow < timeout)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then
        Assert.IsTrue(_receivedErrors.Count > 0, "OnError callback should be invoked");
        var (stageId, stageType, errorCode) = _receivedErrors.First();
        Assert.Equals((ushort)500, errorCode, "Error code should be 500");
    }

    private async Task Test_RequestAsync_Success()
    {
        // Given
        var echoRequest = new EchoRequest
        {
            Content = "Hello, Server!",
            Sequence = 99
        };
        using var packet = new Packet(echoRequest);

        // When
        using var response = await Connector.RequestAsync(packet);

        // Then
        Assert.NotNull(response, "Should receive response");
        Assert.StringContains(response.MsgId, "EchoReply", "Response MsgId should contain 'EchoReply'");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.Equals(echoReply.Content, "Hello, Server!", "Echo content should match");
        Assert.Equals(99, echoReply.Sequence, "Sequence should match");
    }

    private async Task Test_RequestAsync_Timeout()
    {
        // Given - 짧은 타임아웃으로 설정된 임시 Connector
        var tempConnector = new PlayHouse.Connector.Connector();
        tempConnector.Init(new ConnectorConfig { RequestTimeoutMs = 100 });

        try
        {
            var stageId = GenerateUniqueStageId();
            await tempConnector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
            await Task.Delay(100);

            // 인증
            using var authPacket = Packet.Empty("AuthenticateRequest");
            await tempConnector.AuthenticateAsync(authPacket);
            await Task.Delay(100);

            // When - 서버가 응답하지 않는 메시지
            using var noResponsePacket = Packet.Empty("NoResponseRequest");

            bool exceptionThrown = false;
            ushort? errorCode = null;

            try
            {
                await tempConnector.RequestAsync(noResponsePacket);
            }
            catch (ConnectorException ex)
            {
                exceptionThrown = true;
                errorCode = ex.ErrorCode;
            }

            // Then
            Assert.IsTrue(exceptionThrown, "Should throw ConnectorException on timeout");
            Assert.NotNull(errorCode, "ErrorCode should be set");
            Assert.Equals((ushort)ConnectorErrorCode.RequestTimeout, errorCode, "ErrorCode should be RequestTimeout");
        }
        finally
        {
            tempConnector.Disconnect();
            await tempConnector.DisposeAsync();
        }
    }

    private async Task Test_RequestAsync_ErrorResponse()
    {
        // Given
        using var failRequest = Packet.Empty("FailRequest");

        // When
        bool exceptionThrown = false;
        ushort? errorCode = null;

        try
        {
            await Connector.RequestAsync(failRequest);
        }
        catch (ConnectorException ex)
        {
            exceptionThrown = true;
            errorCode = ex.ErrorCode;
        }

        // Then
        Assert.IsTrue(exceptionThrown, "Should throw ConnectorException on error");
        Assert.NotNull(errorCode, "ErrorCode should be set");
        Assert.Equals((ushort)500, errorCode, "Error code should be 500");
    }

    private async Task Test_OnReceive_PushMessage()
    {
        // Given
        _receivedPushes.Clear();

        var trigger = new BroadcastNotify
        {
            EventType = "system",
            Data = "Welcome!",
            FromAccountId = 0
        };
        using var triggerPacket = new Packet("BroadcastTrigger", trigger.ToByteArray());

        // When - BroadcastTrigger request (서버가 Push 메시지를 보냄)
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
        Assert.Equals(parsed.EventType, "system", "Event type should match");
        Assert.Equals(parsed.Data, "Welcome!", "Data should match");
    }

    private async Task Test_OnReceive_MultiplePushes()
    {
        // Given
        _receivedPushes.Clear();

        // When - 여러 BroadcastTrigger 요청 전송
        for (int i = 0; i < 3; i++)
        {
            var trigger = new BroadcastNotify
            {
                EventType = $"event_{i}",
                Data = $"Data {i}",
                FromAccountId = 0
            };
            using var triggerPacket = new Packet("BroadcastTrigger", trigger.ToByteArray());

            await Connector.RequestAsync(triggerPacket);
            await Task.Delay(50);
        }

        await Task.Delay(200);

        // Consume pending callbacks
        for (int i = 0; i < 10; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then
        Assert.IsTrue(_receivedPushes.Count >= 3, "Should receive at least 3 push messages");

        var pushMessages = _receivedPushes
            .Where(m => m.msgId.Contains("BroadcastNotify"))
            .ToList();
        Assert.IsTrue(pushMessages.Count >= 3, "Should receive at least 3 BroadcastNotify messages");
    }

    private async Task Test_MultipleRequests_Sequential()
    {
        // Given & When - 여러 요청 순차 전송
        for (int i = 0; i < 5; i++)
        {
            var request = new EchoRequest { Content = $"Message {i}", Sequence = i };
            using var packet = new Packet(request);

            using var response = await Connector.RequestAsync(packet);
            var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);

            // Then - 각 응답 검증
            Assert.Equals($"Message {i}", reply.Content, $"Message {i} content should match");
            Assert.Equals(i, reply.Sequence, $"Sequence {i} should match");
        }
    }

    private async Task Test_MultipleRequests_Parallel()
    {
        // Given & When - 여러 요청 병렬 전송
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var request = new EchoRequest { Content = $"Parallel {i}", Sequence = i };
            using var packet = new Packet(request);
            return await Connector.RequestAsync(packet);
        }).ToList();

        var responses = await Task.WhenAll(tasks);

        // Then
        Assert.Equals(10, responses.Length, "Should receive 10 responses");
        foreach (var response in responses)
        {
            Assert.StringContains(response.MsgId, "EchoReply", "All responses should be EchoReply");
            response.Dispose();
        }
    }
}
