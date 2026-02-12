#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Internal;

namespace PlayHouse.Core.Api.Bootstrap;

/// <summary>
/// API Server 설정 옵션.
/// </summary>
public sealed class ApiServerOption
{
    /// <summary>
    /// 서버 타입 (기본값: ServerType.Api).
    /// </summary>
    public ServerType ServerType { get; set; } = ServerType.Api;

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
    /// 예: "tcp://0.0.0.0:5100"
    /// </summary>
    public string BindEndpoint { get; set; } = "tcp://0.0.0.0:5100";

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
    /// 동시 실행을 담당할 워커 Task 풀의 최소 크기. (기본값: 100)
    /// </summary>
    public int MinTaskPoolSize { get; set; } = 100;

    /// <summary>
    /// 동시 실행을 담당할 워커 Task 풀의 최대 크기. (기본값: 1000)
    /// </summary>
    public int MaxTaskPoolSize { get; set; } = 1000;

    /// <summary>
    /// 설정 유효성 검증.
    /// </summary>
    public void Validate()
    {
        ServerOptionValidator.ValidateIdentity(ServerType, ServerId, BindEndpoint);
    }
}
