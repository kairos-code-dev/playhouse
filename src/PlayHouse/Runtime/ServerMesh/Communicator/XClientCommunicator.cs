#nullable enable

using System.Collections.Concurrent;
using System.Threading.Channels;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ServerMesh.PlaySocket;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Optimized client communicator using System.Threading.Channels to avoid delegate allocations.
/// Maintains transport socket thread-safety by ensuring send operations happen on a single dedicated thread.
/// </summary>
internal sealed class XClientCommunicator : IClientCommunicator
{
    private readonly IPlaySocket _socket;
    private readonly Channel<SendRequest> _sendChannel;
    private readonly ConcurrentDictionary<string, byte> _connected = new();
    private readonly CancellationTokenSource _cts = new();

    private readonly struct SendRequest
    {
        public readonly string TargetServerId;
        public readonly RoutePacket Packet;

        public SendRequest(string targetServerId, RoutePacket packet)
        {
            TargetServerId = targetServerId;
            Packet = packet;
        }
    }

    public string ServerId => _socket.ServerId;

    public XClientCommunicator(IPlaySocket socket)
    {
        _socket = socket;
        // SingleReader optimization: Only the Communicate() loop reads from this channel.
        _sendChannel = Channel.CreateUnbounded<SendRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Send(string targetServerId, RoutePacket packet)
    {
        // Zero-allocation queueing: Just writing a struct to the channel.
        if (!_sendChannel.Writer.TryWrite(new SendRequest(targetServerId, packet)))
        {
            packet.Dispose();
        }
    }

    public void Connect(string targetServerId, string address)
    {
        if (!_connected.TryAdd(address, 0)) return;
        _socket.MarkRouterIdNotReady(targetServerId);
        _socket.Connect(address, targetServerId);
    }

    public void Disconnect(string targetServerId, string address)
    {
        if (!_connected.TryRemove(address, out _)) return;
        _socket.MarkRouterIdNotReady(targetServerId);
        _socket.Disconnect(address);
    }

    /// <summary>
    /// Processes queued messages. Called by dedicated MessageLoop thread.
    /// </summary>
    public void Communicate()
    {
        var reader = _sendChannel.Reader;
        var pending = new List<SendRequest>(64);
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                // Wait for data without spinning
                if (reader.TryRead(out var request))
                {
                    // Batching: Process all available messages before yielding.
                    if (_socket.IsRouterIdReady(request.TargetServerId))
                    {
                        _socket.Send(request.TargetServerId, request.Packet);
                    }
                    else
                    {
                        pending.Add(request);
                    }

                    while (reader.TryRead(out request))
                    {
                        if (_socket.IsRouterIdReady(request.TargetServerId))
                        {
                            _socket.Send(request.TargetServerId, request.Packet);
                        }
                        else
                        {
                            pending.Add(request);
                        }
                    }

                    if (pending.Count > 0)
                    {
                        foreach (var deferred in pending)
                        {
                            if (!_sendChannel.Writer.TryWrite(deferred))
                            {
                                deferred.Packet.Dispose();
                            }
                        }

                        pending.Clear();
                        Thread.Sleep(1);
                    }
                }
                else
                {
                    // Block asynchronously until data arrives
                    if (!reader.WaitToReadAsync(_cts.Token).AsTask().GetAwaiter().GetResult())
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[XClientCommunicator] Send loop error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        _sendChannel.Writer.TryComplete();
    }
}
