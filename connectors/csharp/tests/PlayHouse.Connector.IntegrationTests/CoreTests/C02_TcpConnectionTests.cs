using FluentAssertions;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.CoreTests;

/// <summary>
/// C-02: TCP 연결 테스트
/// </summary>
/// <remarks>
/// Connector가 테스트 서버의 TCP 포트(34001)에 성공적으로 연결할 수 있는지 검증합니다.
/// </remarks>
public class C02_TcpConnectionTests : BaseIntegrationTest
{
    public C02_TcpConnectionTests(TestServerFixture testServer) : base(testServer)
    {
    }

    [Fact(DisplayName = "C-02-01: Stage 생성 후 TCP 연결이 성공한다")]
    public async Task Connect_AfterStageCreation_Succeeds()
    {
        // Given: Stage가 생성되어 있음
        StageInfo = await TestServer.CreateTestStageAsync();

        // When: TCP 연결 시도
        var connected = await Connector!.ConnectAsync(
            TestServer.Host,
            TestServer.TcpPort
        );

        // Then: 연결이 성공해야 함
        connected.Should().BeTrue("TCP 연결이 성공해야 함");
        Connector.IsConnected().Should().BeTrue("연결 상태가 true여야 함");
        Connector.StageId.Should().Be(0, "인증 전에는 Stage ID가 설정되지 않아야 함");
        Connector.StageType.Should().BeEmpty("인증 전에는 Stage 타입이 설정되지 않아야 함");
    }

    [Fact(DisplayName = "C-02-02: 연결 후 IsConnected는 true를 반환한다")]
    public async Task IsConnected_AfterConnection_ReturnsTrue()
    {
        // Given: 연결되지 않은 상태
        Connector!.IsConnected().Should().BeFalse("초기 상태는 연결되지 않음");

        // When: 연결 성공
        await CreateStageAndConnectAsync();

        // Then: IsConnected가 true를 반환해야 함
        Connector.IsConnected().Should().BeTrue("연결 후 IsConnected는 true여야 함");
    }

    [Fact(DisplayName = "C-02-03: 연결 전 IsAuthenticated는 false를 반환한다")]
    public async Task IsAuthenticated_BeforeAuthentication_ReturnsFalse()
    {
        // Given & When: 연결만 성공한 상태 (인증 전)
        await CreateStageAndConnectAsync();

        // Then: 인증 전이므로 IsAuthenticated는 false여야 함
        Connector!.IsAuthenticated().Should().BeFalse("인증 전에는 IsAuthenticated가 false여야 함");
    }

    [Fact(DisplayName = "C-02-04: OnConnect 이벤트가 성공 결과로 발생한다")]
    public async Task OnConnect_Event_TriggersWithSuccess()
    {
        // Given: Stage 생성
        StageInfo = await TestServer.CreateTestStageAsync();

        var connectResult = false;
        var eventTriggered = false;
        var tcs = new TaskCompletionSource<bool>();

        Connector!.OnConnect += result =>
        {
            connectResult = result;
            eventTriggered = true;
            tcs.TrySetResult(result);
        };

        // When: 연결 시도
        Connector.Connect(
            TestServer.Host,
            TestServer.TcpPort
        );

        // OnConnect 이벤트 대기 (MainThreadAction 호출하면서 최대 5초)
        var completed = await WaitForConditionWithMainThreadActionAsync(() => tcs.Task.IsCompleted, 5000);

        // Then: 이벤트가 발생하고 성공 결과를 전달해야 함
        completed.Should().BeTrue("OnConnect 이벤트가 5초 이내에 발생해야 함");
        eventTriggered.Should().BeTrue("OnConnect 이벤트가 발생해야 함");
        connectResult.Should().BeTrue("연결 결과가 true여야 함");
    }

    [Fact(DisplayName = "C-02-05: Stage 정보 없이도 TCP 연결은 성공한다")]
    public async Task Connect_WithoutStageInfo_TcpConnectionSucceeds()
    {
        // When: Stage 정보 없이 연결 시도
        var connected = await Connector!.ConnectAsync(
            TestServer.Host,
            TestServer.TcpPort
        );

        // Then: TCP 연결 자체는 성공함 (Stage ID 검증은 서버 레벨에서 나중에 이루어짐)
        // PlayHouse 서버는 TCP 연결을 먼저 수락하고, 인증/Stage 참여 시 검증함
        connected.Should().BeTrue("TCP 연결 자체는 성공해야 함");
        Connector.IsConnected().Should().BeTrue("TCP 연결 상태여야 함");
        Connector.IsAuthenticated().Should().BeFalse("인증은 아직 안 됨");
    }

    [Fact(DisplayName = "C-02-06: 동일한 Connector로 재연결할 수 있다")]
    public async Task Connect_MultipleTimes_Succeeds()
    {
        // Given: 첫 번째 연결
        await CreateStageAndConnectAsync();
        Connector!.IsConnected().Should().BeTrue();

        // When: 연결 해제 후 재연결
        Connector.Disconnect();
        await Task.Delay(500); // 연결 해제 대기

        await TestServer.CreateTestStageAsync();
        var reconnected = await Connector.ConnectAsync(
            TestServer.Host,
            TestServer.TcpPort
        );

        // Then: 재연결이 성공해야 함
        reconnected.Should().BeTrue("재연결이 성공해야 함");
        Connector.IsConnected().Should().BeTrue("재연결 후 연결 상태가 true여야 함");
        Connector.StageId.Should().Be(0, "재연결 직후 인증 전에는 Stage ID가 설정되지 않아야 함");
    }
}
