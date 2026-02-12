using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;


/// <summary>
/// TCP 연결, 인증, 연결 해제 검증
/// </summary>
public class ConnectionVerifier(ServerContext serverContext) : VerifierBase(serverContext)
{
    private readonly List<bool> _connectResults = new();
    private int _disconnectCount;
    private readonly ConnectorConfig _connectorConfig = new()
    {
        RequestTimeoutMs = 30000
    };

    public override string CategoryName => "Connection";

    public override int GetTestCount() => 12;

    protected override Task SetupAsync()
    {
        // 연결 상태 초기화
        _connectResults.Clear();
        _disconnectCount = 0;

        // 콜백 핸들러 등록
        Connector.OnConnect += OnConnect;
        Connector.OnDisconnect += OnDisconnect;

        // 기존 연결 해제
        if (Connector.IsConnected())
        {
            Connector.Disconnect();
        }

        return Task.CompletedTask;
    }

    protected override Task TeardownAsync()
    {
        // 핸들러 해제
        Connector.OnConnect -= OnConnect;
        Connector.OnDisconnect -= OnDisconnect;

        // 연결 정리
        if (Connector.IsConnected())
        {
            Connector.Disconnect();
        }

        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("Connect_Success", Test_Connect_Success);
        await RunTest("Connect_InvalidHost", Test_Connect_InvalidHost);
        await RunTest("ConnectAsync_Success", Test_ConnectAsync_Success);
        await RunTest("ConnectAsync_InvalidHost", Test_ConnectAsync_InvalidHost);
        await RunTest("Disconnect_ClientInitiated", Test_Disconnect_ClientInitiated);
        await RunTest("OnDisconnect_CallbackInvoked", Test_OnDisconnect_CallbackInvoked);
        await RunTest("Authenticate_Success_Async", Test_Authenticate_Success_Async);
        await RunTest("Authenticate_Success_Callback", Test_Authenticate_Success_Callback);
        await RunTest("Authenticate_SingleStage_AutoCreate", Test_Authenticate_SingleStage_AutoCreate);
        await RunTest("Authenticate_SingleStage_SameUserSameStage", Test_Authenticate_SingleStage_SameUserSameStage);
        await RunTest("Authenticate_MultiStage_WithoutPreCreatedStage_Fails", Test_Authenticate_MultiStage_WithoutPreCreatedStage_Fails);
        await RunTest("Authenticate_MultiStage_WithPreCreatedStage_Joins", Test_Authenticate_MultiStage_WithPreCreatedStage_Joins);
    }

