#nullable enable

namespace PlayHouse.Connector;

/// <summary>
/// Connector 설정
/// </summary>
/// <remarks>
/// 서버 접속 정보(Host, Port)는 Connect() 호출 시 전달합니다.
/// Connector 인스턴스는 여러 서버에 접속/해제가 가능하도록 설계되었습니다.
/// </remarks>
public sealed class ConnectorConfig
{
    /// <summary>
    /// WebSocket 사용 여부 (false면 TCP)
    /// </summary>
    public bool UseWebsocket { get; set; }

    /// <summary>
    /// SSL/TLS 사용 여부 (TCP의 경우 TLS, WebSocket의 경우 wss://)
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// 서버 인증서 검증 건너뛰기 (테스트용 자체 서명 인증서 사용 시)
    /// 주의: 프로덕션에서는 false로 설정하세요.
    /// </summary>
    public bool SkipServerCertificateValidation { get; set; }

    /// <summary>
    /// WebSocket 경로 (기본값: "/ws")
    /// UseWebsocket이 true일 때만 사용됩니다.
    /// </summary>
    public string WebSocketPath { get; set; } = "/ws";

    /// <summary>
    /// 연결 유휴 타임아웃 (밀리초, 기본값 30000)
    /// </summary>
    public int ConnectionIdleTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 하트비트 주기 (밀리초, 기본값 10000)
    /// </summary>
    public int HeartBeatIntervalMs { get; set; } = 10000;

    /// <summary>
    /// 하트비트 타임아웃 (밀리초, 기본값 30000)
    /// 마지막 메시지 수신 후 이 시간이 지나면 연결 끊김으로 판단하여 OnDisconnect 발생
    /// 0이면 비활성화
    /// </summary>
    public int HeartbeatTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 요청 타임아웃 (밀리초, 기본값 30000)
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 응답 시간 로깅 활성화
    /// </summary>
    public bool EnableLoggingResponseTime { get; set; }

    /// <summary>
    /// 추적 로깅 활성화
    /// </summary>
    public bool TurnOnTrace { get; set; }

    /// <summary>
    /// 송신 시 헤더/페이로드 분리 전송 활성화.
    /// true면 payload 최종 복사를 줄이는 segmented send 경로를 사용합니다.
    /// </summary>
    public bool EnableSegmentedSend { get; set; } = true;
}
