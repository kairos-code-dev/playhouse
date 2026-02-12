#nullable enable

using System;
using System.Buffers;
using Google.Protobuf;

namespace PlayHouse.Connector.Protocol;

/// <summary>
/// 빈 페이로드
/// </summary>
public sealed class EmptyPayload : IPayload
{
    public static readonly EmptyPayload Instance = new();

    private EmptyPayload() { }

    public ReadOnlySpan<byte> DataSpan => ReadOnlySpan<byte>.Empty;

    public ReadOnlyMemory<byte> DataMemory => ReadOnlyMemory<byte>.Empty;

    public void Dispose()
    {
        // Nothing to dispose
    }
}

/// <summary>
/// 바이트 배열 페이로드
/// </summary>
public sealed class BytePayload : IPayload
{
    private readonly byte[] _data;

    public BytePayload(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public BytePayload(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
    }

    public ReadOnlySpan<byte> DataSpan => _data;

    public ReadOnlyMemory<byte> DataMemory => _data;

    public void Dispose()
    {
        // Nothing to dispose for byte arrays
    }
}

/// <summary>
/// Protobuf 메시지 페이로드
/// </summary>
public sealed class ProtoPayload : IPayload
{
    private readonly IMessage _proto;
    private byte[]? _cachedData;
    private bool _disposed;

    public ProtoPayload(IMessage proto)
    {
        _proto = proto ?? throw new ArgumentNullException(nameof(proto));
    }

    public ReadOnlySpan<byte> DataSpan
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ProtoPayload));
            }

            _cachedData ??= _proto.ToByteArray();
            return _cachedData;
        }
    }

    public ReadOnlyMemory<byte> DataMemory
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ProtoPayload));
            }

            _cachedData ??= _proto.ToByteArray();
            return _cachedData;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _cachedData = null;
    }
}

/// <summary>
/// ReadOnlyMemory 기반 페이로드 (Zero-Copy)
/// </summary>
public sealed class MemoryPayload : IPayload
{
    private readonly ReadOnlyMemory<byte> _data;

    public MemoryPayload(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    public ReadOnlySpan<byte> DataSpan => _data.Span;

    public ReadOnlyMemory<byte> DataMemory => _data;

    public void Dispose()
    {
        // Nothing to dispose
    }
}

/// <summary>
/// ArrayPool 기반 페이로드 (수신 경로용, 메모리 풀 사용)
/// </summary>
public sealed class ArrayPoolPayload : IPayload
{
    private byte[]? _rentedBuffer;
    private readonly int _actualSize;
    private bool _disposed;

    /// <summary>
    /// ArrayPool에서 대여한 버퍼로 페이로드 생성
    /// </summary>
    /// <param name="rentedBuffer">ArrayPool에서 대여한 버퍼</param>
    /// <param name="actualSize">실제 데이터 크기</param>
    public ArrayPoolPayload(byte[] rentedBuffer, int actualSize)
    {
        _rentedBuffer = rentedBuffer ?? throw new ArgumentNullException(nameof(rentedBuffer));
        _actualSize = actualSize;
    }

    public ReadOnlySpan<byte> DataSpan
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArrayPoolPayload));
            }
            return _rentedBuffer.AsSpan(0, _actualSize);
        }
    }

    public ReadOnlyMemory<byte> DataMemory
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArrayPoolPayload));
            }

            return _rentedBuffer.AsMemory(0, _actualSize);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_rentedBuffer != null)
        {
            // Fix #13: Clear buffer to avoid leaking sensitive data
            ArrayPool<byte>.Shared.Return(_rentedBuffer, clearArray: true);
            _rentedBuffer = null;
        }
    }
}
