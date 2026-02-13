#nullable enable

using System.Security.Cryptography.X509Certificates;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Internal;
using PlayHouse.Infrastructure.Memory;
using PlayHouse.Runtime.ClientTransport;

namespace PlayHouse.Core.Play.Bootstrap;

/// <summary>
/// Play Server 설정 옵션.
/// </summary>
public sealed class PlayServerOption
{
    /// <summary>
    /// 서버 타입 (기본값: ServerType.Play).
    /// </summary>
    public ServerType ServerType { get; set; } = ServerType.Play;

    /// <summary>
    /// 서비스 그룹 ID (같은 ServerType 내에서 서버 군 구분, 기본값: 1).
    /// </summary>
    public ushort ServiceId { get; set; } = ServiceIdDefaults.Default;

    /// <summary>
    /// 서버 인스턴스 ID.
    /// </summary>
    public string ServerId { get; set; } = "1";

    /// <summary>
    /// ZMQ 서버 간 통신용 바인드 주소.
    /// 예: "tcp://0.0.0.0:5000"
    /// </summary>
    public string BindEndpoint { get; set; } = "tcp://0.0.0.0:5000";

    /// <summary>
    /// 요청 타임아웃 (밀리초, 기본값: 30000).
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 송신 High Water Mark (기본값: 300000).
    /// </summary>
    public int SendHighWatermark { get; set; } = 300000;

    /// <summary>
    /// 수신 High Water Mark (기본값: 300000).
    /// </summary>
    public int ReceiveHighWatermark { get; set; } = 300000;

    /// <summary>
    /// 인증 메시지 ID.
    /// 미인증 클라이언트가 전송할 수 있는 유일한 메시지입니다.
    /// 이 메시지 외의 메시지가 미인증 상태에서 수신되면 연결이 끊어집니다.
    /// </summary>
    public string AuthenticateMessageId { get; set; } = string.Empty;

    /// <summary>
    /// 인증 시 자동으로 생성/조인할 기본 Stage 타입.
    /// </summary>
    public string DefaultStageType { get; set; } = string.Empty;

    /// <summary>
    /// 동시 실행을 담당할 워커 Task 풀의 최소 크기. (기본값: 100)
    /// </summary>
    public int MinTaskPoolSize { get; set; } = 100;

    /// <summary>
    /// 동시 실행을 담당할 워커 Task 풀의 최대 크기. (기본값: 1000)
    /// </summary>
    public int MaxTaskPoolSize { get; set; } = 1000;

    /// <summary>
    /// 메시지 전용 메모리 풀 설정.
    /// </summary>
    public MessagePoolConfig MessagePool { get; set; } = new();

    #region Transport Configuration

    /// <summary>
    /// Transport 옵션 (버퍼 크기, 타임아웃 등).
    /// </summary>
    public TransportOptions TransportOptions { get; set; } = new();

    /// <summary>
    /// TCP 포트. null이면 TCP 비활성화, 0이면 자동 포트 할당.
    /// </summary>
    public int? TcpPort { get; set; } = 6000;

    /// <summary>
    /// TCP 바인드 주소 (기본값: 모든 인터페이스).
    /// </summary>
    public string? TcpBindAddress { get; set; }

    /// <summary>
    /// TCP TLS 포트. null이면 TCP TLS 비활성화.
    /// </summary>
    public int? TcpTlsPort { get; set; }

    /// <summary>
    /// TCP TLS 인증서 (null이면 TLS 비활성화).
    /// </summary>
    public X509Certificate2? TcpTlsCertificate { get; set; }

    /// <summary>
    /// WebSocket 경로 (null 또는 빈 문자열이면 WebSocket 비활성화).
    /// </summary>
    public string? WebSocketPath { get; set; }

    /// <summary>
    /// WebSocket TLS 인증서 (null이면 TLS 비활성화).
    /// </summary>
    public X509Certificate2? WebSocketTlsCertificate { get; set; }

    /// <summary>
    /// WebSocket이 활성화되었는지 여부.
    /// </summary>
    public bool IsWebSocketEnabled => !string.IsNullOrEmpty(WebSocketPath);

    /// <summary>
    /// WebSocket TLS가 활성화되었는지 여부.
    /// </summary>
    public bool IsWebSocketTlsEnabled => WebSocketTlsCertificate != null;

    /// <summary>
    /// TCP가 활성화되었는지 여부.
    /// </summary>
    public bool IsTcpEnabled => TcpPort.HasValue;

    /// <summary>
    /// TCP TLS가 활성화되었는지 여부.
    /// </summary>
    public bool IsTcpTlsEnabled => TcpTlsPort.HasValue && TcpTlsCertificate != null;

    internal bool TcpPortExplicitlySet { get; set; }

    #endregion

    /// <summary>
    /// 설정 유효성 검증.
    /// </summary>
    public void Validate()
    {
        ServerOptionValidator.ValidateIdentity(ServerType, ServerId, BindEndpoint);

        if (!IsTcpEnabled && !IsTcpTlsEnabled && !IsWebSocketEnabled)
            throw new InvalidOperationException("At least one transport (TCP or WebSocket) must be enabled");

        if (string.IsNullOrEmpty(AuthenticateMessageId))
            throw new InvalidOperationException("AuthenticateMessageId is required. Use Configure(o => o.AuthenticateMessageId = \"YourAuthMsgId\")");
    }
}
