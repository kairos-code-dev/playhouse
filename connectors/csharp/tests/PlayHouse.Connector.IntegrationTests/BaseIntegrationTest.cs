using Google.Protobuf;
using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests;

/// <summary>
/// 통합 테스트 베이스 클래스
/// </summary>
/// <remarks>
/// xUnit의 IClassFixture를 사용하여 테스트 서버와의 연결을 관리합니다.
/// 테스트 클래스는 이 클래스를 상속받아 TestServerFixture를 사용할 수 있습니다.
/// </remarks>
public abstract class BaseIntegrationTest : IClassFixture<TestServerFixture>, IAsyncLifetime
{
    protected readonly TestServerFixture TestServer;
    protected PlayHouse.Connector.Connector? Connector;
    protected CreateStageResponse? StageInfo;

    protected BaseIntegrationTest(TestServerFixture testServer)
    {
        TestServer = testServer;
    }

    /// <summary>
    /// 각 테스트 실행 전 초기화
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        // 기본 Connector 설정
        Connector = new PlayHouse.Connector.Connector();
        Connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        // 하위 클래스에서 Stage 생성이 필요한 경우 override
        await Task.CompletedTask;
    }

    /// <summary>
    /// 각 테스트 실행 후 정리
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        if (Connector != null)
        {
            if (Connector.IsConnected())
            {
                Connector.Disconnect();
                // 연결 해제가 완료될 때까지 대기
                await Task.Delay(100);
            }

            await Connector.DisposeAsync();
            Connector = null;
        }

        StageInfo = null;
    }

    /// <summary>
    /// 테스트용 Stage 생성 및 연결 헬퍼
    /// </summary>
    /// <param name="stageType">Stage 타입</param>
    /// <returns>연결 성공 여부</returns>
    protected async Task<bool> CreateStageAndConnectAsync(string stageType = "TestStage")
    {
        StageInfo = await TestServer.CreateStageAsync(stageType);

        return await Connector!.ConnectAsync(
            TestServer.Host,
            TestServer.TcpPort,
            StageInfo.StageId,
            StageInfo.StageType
        );
    }

    /// <summary>
    /// 인증 헬퍼 메서드
    /// </summary>
    /// <param name="userId">사용자 ID</param>
    /// <param name="token">인증 토큰</param>
    /// <returns>인증 응답</returns>
    protected async Task<AuthenticateReply> AuthenticateAsync(string userId, string token = "valid_token")
    {
        if (StageInfo != null)
        {
            await TestServer.AssignStageAsync(userId, StageInfo.StageId);
        }

        var authRequest = new AuthenticateRequest
        {
            UserId = userId,
            Token = token
        };

        using var requestPacket = new Packet(authRequest);
        var responsePacket = await Connector!.AuthenticateAsync(requestPacket);

        return AuthenticateReply.Parser.ParseFrom(responsePacket.Payload.DataSpan.ToArray());
    }

    /// <summary>
    /// Echo 요청 헬퍼 메서드
    /// </summary>
    /// <param name="content">에코할 내용</param>
    /// <param name="sequence">시퀀스 번호</param>
    /// <returns>에코 응답</returns>
    protected async Task<EchoReply> EchoAsync(string content, int sequence = 1)
    {
        var echoRequest = new EchoRequest
        {
            Content = content,
            Sequence = sequence
        };

        using var requestPacket = new Packet(echoRequest);
        var responsePacket = await Connector!.RequestAsync(requestPacket);

        return EchoReply.Parser.ParseFrom(responsePacket.Payload.DataSpan.ToArray());
    }

    /// <summary>
    /// Payload를 Protobuf 메시지로 변환하는 헬퍼 메서드
    /// </summary>
    protected static T ParsePayload<T>(IPayload payload) where T : IMessage<T>, new()
    {
        var parser = new MessageParser<T>(() => new T());
        return parser.ParseFrom(payload.DataSpan.ToArray());
    }

    /// <summary>
    /// MainThreadAction을 호출하면서 TaskCompletionSource 대기
    /// 콜백 기반 API 테스트에 사용
    /// </summary>
    protected async Task<T> WaitWithMainThreadActionAsync<T>(TaskCompletionSource<T> tcs, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!tcs.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Connector?.MainThreadAction();
            await Task.Delay(10);
        }

        if (tcs.Task.IsCompleted)
        {
            return await tcs.Task;
        }

        throw new TimeoutException($"Operation timed out after {timeoutMs}ms");
    }

    /// <summary>
    /// MainThreadAction을 호출하면서 특정 조건이 충족될 때까지 대기
    /// </summary>
    protected async Task<bool> WaitForConditionWithMainThreadActionAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Connector?.MainThreadAction();
            await Task.Delay(10);
        }

        return condition();
    }
}
