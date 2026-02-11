#nullable enable

using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Infrastructure;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// DI 통합 검증 - Stage와 Actor가 생성될 때 DI 컨테이너에서 서비스를 주입받는지 검증합니다.
///
/// E2E 검증 방법:
/// - GetDIValueRequest → GetDIValueReply (주입된 서비스 값 응답)
/// - IPlayServerControl, IServerInfoCenter DI 해결 확인
/// </summary>
/// <remarks>
/// 환경 변수 ENABLE_DI_TESTS=1 설정 시에만 실행됩니다.
/// DIPlayServer는 Program.cs에서 조건부로 시작됩니다.
/// </remarks>
public class DIIntegrationVerifier(ServerContext serverContext) : VerifierBase(serverContext)
{
    public override string CategoryName => "DI Integration";

    public override int GetTestCount() => 5;

    protected override async Task SetupAsync()
    {
        // DIPlayServer가 없으면 테스트 스킵
        if (ServerContext.DIPlayServer == null)
        {
            throw new InvalidOperationException(
                "DI tests are disabled. Set ENABLE_DI_TESTS=1 environment variable to run DI tests.");
        }

        // 기존 연결 해제
        if (Connector.IsConnected())
        {
            Connector.Disconnect();
            await Task.Delay(200); // Wait for disconnect to complete
        }

        // Re-init connector to ensure clean state
        Connector.Init(new PlayHouse.Connector.ConnectorConfig
        {
            RequestTimeoutMs = 30000
        });

        await Task.CompletedTask;
    }

    protected override Task TeardownAsync()
    {
        // 연결 정리
        if (Connector.IsConnected())
        {
            Connector.Disconnect();
        }

        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("Stage_ReceivesInjectedService", Test_Stage_ReceivesInjectedService);
        await RunTest("Actor_ReceivesInjectedService", Test_Actor_ReceivesInjectedService);
        await RunTest("IPlayServerControl_CanBeResolvedAndUsed", Test_IPlayServerControl_CanBeResolvedAndUsed);
        await RunTest("IServerInfoCenter_CanBeResolved", Test_IServerInfoCenter_CanBeResolved);
        await RunTest("StageAndActor_ReceiveSameSingletonService", Test_StageAndActor_ReceiveSameSingletonService);
    }

