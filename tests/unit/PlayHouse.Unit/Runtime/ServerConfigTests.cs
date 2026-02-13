using FluentAssertions;
using PlayHouse.Abstractions;
using PlayHouse.Runtime.ServerMesh;
using Xunit;

namespace PlayHouse.Unit.Runtime;

/// <summary>
/// ServerConfig 단위 테스트
/// </summary>
public class ServerConfigTests
{
    #region NID 생성

    [Fact(DisplayName = "ServerId는 문자열로 저장된다")]
    public void ServerConfig_WhenCreated_StoresServerId()
    {
        // Given
        const ushort serviceId = 1;
        const string serverId = "play-server-2";

        // When
        var config = new ServerConfig(ServerType.Play, serviceId, serverId, "tcp://*:5555");

        // Then
        config.ServerId.Should().Be("play-server-2");
        config.ServiceId.Should().Be(1);
        config.ServerType.Should().Be(ServerType.Play);
    }

    [Fact(DisplayName = "Play 서버 설정을 생성할 수 있다")]
    public void ServerConfig_ForPlayServer_HasCorrectServiceId()
    {
        // Given & When
        var config = new ServerConfig(ServerType.Play, 1, "play-1", "tcp://*:5555");

        // Then
        config.ServerId.Should().Be("play-1");
        config.ServiceId.Should().Be(1);
        config.ServerType.Should().Be(ServerType.Play);
    }

    [Fact(DisplayName = "API 서버 설정을 생성할 수 있다")]
    public void ServerConfig_ForApiServer_HasCorrectServiceId()
    {
        // Given & When
        var config = new ServerConfig(ServerType.Api, 1, "api-3", "tcp://*:5556");

        // Then
        config.ServerId.Should().Be("api-3");
        config.ServiceId.Should().Be(1);
        config.ServerType.Should().Be(ServerType.Api);
    }

    #endregion

    #region 기본값 검증

    [Fact(DisplayName = "기본 타임아웃은 30초이다")]
    public void ServerConfig_WithDefaults_HasThirtySecondTimeout()
    {
        // Given & When
        var config = new ServerConfig(ServerType.Play, 1, "test-1", "tcp://*:5555");

        // Then
        config.RequestTimeoutMs.Should().Be(30000);
    }

    [Fact(DisplayName = "기본 High Water Mark는 300000이다")]
    public void ServerConfig_WithDefaults_HasDefaultHighWatermarks()
    {
        // Given & When
        var config = new ServerConfig(ServerType.Play, 1, "test-1", "tcp://*:5555");

        // Then
        config.SendHighWatermark.Should().Be(300000);
        config.ReceiveHighWatermark.Should().Be(300000);
    }

    [Fact(DisplayName = "TCP Keepalive는 기본적으로 활성화된다")]
    public void ServerConfig_WithDefaults_EnablesTcpKeepalive()
    {
        // Given & When
        var config = new ServerConfig(ServerType.Play, 1, "test-1", "tcp://*:5555");

        // Then
        config.TcpKeepalive.Should().BeTrue();
    }

    #endregion

    #region Factory 메서드

    [Fact(DisplayName = "Create는 포트 번호로 바인드 엔드포인트를 생성한다")]
    public void ServerConfig_Create_GeneratesBindEndpointFromPort()
    {
        // Given
        const int port = 5555;

        // When
        var config = ServerConfig.Create(ServerType.Play, 1, "play-1", port);

        // Then
        config.BindEndpoint.Should().Be("tcp://*:5555");
    }

    [Fact(DisplayName = "GetConnectAddress는 호스트와 포트로 연결 주소를 생성한다")]
    public void ServerConfig_GetConnectAddress_GeneratesCorrectAddress()
    {
        // Given
        const string host = "192.168.1.100";
        const int port = 5555;

        // When
        var address = ServerConfig.GetConnectAddress(host, port);

        // Then
        address.Should().Be("tcp://192.168.1.100:5555");
    }

    #endregion

    #region 커스텀 설정

    [Fact(DisplayName = "커스텀 타임아웃을 설정할 수 있다")]
    public void ServerConfig_WithCustomTimeout_UsesProvidedValue()
    {
        // Given
        const int customTimeout = 60000;

        // When
        var config = new ServerConfig(ServerType.Play, 1, "test-1", "tcp://*:5555", requestTimeoutMs: customTimeout);

        // Then
        config.RequestTimeoutMs.Should().Be(customTimeout);
    }

    [Fact(DisplayName = "커스텀 High Water Mark를 설정할 수 있다")]
    public void ServerConfig_WithCustomHighWatermarks_UsesProvidedValues()
    {
        // Given
        const int sendHwm = 5000;
        const int recvHwm = 3000;

        // When
        var config = new ServerConfig(ServerType.Play, 1, "test-1", "tcp://*:5555",
            sendHighWatermark: sendHwm,
            receiveHighWatermark: recvHwm);

        // Then
        config.SendHighWatermark.Should().Be(sendHwm);
        config.ReceiveHighWatermark.Should().Be(recvHwm);
    }

    #endregion
}
