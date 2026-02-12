#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Abstractions.Play;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Extensions.Proto;

/// <summary>
/// Extension methods for ILink and its derived interfaces to support Protobuf messages.
/// </summary>
public static class ProtoLinkExtensions
{
    #region ILink - Reply

    /// <summary>
    /// Sends a Protobuf message as a reply to the current request.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="message">The Protobuf message to send as a reply.</param>
    /// <example>
    /// <code>
    /// public Task OnDispatch(IPacket packet, IStageSender sender)
    /// {
    ///     sender.Reply(new ChatResponse { Message = "Hello" });
    ///     return Task.CompletedTask;
    /// }
    /// </code>
    /// </example>
    public static void Reply<T>(this ILink link, T message) where T : IMessage
    {
        link.Reply(ProtoCPacketExtensions.OfProto(message));
    }

    #endregion

    #region ILink - API Server Communication

    /// <summary>
    /// Sends a one-way Protobuf message to an API server.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="apiServerId">Target API server ID.</param>
    /// <param name="message">The Protobuf message to send.</param>
    public static void SendToApi<T>(this ILink link, string apiServerId, T message) where T : IMessage
    {
        link.SendToApi(apiServerId, ProtoCPacketExtensions.OfProto(message));
    }

    /// <summary>
    /// Sends a Protobuf request to an API server and awaits the reply.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="apiServerId">Target API server ID.</param>
    /// <param name="message">The Protobuf request message.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToApi<T>(this ILink link, string apiServerId, T message) where T : IMessage
    {
        return link.RequestToApi(apiServerId, ProtoCPacketExtensions.OfProto(message));
    }

    #endregion

    #region ILink - Stage Communication

    /// <summary>
    /// Sends a one-way Protobuf message to a stage on a Play server.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="playServerId">Target Play server ID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="message">The Protobuf message to send.</param>
    public static void SendToStage<T>(this ILink link, string playServerId, string stageId, T message) where T : IMessage
    {
        link.SendToStage(playServerId, stageId, ProtoCPacketExtensions.OfProto(message));
    }

    /// <summary>
    /// Sends a Protobuf request to a stage and awaits the reply.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="playServerId">Target Play server ID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="message">The Protobuf request message.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToStage<T>(this ILink link, string playServerId, string stageId, T message) where T : IMessage
    {
        return link.RequestToStage(playServerId, stageId, ProtoCPacketExtensions.OfProto(message));
    }

    #endregion

    #region ILink - API Service Communication

    /// <summary>
    /// Sends a Protobuf message to an API server in the specified service using RoundRobin selection.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="message">The Protobuf message to send.</param>
    public static void SendToApiService<T>(this ILink link, ushort serviceId, T message) where T : IMessage
    {
        link.SendToApiService(serviceId, ProtoCPacketExtensions.OfProto(message));
    }

    /// <summary>
    /// Sends a Protobuf message to an API server in the specified service using the specified selection policy.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="message">The Protobuf message to send.</param>
    /// <param name="policy">Server selection policy.</param>
    public static void SendToApiService<T>(this ILink link, ushort serviceId, T message, ServerSelectionPolicy policy) where T : IMessage
    {
        link.SendToApiService(serviceId, ProtoCPacketExtensions.OfProto(message), policy);
    }

    /// <summary>
    /// Sends a Protobuf request to an API server in the specified service and awaits the reply (RoundRobin).
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="message">The Protobuf request message.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToApiService<T>(this ILink link, ushort serviceId, T message) where T : IMessage
    {
        return link.RequestToApiService(serviceId, ProtoCPacketExtensions.OfProto(message));
    }

    /// <summary>
    /// Sends a Protobuf request to an API server in the specified service and awaits the reply with policy.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="message">The Protobuf request message.</param>
    /// <param name="policy">Server selection policy.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToApiService<T>(this ILink link, ushort serviceId, T message, ServerSelectionPolicy policy) where T : IMessage
    {
        return link.RequestToApiService(serviceId, ProtoCPacketExtensions.OfProto(message), policy);
    }

    #endregion

    #region ILink - System Communication

    /// <summary>
    /// Sends a one-way Protobuf system message to a server.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serverId">Target server ID.</param>
    /// <param name="message">The Protobuf message to send.</param>
    public static void SendToSystem<T>(this ILink link, string serverId, T message) where T : IMessage
    {
        link.SendToSystem(serverId, ProtoCPacketExtensions.OfProto(message));
    }

    /// <summary>
    /// Sends a Protobuf system request and awaits the reply.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serverId">Target server ID.</param>
    /// <param name="message">The Protobuf request message.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToSystem<T>(this ILink link, string serverId, T message) where T : IMessage
    {
        return link.RequestToSystem(serverId, ProtoCPacketExtensions.OfProto(message));
    }

    #endregion

    #region IApiLink - Stage Creation

    /// <summary>
    /// Creates a new stage on a Play server with a Protobuf payload.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type for the creation payload.</typeparam>
    /// <param name="link">The API sender instance.</param>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageType">Type of stage to create.</param>
    /// <param name="stageId">ID for the new stage.</param>
    /// <param name="payload">Protobuf creation payload.</param>
    /// <returns>Result containing the error code and create response.</returns>
    public static Task<CreateStageResult> CreateStage<T>(
        this IApiLink link,
        string playNid,
        string stageType,
        string stageId,
        T payload) where T : IMessage
    {
        return link.CreateStage(playNid, stageType, stageId, ProtoCPacketExtensions.OfProto(payload));
    }

    /// <summary>
    /// Gets an existing stage or creates a new one if it doesn't exist, with a Protobuf payload.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type for the creation payload.</typeparam>
    /// <param name="link">The API sender instance.</param>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageType">Type of stage to create if needed.</param>
    /// <param name="stageId">ID for the stage.</param>
    /// <param name="createPayload">Protobuf creation payload (used if stage doesn't exist).</param>
    /// <returns>Result containing whether stage was created and the response.</returns>
    public static Task<GetOrCreateStageResult> GetOrCreateStage<T>(
        this IApiLink link,
        string playNid,
        string stageType,
        string stageId,
        T createPayload) where T : IMessage
    {
        return link.GetOrCreateStage(playNid, stageType, stageId, ProtoCPacketExtensions.OfProto(createPayload));
    }

    #endregion

    #region IStageLink - Client Communication

    /// <summary>
    /// Sends a Protobuf message to a specific client from a Stage.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The stage sender instance.</param>
    /// <param name="sessionServerId">The session server ID.</param>
    /// <param name="sid">The session ID.</param>
    /// <param name="message">The Protobuf message to send.</param>
    public static void SendToClient<T>(this IStageLink link, string sessionServerId, long sid, T message) where T : IMessage
    {
        link.SendToClient(sessionServerId, sid, ProtoCPacketExtensions.OfProto(message));
    }

    #endregion

    #region IActorLink - Client Communication

    /// <summary>
    /// Sends a Protobuf message directly to the connected client from an Actor.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="link">The actor sender instance.</param>
    /// <param name="message">The Protobuf message to send.</param>
    public static void SendToClient<T>(this IActorLink link, T message) where T : IMessage
    {
        link.SendToClient(ProtoCPacketExtensions.OfProto(message));
    }

    #endregion
}
