#nullable enable

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Zlink;
using ZSocket = Zlink.Socket;

namespace PlayHouse.Runtime.ClientTransport.Zlink;

/// <summary>
/// Client transport server backed by a Zlink STREAM socket.
/// </summary>
public sealed class ZlinkStreamTransportServer : ITransportServer, ITransportTcpPortProvider
{
    private const int AgainErrno = 11;

    private readonly string _configuredEndpoint;
    private readonly int _configuredPort;
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger _logger;

    private readonly ConcurrentDictionary<long, ZlinkStreamTransportSession> _sessionsById = new();
    private readonly ConcurrentDictionary<string, ZlinkStreamTransportSession> _sessionsByRoutingKey = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _sendLock = new();

    private readonly Context _context;
    private readonly ZSocket _socket;
    private readonly TlsMaterialPaths? _tlsMaterialPaths;
    private Task? _receiveTask;
    private bool _disposed;
    private long _nextSessionId;
    private string _boundEndpoint;

    private sealed record TlsMaterialPaths(string CertPath, string KeyPath);

    public ZlinkStreamTransportServer(
        IPEndPoint endpoint,
        TransportOptions options,
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger logger) : this(
            BuildEndpoint("tcp", endpoint),
            options,
            onMessage,
            onDisconnect,
            logger)
    {
    }

    public ZlinkStreamTransportServer(
        string endpoint,
        TransportOptions options,
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger logger,
        X509Certificate2? tlsCertificate = null)
    {
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _logger = logger;

        _configuredEndpoint = ValidateEndpoint(endpoint, out var scheme);
        _configuredPort = TryExtractPort(_configuredEndpoint) ?? 0;

        _context = new Context();
        _context.SetOption(ContextOption.IoThreads, 1);
        _socket = new ZSocket(_context, SocketType.Stream);
        ConfigureSocket(_socket, options);
        ConfigureTlsIfNeeded(_socket, scheme, tlsCertificate, out _tlsMaterialPaths);
        _boundEndpoint = _configuredEndpoint;
    }

    public int SessionCount => _sessionsById.Count;

