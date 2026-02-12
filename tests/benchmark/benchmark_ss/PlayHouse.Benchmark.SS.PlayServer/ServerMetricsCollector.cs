using System.Collections.Concurrent;
using System.Diagnostics;

namespace PlayHouse.Benchmark.SS.PlayServer;

/// <summary>
/// 서버 성능 지표 수집기.
/// </summary>
public class ServerMetricsCollector
{
    public static ServerMetricsCollector Instance { get; } = new();

    private long _processedMessages;
    private long _totalBytes;
    private long _generation;
    private readonly ConcurrentQueue<long> _latencies = new();
    private long _startTicks = Stopwatch.GetTimestamp();
    
    // CPU 측정용
    private TimeSpan _startCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
    private DateTime _startTime = DateTime.UtcNow;

    public long Generation => Interlocked.Read(ref _generation);

    public void RecordMessage(long latencyTicks, long bytes)
    {
        Interlocked.Increment(ref _processedMessages);
        Interlocked.Add(ref _totalBytes, bytes);
        if (_latencies.Count < 1000000)
        {
            _latencies.Enqueue(latencyTicks);
        }
    }

    public void Reset()
    {
        Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _processedMessages, 0);
        Interlocked.Exchange(ref _totalBytes, 0);
        _latencies.Clear();
        _startTicks = Stopwatch.GetTimestamp();
        _startCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
        _startTime = DateTime.UtcNow;
    }

    public MetricsReport GetMetrics()
    {
        var nowTicks = Stopwatch.GetTimestamp();
        var elapsedSec = (double)(nowTicks - _startTicks) / Stopwatch.Frequency;
        var processed = Interlocked.Read(ref _processedMessages);
        var bytes = Interlocked.Read(ref _totalBytes);

        var sortedLatencies = _latencies.Select(t => (double)t * 1000 / Stopwatch.Frequency).OrderBy(t => t).ToList();
        
        // CPU 사용률 계산
        var endCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
        var endTime = DateTime.UtcNow;
        var cpuUsedMs = (endCpuTime - _startCpuTime).TotalMilliseconds;
        var wallElapsedMs = (endTime - _startTime).TotalMilliseconds;
        var cpuUsage = (cpuUsedMs / (wallElapsedMs * Environment.ProcessorCount)) * 100.0;

        return new MetricsReport
        {
            ProcessedMessages = processed,
            ThroughputMessagesPerSec = elapsedSec > 0 ? processed / elapsedSec : 0,
            ThroughputMbPerSec = elapsedSec > 0 ? (bytes / 1024.0 / 1024.0) / elapsedSec : 0,
            LatencyMeanMs = sortedLatencies.Count > 0 ? sortedLatencies.Average() : 0,
            LatencyP50Ms = GetPercentile(sortedLatencies, 0.5),
            LatencyP95Ms = GetPercentile(sortedLatencies, 0.95),
            LatencyP99Ms = GetPercentile(sortedLatencies, 0.99),
            MemoryAllocatedMb = GC.GetTotalMemory(false) / 1024.0 / 1024.0,
            GcGen0Count = GC.CollectionCount(0),
            GcGen1Count = GC.CollectionCount(1),
            GcGen2Count = GC.CollectionCount(2),
            CpuUsagePercent = cpuUsage
        };
    }

    private double GetPercentile(List<double> sortedData, double percentile)
    {
        if (sortedData.Count == 0) return 0;
        int index = (int)(sortedData.Count * percentile);
        return sortedData[Math.Min(index, sortedData.Count - 1)];
    }
}

public record MetricsReport
{
    public long ProcessedMessages { get; init; }
    public double ThroughputMessagesPerSec { get; init; }
    public double ThroughputMbPerSec { get; init; }
    public double LatencyMeanMs { get; init; }
    public double LatencyP50Ms { get; init; }
    public double LatencyP95Ms { get; init; }
    public double LatencyP99Ms { get; init; }
    public double MemoryAllocatedMb { get; init; }
    public int GcGen0Count { get; init; }
    public int GcGen1Count { get; init; }
    public int GcGen2Count { get; init; }
    public double CpuUsagePercent { get; init; }
}
