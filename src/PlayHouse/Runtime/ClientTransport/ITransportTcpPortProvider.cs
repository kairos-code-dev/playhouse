#nullable enable

namespace PlayHouse.Runtime.ClientTransport;

/// <summary>
/// Optional capability interface for transport servers that can expose the actual bound TCP port.
/// </summary>
internal interface ITransportTcpPortProvider
{
    /// <summary>
    /// Gets the actual bound TCP port.
    /// Returns 0 when not available.
    /// </summary>
    int ActualTcpPort { get; }
}

