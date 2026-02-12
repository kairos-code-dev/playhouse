#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.System;
using PlayHouse.Runtime.ServerMesh.Communicator;

namespace PlayHouse.Runtime.ServerMesh.Discovery;

/// <summary>
/// 서버 디스커버리 및 주소 해석기.
/// </summary>
/// <remarks>
/// ISystemController를 통해 주기적으로 서버 목록을 갱신하고,
/// 변경된 서버에 대한 연결/해제를 PlayCommunicator에 위임합니다.
/// </remarks>
public sealed class ServerAddressResolver : IDisposable
{
    private readonly IServerInfo _myServerInfo;
    private readonly ISystemController _systemController;
    private readonly IServerInfoCenter _serverInfoCenter;
    private readonly ICommunicator? _communicator;
    private readonly TimeSpan _refreshInterval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _refreshLoopTask;
    private bool _disposed;

    /// <summary>
    /// 서버 목록이 변경되었을 때 발생하는 이벤트.
    /// </summary>
    public event Action<IReadOnlyList<ServerChange>>? OnServerListChanged;

    /// <summary>
    /// 새 ServerAddressResolver 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="myServerInfo">내 서버 정보.</param>
    /// <param name="systemController">시스템 컨트롤러 (서버 목록 조회용).</param>
    /// <param name="serverInfoCenter">서버 정보 캐시.</param>
    /// <param name="communicator">통신기 (연결 관리용, 선택적).</param>
    /// <param name="refreshInterval">갱신 주기 (기본 3초).</param>
    public ServerAddressResolver(
        IServerInfo myServerInfo,
        ISystemController systemController,
        IServerInfoCenter serverInfoCenter,
        ICommunicator? communicator = null,
        TimeSpan? refreshInterval = null)
    {
        _myServerInfo = myServerInfo;
        _systemController = systemController;
        _serverInfoCenter = serverInfoCenter;
        _communicator = communicator;
        _refreshInterval = refreshInterval ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>
    /// 주기적 갱신을 시작합니다.
    /// </summary>
    public void Start()
    {
        if (_refreshLoopTask != null) return;

        _refreshLoopTask = RefreshLoopAsync(_cts.Token);
    }

    /// <summary>
    /// 주기적 갱신을 중지합니다.
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        try
        {
            _refreshLoopTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // 취소됨
        }
    }

    /// <summary>
    /// 서버 목록을 즉시 갱신합니다.
    /// </summary>
    /// <returns>변경된 서버 목록.</returns>
    public async Task RefreshAsync()
    {
        try
        {
            // 시스템 컨트롤러에서 서버 목록 조회
            var serverList = await _systemController.UpdateServerInfoAsync(_myServerInfo);

            // 서버 정보 센터 갱신
            var changes = _serverInfoCenter.Update(serverList);

            // 연결 관리
            if (_communicator != null && changes.Count > 0)
            {
                ApplyConnectionChanges(changes);
            }

            // 이벤트 발생
            if (changes.Count > 0)
            {
                OnServerListChanged?.Invoke(changes);
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[ServerAddressResolver] Refresh failed: {ex.Message}");
        }
    }

    /// <summary>
    /// ServerId로 서버 주소를 조회합니다.
    /// </summary>
    /// <param name="serverId">Server ID.</param>
    /// <returns>서버 주소 또는 null.</returns>
    public string? ResolveAddress(string serverId)
    {
        return _serverInfoCenter.GetServer(serverId)?.Address;
    }

    /// <summary>
    /// 서비스별로 서버를 선택합니다 (로드밸런싱).
    /// </summary>
    /// <param name="serverType">서버 타입.</param>
    /// <param name="serviceId">서비스 ID.</param>
    /// <returns>선택된 서버 정보 또는 null.</returns>
    public XServerInfo? SelectServer(ServerType serverType, ushort serviceId)
    {
        return _serverInfoCenter.GetServerByService(serverType, serviceId);
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        // 즉시 첫 갱신 실행
        await RefreshAsync();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_refreshInterval, ct);
                await RefreshAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ServerAddressResolver] Refresh loop error: {ex.Message}");
            }
        }
    }

    private void ApplyConnectionChanges(IReadOnlyList<ServerChange> changes)
    {
        foreach (var change in changes)
        {
            if (string.Equals(change.Server.ServerId, _myServerInfo.ServerId, StringComparison.Ordinal))
            {
                continue;
            }

            switch (change.Type)
            {
                case ChangeType.Added:
                    if (change.Server.State == ServerState.Running)
                    {
                        Console.WriteLine($"[ServerAddressResolver] Connecting to server: {change.Server.ServerId} at {change.Server.Address}");
                        _communicator!.Connect(change.Server.ServerId, change.Server.Address);
                    }
                    break;

                case ChangeType.Updated:
                    // 상태가 Disabled로 변경되면 연결 해제
                    if (change.Server.State == ServerState.Disabled)
                    {
                        _communicator!.Disconnect(change.Server.ServerId, change.Server.Address);
                    }
                    else
                    {
                        // 주소가 변경되었을 수 있으므로 재연결
                        _communicator!.Disconnect(change.Server.ServerId, change.Server.Address);
                        _communicator.Connect(change.Server.ServerId, change.Server.Address);
                    }
                    break;

                case ChangeType.Removed:
                    _communicator!.Disconnect(change.Server.ServerId, change.Server.Address);
                    break;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _cts.Dispose();
    }
}
