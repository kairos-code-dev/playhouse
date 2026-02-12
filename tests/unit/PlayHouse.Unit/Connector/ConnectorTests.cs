#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;

namespace PlayHouse.Unit.Connector;

/// <summary>
/// 단위 테스트: Connector 클래스의 기본 기능 검증
/// 실제 네트워크 연결 없이 테스트 가능한 부분만 검증합니다.
/// Connect() 시 host, port, stageId를 전달하는 새 API를 사용합니다.
/// </summary>
public class ConnectorTests
{
    [Fact(DisplayName = "Init - Config가 설정된다")]
    public void Init_SetsConfig()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        var config = new ConnectorConfig
        {
            RequestTimeoutMs = 5000
        };

        // When (행동)
        connector.Init(config);

        // Then (결과)
        connector.ConnectorConfig.Should().BeSameAs(config, "설정이 저장되어야 함");
        connector.ConnectorConfig.RequestTimeoutMs.Should().Be(5000);
    }

    [Fact(DisplayName = "Init - null Config는 예외를 발생시킨다")]
    public void Init_NullConfig_ThrowsException()
    {
        // Given (전제조건)
        var connector = new ClientConnector();

        // When (행동)
        var action = () => connector.Init(null!);

        // Then (결과)
        action.Should().Throw<ArgumentNullException>("null config는 허용되지 않아야 함");
    }

    [Fact(DisplayName = "IsConnected - 초기 상태는 false")]
    public void IsConnected_InitialState_ReturnsFalse()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        // When (행동)
        var isConnected = connector.IsConnected();

        // Then (결과)
        isConnected.Should().BeFalse("초기 상태는 연결되지 않음");
    }

    [Fact(DisplayName = "IsAuthenticated - 초기 상태는 false")]
    public void IsAuthenticated_InitialState_ReturnsFalse()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        // When (행동)
        var isAuthenticated = connector.IsAuthenticated();

        // Then (결과)
        isAuthenticated.Should().BeFalse("초기 상태는 인증되지 않음");
    }

    [Fact(DisplayName = "StageId - 초기 상태는 0")]
    public void StageId_InitialState_ReturnsZero()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        // When (행동)
        var stageId = connector.StageId;

        // Then (결과)
        stageId.Should().Be(0, "초기 StageId는 0");
    }

    [Fact(DisplayName = "OnConnect 이벤트 - 핸들러 등록 및 해제")]
    public void OnConnect_EventHandlerRegistration()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        var eventTriggered = false;
        Action<bool> handler = result => eventTriggered = true;

        // When (행동)
        connector.OnConnect += handler;
        connector.OnConnect -= handler;

        // Then (결과)
        // 이벤트 핸들러가 등록/해제되어도 예외가 발생하지 않음
        eventTriggered.Should().BeFalse("이벤트가 아직 발생하지 않음");
    }

    [Fact(DisplayName = "OnReceive 이벤트 - 핸들러 등록")]
    public void OnReceive_EventHandlerRegistration()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        var receivedStageId = 0L;
        IPacket? receivedPacket = null;

        // When (행동)
        connector.OnReceive += (stageId, stageType, packet) =>
        {
            receivedStageId = stageId;
            receivedPacket = packet;
        };

        // Then (결과)
        // 이벤트 핸들러가 등록되어도 예외가 발생하지 않음
    }

    [Fact(DisplayName = "OnError 이벤트 - 핸들러 등록")]
    public void OnError_EventHandlerRegistration()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        var receivedErrorCode = (ushort)0;

        // When (행동)
        connector.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedErrorCode = errorCode;
        };

        // Then (결과)
        // 이벤트 핸들러가 등록되어도 예외가 발생하지 않음
    }

    [Fact(DisplayName = "OnDisconnect 이벤트 - 핸들러 등록")]
    public void OnDisconnect_EventHandlerRegistration()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        var disconnected = false;

        // When (행동)
        connector.OnDisconnect += () => disconnected = true;

        // Then (결과)
        disconnected.Should().BeFalse("이벤트가 아직 발생하지 않음");
    }

    [Fact(DisplayName = "Send - 연결되지 않은 상태에서 OnError 이벤트 발생")]
    public void Send_NotConnected_TriggersOnError()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        ushort receivedErrorCode = 0;
        connector.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedErrorCode = errorCode;
        };

        using var packet = Packet.Empty("Test.Message");

        // When (행동)
        connector.Send(packet);

        // Then (결과)
        receivedErrorCode.Should().Be((ushort)ConnectorErrorCode.Disconnected,
            "연결되지 않은 상태에서 Send하면 Disconnected 에러가 발생해야 함");
    }

    [Fact(DisplayName = "Send(ReadOnlyMemory) - 연결되지 않은 상태에서 OnError 이벤트 발생")]
    public void Send_ReadOnlyMemory_NotConnected_TriggersOnError()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        ushort receivedErrorCode = 0;
        string? receivedMsgId = null;
        connector.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedErrorCode = errorCode;
            receivedMsgId = request.MsgId;
        };

        var payload = new byte[] { 1, 2, 3 }.AsMemory();

        // When (행동)
        connector.Send("Test.MemorySend", payload);

        // Then (결과)
        receivedErrorCode.Should().Be((ushort)ConnectorErrorCode.Disconnected);
        receivedMsgId.Should().Be("Test.MemorySend");
    }

    [Fact(DisplayName = "Send(ReadOnlySpan) - 연결되지 않은 상태에서 OnError 이벤트 발생")]
    public void Send_ReadOnlySpan_NotConnected_TriggersOnError()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        ushort receivedErrorCode = 0;
        string? receivedMsgId = null;
        connector.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedErrorCode = errorCode;
            receivedMsgId = request.MsgId;
        };

        var payload = new byte[] { 10, 20, 30 }.AsSpan();

        // When (행동)
        connector.Send("Test.SpanSend", payload);

        // Then (결과)
        receivedErrorCode.Should().Be((ushort)ConnectorErrorCode.Disconnected);
        receivedMsgId.Should().Be("Test.SpanSend");
    }

    [Fact(DisplayName = "Request - 연결되지 않은 상태에서 OnError 이벤트 발생")]
    public void Request_NotConnected_TriggersOnError()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        ushort receivedErrorCode = 0;
        connector.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedErrorCode = errorCode;
        };

        using var packet = Packet.Empty("Test.Request");

        // When (행동)
        connector.Request(packet, response => { });

        // Then (결과)
        receivedErrorCode.Should().Be((ushort)ConnectorErrorCode.Disconnected,
            "연결되지 않은 상태에서 Request하면 Disconnected 에러가 발생해야 함");
    }

    [Fact(DisplayName = "Request(ReadOnlyMemory) - 연결되지 않은 상태에서 OnError 이벤트 발생")]
    public void Request_ReadOnlyMemory_NotConnected_TriggersOnError()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        ushort receivedErrorCode = 0;
        string? receivedMsgId = null;
        connector.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedErrorCode = errorCode;
            receivedMsgId = request.MsgId;
        };

        var payload = new byte[] { 7, 8, 9 }.AsMemory();

        // When (행동)
        connector.Request("Test.MemoryRequest", payload, response => { });

        // Then (결과)
        receivedErrorCode.Should().Be((ushort)ConnectorErrorCode.Disconnected);
        receivedMsgId.Should().Be("Test.MemoryRequest");
    }

    [Fact(DisplayName = "Request(ReadOnlySpan) - 연결되지 않은 상태에서 OnError 이벤트 발생")]
    public void Request_ReadOnlySpan_NotConnected_TriggersOnError()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        ushort receivedErrorCode = 0;
        string? receivedMsgId = null;
        connector.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedErrorCode = errorCode;
            receivedMsgId = request.MsgId;
        };

        var payload = new byte[] { 70, 80, 90 }.AsSpan();

        // When (행동)
        connector.Request("Test.SpanRequest", payload, response => { });

        // Then (결과)
        receivedErrorCode.Should().Be((ushort)ConnectorErrorCode.Disconnected);
        receivedMsgId.Should().Be("Test.SpanRequest");
    }

    [Fact(DisplayName = "RequestAsync - 연결되지 않은 상태에서 예외 발생")]
    public async Task RequestAsync_NotConnected_ThrowsException()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        using var packet = Packet.Empty("Test.Request");

        // When (행동)
        var action = async () => await connector.RequestAsync(packet);

        // Then (결과)
        await action.Should().ThrowAsync<ConnectorException>()
            .Where(ex => ex.ErrorCode == (ushort)ConnectorErrorCode.Disconnected,
                "연결되지 않은 상태에서 예외가 발생해야 함");
    }

    [Fact(DisplayName = "RequestAsync(ReadOnlyMemory) - 연결되지 않은 상태에서 예외 발생")]
    public async Task RequestAsync_ReadOnlyMemory_NotConnected_ThrowsException()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());
        var payload = new byte[] { 1, 1, 2, 3 }.AsMemory();

        // When (행동)
        var action = async () => await connector.RequestAsync("Test.MemoryRequestAsync", payload);

        // Then (결과)
        await action.Should().ThrowAsync<ConnectorException>()
            .Where(ex => ex.ErrorCode == (ushort)ConnectorErrorCode.Disconnected);
    }

    [Fact(DisplayName = "RequestAsync(ReadOnlySpan) - 연결되지 않은 상태에서 예외 발생")]
    public async Task RequestAsync_ReadOnlySpan_NotConnected_ThrowsException()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());
        var payload = new byte[] { 4, 5, 6 };

        // When (행동)
        Func<Task> action = () => connector.RequestAsync("Test.SpanRequestAsync", payload.AsSpan());

        // Then (결과)
        await action.Should().ThrowAsync<ConnectorException>()
            .Where(ex => ex.ErrorCode == (ushort)ConnectorErrorCode.Disconnected);
    }

    [Fact(DisplayName = "Authenticate - 연결되지 않은 상태에서 OnError 이벤트 발생")]
    public void Authenticate_NotConnected_TriggersOnError()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        ushort receivedErrorCode = 0;
        connector.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedErrorCode = errorCode;
        };

        using var packet = Packet.Empty("Auth.Login");

        // When (행동)
        connector.Authenticate(packet, response => { });

        // Then (결과)
        receivedErrorCode.Should().Be((ushort)ConnectorErrorCode.Disconnected,
            "연결되지 않은 상태에서 Authenticate하면 Disconnected 에러가 발생해야 함");
    }

    [Fact(DisplayName = "AuthenticateAsync - 연결되지 않은 상태에서 예외 발생")]
    public async Task AuthenticateAsync_NotConnected_ThrowsException()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        using var packet = Packet.Empty("Auth.Login");

        // When (행동)
        var action = async () => await connector.AuthenticateAsync(packet);

        // Then (결과)
        await action.Should().ThrowAsync<ConnectorException>()
            .Where(ex => ex.ErrorCode == (ushort)ConnectorErrorCode.Disconnected);
    }

    [Fact(DisplayName = "Disconnect - 연결되지 않은 상태에서 호출해도 예외 없음")]
    public void Disconnect_NotConnected_NoException()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        // When (행동)
        var action = () => connector.Disconnect();

        // Then (결과)
        action.Should().NotThrow("연결되지 않은 상태에서 Disconnect해도 예외가 없어야 함");
    }

    [Fact(DisplayName = "MainThreadAction - 초기 상태에서 호출해도 예외 없음")]
    public void MainThreadAction_InitialState_NoException()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        // When (행동)
        var action = () => connector.MainThreadAction();

        // Then (결과)
        action.Should().NotThrow("MainThreadAction은 항상 안전하게 호출 가능해야 함");
    }

}