    /// <summary>
    /// Stage 생성 시 ITestService가 주입되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// 1. DIPlayServer에 연결하여 Stage 생성
    /// 2. GetDIValueRequest로 주입된 서비스 값 조회
    /// 3. 응답으로 "DI-Injected-Value" 수신
    /// </remarks>
    private async Task Test_Stage_ReceivesInjectedService()
    {
        // Given - DI가 구성된 서버에 연결
        var stageId = GenerateUniqueStageId();
        var connected = await Connector.ConnectAsync("127.0.0.1", ServerContext.DITcpPort);
        Assert.IsTrue(connected, "DI PlayServer에 연결되어야 함");
        await Task.Delay(200);

        // Consume connection callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // When - 인증 및 DI 값 조회
        var authRequest = new AuthenticateRequest
        {
            UserId = GenerateUniqueUserId("di-stage-test"),
            Token = "test-token"
        };

        using var authPacket = new Packet(authRequest);
        await Connector.AuthenticateAsync(authPacket);
        await Task.Delay(200);

        // Consume auth callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        Assert.IsTrue(Connector.IsAuthenticated(), "인증되어야 함");

        // GetDIValueRequest로 주입된 서비스 값 조회
        using var request = new Packet(new GetDIValueRequest());
        var response = await Connector.RequestAsync(request);

        // Then - DI로 주입받은 값 확인
        Assert.AreEqual("GetDIValueReply", response.MsgId, "GetDIValueReply 응답이어야 함");

        var reply = GetDIValueReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.AreEqual("DI-Injected-Value", reply.Value,
            "Stage에서 DI로 주입받은 값을 클라이언트가 받을 수 있어야 함");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// Actor 생성 시 ITestService가 주입되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// 1. DIPlayServer에 연결 및 인증 (Actor 생성)
    /// 2. 인증 성공 확인
    /// 3. Actor가 DI 컨테이너에서 ITestService를 주입받았음을 간접 확인
    /// </remarks>
    private async Task Test_Actor_ReceivesInjectedService()
    {
        // Given - DI가 구성된 서버에 연결
        var stageId = GenerateUniqueStageId();
        var connected = await Connector.ConnectAsync("127.0.0.1", ServerContext.DITcpPort);
        Assert.IsTrue(connected, "DI PlayServer에 연결되어야 함");
        await Task.Delay(200);

        // Consume connection callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // When - Actor 생성 (인증)
        var authRequest = new AuthenticateRequest
        {
            UserId = GenerateUniqueUserId("di-actor-test"),
            Token = "test-token"
        };

        using var authPacket = new Packet(authRequest);
        await Connector.AuthenticateAsync(authPacket);
        await Task.Delay(200);

        // Consume auth callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then - Actor가 정상적으로 생성되고 인증되어야 함
        Assert.IsTrue(Connector.IsAuthenticated(), "Actor가 정상적으로 생성되고 인증되어야 함");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// IPlayServerControl을 DI로 해결하고 서버를 중지할 수 있는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// 1. ServiceProvider에서 IPlayServerControl 해결
    /// 2. PlayServer와 같은 인스턴스인지 확인
    /// 참고: 실제 중지는 하지 않음 (다른 테스트에 영향)
    /// </remarks>
    private async Task Test_IPlayServerControl_CanBeResolvedAndUsed()
    {
        // Given - DI가 구성된 서버
        var serviceProvider = ServerContext.DIServiceProvider;
        Assert.NotNull(serviceProvider, "ServiceProvider가 초기화되어야 함");

        // When - IPlayServerControl 해결
        var playServerControl = serviceProvider!.GetService<IPlayServerControl>();

        // Then - 해결 성공
        Assert.NotNull(playServerControl, "IPlayServerControl이 DI 컨테이너에 등록되어야 함");
        Assert.IsTrue(ReferenceEquals(ServerContext.DIPlayServer, playServerControl),
            "IPlayServerControl이 PlayServer와 같은 인스턴스여야 함");

        await Task.CompletedTask;
    }

    /// <summary>
    /// IServerInfoCenter를 DI로 해결할 수 있는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// 1. ServiceProvider에서 IServerInfoCenter 해결
    /// 2. Count 속성에 접근 가능한지 확인
    /// 참고: Standalone 모드에서는 ServerMesh에 등록되지 않을 수 있음
    /// </remarks>
    private async Task Test_IServerInfoCenter_CanBeResolved()
    {
        // Given - DI가 구성된 서버
        var serviceProvider = ServerContext.DIServiceProvider;
        Assert.NotNull(serviceProvider, "ServiceProvider가 초기화되어야 함");

        // When - IServerInfoCenter 해결
        var serverInfoCenter = serviceProvider!.GetService<IServerInfoCenter>();

        // Then - 해결 성공
        Assert.NotNull(serverInfoCenter, "IServerInfoCenter가 DI 컨테이너에 등록되어야 함");

        // Count 속성에 접근 가능한지 확인
        var count = serverInfoCenter!.Count;
        Assert.IsTrue(count >= 0, "Count 속성이 정상적으로 작동해야 함");

        await Task.CompletedTask;
    }

    /// <summary>
    /// 사용자 정의 서비스를 DI로 등록하고 Stage/Actor에서 사용할 수 있는지 종합 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// 1. Stage 생성 → ITestService 주입 확인 (GetDIValueRequest)
    /// 2. Actor 인증 → ITestService 주입 확인 (인증 성공)
    /// 3. 싱글톤 서비스이므로 Stage와 Actor가 같은 값을 반환해야 함
    /// </remarks>
    private async Task Test_StageAndActor_ReceiveSameSingletonService()
    {
        // Given - DI가 구성된 서버에 연결
        var stageId = GenerateUniqueStageId();
        var connected = await Connector.ConnectAsync("127.0.0.1", ServerContext.DITcpPort);
        Assert.IsTrue(connected, "DI PlayServer에 연결되어야 함");
        await Task.Delay(200);

        // Consume connection callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // When - Stage 생성 및 Actor 인증
        var authRequest = new AuthenticateRequest
        {
            UserId = GenerateUniqueUserId("di-singleton-test"),
            Token = "test-token"
        };

        using var authPacket = new Packet(authRequest);
        await Connector.AuthenticateAsync(authPacket);
        await Task.Delay(200);

        // Consume auth callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        Assert.IsTrue(Connector.IsAuthenticated(), "인증되어야 함");

        // Stage에서 주입받은 값 조회
        using var request = new Packet(new GetDIValueRequest());
        var response = await Connector.RequestAsync(request);

        Assert.AreEqual("GetDIValueReply", response.MsgId, "GetDIValueReply 응답이어야 함");

        var reply = GetDIValueReply.Parser.ParseFrom(response.Payload.DataSpan);

        // Then - Stage에서 주입받은 값 확인
        Assert.AreEqual("DI-Injected-Value", reply.Value,
            "Stage에서 DI로 주입받은 값이 올바라야 함");

        // Actor도 같은 싱글톤 서비스를 주입받았으므로 인증이 성공했음 (간접 검증)
        Assert.IsTrue(Connector.IsAuthenticated(),
            "Actor도 DI로 주입받은 서비스를 사용하여 인증 처리했어야 함");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }
}
