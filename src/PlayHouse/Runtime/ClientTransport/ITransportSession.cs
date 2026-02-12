#nullable enable

using System.IO.Pipelines;
using PlayHouse.Abstractions;

namespace PlayHouse.Runtime.ClientTransport;

/// <summary>
/// Transport session interface for client connections.
/// </summary>
/// <remarks>
/// Provides a unified interface for different transport protocols (TCP, WebSocket, etc.).
/// Implementations handle protocol-specific details while exposing a common API.
/// </remarks>
public interface ITransportSession : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique session identifier.
    /// </summary>
    long SessionId { get; }

    /// <summary>
    /// Gets or sets the account ID associated with this session.
    /// Set after successful authentication in OnAuthenticate callback.
    /// </summary>
    string AccountId { get; set; }

    /// <summary>
    /// Gets or sets whether the session is authenticated.
    /// </summary>
    bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets the stage ID associated with this session.
    /// Set after successful authentication.
    /// </summary>
    string StageId { get; set; }

    /// <summary>
    /// Gets whether the session is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets or sets an optional context for message routing (e.g., BaseStage/BaseActor reference).
    /// Used for performance optimization to avoid dictionary lookups.
    /// </summary>
    object? ProcessorContext { get; set; }

    /// <summary>
    /// Sends data to the remote endpoint.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a response packet with zero-copy optimization.
    /// Thread-safe: Queue-based implementation ensures thread safety.
    /// </summary>
    /// <param name="msgId">Message identifier.</param>
    /// <param name="msgSeq">Message sequence number.</param>
    /// <param name="stageId">Stage identifier.</param>
    /// <param name="errorCode">Error code (0 for success).</param>
    /// <param name="payload">Message payload.</param>
    void SendResponse(
        string msgId,
        ushort msgSeq,
        string stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload);

    /// <summary>
    /// Gracefully disconnects the session.
    /// </summary>
    ValueTask DisconnectAsync();
}

/// <summary>
/// Callback delegate for received messages.
/// Fire-and-forget pattern for high throughput.
/// </summary>
/// <param name="session">The session that received the message.</param>
/// <param name="msgId">Message identifier.</param>
/// <param name="msgSeq">Message sequence number.</param>
/// <param name="stageId">Target stage ID.</param>
/// <param name="payload">Message payload (must be disposed by caller).</param>
public delegate void MessageReceivedCallback(
    ITransportSession session,
    string msgId,
    ushort msgSeq,
    string stageId,
    IPayload payload);

/// <summary>
/// Callback delegate for session disconnection.
/// </summary>
/// <param name="session">The disconnected session.</param>
/// <param name="exception">Exception if disconnection was due to an error; otherwise null.</param>
public delegate void SessionDisconnectedCallback(ITransportSession session, Exception? exception);
