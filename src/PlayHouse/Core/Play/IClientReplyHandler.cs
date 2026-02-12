#nullable enable

namespace PlayHouse.Core.Play;

/// <summary>
/// Handler for sending replies back to clients through TCP Transport.
/// </summary>
/// <remarks>
/// This interface decouples PlayDispatcher/BaseStage from PlayServer,
/// allowing client replies to be routed through the transport layer.
/// </remarks>
internal interface IClientReplyHandler
{
    /// <summary>
    /// Sends a reply to a client.
    /// </summary>
    /// <param name="sessionId">Session ID of the client.</param>
    /// <param name="msgId">Message ID.</param>
    /// <param name="msgSeq">Message sequence number.</param>
    /// <param name="stageId">Stage ID.</param>
    /// <param name="errorCode">Error code (0 for success).</param>
    /// <param name="payload">Response payload.</param>
    ValueTask SendClientReplyAsync(long sessionId, string msgId, ushort msgSeq, string stageId, ushort errorCode, Abstractions.IPayload payload);
}
