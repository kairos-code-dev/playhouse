using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Serilog;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Benchmark.Client;

/// <summary>
/// 벤치마크 시나리오를 실행합니다 (duration 기반).
/// </summary>
public class BenchmarkRunner(
    string serverHost,
    int serverPort,
    int ccu,
    int messageSize,
    BenchmarkMode mode,
    ClientMetricsCollector metricsCollector,
    int stageIdOffset = 0,
    string stageName = "BenchStage",
    int durationSeconds = 10,  // Test duration in seconds
    int inflight = 200,     // Maximum in-flight requests
    int warmup = 3)    // Warmup duration in seconds
{
    // 재사용 버퍼
    private readonly ByteString _requestPayload = CreatePayload(messageSize);

    // 연결 진행 카운터
    private int _connectedCount;
    private int _failedCount;
    private readonly object _progressLock = new();

    private void UpdateConnectionProgress()
    {
        lock (_progressLock)
        {
            var connected = _connectedCount;
            var failed = _failedCount;
            var failedStr = failed > 0 ? $", failed: {failed}" : "";
            Console.Write($"\r  Connecting: {connected:N0}/{ccu:N0}{failedStr}    ");
        }
    }

    /// <summary>
    /// 지정된 크기의 페이로드를 생성합니다. (압축 방지를 위해 패턴으로 채움)
    /// </summary>
    private static ByteString CreatePayload(int size)
    {
        if (size <= 0) return ByteString.Empty;

        var payload = new byte[size];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return ByteString.CopyFrom(payload);
    }

    public async Task RunAsync()
    {
        Log.Information("[{Time:HH:mm:ss}] Starting benchmark...", DateTime.Now);
        Log.Information("  Mode: {Mode}", mode);
        Log.Information("  Connections: {Connections:N0}", ccu);
        Log.Information("  Duration: {Duration:N0} seconds", durationSeconds);
        Log.Information("  Warmup: {WarmupDuration:N0} seconds", warmup);
        Log.Information("  Message size: {MessageSize:N0} bytes", messageSize);

        metricsCollector.Reset();

        // Phase 1: 모든 연결 + 인증 완료
        Log.Information("[{Time:HH:mm:ss}] Phase 1: Connecting and authenticating...", DateTime.Now);
        var connectors = new ClientConnector[ccu];
        var connectTasks = new Task[ccu];

        for (int i = 0; i < ccu; i++)
        {
            var connectionId = i;
            connectTasks[i] = Task.Run(async () => {
                var connector = await ConnectAndAuthenticateAsync(connectionId);
                if (connector != null)
                {
                    connectors[connectionId] = connector;
                }
            });
        }

        await Task.WhenAll(connectTasks);

        // 진행 상황 줄 마무리
        Console.WriteLine();
        Log.Information("  Phase 1 completed: {Connected:N0}/{Total:N0} connected (failed: {Failed:N0})",
            _connectedCount, ccu, _failedCount);

        if (_connectedCount == 0)
        {
            Log.Error("No ccu established. Aborting benchmark.");
            return;
        }

        // Phase 2: Warm-up (JIT compilation, 메모리 풀 예열)
        if (warmup > 0)
        {
            Log.Information("[{Time:HH:mm:ss}] Phase 2: Warming up ({WarmupDuration:N0} seconds)...", DateTime.Now, warmup);

            // 임시 메트릭 수집기 (warm-up 결과는 버림)
            var warmupMetrics = new ClientMetricsCollector();
            var warmupTasks = new List<Task>(_connectedCount);

            for (int i = 0; i < ccu; i++)
            {
                if (connectors[i] != null)
                {
                    var connector = connectors[i];
                    var connectionId = i;
                    warmupTasks.Add(Task.Run(async () => {
                        try
                        {
                            // 모드에 따라 warm-up 메시지 전송
                            if (mode == BenchmarkMode.RequestAsync)
                            {
                                await RunWarmupRequestAsync(connector, connectionId, warmupMetrics, warmup);
                            }
                            else if (mode == BenchmarkMode.RequestCallback)
                            {
                                await RunWarmupRequestCallback(connector, connectionId, warmupMetrics, warmup);
                            }
                            else if (mode == BenchmarkMode.Send)
                            {
                                await RunWarmupSend(connector, connectionId, warmupMetrics, warmup);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "[Connection {ConnectionId}] Warmup failed", connectionId);
                        }
                    }));
                }
            }

            await Task.WhenAll(warmupTasks);
            Log.Information("  Phase 2 completed: Warmup finished.");
        }

        // Phase 3: 모든 연결이 준비된 후 동시에 벤치마크 시작
        Log.Information("[{Time:HH:mm:ss}] Phase 3: Starting benchmark for all ccu...", DateTime.Now);

        // 실제 벤치마크 전에 메트릭 리셋
        metricsCollector.Reset();
        var benchmarkTasks = new List<Task>(_connectedCount);

        for (int i = 0; i < ccu; i++)
        {
            if (connectors[i] != null)
            {
                var connector = connectors[i];
                var connectionId = i;
                benchmarkTasks.Add(Task.Run(async () => {
                    try
                    {
                        // 모드에 따라 메시지 전송
                        if (mode == BenchmarkMode.RequestAsync)
                        {
                            await RunRequestAsyncMode(connector, connectionId);
                        }
                        else if (mode == BenchmarkMode.RequestCallback)
                        {
                            await RunRequestCallbackMode(connector, connectionId);
                        }
                        else if (mode == BenchmarkMode.Send)
                        {
                            await RunSendMode(connector, connectionId);
                        }
                    }
                    finally
                    {
                        connector.Disconnect();
                    }
                }));
            }
        }

        await Task.WhenAll(benchmarkTasks);

        Log.Information("[{Time:HH:mm:ss}] Phase 3 completed: Benchmark finished.", DateTime.Now);
    }

    /// <summary>
    /// Warm-up: RequestAsync 모드 (메트릭 수집하지만 결과는 버림)
    /// </summary>
    private async Task RunWarmupRequestAsync(ClientConnector connector, int connectionId, ClientMetricsCollector metrics, int duration)
    {
        var payloadBytes = _requestPayload.ToByteArray();
        var endTime = DateTime.UtcNow.AddSeconds(duration);

        const int WorkersPerConnection = 1;
        var workerCount = Math.Min(WorkersPerConnection, inflight);
        var workers = new Task[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            var workerId = i;
            workers[i] = Task.Run(async () =>
            {
                int requestCount = 0;
                while (DateTime.UtcNow < endTime)
                {
                    try
                    {
                        using var packet = new ClientPacket("EchoRequest", payloadBytes);
                        metrics.RecordSent();
                        using var response = await connector.RequestAsync(packet);
                        metrics.RecordReceived(0); // duration 0 (레이턴시 측정 안 함)
                    }
                    catch
                    {
                        // Warm-up 중 에러는 무시
                    }

                    if (workerId == 0 && ++requestCount % 100 == 0)
                    {
                        connector.MainThreadAction();
                    }
                }
            });
        }

        await Task.WhenAll(workers);
    }

    /// <summary>
    /// Warm-up: RequestCallback 모드
    /// </summary>
    private async Task RunWarmupRequestCallback(ClientConnector connector, int connectionId, ClientMetricsCollector metrics, int duration)
    {
        var endTime = DateTime.UtcNow.AddSeconds(duration);
        var payloadBytes = _requestPayload.ToByteArray();
        var inFlight = 0;

        while (DateTime.UtcNow < endTime)
        {
            connector.MainThreadAction();

            while (inFlight >= inflight)
            {
                connector.MainThreadAction();
                await Task.Yield();
            }

            var packet = new ClientPacket("EchoRequest", payloadBytes);
            metrics.RecordSent();
            Interlocked.Increment(ref inFlight);

            connector.Request(packet, response =>
            {
                metrics.RecordReceived(0);
                Interlocked.Decrement(ref inFlight);
                packet.Dispose();
            });
        }

        // 남은 요청 대기
        while (inFlight > 0)
        {
            connector.MainThreadAction();
            await Task.Yield();
        }
    }

    /// <summary>
    /// Warm-up: Send 모드
    /// </summary>
    private async Task RunWarmupSend(ClientConnector connector, int connectionId, ClientMetricsCollector metrics, int duration)
    {
        var endTime = DateTime.UtcNow.AddSeconds(duration);
        var payload = _requestPayload.ToByteArray();
        var inFlight = 0;

        void OnReceiveHandler(long stageId, string stageType, IPacket packet)
        {
            if (packet.MsgId == "SendReply")
            {
                metrics.RecordReceived(0);
                Interlocked.Decrement(ref inFlight);
            }
            packet.Dispose();
        }

        connector.OnReceive += OnReceiveHandler;

        try
        {
            while (DateTime.UtcNow < endTime)
            {
                connector.MainThreadAction();

                while (inFlight >= inflight)
                {
                    connector.MainThreadAction();
                    await Task.Yield();
                }

                using var packet = new ClientPacket("SendRequest", payload);
                metrics.RecordSent();
                Interlocked.Increment(ref inFlight);

                try
                {
                    connector.Send(packet);
                }
                catch
                {
                    Interlocked.Decrement(ref inFlight);
                }
            }

            // 남은 응답 대기
            while (inFlight > 0)
            {
                connector.MainThreadAction();
                await Task.Yield();
            }
        }
        finally
        {
            connector.OnReceive -= OnReceiveHandler;
        }
    }

    private async Task<ClientConnector?> ConnectAndAuthenticateAsync(int connectionId)
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        var stageId = 1000 + stageIdOffset + connectionId; // 각 연결마다 고유 StageId

        // 연결
        var connected = await connector.ConnectAsync(serverHost, serverPort);
        if (!connected)
        {
            Interlocked.Increment(ref _failedCount);
            UpdateConnectionProgress();
            return null;
        }

        // 인증
        try
        {
            using (var authPacket = ClientPacket.Empty("AuthenticateRequest"))
            {
                await connector.AuthenticateAsync(authPacket);
                Interlocked.Increment(ref _connectedCount);
                UpdateConnectionProgress();
                return connector;
            }
        }
        catch
        {
            Interlocked.Increment(ref _failedCount);
            UpdateConnectionProgress();
            connector.Disconnect();
            return null;
        }
    }

    private async Task RunRequestAsyncMode(ClientConnector connector, int connectionId)
    {
        // Echo 요청: raw payload 사용 (서버에서 zero-copy Move로 반환)
        var payloadBytes = _requestPayload.ToByteArray();
        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);
    
        // 연결당 Worker 수 계산:
        // - 기존: 요청마다 Task.Run() → 수만~수십만 Task 생성 (GC 압박, ThreadPool 경합)
        // - 개선: 연결당 고정된 Worker가 루프에서 요청 처리 → Task 개수 최소화
        // - 10000 연결 × 200 Worker = 2백만 Task (X) → 10000 연결 × 4 Worker = 4만 Task (O)
        const int WorkersPerConnection = 1;
        var workerCount = Math.Min(WorkersPerConnection, inflight);
        var workers = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            var workerId = i;
            workers[i] = Task.Run(async () =>
            {
                int requestCount = 0;
                while (DateTime.UtcNow < endTime)
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        using var packet = new ClientPacket("EchoRequest", payloadBytes);
                        metricsCollector.RecordSent();
                        using var response = await connector.RequestAsync(packet);
                        sw.Stop();
                        metricsCollector.RecordReceived(sw.ElapsedTicks);
                    }
                    catch (Exception ex)
                    {
                        // 예외 발생 시 로깅 후 계속 진행
                        Log.Error(ex, "[Connection {ConnectionId}] Worker {WorkerId} request failed",
                            connectionId, workerId);
                    }

                    // 주기적으로 MainThreadAction 호출 (첫 번째 Worker만, 100회당 1회)
                    // RequestAsync는 네트워크 스레드에서 처리되므로 필수는 아니지만
                    // Heartbeat 등 연결 관리를 위해 주기적으로 호출
                    if (workerId == 0 && ++requestCount % 100 == 0)
                    {
                        connector.MainThreadAction();
                    }
                }
            });
        }

        await Task.WhenAll(workers);
    }

    private async Task RunRequestCallbackMode(ClientConnector connector, int connectionId)
    {
        var receivedCount = 0;
        var sentCount = 0;
        var inFlight = 0;
        var timestamps = new ConcurrentDictionary<int, long>();

        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);

        // Echo 요청: raw payload 사용 (서버에서 zero-copy Move로 반환)
        var payloadBytes = _requestPayload.ToByteArray();

        // 메시지 전송 (Request with callback)
        var i = 0;
        while (DateTime.UtcNow < endTime)
        {
            connector.MainThreadAction();

            // In-flight 제한: 최대치에 도달하면 대기
            while (inFlight >= inflight)
            {
                connector.MainThreadAction(); // 대기 중에도 메시지 처리
                await Task.Yield(); // 콜백 실행 기회 제공
            }

            var packet = new ClientPacket("EchoRequest", payloadBytes);
            var seq = i;
            timestamps[seq] = Stopwatch.GetTimestamp();
            metricsCollector.RecordSent();
            Interlocked.Increment(ref sentCount);
            Interlocked.Increment(ref inFlight);

            connector.Request(packet, response =>
            {
                // 콜백에서 응답 처리
                if (timestamps.TryRemove(seq, out var startTicks))
                {
                    var elapsed = Stopwatch.GetTimestamp() - startTicks;
                    metricsCollector.RecordReceived(elapsed);
                }

                Interlocked.Increment(ref receivedCount);
                Interlocked.Decrement(ref inFlight);
                packet.Dispose();
            });

            i++;
        }

                    // 모든 응답 수신 대기 (남은 in-flight 요청)

                    while (inFlight > 0)

                    {

                        connector.MainThreadAction();

                        await Task.Yield();

                    }

        

                    if (receivedCount < sentCount)

        
        {
            Log.Warning("[Connection {ConnectionId}] Incomplete responses (received: {Received}/{Total})",
                connectionId, receivedCount, sentCount);
        }
    }

    /// <summary>
    /// Duration-based Send mode: Send 요청 → SendToClient 응답 (OnReceive 콜백)
    /// </summary>
    private async Task RunSendMode(ClientConnector connector, int connectionId)
    {
        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);
        // Echo 요청: raw payload 사용 (서버에서 zero-copy Move로 반환)
        var payload = _requestPayload.ToByteArray();
        var timestamps = new ConcurrentQueue<long>();
        var sentCount = 0;
        var receivedCount = 0;
        var inFlight = 0;

        // OnReceive 이벤트 핸들러 등록 (서버의 SendToClient 응답 수신)
        void OnReceiveHandler(long stageId, string stageType, IPacket packet)
        {
            if (packet.MsgId == "SendReply")
            {
                // FIFO 방식으로 타임스탬프 디큐
                if (timestamps.TryDequeue(out var startTicks))
                {
                    var elapsed = Stopwatch.GetTimestamp() - startTicks;
                    metricsCollector.RecordReceived(elapsed);
                    Interlocked.Increment(ref receivedCount);
                    Interlocked.Decrement(ref inFlight);
                }
            }
            packet.Dispose();
        }

        connector.OnReceive += OnReceiveHandler;

        try
        {
            while (DateTime.UtcNow < endTime)
            {
                connector.MainThreadAction();

                // In-flight 제한: 최대치에 도달하면 대기
                while (inFlight >= inflight)
                {
                    connector.MainThreadAction();
                    await Task.Yield(); // 콜백 실행 기회 제공
                }

                timestamps.Enqueue(Stopwatch.GetTimestamp());

                using var packet = new ClientPacket("SendRequest", payload);

                metricsCollector.RecordSent();
                Interlocked.Increment(ref sentCount);
                Interlocked.Increment(ref inFlight);

                try
                {
                    connector.Send(packet);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Connection {ConnectionId}] Send request failed", connectionId);
                    Interlocked.Decrement(ref inFlight); // 전송 실패 시 카운트 복구
                    timestamps.TryDequeue(out _); // 전송 실패 시 Queue에서도 제거
                }
            }

            // 모든 응답 수신 대기 (남은 in-flight 요청)
            while (inFlight > 0)
            {
                connector.MainThreadAction();
                await Task.Yield();
            }

            if (receivedCount < sentCount)
            {
                Log.Warning("[Connection {ConnectionId}] Incomplete responses (received: {Received}/{Total})",
                    connectionId, receivedCount, sentCount);
            }
        }
        finally
        {
            connector.OnReceive -= OnReceiveHandler;
        }
    }
}

public enum BenchmarkMode
{
    RequestAsync,     // await 기반 요청/응답 (Echo 테스트)
    RequestCallback,  // 콜백 기반 요청/응답 (Echo 테스트)
    Send              // Fire-and-forget (응답 없음, duration 기반)
}
