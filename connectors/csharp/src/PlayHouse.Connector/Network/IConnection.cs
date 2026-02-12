using System;
using System.Threading;
using System.Threading.Tasks;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Network;

/// <summary>
/// Abstraction for network connection (TCP or WebSocket).
/// </summary>
public interface IConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets whether the connection is currently active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Raised when a complete packet is received from the server.
    /// </summary>
    event EventHandler<IPacket>? PacketReceived;

    /// <summary>
    /// Raised when the connection is disconnected.
    /// </summary>
    event EventHandler<Exception?>? Disconnected;

    /// <summary>
    /// Connects to the specified server endpoint.
    /// </summary>
    /// <param name="host">Server hostname or IP address</param>
    /// <param name="port">Server port</param>
    /// <param name="useSsl">Whether to use SSL/TLS encryption</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ConnectAsync(string host, int port, bool useSsl = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the server gracefully.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends data to the server.
    /// </summary>
    /// <param name="data">Data to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends header and payload as a single logical packet.
    /// </summary>
    /// <param name="header">Encoded packet header bytes</param>
    /// <param name="payload">Packet payload bytes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask SendAsync(
        ReadOnlyMemory<byte> header,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}
