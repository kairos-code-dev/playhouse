using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Zlink;
using Google.Protobuf;
using PlayHouse.Runtime.Proto;

public class Program
{
    [ThreadStatic]
    private static byte[]? _headerBuffer;

    public static void Main(string[] args)
    {
        const int warmUpCount = 100000;
        const int messageCount = 200000;
        const int messageSize = 1024;
        
        using var context = new Context();
        using var r1 = new Socket(context, SocketType.Router);
        using var r2 = new Socket(context, SocketType.Router);

        r1.SetOption(SocketOption.SndHwm, 0); r1.SetOption(SocketOption.RcvHwm, 0);
        r2.SetOption(SocketOption.SndHwm, 0); r2.SetOption(SocketOption.RcvHwm, 0);

        byte[] r2Id = Encoding.UTF8.GetBytes("R2");
        r2.SetOption(SocketOption.RoutingId, r2Id);
        
        r1.Bind("tcp://127.0.0.1:20050");
        r2.Connect("tcp://127.0.0.1:20050");
        Thread.Sleep(1000); 

        var header = new RouteHeader {
            MsgSeq = 1, ServiceId = 1, MsgId = "Benchmark", PayloadSize = messageSize
        };
        byte[] payload = new byte[messageSize];
        new Random(42).NextBytes(payload);

        // --- Phase 1: Warm-up ---
        Console.WriteLine("Starting Warm-up (3-Frame, Header Serialization)...");
        RunTest(r1, r2, r2Id, header, payload, warmUpCount);
        
        // --- Phase 2: Measured Test ---
        Console.WriteLine("Starting Measured Test...");
        var sw = Stopwatch.StartNew();
        RunTest(r1, r2, r2Id, header, payload, messageCount);
        sw.Stop();

        double tps = messageCount / sw.Elapsed.TotalSeconds;
        Console.WriteLine("=================================================");
        Console.WriteLine($"Result -> 3-Frame TPS: {tps:F0} msg/s");
        Console.WriteLine("=================================================");
    }

    private static void RunTest(Socket r1, Socket r2, byte[] r2Id, RouteHeader header, byte[] payload, int count)
    {
        _headerBuffer ??= new byte[128];
        using var cd = new CountdownEvent(1);
        
        // Receiver Thread (3-Frame Recv)
        var t = new Thread(() => {
            byte[] b1 = new byte[256]; // Id
            byte[] b2 = new byte[256]; // Header
            byte[] b3 = new byte[65536]; // Payload
            for (int n = 0; n < count; n++) {
                r2.Receive(b1);
                int hLen = r2.Receive(b2);
                r2.Receive(b3);
                
                // Simulate minimal parsing
                // var h = RouteHeader.Parser.ParseFrom(b2.AsSpan(0, hLen));
            }
            cd.Signal();
        });
        t.Start();

        // Sender (3-Frame Send)
        for (int i = 0; i < count; i++) {
            // 1. Identity
            r1.Send(r2Id, SendFlags.SendMore);
            
            // 2. Header (Actual Serialization)
            int hSize = header.CalculateSize();
            header.WriteTo(_headerBuffer.AsSpan(0, hSize));
            r1.Send(_headerBuffer.AsSpan(0, hSize), SendFlags.SendMore);
            
            // 3. Payload
            r1.Send(payload);
        }
        cd.Wait();
    }
}