    /// <summary>
    /// Gets the actual bound TCP port.
    /// </summary>
    public int ActualTcpPort
    {
        get
        {
            var boundPort = TryGetBoundPort();
            if (boundPort is > 0)
            {
                return boundPort.Value;
            }

            return _configuredPort;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        _socket.Bind(_configuredEndpoint);
        _boundEndpoint = TryGetLastEndpoint() ?? _boundEndpoint;

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("Zlink STREAM server started on {Endpoint}", _boundEndpoint);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogInformation("Stopping Zlink STREAM server on {Endpoint}", _boundEndpoint);

        _cts.Cancel();
        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        await DisconnectAllSessionsAsync();
    }

    public ITransportSession? GetSession(long sessionId)
    {
        _sessionsById.TryGetValue(sessionId, out var session);
        return session;
    }

    public async ValueTask DisconnectSessionAsync(long sessionId)
    {
        if (_sessionsById.TryGetValue(sessionId, out var session))
        {
            await session.DisconnectAsync();
        }
    }

    public async Task DisconnectAllSessionsAsync()
    {
        var tasks = _sessionsById.Values.Select(s => s.DisconnectAsync().AsTask());
        await Task.WhenAll(tasks);
        _sessionsById.Clear();
        _sessionsByRoutingKey.Clear();
    }

    public IEnumerable<ITransportSession> GetAllSessions()
    {
        return _sessionsById.Values;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();

        try
        {
            _socket.Dispose();
            _context.Dispose();
            _cts.Dispose();
        }
        finally
        {
            CleanupTlsMaterial();
        }
    }

    internal void SendFrame(ReadOnlySpan<byte> routingId, ReadOnlySpan<byte> payload)
    {
        EnsureNotDisposed();

        lock (_sendLock)
        {
            _socket.Send(routingId, SendFlags.SendMore);
            _socket.Send(payload);
        }
    }

    internal void TryDisconnect(ReadOnlySpan<byte> routingId)
    {
        try
        {
            SendFrame(routingId, ReadOnlySpan<byte>.Empty);
        }
        catch (ZlinkException ex) when (ex.Errno == AgainErrno)
        {
            // Best effort on disconnect signal.
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            byte[] routingId;
            bool hasMore;

            if (!TryReceiveFrame(out routingId, out hasMore))
            {
                await Task.Yield();
                continue;
            }

            if (!hasMore)
            {
                _logger.LogWarning("Dropped malformed STREAM frame without payload part");
                continue;
            }

            byte[] payload;
            bool unexpectedMore;
            if (!TryReceiveFrame(out payload, out unexpectedMore))
            {
                await Task.Yield();
                continue;
            }

            if (unexpectedMore)
            {
                DrainUnexpectedFrames();
                _logger.LogWarning("Dropped malformed STREAM multipart message with extra frames");
                continue;
            }

            HandleIncoming(routingId, payload);
        }
    }

    private bool TryReceiveFrame(out byte[] frame, out bool hasMore)
    {
        frame = Array.Empty<byte>();
        hasMore = false;

        try
        {
            using var message = _socket.ReceiveMessage();
            frame = message.ToArray();
            hasMore = message.More;
            return true;
        }
        catch (ZlinkException ex) when (ex.Errno == AgainErrno)
        {
            return false;
        }
        catch (ZlinkException ex) when (_cts.IsCancellationRequested)
        {
            _logger.LogDebug("Receive loop stopping after cancellation: errno={Errno}", ex.Errno);
            return false;
        }
        catch (Exception ex)
        {
            if (!_cts.IsCancellationRequested && !_disposed)
            {
                _logger.LogError(ex, "Error in Zlink STREAM receive loop");
            }

            return false;
        }
    }

    private void HandleIncoming(byte[] routingId, byte[] payload)
    {
        var routingKey = Convert.ToBase64String(routingId);

        if (payload.Length == 1 && payload[0] == 0x01)
        {
            // Connect event.
            _ = GetOrCreateSession(routingKey, routingId);
            return;
        }

        if (payload.Length == 1 && payload[0] == 0x00)
        {
            // Disconnect event.
            if (_sessionsByRoutingKey.TryGetValue(routingKey, out var existing))
            {
                _ = existing.DisconnectAsync();
            }
            return;
        }

        var session = GetOrCreateSession(routingKey, routingId);
        session.OnIncomingData(payload);
    }

    private ZlinkStreamTransportSession GetOrCreateSession(string routingKey, byte[] routingId)
    {
        return _sessionsByRoutingKey.GetOrAdd(routingKey, _ =>
        {
            var sessionId = Interlocked.Increment(ref _nextSessionId);
            var session = new ZlinkStreamTransportSession(
                sessionId,
                routingKey,
                routingId,
                _onMessage,
                OnSessionDisconnected,
                _logger,
                this,
                _boundEndpoint);

            _sessionsById[sessionId] = session;
            _logger.LogDebug("Created stream session {SessionId}", sessionId);
            return session;
        });
    }

    private void OnSessionDisconnected(ITransportSession session, Exception? ex)
    {
        _sessionsById.TryRemove(session.SessionId, out _);

        if (session is ZlinkStreamTransportSession streamSession)
        {
            _sessionsByRoutingKey.TryRemove(streamSession.RoutingKey, out _);
        }

        _onDisconnect(session, ex);
    }

    private void DrainUnexpectedFrames()
    {
        while (true)
        {
            try
            {
                using var extra = _socket.ReceiveMessage(ReceiveFlags.DontWait);
                if (!extra.More)
                {
                    return;
                }
            }
            catch (ZlinkException ex) when (ex.Errno == AgainErrno)
            {
                return;
            }
        }
    }

    internal static string BuildEndpoint(string scheme, IPEndPoint endpoint, string? webSocketPath = null)
    {
        var normalizedScheme = scheme.ToLowerInvariant();
        if (normalizedScheme is not ("tcp" or "tls" or "ws" or "wss"))
        {
            throw new ArgumentOutOfRangeException(nameof(scheme), scheme, "Supported schemes are tcp, tls, ws, wss.");
        }

        string host;
        if (endpoint.Address.Equals(IPAddress.Any) || endpoint.Address.Equals(IPAddress.IPv6Any))
        {
            host = "*";
        }
        else if (endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            host = $"[{endpoint.Address}]";
        }
        else
        {
            host = endpoint.Address.ToString();
        }

        var portToken = endpoint.Port == 0
            ? "*"
            : endpoint.Port.ToString(CultureInfo.InvariantCulture);

        if (normalizedScheme is "ws" or "wss")
        {
            return $"{normalizedScheme}://{host}:{portToken}{NormalizeWebSocketPath(webSocketPath)}";
        }

        return $"{normalizedScheme}://{host}:{portToken}";
    }

    private static string NormalizeWebSocketPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        return path[0] == '/' ? path : "/" + path;
    }

