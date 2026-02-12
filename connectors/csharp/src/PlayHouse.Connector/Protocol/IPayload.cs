#nullable enable

using System;

namespace PlayHouse.Connector.Protocol;

/// <summary>
/// 페이로드 인터페이스 - 직렬화된 데이터
/// </summary>
public interface IPayload : IDisposable
{
    /// <summary>
    /// 페이로드 데이터 Span
    /// </summary>
    ReadOnlySpan<byte> DataSpan { get; }

    /// <summary>
    /// 페이로드 데이터 Memory
    /// </summary>
    ReadOnlyMemory<byte> DataMemory { get; }

    /// <summary>
    /// 페이로드 길이
    /// </summary>
    int Length => DataSpan.Length;
}
