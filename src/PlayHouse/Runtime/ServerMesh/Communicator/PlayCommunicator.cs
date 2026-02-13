#nullable enable

using System;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ServerMesh.PlaySocket;
using Zlink;

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Facade for bidirectional server-to-server communication.
/// Internally uses XClientCommunicator + XServerCommunicator with integrated thread management.
/// </summary>
internal sealed class PlayCommunicator : ICommunicator, ICommunicateListener
{
    private readonly Context _serverContext;
    private readonly Context _clientContext;
    private readonly IPlaySocket _serverSocket;  // For Bind + Receive
    private readonly IPlaySocket _clientSocket;  // For Connect + Send
    private readonly XClientCommunicator _client;
    private readonly XServerCommunicator _server;
    private Thread? _serverThread;
    private Thread? _clientThread;
    private Action<string, RoutePacket>? _handler;
    private bool _disposed;

    /// <inheritdoc/>
    public string ServerId => _serverSocket.ServerId;

    /// <inheritdoc/>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Gets or sets the diagnostic level for performance testing.
    /// </summary>
    public int DiagnosticLevel
    {
        get => _server.DiagnosticLevel;
        set => _server.DiagnosticLevel = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayCommunicator"/> class.
    /// </summary>
    /// <param name="config">Server configuration.</param>
    public PlayCommunicator(ServerConfig config)
    {
        _serverContext = new Context();
        _clientContext = new Context();

        var socketConfig = PlaySocketConfig.FromServerConfig(config);
        var sharedReadyRouterIds = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        _serverSocket = new ZlinkPlaySocket(config.ServerId, _serverContext, socketConfig, sharedReadyRouterIds);
        _clientSocket = new ZlinkPlaySocket(config.ServerId, _clientContext, socketConfig, sharedReadyRouterIds);

        _server = new XServerCommunicator(_serverSocket, config.BindEndpoint);
        _client = new XClientCommunicator(_clientSocket);
    }

    /// <inheritdoc/>
    public void OnReceive(RoutePacket packet)
    {
        _handler?.Invoke(packet.From, packet);
    }

    /// <inheritdoc/>
    public void Bind(ICommunicateListener listener)
    {
        _handler = (senderServerId, packet) => listener.OnReceive(packet);
    }

    /// <inheritdoc/>
    public void Send(string targetServerId, RoutePacket packet) => _client.Send(targetServerId, packet);

    /// <inheritdoc/>
    public void Connect(string targetServerId, string address) => _client.Connect(targetServerId, address);

    /// <inheritdoc/>
    public void Disconnect(string targetServerId, string endpoint) => _client.Disconnect(targetServerId, endpoint);

    /// <inheritdoc/>
    public void Start()
    {
        if (IsRunning) return;

        // Bind is handled by XServerCommunicator
        _server.Bind(this);

        _serverThread = new Thread(() => _server.Communicate())
        {
            Name = "server:Communicator"
        };

        _clientThread = new Thread(() => _client.Communicate())
        {
            Name = "client:Communicator"
        };

        _serverThread.Start();
        _clientThread.Start();
        IsRunning = true;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _server.Stop();
        _client.Stop();
    }

    /// <summary>
    /// Waits for both communication threads to terminate.
    /// </summary>
    private void AwaitTermination()
    {
        _clientThread?.Join();
        _serverThread?.Join();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();                  // 1. _running = false
        _serverContext.Shutdown(); // 2. 블로킹 해제
        _clientContext.Shutdown(); // 2. 블로킹 해제
        AwaitTermination();        // 3. 스레드 종료 대기
        _serverSocket.Dispose();   // 4. 서버 소켓 정리
        _clientSocket.Dispose();   // 5. 클라이언트 소켓 정리
        _serverContext.Dispose();  // 6. Context 정리
        _clientContext.Dispose();  // 6. Context 정리
    }
}
