#nullable enable

namespace PlayHouse.Abstractions.Play;

/// <summary>
/// Interface for Actor lifecycle and authentication.
/// </summary>
/// <remarks>
/// Content developers implement this interface to define Actor behavior.
/// The framework calls these methods during the Actor join process:
///
/// Join Sequence (in JoinStageCmd):
/// 1. OnCreate() - Initialize actor state
/// 2. OnAuthenticate() - Authenticate the client (MUST set ActorLink.AccountId)
/// 3. OnCheckStage() - Resolve target Stage ID after authentication
/// 4. OnPostAuthenticate() - Load user data from API server, etc.
///
/// Cleanup:
/// - OnDestroy() - Final cleanup when actor leaves stage
///
/// IMPORTANT: ActorLink.AccountId MUST be set during OnAuthenticate().
/// If it remains empty after authentication, the connection will be terminated.
/// </remarks>
public interface IActor
{
    /// <summary>
    /// Gets the sender for this Actor.
    /// </summary>
    IActorLink ActorLink { get; }

    #region Lifecycle

    /// <summary>
    /// Called when the Actor is being created.
    /// Initialize actor-specific state here.
    /// </summary>
    Task OnCreate();

    /// <summary>
    /// Called when the Actor is being destroyed.
    /// Perform cleanup operations here.
    /// </summary>
    /// <remarks>
    /// This is called when:
    /// - LeaveStage() is invoked
    /// - Authentication fails
    /// - Stage is destroyed
    /// </remarks>
    Task OnDestroy();

    #endregion

    #region Authentication

    /// <summary>
    /// Called to authenticate the client.
    /// </summary>
    /// <param name="authPacket">The authentication request packet.</param>
    /// <returns>
    /// Tuple of (result, reply):
    /// - result: true if authentication succeeds, false otherwise
    /// - reply: Optional response packet to send back to client
    /// </returns>
    /// <remarks>
    /// CRITICAL: You MUST set ActorLink.AccountId in this method upon successful authentication.
    /// If AccountId remains empty ("") after this method returns true,
    /// the framework will throw an exception and terminate the connection.
    ///
    /// Example:
    /// <code>
    /// public async Task&lt;(bool, IPacket?)&gt; OnAuthenticate(IPacket authPacket)
    /// {
    ///     var authReq = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);
    ///     if (ValidateToken(authReq.Token))
    ///     {
    ///         ActorLink.AccountId = authReq.UserId; // REQUIRED!
    ///         var reply = new AuthReply { Success = true };
    ///         return (true, CPacket.Of(reply));
    ///     }
    ///     return (false, null);
    /// }
    /// </code>
    /// </remarks>
    Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket);

    /// <summary>
    /// Called after successful authentication to resolve the target Stage ID.
    /// </summary>
    /// <returns>
    /// Stage ID to join. Must be greater than 0.
    /// Returning 0 or negative value is treated as authentication failure.
    /// </returns>
    /// <remarks>
    /// This callback is used when Stage assignment is managed by server-side logic
    /// (for example querying an API server cache by AccountId).
    /// </remarks>
    Task<long> OnCheckStage();

    /// <summary>
    /// Called after successful authentication.
    /// Use for loading user data from API server, etc.
    /// </summary>
    /// <remarks>
    /// This is called after OnAuthenticate returns true and AccountId is validated.
    /// At this point, the Actor is not yet added to the Stage's actor list.
    /// </remarks>
    Task OnPostAuthenticate();

    #endregion
}
