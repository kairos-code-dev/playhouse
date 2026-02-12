#nullable enable

using System;
using Google.Protobuf;

namespace PlayHouse.Connector.Protocol;

/// <summary>
/// IPacket 구현 - 메시지 ID와 페이로드를 포함하는 패킷
/// </summary>
public sealed class Packet : IPacket
{
    private bool _disposed;

    /// <summary>
    /// 메시지 ID와 페이로드로 패킷 생성
    /// </summary>
    /// <param name="msgId">메시지 ID</param>
    /// <param name="payload">페이로드</param>
    public Packet(string msgId, IPayload payload)
    {
        MsgId = msgId ?? throw new ArgumentNullException(nameof(msgId));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    /// <summary>
    /// Protobuf 메시지로 패킷 생성
    /// </summary>
    /// <param name="message">Protobuf 메시지</param>
    public Packet(IMessage message)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        MsgId = message.Descriptor.Name;
        Payload = new ProtoPayload(message);
    }

    /// <summary>
    /// 메시지 ID와 바이트 배열로 패킷 생성
    /// </summary>
    /// <param name="msgId">메시지 ID</param>
    /// <param name="data">바이트 배열 데이터</param>
    public Packet(string msgId, byte[] data)
    {
        MsgId = msgId ?? throw new ArgumentNullException(nameof(msgId));
        Payload = new BytePayload(data);
    }

    /// <summary>
    /// 메시지 ID와 ReadOnlySpan으로 패킷 생성 (복사)
    /// </summary>
    /// <param name="msgId">메시지 ID</param>
    /// <param name="data">바이트 Span 데이터</param>
    public Packet(string msgId, ReadOnlySpan<byte> data)
    {
        MsgId = msgId ?? throw new ArgumentNullException(nameof(msgId));
        Payload = new BytePayload(data);
    }

    /// <summary>
    /// 메시지 ID와 ReadOnlyMemory로 패킷 생성 (Zero-copy)
    /// </summary>
    /// <param name="msgId">메시지 ID</param>
    /// <param name="data">바이트 Memory 데이터</param>
    public Packet(string msgId, ReadOnlyMemory<byte> data)
    {
        MsgId = msgId ?? throw new ArgumentNullException(nameof(msgId));
        Payload = new MemoryPayload(data);
    }

    /// <inheritdoc/>
    public string MsgId { get; }

    /// <inheritdoc/>
    public IPayload Payload { get; }

    /// <summary>
    /// 빈 패킷 생성
    /// </summary>
    /// <param name="msgId">메시지 ID</param>
    /// <returns>빈 패킷</returns>
    public static Packet Empty(string msgId)
    {
        return new Packet(msgId, EmptyPayload.Instance);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Payload.Dispose();
    }
}
