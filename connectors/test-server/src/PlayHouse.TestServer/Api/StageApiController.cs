#nullable enable

using Microsoft.AspNetCore.Mvc;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Shared;
using PlayHouse.Extensions.Proto;
using PlayHouse.TestServer.Shared;

namespace PlayHouse.TestServer.Api;

/// <summary>
/// HTTP API Controller for TestServer Stage operations.
/// Provides HTTP endpoints for connector E2E testing.
/// </summary>
[ApiController]
[Route("api")]
public class StageApiController : ControllerBase
{
    private readonly IApiLink _apiLink;
    private readonly string _playServerId;
    private readonly ILogger<StageApiController> _logger;

    /// <summary>
    /// Initializes a new instance of the StageApiController.
    /// </summary>
    /// <param name="apiLink">The API link for communicating with PlayServer</param>
    /// <param name="configuration">Configuration containing PlayServerId</param>
    /// <param name="logger">Logger instance</param>
    public StageApiController(
        IApiLink apiLink,
        IConfiguration configuration,
        ILogger<StageApiController> logger)
    {
        _apiLink = apiLink;
        _playServerId = configuration["PlayServerId"]
            ?? throw new InvalidOperationException("PlayServerId not configured");
        _logger = logger;
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    /// <returns>Server status</returns>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", serverId = _playServerId });
    }

    /// <summary>
    /// Creates a new stage on the specified PlayServer.
    /// </summary>
    /// <param name="request">The create stage request</param>
    /// <returns>Result of the create operation</returns>
    [HttpPost("stages")]
    public async Task<IActionResult> CreateStage([FromBody] CreateStageRequest request)
    {
        try
        {
            _logger.LogInformation(
                "CreateStage request: StageType={StageType}, StageId={StageId}",
                request.StageType,
                request.StageId);

            var createPayload = new Proto.CreateStagePayload
            {
                StageName = request.StageType,
                MaxPlayers = 10
            };

            var result = await _apiLink.CreateStage(
                _playServerId,
                request.StageType,
                request.StageId,
                ProtoCPacketExtensions.OfProto(createPayload));

            // Parse OnCreate reply payload
            string? replyPayloadId = null;
            if (result.CreateStageRes.Payload.Length > 0)
            {
                var createReply = Proto.CreateStageReply.Parser.ParseFrom(
                    result.CreateStageRes.Payload.DataSpan);
                replyPayloadId = $"{createReply.ReceivedStageName}:{createReply.ReceivedMaxPlayers}";
            }

            _logger.LogInformation(
                "CreateStage result: Success={Success}, StageId={StageId}, ReplyPayload={ReplyPayload}",
                result.Result,
                request.StageId,
                replyPayloadId);

            if (result.Result)
            {
                StageAssignmentStore.MarkStageCreated(request.StageId);
            }

            return Ok(new CreateStageResponse
            {
                Success = result.Result,
                StageId = request.StageId,
                ReplyPayloadId = replyPayloadId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating stage");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets or creates a stage on the specified PlayServer.
    /// </summary>
    /// <param name="request">The get or create stage request</param>
    /// <returns>Result of the get or create operation</returns>
    [HttpPost("stages/get-or-create")]
    public async Task<IActionResult> GetOrCreateStage([FromBody] GetOrCreateStageRequest request)
    {
        try
        {
            _logger.LogInformation(
                "GetOrCreateStage request: StageType={StageType}, StageId={StageId}",
                request.StageType,
                request.StageId);

            var createPayload = new Proto.CreateStagePayload
            {
                StageName = request.StageType,
                MaxPlayers = 10
            };

            var result = await _apiLink.GetOrCreateStage(
                _playServerId,
                request.StageType,
                request.StageId,
                ProtoCPacketExtensions.OfProto(createPayload));

            // Parse OnCreate reply payload (only when newly created)
            string? replyPayloadId = null;
            if (result.IsCreated && result.Payload.Payload.Length > 0)
            {
                var createReply = Proto.CreateStageReply.Parser.ParseFrom(
                    result.Payload.Payload.DataSpan);
                replyPayloadId = $"{createReply.ReceivedStageName}:{createReply.ReceivedMaxPlayers}";
            }

            _logger.LogInformation(
                "GetOrCreateStage result: Success={Success}, IsCreated={IsCreated}, StageId={StageId}, ReplyPayload={ReplyPayload}",
                result.Result,
                result.IsCreated,
                request.StageId,
                replyPayloadId);

            return Ok(new GetOrCreateStageResponse
            {
                Success = result.Result,
                IsCreated = result.IsCreated,
                StageId = request.StageId,
                ReplyPayloadId = replyPayloadId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating stage");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Assigns a user to a stage for post-authentication stage lookup.
    /// </summary>
    [HttpPost("stage-assignments")]
    public IActionResult AssignStage([FromBody] AssignStageRequest request)
    {
        StageAssignmentStore.Assign(request.UserId, request.StageId);
        return Ok(new { success = true, request.UserId, request.StageId });
    }
}

/// <summary>
/// Request DTO for creating a stage.
/// </summary>
public record CreateStageRequest
{
    /// <summary>
    /// The type of stage to create (e.g., "TestStage")
    /// </summary>
    public required string StageType { get; init; }

    /// <summary>
    /// The unique stage ID
    /// </summary>
    public required ushort StageId { get; init; }
}

/// <summary>
/// Response DTO for create stage operation.
/// </summary>
public record CreateStageResponse
{
    /// <summary>
    /// Indicates whether the stage was successfully created
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The stage ID that was created
    /// </summary>
    public required ushort StageId { get; init; }

    /// <summary>
    /// OnCreate reply payload info (format: "StageName:MaxPlayers")
    /// </summary>
    public string? ReplyPayloadId { get; init; }
}

/// <summary>
/// Request DTO for getting or creating a stage.
/// </summary>
public record GetOrCreateStageRequest
{
    /// <summary>
    /// The type of stage to get or create (e.g., "TestStage")
    /// </summary>
    public required string StageType { get; init; }

    /// <summary>
    /// The unique stage ID
    /// </summary>
    public required ushort StageId { get; init; }
}

/// <summary>
/// Response DTO for get or create stage operation.
/// </summary>
public record GetOrCreateStageResponse
{
    /// <summary>
    /// Indicates whether the operation was successful
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Indicates whether a new stage was created (true) or existing stage was returned (false)
    /// </summary>
    public required bool IsCreated { get; init; }

    /// <summary>
    /// The stage ID that was retrieved or created
    /// </summary>
    public required ushort StageId { get; init; }

    /// <summary>
    /// OnCreate reply payload info (format: "StageName:MaxPlayers", only when IsCreated=true)
    /// </summary>
    public string? ReplyPayloadId { get; init; }
}

public record AssignStageRequest
{
    public required string UserId { get; init; }
    public required long StageId { get; init; }
}
