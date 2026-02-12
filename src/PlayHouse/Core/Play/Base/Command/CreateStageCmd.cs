#nullable enable

using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Play.Base.Command;

/// <summary>
/// CreateStageReq 처리 명령.
/// </summary>
internal sealed class CreateStageCmd(ILogger logger) : IBaseStageCmd
{
    public async Task Execute(BaseStage baseStage, RoutePacket packet)
    {
        var req = CreateStageReq.Parser.ParseFrom(packet.Payload.DataSpan);
        logger.LogDebug("CreateStageReq: StageType={StageType}, PayloadId={PayloadId}",
            req.StageType, req.PayloadId);

        // Zero-copy: use ByteString.Memory directly
        using var contentPacket = CPacket.Of(req.PayloadId, new MemoryPayload(req.Payload.Memory));

        IPacket? replyPacket = null;
        try
        {
            var (success, createReplyPacket) = await baseStage.CreateStage(req.StageType, contentPacket);
            replyPacket = createReplyPacket;

            if (!success)
            {
                logger.LogWarning("Stage.OnCreate failed for StageId={StageId}", baseStage.StageId);
                var failRes = new CreateStageRes
                {
                    Result = false,
                    PayloadId = replyPacket?.MsgId ?? "",
                    Payload = replyPacket != null
                        ? ByteString.CopyFrom(replyPacket.Payload.DataSpan)
                        : ByteString.Empty
                };
                baseStage.Reply(CPacket.Of(failRes));
                return;
            }

            try
            {
                await baseStage.OnPostCreate();
            }
            catch (Exception ex)
            {
                baseStage.MarkAsNotCreated();
                logger.LogError(ex, "Stage.OnPostCreate failed for StageId={StageId}", baseStage.StageId);
                var failRes = new CreateStageRes
                {
                    Result = false,
                    PayloadId = replyPacket?.MsgId ?? "",
                    Payload = replyPacket != null
                        ? ByteString.CopyFrom(replyPacket.Payload.DataSpan)
                        : ByteString.Empty
                };
                baseStage.Reply(CPacket.Of(failRes));
                return;
            }

            var successRes = new CreateStageRes
            {
                Result = true,
                PayloadId = replyPacket?.MsgId ?? "",
                Payload = replyPacket != null
                    ? ByteString.CopyFrom(replyPacket.Payload.DataSpan)
                    : ByteString.Empty
            };
            baseStage.Reply(CPacket.Of(successRes));

            logger.LogInformation("Stage created: StageId={StageId}, StageType={StageType}",
                baseStage.StageId, req.StageType);
        }
        finally
        {
            replyPacket?.Dispose();
        }
    }
}
