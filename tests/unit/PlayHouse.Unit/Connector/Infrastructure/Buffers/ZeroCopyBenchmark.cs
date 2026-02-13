#nullable enable

using System;
using System.Buffers;
using System.Diagnostics;
using PlayHouse.Connector.Infrastructure.Buffers;
using Xunit;
using Xunit.Abstractions;

namespace PlayHouse.Unit.Connector.Infrastructure.Buffers;

/// <summary>
/// Zero-copy 리팩토링 성능 벤치마크
/// Before: 3번 복사 (byte[] → PooledBuffer → byte[])
/// After: 0번 복사 (RingBuffer Zero-copy)
/// </summary>
public class ZeroCopyBenchmark
{
    private readonly ITestOutputHelper _output;
    private const int Iterations = 10_000;
    private const int PacketSize = 256;

    public ZeroCopyBenchmark(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(DisplayName = "벤치마크 - Before: 3번 복사 방식", Skip = "수동 실행용 벤치마크 테스트")]
    public void Benchmark_OldWay_ThreeCopies()
    {
        _output.WriteLine("=== Before: 3번 복사 방식 ===");
        _output.WriteLine($"Iterations: {Iterations:N0}, PacketSize: {PacketSize}");

        // Warmup
        SimulateOldWay(1000);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memBefore = GC.GetTotalMemory(false);
        var sw = Stopwatch.StartNew();

        SimulateOldWay(Iterations);

        sw.Stop();
        var memAfter = GC.GetTotalMemory(false);

        _output.WriteLine($"Time: {sw.Elapsed.TotalMilliseconds:F3} ms");
        _output.WriteLine($"Memory Delta: {memAfter - memBefore:N0} bytes");
        _output.WriteLine($"Throughput: {Iterations / sw.Elapsed.TotalSeconds:N0} packets/sec");
    }

    [Fact(DisplayName = "벤치마크 - After: Zero-copy RingBuffer", Skip = "수동 실행용 벤치마크 테스트")]
    public void Benchmark_NewWay_ZeroCopy()
    {
        _output.WriteLine("=== After: Zero-copy RingBuffer ===");
        _output.WriteLine($"Iterations: {Iterations:N0}, PacketSize: {PacketSize}");

        // Warmup
        SimulateNewWay(1000);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memBefore = GC.GetTotalMemory(false);
        var sw = Stopwatch.StartNew();

        SimulateNewWay(Iterations);

        sw.Stop();
        var memAfter = GC.GetTotalMemory(false);

        _output.WriteLine($"Time: {sw.Elapsed.TotalMilliseconds:F3} ms");
        _output.WriteLine($"Memory Delta: {memAfter - memBefore:N0} bytes");
        _output.WriteLine($"Throughput: {Iterations / sw.Elapsed.TotalSeconds:N0} packets/sec");
    }

    [Fact(DisplayName = "벤치마크 - 비교 결과", Skip = "수동 실행용 벤치마크 테스트")]
    public void Benchmark_Comparison()
    {
        _output.WriteLine("=== 성능 비교 ===");
        _output.WriteLine($"Iterations: {Iterations:N0}, PacketSize: {PacketSize}");
        _output.WriteLine("");

        // Warmup
        SimulateOldWay(1000);
        SimulateNewWay(1000);

        // Old way
        GC.Collect();
        var oldMemBefore = GC.GetTotalMemory(true);
        var oldSw = Stopwatch.StartNew();
        SimulateOldWay(Iterations);
        oldSw.Stop();
        var oldMemAfter = GC.GetTotalMemory(false);

        // New way
        GC.Collect();
        var newMemBefore = GC.GetTotalMemory(true);
        var newSw = Stopwatch.StartNew();
        SimulateNewWay(Iterations);
        newSw.Stop();
        var newMemAfter = GC.GetTotalMemory(false);

        var oldTime = oldSw.Elapsed.TotalMilliseconds;
        var newTime = newSw.Elapsed.TotalMilliseconds;
        var oldMem = oldMemAfter - oldMemBefore;
        var newMem = newMemAfter - newMemBefore;

        _output.WriteLine($"Old (3 copies): {oldTime:F3} ms, Memory: {oldMem:N0} bytes");
        _output.WriteLine($"New (Zero-copy): {newTime:F3} ms, Memory: {newMem:N0} bytes");
        _output.WriteLine("");

        var timeImprovement = (oldTime - newTime) / oldTime * 100;
        var memImprovement = (double)(oldMem - newMem) / oldMem * 100;

        _output.WriteLine($"Time Improvement: {timeImprovement:F1}%");
        _output.WriteLine($"Memory Improvement: {memImprovement:F1}%");
    }

    /// <summary>
    /// 기존 방식 시뮬레이션 (3번 복사)
    /// </summary>
    private void SimulateOldWay(int iterations)
    {
        var socketBuffer = ArrayPool<byte>.Shared.Rent(65536);
        var receiveBuffer = new byte[65536];
        var receiveOffset = 0;

        try
        {
            for (int i = 0; i < iterations; i++)
            {
                // 소켓에서 데이터 수신 시뮬레이션
                var packetData = CreateTestPacket(i);

                // 복사 1: 소켓 버퍼 → new byte[]
                var data = new byte[packetData.Length];
                packetData.CopyTo(data, 0);

                // 복사 2: PooledBuffer.Append() 시뮬레이션
                data.CopyTo(receiveBuffer, receiveOffset);
                receiveOffset += data.Length;

                // 복사 3: .ToArray() 시뮬레이션
                var packetBytes = new byte[PacketSize];
                Array.Copy(receiveBuffer, 0, packetBytes, 0, PacketSize);

                // 패킷 처리 완료, 버퍼 정리
                Array.Copy(receiveBuffer, PacketSize, receiveBuffer, 0, receiveOffset - PacketSize);
                receiveOffset -= PacketSize;

                // 패킷 사용 (GC 대상)
                _ = packetBytes.Length;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(socketBuffer);
        }
    }

    /// <summary>
    /// 새로운 방식 시뮬레이션 (Zero-copy RingBuffer)
    /// </summary>
    private void SimulateNewWay(int iterations)
    {
        using var ringBuffer = new RingBuffer(65536);

        for (int i = 0; i < iterations; i++)
        {
            // 소켓에서 데이터 수신 시뮬레이션
            var packetData = CreateTestPacket(i);

            // Zero-copy: RingBuffer에 직접 Write
            ringBuffer.WriteBytes(packetData);

            // Zero-copy: Peek로 읽기 (복사 없음)
            var span = ringBuffer.Peek(PacketSize);

            // 패킷 파싱 (Span 직접 사용)
            _ = span.Length;

            // Consume (포인터만 이동)
            ringBuffer.Consume(PacketSize);
        }
    }

    private byte[] CreateTestPacket(int seq)
    {
        var packet = new byte[PacketSize];
        packet[0] = (byte)(seq & 0xFF);
        packet[1] = (byte)((seq >> 8) & 0xFF);
        return packet;
    }
}
