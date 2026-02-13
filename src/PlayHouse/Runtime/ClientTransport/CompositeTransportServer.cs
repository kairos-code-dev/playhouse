#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Runtime.ClientTransport.Tcp;
using PlayHouse.Runtime.ClientTransport.WebSocket;

namespace PlayHouse.Runtime.ClientTransport;

/// <summary>
/// Composite transport server that manages multiple transport servers.
/// </summary>
/// <remarks>
/// Allows running TCP and WebSocket servers simultaneously.
/// All sessions are accessible through a unified interface.
/// </remarks>
public sealed class CompositeTransportServer : ITransportServer, ITransportTcpPortProvider
{
    private readonly List<ITransportServer> _servers = new();
    private readonly ILogger _logger;
    private bool _disposed;

    public int SessionCount => _servers.Sum(s => s.SessionCount);

    /// <summary>
    /// Creates a new composite transport server.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public CompositeTransportServer(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds a transport server to this composite.
    /// </summary>
    /// <param name="server">The server to add.</param>
    /// <returns>This instance for chaining.</returns>
    public CompositeTransportServer Add(ITransportServer server)
    {
        _servers.Add(server);
        return this;
    }

    /// <summary>
    /// Gets all WebSocket servers in this composite.
    /// </summary>
    public IEnumerable<WebSocketTransportServer> WebSocketServers =>
        _servers.OfType<WebSocketTransportServer>();

    /// <summary>
    /// Gets all TCP servers in this composite.
    /// </summary>
    public IEnumerable<TcpTransportServer> TcpServers =>
        _servers.OfType<TcpTransportServer>();

    int ITransportTcpPortProvider.ActualTcpPort =>
        _servers
            .OfType<ITransportTcpPortProvider>()
            .Select(s => s.ActualTcpPort)
            .FirstOrDefault(p => p > 0);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting composite transport server with {Count} servers", _servers.Count);

        foreach (var server in _servers)
        {
            await server.StartAsync(cancellationToken);
        }
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping composite transport server");

        foreach (var server in _servers)
        {
            await server.StopAsync();
        }
    }

    public ITransportSession? GetSession(long sessionId)
    {
        foreach (var server in _servers)
        {
            var session = server.GetSession(sessionId);
            if (session != null) return session;
        }
        return null;
    }

    public async ValueTask DisconnectSessionAsync(long sessionId)
    {
        foreach (var server in _servers)
        {
            await server.DisconnectSessionAsync(sessionId);
        }
    }

    public async Task DisconnectAllSessionsAsync()
    {
        var tasks = _servers.Select(s => s.DisconnectAllSessionsAsync());
        await Task.WhenAll(tasks);
    }

    public IEnumerable<ITransportSession> GetAllSessions()
    {
        return _servers.SelectMany(s => s.GetAllSessions());
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }
    }
}