    private static string ValidateEndpoint(string endpoint, out string scheme)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint is required.", nameof(endpoint));
        }

        var normalized = endpoint.Trim();
        var separator = normalized.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new ArgumentException("Endpoint must include protocol scheme (tcp://, tls://, ws://, wss://).", nameof(endpoint));
        }

        scheme = normalized[..separator].ToLowerInvariant();
        if (scheme is not ("tcp" or "tls" or "ws" or "wss"))
        {
            throw new ArgumentException($"Unsupported endpoint scheme: {scheme}.", nameof(endpoint));
        }

        return normalized;
    }

    private string? TryGetLastEndpoint()
    {
        try
        {
            var endpoint = _socket.GetOptionString(SocketOption.LastEndpoint);
            return string.IsNullOrWhiteSpace(endpoint) ? null : endpoint;
        }
        catch
        {
            return null;
        }
    }

    private int? TryGetBoundPort()
    {
        return TryExtractPort(_boundEndpoint);
    }

    private static int? TryExtractPort(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        var schemeSeparator = endpoint.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0 || schemeSeparator + 3 >= endpoint.Length)
        {
            return null;
        }

        var addressPart = endpoint[(schemeSeparator + 3)..];
        if (addressPart.Length == 0)
        {
            return null;
        }

        if (addressPart[0] == '[')
        {
            var closingBracket = addressPart.IndexOf(']');
            if (closingBracket < 0 || closingBracket + 2 > addressPart.Length || addressPart[closingBracket + 1] != ':')
            {
                return null;
            }

            var portToken = addressPart[(closingBracket + 2)..];
            var slashIndex = portToken.IndexOf('/');
            if (slashIndex >= 0)
            {
                portToken = portToken[..slashIndex];
            }

            return ParsePortToken(portToken);
        }

        var authority = addressPart;
        var firstSlash = authority.IndexOf('/');
        if (firstSlash >= 0)
        {
            authority = authority[..firstSlash];
        }

        var colon = authority.LastIndexOf(':');
        if (colon <= 0 || colon >= authority.Length - 1)
        {
            return null;
        }

        return ParsePortToken(authority[(colon + 1)..]);
    }

    private static int? ParsePortToken(string token)
    {
        if (token == "*")
        {
            return 0;
        }

        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            return port;
        }

        return null;
    }

    private static void ConfigureTlsIfNeeded(
        ZSocket socket,
        string scheme,
        X509Certificate2? certificate,
        out TlsMaterialPaths? tlsPaths)
    {
        tlsPaths = null;
        var isTlsScheme = scheme is "tls" or "wss";

        if (!isTlsScheme)
        {
            if (certificate != null)
            {
                throw new ArgumentException("TLS certificate is only valid for tls:// or wss:// endpoints.", nameof(certificate));
            }

            return;
        }

        if (certificate == null)
        {
            throw new ArgumentException("TLS certificate is required for tls:// and wss:// endpoints.", nameof(certificate));
        }

        tlsPaths = WriteTlsMaterial(certificate);
        socket.SetOption(SocketOption.TlsCert, tlsPaths.CertPath);
        socket.SetOption(SocketOption.TlsKey, tlsPaths.KeyPath);
    }

    private static TlsMaterialPaths WriteTlsMaterial(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("The provided TLS certificate does not contain a private key.");
        }

        var certPem = certificate.ExportCertificatePem();
        var keyPem = ExportPrivateKeyPem(certificate);

        var directory = Path.Combine(Path.GetTempPath(), "playhouse-zlink-tls");
        Directory.CreateDirectory(directory);

        var token = Guid.NewGuid().ToString("N");
        var certPath = Path.Combine(directory, $"zlink-{token}.cert.pem");
        var keyPath = Path.Combine(directory, $"zlink-{token}.key.pem");

        File.WriteAllText(certPath, certPem);
        File.WriteAllText(keyPath, keyPem);

        return new TlsMaterialPaths(certPath, keyPath);
    }

    private static string ExportPrivateKeyPem(X509Certificate2 certificate)
    {
        using (var rsa = certificate.GetRSAPrivateKey())
        {
            if (rsa != null)
            {
                return rsa.ExportPkcs8PrivateKeyPem();
            }
        }

        using (var ecdsa = certificate.GetECDsaPrivateKey())
        {
            if (ecdsa != null)
            {
                return ecdsa.ExportPkcs8PrivateKeyPem();
            }
        }

        using (var dsa = certificate.GetDSAPrivateKey())
        {
            if (dsa != null)
            {
                return dsa.ExportPkcs8PrivateKeyPem();
            }
        }

        using (var ecdh = certificate.GetECDiffieHellmanPrivateKey())
        {
            if (ecdh != null)
            {
                return ecdh.ExportPkcs8PrivateKeyPem();
            }
        }

        throw new NotSupportedException("The certificate private key algorithm is not supported for TLS export.");
    }

    private void CleanupTlsMaterial()
    {
        if (_tlsMaterialPaths == null)
        {
            return;
        }

        TryDeleteFile(_tlsMaterialPaths.CertPath);
        TryDeleteFile(_tlsMaterialPaths.KeyPath);
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best-effort cleanup for temporary TLS materials.
        }
    }

    private static void ConfigureSocket(ZSocket socket, TransportOptions options)
    {
        socket.SetOption(SocketOption.Linger, 0);
        socket.SetOption(SocketOption.RcvTimeo, 100);
        socket.SetOption(SocketOption.SndBuf, options.SendBufferSize);
        socket.SetOption(SocketOption.RcvBuf, options.ReceiveBufferSize);

        if (options.EnableKeepAlive)
        {
            socket.SetOption(SocketOption.TcpKeepalive, 1);
            socket.SetOption(SocketOption.TcpKeepaliveIdle, options.KeepAliveTime);
            socket.SetOption(SocketOption.TcpKeepaliveIntvl, options.KeepAliveInterval);
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZlinkStreamTransportServer));
        }
    }
}
