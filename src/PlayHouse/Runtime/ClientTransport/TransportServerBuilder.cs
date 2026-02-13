#nullable enable

using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using PlayHouse.Runtime.ClientTransport.Zlink;

namespace PlayHouse.Runtime.ClientTransport;

/// <summary>
/// Builder for creating transport servers with various configurations.
/// </summary>
/// <remarks>
/// Supports TCP, WebSocket, and combinations with optional TLS.
/// </remarks>
public sealed class TransportServerBuilder
{
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger _logger;

    private readonly List<Action<CompositeTransportServer>> _serverFactories = new();
    private TransportOptions _options = new();

    /// <summary>
    /// Creates a new transport server builder.
    /// </summary>
    /// <param name="onMessage">Message received callback.</param>
    /// <param name="onDisconnect">Session disconnected callback.</param>
    /// <param name="logger">Logger instance.</param>
    public TransportServerBuilder(
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger logger)
    {
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _logger = logger;
    }

    /// <summary>
    /// Configures transport options.
    /// </summary>
    /// <param name="configure">Configuration action.</param>
    /// <returns>This builder for chaining.</returns>
    public TransportServerBuilder WithOptions(Action<TransportOptions> configure)
    {
        var options = new TransportOptions();
        configure(options);
        _options = options;
        return this;
    }

    /// <summary>
    /// Adds a TCP server.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="bindAddress">The address to bind to (default: any).</param>
    /// <returns>This builder for chaining.</returns>
    public TransportServerBuilder AddTcp(int port, string? bindAddress = null)
    {
        var endpoint = CreateEndpoint(port, bindAddress);

        _serverFactories.Add(composite =>
        {
            var server = new ZlinkStreamTransportServer(endpoint, _options, _onMessage, _onDisconnect, _logger);
            composite.Add(server);
        });

        return this;
    }

    /// <summary>
    /// Adds a TCP server with TLS.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="certificate">The server certificate.</param>
    /// <param name="bindAddress">The address to bind to (default: any).</param>
    /// <returns>This builder for chaining.</returns>
    public TransportServerBuilder AddTcpWithTls(int port, X509Certificate2 certificate, string? bindAddress = null)
    {
        var endpoint = CreateEndpoint(port, bindAddress);
        var tlsEndpoint = ZlinkStreamTransportServer.BuildEndpoint("tls", endpoint);

        _serverFactories.Add(composite =>
        {
            var server = new ZlinkStreamTransportServer(
                tlsEndpoint,
                _options,
                _onMessage,
                _onDisconnect,
                _logger,
                certificate);
            composite.Add(server);
        });

        return this;
    }

    /// <summary>
    /// Adds a WebSocket server.
    /// </summary>
    /// <param name="path">
    /// The WebSocket path (e.g., "/ws") or full endpoint (e.g., "ws://127.0.0.1:8080/ws").
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public TransportServerBuilder AddWebSocket(string path = "/ws")
    {
        var endpoint = ResolveWebSocketEndpoint(path, secure: false);

        _serverFactories.Add(composite =>
        {
            var server = new ZlinkStreamTransportServer(endpoint, _options, _onMessage, _onDisconnect, _logger);
            composite.Add(server);
        });

        return this;
    }

    /// <summary>
    /// Adds a WebSocket server with TLS.
    /// </summary>
    /// <param name="path">
    /// The WebSocket path (e.g., "/ws") or full endpoint (e.g., "wss://127.0.0.1:8443/ws").
    /// </param>
    /// <param name="certificate">The server certificate for TLS.</param>
    /// <returns>This builder for chaining.</returns>
    public TransportServerBuilder AddWebSocketWithTls(string path, X509Certificate2 certificate)
    {
        var endpoint = ResolveWebSocketEndpoint(path, secure: true);

        _serverFactories.Add(composite =>
        {
            var server = new ZlinkStreamTransportServer(
                endpoint,
                _options,
                _onMessage,
                _onDisconnect,
                _logger,
                certificate);
            composite.Add(server);
        });

        return this;
    }

    /// <summary>
    /// Builds the transport server.
    /// </summary>
    /// <returns>The configured transport server.</returns>
    public ITransportServer Build()
    {
        if (_serverFactories.Count == 0)
        {
            throw new InvalidOperationException("At least one transport must be configured");
        }

        if (_serverFactories.Count == 1)
        {
            // Single server, no need for composite
            var composite = new CompositeTransportServer(_logger);
            _serverFactories[0](composite);
            // Return the first (and only) server directly
            // But we still need the composite for unified interface
            return composite;
        }

        var result = new CompositeTransportServer(_logger);
        foreach (var factory in _serverFactories)
        {
            factory(result);
        }
        return result;
    }

    private static IPEndPoint CreateEndpoint(int port, string? bindAddress)
    {
        var address = string.IsNullOrEmpty(bindAddress)
            ? IPAddress.Any
            : IPAddress.Parse(bindAddress);

        return new IPEndPoint(address, port);
    }

    private static string ResolveWebSocketEndpoint(string value, bool secure)
    {
        if (TryGetEndpointScheme(value, out var scheme))
        {
            if (scheme is not ("ws" or "wss"))
            {
                throw new ArgumentException("WebSocket endpoint must use ws:// or wss:// scheme.", nameof(value));
            }

            if (secure && scheme == "ws")
            {
                return "wss://" + value.Trim()["ws://".Length..];
            }

            if (!secure && scheme == "wss")
            {
                return "ws://" + value.Trim()["wss://".Length..];
            }

            return value.Trim();
        }

        var normalizedPath = NormalizeWebSocketPath(value);
        return $"{(secure ? "wss" : "ws")}://*:*{normalizedPath}";
    }

    private static string NormalizeWebSocketPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        return path[0] == '/' ? path : "/" + path;
    }

    private static bool TryGetEndpointScheme(string value, out string scheme)
    {
        scheme = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var separator = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        scheme = trimmed[..separator].ToLowerInvariant();
        return true;
    }
}

/// <summary>
/// Transport type enumeration for configuration.
/// </summary>
[Flags]
public enum TransportType
{
    /// <summary>
    /// No transport configured.
    /// </summary>
    None = 0,

    /// <summary>
    /// TCP transport.
    /// </summary>
    Tcp = 1,

    /// <summary>
    /// TCP with TLS transport.
    /// </summary>
    TcpTls = 2,

    /// <summary>
    /// WebSocket transport (WS).
    /// </summary>
    WebSocket = 4,

    /// <summary>
    /// WebSocket with TLS transport (WSS).
    /// </summary>
    WebSocketTls = 8,

    /// <summary>
    /// All transport types.
    /// </summary>
    All = Tcp | TcpTls | WebSocket | WebSocketTls
}
