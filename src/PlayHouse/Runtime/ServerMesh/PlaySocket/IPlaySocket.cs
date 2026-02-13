#nullable enable

using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Runtime.ServerMesh.PlaySocket;

/// <summary>
/// Interface for server-to-server communication sockets.
/// </summary>
/// <remarks>
/// Implements Router-Router pattern for bidirectional messaging.
/// 3-Frame message structure:
/// - Frame 0: Target router ID (UTF-8)
/// - Frame 1: RouteHeader (Protobuf serialized)
/// - Frame 2: Payload (binary)
/// </remarks>
internal interface IPlaySocket : IDisposable
{
    /// <summary>
    /// Gets the Server ID of this socket.
    /// </summary>
    string ServerId { get; }

    /// <summary>
    /// Gets the bind endpoint address.
    /// </summary>
    string EndPoint { get; }

    /// <summary>
    /// Binds the socket to the specified endpoint for receiving messages.
    /// </summary>
    /// <param name="endpoint">Bind endpoint address (e.g., "tcp://*:5555").</param>
    void Bind(string endpoint);

    /// <summary>
    /// Connects to a remote socket.
    /// </summary>
    /// <param name="endpoint">Connect address (e.g., "tcp://localhost:5555").</param>
    /// <param name="routerId">Optional router ID alias used by ROUTER sockets.</param>
    void Connect(string endpoint, string? routerId = null);

    /// <summary>
    /// Disconnects from a remote socket.
    /// </summary>
    /// <param name="endpoint">Address to disconnect from.</param>
    void Disconnect(string endpoint);

    /// <summary>
    /// Sends a RuntimeRoutePacket to the specified target.
    /// </summary>
    /// <param name="routerId">Target router ID.</param>
    /// <param name="packet">Route packet to send.</param>
    /// <remarks>
    /// Packet will be disposed after sending. Sends as 3-frame multipart message.
    /// </remarks>
    void Send(string routerId, RoutePacket packet);

    /// <summary>
    /// Receives a RoutePacket (blocking until message arrives).
    /// </summary>
    /// <returns>RoutePacket if received, null on error.</returns>
    RoutePacket? Receive();

    /// <summary>
    /// Returns whether the target router ID is ready for send.
    /// </summary>
    /// <param name="routerId">Target router ID.</param>
    bool IsRouterIdReady(string routerId);

    /// <summary>
    /// Marks the target router ID as not ready.
    /// </summary>
    /// <param name="routerId">Target router ID.</param>
    void MarkRouterIdNotReady(string routerId);

    /// <summary>
    /// Receives raw frames and echoes them back immediately for diagnostic purposes.
    /// This bypasses RoutePacket creation and parsing.
    /// </summary>
    /// <param name="level">Diagnostic level (0: Raw, 1: Header Only).</param>
    void ReceiveDirect(int level);
}

/// <summary>
/// Configuration options for PlaySocket.
/// </summary>
public sealed class PlaySocketConfig
{
    /// <summary>
    /// Gets or sets the send high water mark.
    /// </summary>
    public int SendHighWatermark { get; set; } = 300000;

    /// <summary>
    /// Gets or sets the receive high water mark.
    /// </summary>
    public int ReceiveHighWatermark { get; set; } = 300000;

    /// <summary>
    /// Gets or sets whether to enable TCP keepalive.
    /// </summary>
    public bool TcpKeepalive { get; set; } = true;

    /// <summary>
    /// Gets or sets the TCP keepalive idle time in seconds.
    /// </summary>
    public int TcpKeepaliveIdle { get; set; } = 60;

    /// <summary>
    /// Gets or sets the TCP keepalive interval in seconds.
    /// </summary>
    public int TcpKeepaliveInterval { get; set; } = 10;

    /// <summary>
    /// Gets or sets the linger time in milliseconds.
    /// </summary>
    public int Linger { get; set; } = 0;

    /// <summary>
    /// Gets or sets the receive timeout in milliseconds.
    /// -1 means infinite (block forever).
    /// </summary>
    public int ReceiveTimeout { get; set; } = -1;

    /// <summary>
    /// Creates a default configuration.
    /// </summary>
    public static PlaySocketConfig Default => new();

    /// <summary>
    /// Creates configuration from ServerConfig.
    /// </summary>
    /// <param name="config">Server configuration.</param>
    /// <returns>Socket configuration.</returns>
    public static PlaySocketConfig FromServerConfig(ServerConfig config) => new()
    {
        SendHighWatermark = config.SendHighWatermark,
        ReceiveHighWatermark = config.ReceiveHighWatermark,
        TcpKeepalive = config.TcpKeepalive
    };
}
