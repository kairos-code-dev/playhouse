#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace PlayHouse.Unit.Connector.Infrastructure.Buffers;

/// <summary>
/// 송신 경로 인코딩 벤치마크.
/// Legacy: 헤더+페이로드를 단일 버퍼에 복사
/// Segmented: 헤더만 인코딩하고 payload는 별도 전송(복사 없음)
/// </summary>
public sealed class SendPathEncodingBenchmark
{
    private readonly ITestOutputHelper _output;
    private const string MsgId = "Benchmark.EchoRequest";

    private static readonly (int payloadSize, int iterations)[] Scenarios =
    {
        (256, 100_000),
        (4 * 1024, 30_000),
        (64 * 1024, 3_000)
    };

    public SendPathEncodingBenchmark(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(DisplayName = "벤치마크 - 송신 인코딩 경로 비교 (Legacy Copy vs Segmented)")]
    public void Benchmark_SendPathEncoding_Comparison()
    {
        _output.WriteLine("=== Send Path Encoding Benchmark ===");
        _output.WriteLine("Legacy: 단일 버퍼(헤더+페이로드) 복사");
        _output.WriteLine("Segmented: 헤더만 인코딩 + payload 분리 전송");
        _output.WriteLine("");

        foreach (var scenario in Scenarios)
        {
            var payload = new byte[scenario.payloadSize];
            new Random(42).NextBytes(payload);

            // JIT warmup
            RunLegacyEncoding(payload, 1000);
            RunSegmentedEncoding(payload, 1000);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var legacy = RunLegacyEncoding(payload, scenario.iterations);
            var segmented = RunSegmentedEncoding(payload, scenario.iterations);

            _output.WriteLine($"[Payload: {scenario.payloadSize:N0} bytes, Iterations: {scenario.iterations:N0}]");
            _output.WriteLine($"Legacy    - Time: {legacy.elapsedMs:F3} ms, Alloc(Thread): {legacy.allocatedBytes:N0} B, Copied: {legacy.copiedBytes / (1024.0 * 1024.0):F2} MB");
            _output.WriteLine($"Segmented - Time: {segmented.elapsedMs:F3} ms, Alloc(Thread): {segmented.allocatedBytes:N0} B, Copied: {segmented.copiedBytes / (1024.0 * 1024.0):F2} MB");

            var timeImprovement = legacy.elapsedMs > 0
                ? (legacy.elapsedMs - segmented.elapsedMs) / legacy.elapsedMs * 100.0
                : 0.0;

            var copiedReduction = legacy.copiedBytes > 0
                ? (legacy.copiedBytes - segmented.copiedBytes) / (double)legacy.copiedBytes * 100.0
                : 0.0;

            _output.WriteLine($"Improvement - Time: {timeImprovement:F1}%, Copied bytes: {copiedReduction:F1}%");
            _output.WriteLine("");
        }
    }

    private static (double elapsedMs, long allocatedBytes, long copiedBytes) RunLegacyEncoding(byte[] payload, int iterations)
    {
        long copiedBytes = 0;
        var msgSeq = (ushort)1;
        int sink = 0;

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var msgIdByteCount = Encoding.UTF8.GetByteCount(MsgId);
            var contentSize = 1 + msgIdByteCount + 2 + payload.Length;
            var totalSize = 4 + contentSize;
            var buffer = ArrayPool<byte>.Shared.Rent(totalSize);

            try
            {
                int offset = 0;

                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), contentSize);
                offset += 4;

                buffer[offset++] = (byte)msgIdByteCount;
                Encoding.UTF8.GetBytes(MsgId, buffer.AsSpan(offset, msgIdByteCount));
                offset += msgIdByteCount;

                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), msgSeq);
                offset += 2;

                payload.AsSpan().CopyTo(buffer.AsSpan(offset));
                copiedBytes += payload.Length;

                // dead-code elimination 방지용
                sink ^= buffer[0];
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        sw.Stop();
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        // sink 사용 강제
        GC.KeepAlive(sink);
        return (sw.Elapsed.TotalMilliseconds, allocatedAfter - allocatedBefore, copiedBytes);
    }

    private static (double elapsedMs, long allocatedBytes, long copiedBytes) RunSegmentedEncoding(byte[] payload, int iterations)
    {
        long copiedBytes = 0;
        var msgSeq = (ushort)1;
        int sink = 0;

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var msgIdByteCount = Encoding.UTF8.GetByteCount(MsgId);
            var contentSize = 1 + msgIdByteCount + 2 + payload.Length;
            var headerLength = 4 + 1 + msgIdByteCount + 2;
            var headerBuffer = ArrayPool<byte>.Shared.Rent(headerLength);

            try
            {
                int offset = 0;

                BinaryPrimitives.WriteInt32LittleEndian(headerBuffer.AsSpan(offset), contentSize);
                offset += 4;

                headerBuffer[offset++] = (byte)msgIdByteCount;
                Encoding.UTF8.GetBytes(MsgId, headerBuffer.AsSpan(offset, msgIdByteCount));
                offset += msgIdByteCount;

                BinaryPrimitives.WriteUInt16LittleEndian(headerBuffer.AsSpan(offset), msgSeq);

                // dead-code elimination 방지용
                sink ^= headerBuffer[0] ^ payload[0];
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(headerBuffer, clearArray: true);
            }
        }

        sw.Stop();
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        GC.KeepAlive(sink);
        return (sw.Elapsed.TotalMilliseconds, allocatedAfter - allocatedBefore, copiedBytes);
    }
}
