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
        var replyPacket = ProtoCPacketExtensions.OfProto(new SSEchoReply { Payload = request.Payload });

        // IsRequest 속성을 사용하여 응답 방식 결정
        if (link.IsRequest)
        {
            link.Reply(replyPacket);
        }
        else if (!string.IsNullOrEmpty(link.StageId))
        {
            link.SendToStage(link.FromNid, link.StageId, replyPacket);
        }

        return Task.CompletedTask;
    }
}
