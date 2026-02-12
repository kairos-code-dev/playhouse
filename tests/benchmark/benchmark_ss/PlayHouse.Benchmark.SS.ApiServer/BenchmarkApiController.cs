#nullable enable

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Core.Shared;
using PlayHouse.Extensions.Proto;

namespace PlayHouse.Benchmark.SS.ApiServer;

public class BenchmarkApiController : IApiController
{
    private static long _echoRequestLogCount;
    private static long _echoRequestTotalCount;
    private static long _echoRequestReplyCount;
    private static long _echoRequestSendToStageCount;
    private static long _echoRequestDroppedCount;
    private readonly ILogger<BenchmarkApiController> _logger;

    public BenchmarkApiController(ILogger<BenchmarkApiController>? logger = null)
    {
        _logger = logger ?? NullLogger<BenchmarkApiController>.Instance;
    }

    public void Handles(IHandlerRegister register)
    {
        register.Add(nameof(CreateStageRequest), HandleCreateStage);
        register.Add(nameof(SSEchoRequest), HandleSSEchoRequest);
    }

    private async Task HandleCreateStage(IPacket packet, IApiLink link)
    {
        var request = CreateStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        try {
            var result = await link.CreateStage(request.PlayNid, request.StageType, request.StageId, CPacket.Empty("CreateStage"));
            link.Reply(ProtoCPacketExtensions.OfProto(new CreateStageReply { Success = result.Result, StageId = request.StageId, PlayNid = request.PlayNid }));
        } catch (Exception ex) {
            link.Reply(ProtoCPacketExtensions.OfProto(new CreateStageReply { Success = false, ErrorMessage = ex.Message }));
        }
    }

    private Task HandleSSEchoRequest(IPacket packet, IApiLink link)
    {
        var request = SSEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var totalCount = Interlocked.Increment(ref _echoRequestTotalCount);
        var logCount = Interlocked.Increment(ref _echoRequestLogCount);
        if (logCount <= 20)
        {
            _logger.LogInformation(
                "SSEchoRequest recv: isRequest={IsRequest}, from={FromNid}, stageId={StageId}, payload={PayloadSize}",
                link.IsRequest,
                link.FromNid,
                string.IsNullOrEmpty(link.StageId) ? "<empty>" : link.StageId,
                request.Payload.Length);
        }

        var replyPacket = ProtoCPacketExtensions.OfProto(new SSEchoReply { Payload = request.Payload });

        // IsRequest 속성을 사용하여 응답 방식 결정
        if (link.IsRequest)
        {
            Interlocked.Increment(ref _echoRequestReplyCount);
            link.Reply(replyPacket);
        }
        else if (!string.IsNullOrEmpty(link.StageId))
        {
            Interlocked.Increment(ref _echoRequestSendToStageCount);
            link.SendToStage(link.FromNid, link.StageId, replyPacket);
        }
        else if (logCount <= 20)
        {
            Interlocked.Increment(ref _echoRequestDroppedCount);
            _logger.LogWarning("SSEchoRequest dropped: empty StageId for from={FromNid}", link.FromNid);
        }
        else
        {
            Interlocked.Increment(ref _echoRequestDroppedCount);
        }

        if (totalCount % 5000 == 0)
        {
            _logger.LogInformation(
                "SSEchoRequest summary: total={Total}, reply={Reply}, sendToStage={SendToStage}, dropped={Dropped}",
                totalCount,
                Interlocked.Read(ref _echoRequestReplyCount),
                Interlocked.Read(ref _echoRequestSendToStageCount),
                Interlocked.Read(ref _echoRequestDroppedCount));
        }

        return Task.CompletedTask;
    }
}
