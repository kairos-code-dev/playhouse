#nullable enable

using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ServerMesh.PlaySocket;
using Zlink;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Server-side communicator for receiving messages from clients/servers.
/// Thread management handled by MessageLoop.
/// </summary>
internal sealed class XServerCommunicator : IServerCommunicator
{
    private const int ZlinkEterm = 156384765;

    private readonly IPlaySocket _socket;
    private readonly string _bindEndpoint;
    private ICommunicateListener? _listener;
    private volatile bool _running = true;

    /// <summary>
    /// Gets or sets the diagnostic level.
    /// -1: Normal, 0: Raw Echo, 1: Header Parse Echo
    /// </summary>
    public int DiagnosticLevel { get; set; } = -1;

    /// <inheritdoc/>
    public string ServerId => _socket.ServerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XServerCommunicator"/> class.
    /// </summary>
    /// <param name="socket">The underlying socket.</param>
    /// <param name="bindEndpoint">Endpoint to bind to.</param>
    public XServerCommunicator(IPlaySocket socket, string bindEndpoint)
    {
        _socket = socket;
        _bindEndpoint = bindEndpoint;
    }

    /// <summary>
    /// Binds the socket and registers message listener.
    /// </summary>
    /// <param name="listener">Message listener to handle received packets.</param>
    public void Bind(ICommunicateListener listener)
    {
        _listener = listener;
        _socket.Bind(_bindEndpoint);
    }

    /// <summary>
    /// Processes incoming messages. Called by MessageLoop thread.
    /// </summary>
    public void Communicate()
    {
        while (_running)
        {
            try
            {
                if (DiagnosticLevel >= 0)
                {
                    _socket.ReceiveDirect(DiagnosticLevel);
                    continue;
                }

                // Receive with 10ms timeout - no additional sleep needed as timeout provides waiting
                var packet = _socket.Receive();
                if (packet != null)
                {
                    _listener?.OnReceive(packet);
                }
            }
            catch (ZlinkException ex) when (!_running || ex.Errno == ZlinkEterm)
            {
                // Context terminated - 정상 종료
                break;
            }
            catch (Exception ex)
            {
                // Log receive errors for debugging
                Console.Error.WriteLine($"[XServerCommunicator] Receive error on server {ServerId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Stops the receive loop.
    /// </summary>
    public void Stop()
    {
        _running = false;
    }
}