    private async Task Test_Connect_Success()
    {
        // Given
        _connectResults.Clear();
        // When
        var result = await Connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        await Task.Delay(200);

        // Consume callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then
        Assert.IsTrue(result, "ConnectAsync should return true");
        Assert.IsTrue(Connector.IsConnected(), "IsConnected() should be true");
        Assert.Contains(_connectResults, true, "OnConnect(true) callback should be invoked");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    private async Task Test_Connect_InvalidHost()
    {
        // Given
        _connectResults.Clear();
        // When - 존재하지 않는 포트로 연결 시도
        var result = await Connector.ConnectAsync("127.0.0.1", 59999);
        await Task.Delay(200);

        // Consume callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then
        Assert.IsFalse(result, "ConnectAsync should return false");
        Assert.IsFalse(Connector.IsConnected(), "IsConnected() should be false");
        Assert.Contains(_connectResults, false, "OnConnect(false) callback should be invoked");
    }

    private async Task Test_ConnectAsync_Success()
    {
        // Given
        // When
        var result = await Connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        await Task.Delay(100);

        // Then
        Assert.IsTrue(result, "await ConnectAsync() should return true");
        Assert.IsTrue(Connector.IsConnected(), "IsConnected() should be true");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    private async Task Test_ConnectAsync_InvalidHost()
    {
        // Given
        // When
        var result = await Connector.ConnectAsync("127.0.0.1", 59999);

        // Then
        Assert.IsFalse(result, "await ConnectAsync() should return false");
    }

    private async Task Test_Disconnect_ClientInitiated()
    {
        // Given - 서버에 연결된 상태
        await ConnectOnlyAsync();
        Assert.IsTrue(Connector.IsConnected(), "Should be connected");

        // When - 클라이언트가 연결 해제
        Connector.Disconnect();
        await Task.Delay(100);

        // Then
        Assert.IsFalse(Connector.IsConnected(), "IsConnected() should be false after Disconnect");
    }

    private async Task Test_OnDisconnect_CallbackInvoked()
    {
        // Given - 서버에 연결된 상태
        await ConnectOnlyAsync();
        _disconnectCount = 0;

        // When - 클라이언트가 연결 해제
        Connector.Disconnect();

        // Consume callbacks
        for (int i = 0; i < 10; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(100);
        }

        // Then - 클라이언트 주도 해제 시에는 OnDisconnect가 호출되지 않음
        // (Connector.cs의 NotifyDisconnect 메서드에서 _disconnectFromClient가 true이면 콜백 호출하지 않음)
        Assert.Equals(0, _disconnectCount, "OnDisconnect should NOT be invoked when client initiates disconnect");
    }

    private async Task Test_Authenticate_Success_Async()
    {
        // Given - 연결만 된 상태 (인증 전)
        await ConnectOnlyAsync();
        Assert.IsFalse(Connector.IsAuthenticated(), "Should not be authenticated yet");

        // When
        using var authPacket = Packet.Empty("AuthenticateRequest");
        var response = await Connector.AuthenticateAsync(authPacket);

        // Then
        Assert.NotNull(response, "Should receive authentication response");
        Assert.IsTrue(Connector.IsAuthenticated(), "IsAuthenticated() should be true after authentication");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    private async Task Test_Authenticate_Success_Callback()
    {
        // Given - 연결만 된 상태
        await ConnectOnlyAsync();

        // proto 메시지로 인증 요청
        var authRequest = new AuthenticateRequest
        {
            UserId = "callback-user-123",
            Token = "callback-token-abc"
        };
        using var authPacket = new Packet(authRequest);

        // callback 내에서 파싱한 결과 저장 (패킷은 callback 후 auto-dispose됨)
        AuthenticateReply? authReply = null;
        bool hasPayload = false;
        var authCompleted = new ManualResetEventSlim(false);

        // When - 콜백과 함께 인증
        Connector.Authenticate(authPacket, response =>
        {
            // callback 내에서 payload 파싱 (response는 callback 후 auto-dispose됨)
            hasPayload = response.Payload.Length > 0;
            if (hasPayload)
            {
                authReply = AuthenticateReply.Parser.ParseFrom(response.Payload.DataSpan);
            }
            authCompleted.Set();
        });

        // 콜백 대기
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!authCompleted.IsSet && DateTime.UtcNow < timeout)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then
        Assert.IsTrue(authCompleted.IsSet, "Authenticate callback should be invoked");
        Assert.IsTrue(Connector.IsAuthenticated(), "IsAuthenticated() should be true after authentication");

        // payload 검증 - callback 내에서 파싱된 결과 사용
        Assert.IsTrue(hasPayload, "Should have reply payload");
        Assert.NotNull(authReply, "AuthenticateReply should be parsed");
        Assert.IsTrue(authReply!.Success, "AuthenticateReply.Success should be true");
        Assert.Equals("callback-user-123", authReply.ReceivedUserId, "Should echo UserId");
        Assert.Equals("callback-token-abc", authReply.ReceivedToken, "Should echo Token");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    private async Task Test_Authenticate_SingleStage_AutoCreate()
    {
        await ConnectOnlyAsync();

        var userId = GenerateUniqueUserId("single_auto");
        var authRequest = new AuthenticateRequest
        {
            UserId = userId,
            Token = "single-stage"
        };

        using var authPacket = new Packet(authRequest);
        var response = await Connector.AuthenticateAsync(authPacket);
        var reply = AuthenticateReply.Parser.ParseFrom(response.Payload.DataSpan);

        Assert.IsTrue(Connector.IsAuthenticated(), "single-stage auth should authenticate connector");
        Assert.IsTrue(Connector.StageId > 0, "single-stage auth should assign a stage id");
        Assert.Equals(userId, reply.ReceivedUserId, "AuthenticateReply should echo user id");

        Connector.Disconnect();
        await Task.Delay(100);
    }

    private async Task Test_Authenticate_SingleStage_SameUserSameStage()
    {
        var userId = GenerateUniqueUserId("single_same_user");

        await ConnectOnlyAsync();
        var stageId1 = await AuthenticateSingleStageAsync(Connector, userId);
        Connector.Disconnect();
        await Task.Delay(100);

        var connector2 = new PlayHouse.Connector.Connector();
        connector2.Init(_connectorConfig);
        try
        {
            var connected = await connector2.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
            Assert.IsTrue(connected, "second connector should connect");

            var stageId2 = await AuthenticateSingleStageAsync(connector2, userId);
            Assert.Equals(stageId1, stageId2, "same account + stageType should resolve to same single stage");
        }
        finally
        {
            if (connector2.IsConnected())
            {
                connector2.Disconnect();
            }

            await connector2.DisposeAsync();
        }
    }

    private async Task Test_Authenticate_MultiStage_WithoutPreCreatedStage_Fails()
    {
        await ConnectOnlyAsync();

        var stageId = GenerateUniqueStageId(51000);
        var authRequest = new AuthenticateRequest
        {
            UserId = GenerateUniqueUserId("multi_missing"),
            Token = $"multi-stage:{stageId}"
        };

        using var authPacket = new Packet(authRequest);
        ConnectorException? caughtException = null;
        try
        {
            _ = await Connector.AuthenticateAsync(authPacket);
        }
        catch (ConnectorException ex)
        {
            caughtException = ex;
        }

        Assert.NotNull(caughtException, "auth should fail when target multi-stage does not exist");
        Assert.Equals(3, caughtException!.ErrorCode, "missing multi-stage should return StageNotFound");
        Assert.IsFalse(Connector.IsAuthenticated(), "connector should remain unauthenticated on auth failure");

        Connector.Disconnect();
        await Task.Delay(100);
    }

    private async Task Test_Authenticate_MultiStage_WithPreCreatedStage_Joins()
    {
        await ConnectOnlyAsync();

        var stageId = GenerateUniqueStageId(52000);
        var stageReady = ServerContext.PlayServer.CreateStageIfNotExists(stageId.ToString(), "TestStage");
        Assert.IsTrue(stageReady, "target multi-stage should be pre-created for join");

        var authRequest = new AuthenticateRequest
        {
            UserId = GenerateUniqueUserId("multi_existing"),
            Token = $"multi-stage:{stageId}"
        };

        using var authPacket = new Packet(authRequest);
        _ = await Connector.AuthenticateAsync(authPacket);

        Assert.IsTrue(Connector.IsAuthenticated(), "auth should succeed when target multi-stage exists");
        Assert.IsTrue(Connector.StageId > 0, "connector should hold a valid stage id after joining a pre-created multi-stage");

        Connector.Disconnect();
        await Task.Delay(100);
    }

    #region Helper Methods

    private async Task ConnectOnlyAsync()
    {
        var connected = await Connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        Assert.IsTrue(connected, "Should connect to server");
        await Task.Delay(100);
    }

    private static async Task<long> AuthenticateSingleStageAsync(PlayHouse.Connector.Connector connector, string userId)
    {
        var request = new AuthenticateRequest
        {
            UserId = userId,
            Token = "single-stage"
        };

        using var authPacket = new Packet(request);
        var response = await connector.AuthenticateAsync(authPacket);
        _ = AuthenticateReply.Parser.ParseFrom(response.Payload.DataSpan);
        return connector.StageId;
    }

    private void OnConnect(bool result)
    {
        _connectResults.Add(result);
    }

    private void OnDisconnect()
    {
        System.Threading.Interlocked.Increment(ref _disconnectCount);
    }

    #endregion
}
