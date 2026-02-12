#nullable enable

using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Abstractions;

/// <summary>
/// Callback delegate for handling reply packets.
/// </summary>
/// <param name="errorCode">Error code from the reply (0 = success).</param>
/// <param name="reply">The reply packet, or null if error.</param>
public delegate void ReplyCallback(ushort errorCode, IPacket? reply);

/// <summary>
/// Provides base functionality for sending packets and replies.
/// </summary>
/// <remarks>
/// ILink is the base interface for all link types in the framework.
/// It provides methods for sending messages to API servers and Play stages,
/// as well as replying to incoming requests.
/// </remarks>
public interface ILink
{
    /// <summary>
    /// Gets the server type of this sender.
    /// </summary>
    ServerType ServerType { get; }

    /// <summary>
    /// Gets the service ID of this sender.
    /// </summary>
    ushort ServiceId { get; }

    #region API Server Communication

    /// <summary>
    /// Sends a one-way packet to an API server.
    /// </summary>
    /// <param name="apiServerId">Target API server ID.</param>
    /// <param name="packet">Packet to send.</param>
    void SendToApi(string apiServerId, IPacket packet);

    /// <summary>
    /// Sends a request to an API server with a callback for the reply.
    /// </summary>
    /// <param name="apiServerId">Target API server ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <param name="replyCallback">Callback to handle the reply.</param>
    void RequestToApi(string apiServerId, IPacket packet, ReplyCallback replyCallback);

    /// <summary>
    /// Sends a request to an API server and awaits the reply.
    /// </summary>
    /// <param name="apiServerId">Target API server ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <returns>The reply packet.</returns>
    Task<IPacket> RequestToApi(string apiServerId, IPacket packet);

    #endregion

    #region Stage Communication

    /// <summary>
    /// Sends a one-way packet to a stage on a Play server.
    /// </summary>
    /// <param name="playServerId">Target Play server ID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="packet">Packet to send.</param>
    void SendToStage(string playServerId, string stageId, IPacket packet);

    /// <summary>
    /// Sends a request to a stage with a callback for the reply.
    /// </summary>
    /// <param name="playServerId">Target Play server ID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <param name="replyCallback">Callback to handle the reply.</param>
    void RequestToStage(string playServerId, string stageId, IPacket packet, ReplyCallback replyCallback);

    /// <summary>
    /// Sends a request to a stage and awaits the reply.
    /// </summary>
    /// <param name="playServerId">Target Play server ID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <returns>The reply packet.</returns>
    Task<IPacket> RequestToStage(string playServerId, string stageId, IPacket packet);

    #endregion

    #region API Service Communication

    /// <summary>
    /// Sends a packet to an API server in the specified service using RoundRobin selection.
    /// </summary>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="packet">Packet to send.</param>
    void SendToApiService(ushort serviceId, IPacket packet);

    /// <summary>
    /// Sends a packet to an API server in the specified service using the specified selection policy.
    /// </summary>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="packet">Packet to send.</param>
    /// <param name="policy">Server selection policy.</param>
    void SendToApiService(ushort serviceId, IPacket packet, ServerSelectionPolicy policy);

    /// <summary>
    /// Sends a request to an API server in the specified service with a callback (RoundRobin).
    /// </summary>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <param name="replyCallback">Callback to handle the reply.</param>
    void RequestToApiService(ushort serviceId, IPacket packet, ReplyCallback replyCallback);

    /// <summary>
    /// Sends a request to an API server in the specified service with a callback and policy.
    /// </summary>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <param name="replyCallback">Callback to handle the reply.</param>
    /// <param name="policy">Server selection policy.</param>
    void RequestToApiService(ushort serviceId, IPacket packet, ReplyCallback replyCallback, ServerSelectionPolicy policy);

    /// <summary>
    /// Sends a request to an API server in the specified service and awaits the reply (RoundRobin).
    /// </summary>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <returns>The reply packet.</returns>
    Task<IPacket> RequestToApiService(ushort serviceId, IPacket packet);

    /// <summary>
    /// Sends a request to an API server in the specified service and awaits the reply with policy.
    /// </summary>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <param name="policy">Server selection policy.</param>
    /// <returns>The reply packet.</returns>
    Task<IPacket> RequestToApiService(ushort serviceId, IPacket packet, ServerSelectionPolicy policy);

    #endregion

    #region System Communication

    /// <summary>
    /// Sends a one-way system message to a server.
    /// </summary>
    /// <remarks>
    /// System messages are handled by ISystemController.Handles() registered handlers.
    /// This method does not wait for a response.
    /// </remarks>
    /// <param name="serverId">Target server ID.</param>
    /// <param name="packet">Packet to send.</param>
    void SendToSystem(string serverId, IPacket packet);

    /// <summary>
    /// Sends a system request with a callback for the reply.
    /// </summary>
    /// <remarks>
    /// Note: The receiving server's system handler must explicitly send a reply
    /// for the callback to be invoked. Phase 1 system handlers do not have
    /// automatic reply support.
    /// </remarks>
    /// <param name="serverId">Target server ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <param name="replyCallback">Callback to handle the reply.</param>
    void RequestToSystem(string serverId, IPacket packet, ReplyCallback replyCallback);

    /// <summary>
    /// Sends a system request and awaits the reply.
    /// </summary>
    /// <remarks>
    /// Note: The receiving server's system handler must explicitly send a reply
    /// for this task to complete. Phase 1 system handlers do not have
    /// automatic reply support.
    /// </remarks>
    /// <param name="serverId">Target server ID.</param>
    /// <param name="packet">Request packet.</param>
    /// <returns>The reply packet.</returns>
    Task<IPacket> RequestToSystem(string serverId, IPacket packet);

    #endregion

    #region Reply

    /// <summary>
    /// Sends an error-only reply to the current request.
    /// </summary>
    /// <param name="errorCode">Error code to send.</param>
    void Reply(ushort errorCode);

    /// <summary>
    /// Sends a reply packet to the current request.
    /// </summary>
    /// <param name="reply">Reply packet.</param>
    void Reply(IPacket reply);

    #endregion
}
