#nullable enable

namespace PlayHouse.Abstractions.Play;

/// <summary>
/// Provides Actor-specific communication capabilities.
/// </summary>
/// <remarks>
/// IActorLink extends ILink with:
/// - AccountId/StageId properties for session-stage binding (must be set in OnAuthenticate)
/// - LeaveStage() to exit from current Stage
/// - SendToClient() for direct client messaging
/// </remarks>
public interface IActorLink : ILink
{
    /// <summary>
    /// Gets or sets the account identifier for this Actor.
    /// </summary>
    /// <remarks>
    /// MUST be set in IActor.OnAuthenticate() upon successful authentication.
    /// If empty ("") after OnAuthenticate completes, connection will be terminated.
    /// </remarks>
    string AccountId { get; set; }

    /// <summary>
    /// Gets or sets the stage identifier assigned to this Actor.
    /// </summary>
    /// <remarks>
    /// MUST be set to a positive value in IActor.OnAuthenticate() upon successful authentication.
    /// If 0 or negative after OnAuthenticate completes, connection will be terminated.
    /// </remarks>
    long StageId { get; set; }

    /// <summary>
    /// Sets AccountId and StageId together for authenticated context.
    /// </summary>
    /// <param name="accountId">Account identifier.</param>
    /// <param name="stageId">Assigned stage identifier (must be positive).</param>
    void SetAuthContext(string accountId, long stageId);

    /// <summary>
    /// Sets AccountId and StageType for single-stage authentication context.
    /// </summary>
    /// <param name="accountId">Account identifier.</param>
    /// <param name="stageType">Registered stage type configured as StageMode.Single.</param>
    void SetAuthSingleContext(string accountId, string stageType);

    /// <summary>
    /// Removes this Actor from the current Stage.
    /// </summary>
    /// <remarks>
    /// This method:
    /// 1. Removes the Actor from BaseStage._actors
    /// 2. Calls IActor.OnDestroy()
    /// 3. Does NOT close the client connection (actor can join another stage)
    /// </remarks>
    Task LeaveStageAsync();

    /// <summary>
    /// Sends a message directly to the connected client.
    /// </summary>
    /// <param name="packet">The packet to send to the client.</param>
    void SendToClient(IPacket packet);
}
