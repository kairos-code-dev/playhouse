#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using Xunit;

namespace PlayHouse.Unit.Connector;

/// <summary>
/// 단위 테스트: ConnectorConfig의 설정 값 검증
/// Host/Port는 Connect() 호출 시 전달되므로 ConnectorConfig에서 제외됨
/// </summary>
public class ConnectorConfigTests
{
    [Fact(DisplayName = "기본 생성자 - 기본값이 설정된다")]
    public void DefaultConstructor_SetsDefaultValues()
    {
        // Given (전제조건)
        // When (행동)
        var config = new ConnectorConfig();

        // Then (결과)
        config.UseWebsocket.Should().BeFalse("기본은 TCP 연결");
        config.ConnectionIdleTimeoutMs.Should().Be(30000, "기본 연결 타임아웃은 30초");
        config.HeartBeatIntervalMs.Should().Be(10000, "기본 하트비트 간격은 10초");
        config.RequestTimeoutMs.Should().Be(30000, "기본 요청 타임아웃은 30초");
        config.EnableSegmentedSend.Should().BeTrue("기본값은 segmented send 활성화");
    }

    [Fact(DisplayName = "UseWebsocket - WebSocket 사용 설정")]
    public void UseWebsocket_SetAndGet()
    {
        // Given (전제조건)
        var config = new ConnectorConfig
        {
            // When (행동)
            UseWebsocket = true
        };

        // Then (결과)
        config.UseWebsocket.Should().BeTrue("WebSocket 사용 설정이 반영되어야 함");
    }

    [Fact(DisplayName = "ConnectionIdleTimeoutMs - 커스텀 타임아웃 설정")]
    public void ConnectionIdleTimeoutMs_CustomValue()
    {
        // Given (전제조건)
        var config = new ConnectorConfig
        {
            // When (행동)
            ConnectionIdleTimeoutMs = 60000
        };

        // Then (결과)
        config.ConnectionIdleTimeoutMs.Should().Be(60000, "커스텀 타임아웃이 설정되어야 함");
    }

    [Fact(DisplayName = "HeartBeatIntervalMs - 커스텀 하트비트 간격 설정")]
    public void HeartBeatIntervalMs_CustomValue()
    {
        // Given (전제조건)
        var config = new ConnectorConfig
        {
            // When (행동)
            HeartBeatIntervalMs = 5000
        };

        // Then (결과)
        config.HeartBeatIntervalMs.Should().Be(5000, "커스텀 하트비트 간격이 설정되어야 함");
    }

    [Fact(DisplayName = "RequestTimeoutMs - 커스텀 요청 타임아웃 설정")]
    public void RequestTimeoutMs_CustomValue()
    {
        // Given (전제조건)
        var config = new ConnectorConfig
        {
            // When (행동)
            RequestTimeoutMs = 15000
        };

        // Then (결과)
        config.RequestTimeoutMs.Should().Be(15000, "커스텀 요청 타임아웃이 설정되어야 함");
    }

    [Fact(DisplayName = "모든 속성을 한 번에 설정")]
    public void AllProperties_SetAtOnce()
    {
        // Given (전제조건)
        // When (행동)
        var config = new ConnectorConfig
        {
            UseWebsocket = true,
            ConnectionIdleTimeoutMs = 45000,
            HeartBeatIntervalMs = 15000,
            RequestTimeoutMs = 20000,
            EnableSegmentedSend = false
        };

        // Then (결과)
        config.UseWebsocket.Should().BeTrue();
        config.ConnectionIdleTimeoutMs.Should().Be(45000);
        config.HeartBeatIntervalMs.Should().Be(15000);
        config.RequestTimeoutMs.Should().Be(20000);
        config.EnableSegmentedSend.Should().BeFalse();
    }
}
