#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Runtime.ServerMesh;

/// <summary>
/// Configuration for a PlayHouse server instance.
/// </summary>
/// <remarks>
/// NID (Node ID) format: "{ServiceId}:{ServerId}" (legacy).
/// ServerType is transported separately in RouteHeader.
/// </remarks>
public sealed class ServerConfig
{
    /// <summary>
    /// Gets the server type (Play, Api).
    /// </summary>
    public ServerType ServerType { get; }

    /// <summary>
    /// Gets the service group identifier (within the same ServerType).
    /// </summary>
    public ushort ServiceId { get; }

    /// <summary>
    /// Gets the server instance identifier (unique string, e.g., "play-1", "api-seoul-1").
    /// </summary>
    public string ServerId { get; }

    /// <summary>
    /// Gets the bind endpoint for ZMQ socket (e.g., "tcp://*:5555").
    /// </summary>
    public string BindEndpoint { get; }

    /// <summary>
    /// Gets the request timeout in milliseconds (default: 30000ms).
    /// </summary>
    public int RequestTimeoutMs { get; }

    /// <summary>
    /// Gets the high water mark for outgoing messages.
    /// </summary>
    public int SendHighWatermark { get; }

    /// <summary>
    /// Gets the high water mark for incoming messages.
    /// </summary>
    public int ReceiveHighWatermark { get; }

    /// <summary>
    /// Gets whether TCP keepalive is enabled.
    /// </summary>
    public bool TcpKeepalive { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerConfig"/> class.
    /// </summary>
    /// <param name="serverType">Server type (Play, Api).</param>
    /// <param name="serviceId">Service group identifier.</param>
    /// <param name="serverId">Server instance identifier.</param>
    /// <param name="bindEndpoint">ZMQ bind endpoint.</param>
    /// <param name="requestTimeoutMs">Request timeout in milliseconds.</param>
    /// <param name="sendHighWatermark">Send high water mark.</param>
    /// <param name="receiveHighWatermark">Receive high water mark.</param>
    /// <param name="tcpKeepalive">Enable TCP keepalive.</param>
    public ServerConfig(
        ServerType serverType,
        ushort serviceId,
        string serverId,
        string bindEndpoint,
        int requestTimeoutMs = 30000,
        int sendHighWatermark = 300000,
        int receiveHighWatermark = 300000,
        bool tcpKeepalive = true)
    {
        ServerType = serverType;
        ServiceId = serviceId;
        ServerId = serverId;
        BindEndpoint = bindEndpoint;
        RequestTimeoutMs = requestTimeoutMs;
        SendHighWatermark = sendHighWatermark;
        ReceiveHighWatermark = receiveHighWatermark;
        TcpKeepalive = tcpKeepalive;
    }

    /// <summary>
    /// Creates a ServerConfig with default settings.
    /// </summary>
    /// <param name="serverType">Server type (Play, Api).</param>
    /// <param name="serviceId">Service group identifier.</param>
    /// <param name="serverId">Server instance identifier.</param>
    /// <param name="port">Port number for binding.</param>
    /// <returns>A new ServerConfig instance.</returns>
    public static ServerConfig Create(ServerType serverType, ushort serviceId, string serverId, int port)
    {
        return new ServerConfig(
            serverType,
            serviceId,
            serverId,
            $"tcp://*:{port}");
    }

    /// <summary>
    /// Gets the connect address for a remote server.
    /// </summary>
    /// <param name="host">Remote host address.</param>
    /// <param name="port">Remote port number.</param>
    /// <returns>Connect address string.</returns>
    public static string GetConnectAddress(string host, int port)
    {
        return $"tcp://{host}:{port}";
    }
}
