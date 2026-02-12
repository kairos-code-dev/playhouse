#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlayHouse.Connector.Internal;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector;

/// <summary>
/// PlayHouse 클라이언트 Connector
/// </summary>
/// <remarks>
/// 클라이언트가 Play Server에 연결하여 실시간 통신을 수행하는 메인 클래스입니다.
/// Stage ID는 인증 성공 후 서버 응답에서 자동으로 설정됩니다.
/// </remarks>
public sealed class Connector : IConnectorCallback, IAsyncDisposable
{
    private ClientNetwork? _clientNetwork;
    private bool _disconnectFromClient;
    private long _stageId;
    private string _stageType = string.Empty;

    /// <summary>
    /// Connector 설정
    /// </summary>
    public ConnectorConfig ConnectorConfig { get; private set; } = new();

    /// <summary>
    /// 현재 연결의 Stage ID
    /// </summary>
    public long StageId => _stageId;

    /// <summary>
    /// 현재 연결된 Stage의 타입
    /// </summary>
    public string StageType => _stageType;

    #region Events

    /// <summary>
    /// 연결 결과 이벤트
    /// </summary>
    public event Action<bool>? OnConnect;

    /// <summary>
    /// 메시지 수신 이벤트 (stageId, stageType, packet)
    /// </summary>
    public event Action<long, string, IPacket>? OnReceive;

    /// <summary>
    /// 에러 이벤트 (stageId, stageType, errorCode, request)
    /// </summary>
    public event Action<long, string, ushort, IPacket>? OnError;

    /// <summary>
    /// 연결 끊김 이벤트
    /// </summary>
    public event Action? OnDisconnect;

    #endregion

    #region IConnectorCallback Implementation

    void IConnectorCallback.ConnectCallback(bool result)
    {
        OnConnect?.Invoke(result);
    }

    void IConnectorCallback.ReceiveCallback(long stageId, IPacket packet)
    {
        OnReceive?.Invoke(stageId, _stageType, packet);
    }

    void IConnectorCallback.ErrorCallback(long stageId, ushort errorCode, IPacket request)
    {
        OnError?.Invoke(stageId, _stageType, errorCode, request);
    }

    void IConnectorCallback.DisconnectCallback()
    {
        if (_disconnectFromClient)
        {
            return;
        }

        OnDisconnect?.Invoke();
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Connector 초기화
    /// </summary>
    /// <param name="config">설정</param>
    public void Init(ConnectorConfig config)
    {
        ConnectorConfig = config ?? throw new ArgumentNullException(nameof(config));
        _clientNetwork = new ClientNetwork(config, this);
    }

    #endregion

    #region Connection

    /// <summary>
    /// 서버에 연결 (비동기로 OnConnect 이벤트 발생)
    /// </summary>
    /// <param name="host">서버 호스트 주소</param>
    /// <param name="port">서버 포트</param>
    /// <param name="debugMode">디버그 모드</param>
    public void Connect(string host, int port, bool debugMode = false)
    {
        if (_clientNetwork == null)
        {
            throw new InvalidOperationException("Connector not initialized. Call Init() before Connect().");
        }

        _disconnectFromClient = false;
        _stageId = 0;
        _stageType = string.Empty;
        _clientNetwork.Connect(host, port, debugMode);
    }


    /// <summary>
    /// 서버에 비동기 연결
    /// </summary>
    /// <param name="host">서버 호스트 주소</param>
    /// <param name="port">서버 포트</param>
    /// <param name="debugMode">디버그 모드</param>
    /// <returns>연결 성공 여부</returns>
    public async Task<bool> ConnectAsync(string host, int port, bool debugMode = false)
    {
        if (_clientNetwork == null)
        {
            throw new InvalidOperationException("Connector not initialized. Call Init() before ConnectAsync().");
        }

        _disconnectFromClient = false;
        _stageId = 0;
        _stageType = string.Empty;
        return await _clientNetwork.ConnectAsync(host, port, debugMode);
    }

    /// <summary>
    /// 서버 연결 끊기
    /// </summary>
    public void Disconnect()
    {
        _disconnectFromClient = true;
        _ = _clientNetwork?.DisconnectAsync();
    }

    /// <summary>
    /// 연결 상태 확인
    /// </summary>
    public bool IsConnected()
    {
        return _clientNetwork?.IsConnect() ?? false;
    }

    /// <summary>
    /// 인증 상태 확인
    /// </summary>
    public bool IsAuthenticated()
    {
        return _clientNetwork?.IsAuthenticated() ?? false;
    }

    #endregion

    #region Authentication

    /// <summary>
    /// 인증 요청 (콜백 방식)/
    /// </summary>
    /// <param name="request">인증 요청 패킷</param>
    /// <param name="callback">응답 콜백</param>
    public void Authenticate(IPacket request, Action<IPacket> callback)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(_stageId, _stageType, (ushort)ConnectorErrorCode.Disconnected, request);
            return;
        }

        _clientNetwork!.Request(request, response =>
        {
            _stageId = _clientNetwork.StageId;
            if (TryExtractAuthStageType(response.Payload.DataSpan, out var stageType))
            {
                _stageType = stageType;
            }
            callback(response);
        }, stageId: 0, isAuthenticate: true);
    }

    /// <summary>
    /// 인증 요청 (async/await 방식)
    /// </summary>
    /// <param name="request">인증 요청 패킷</param>
    /// <returns>인증 응답 패킷</returns>
    public async Task<IPacket> AuthenticateAsync(IPacket request)
    {
        if (!IsConnected())
        {
            throw new ConnectorException(_stageId, (ushort)ConnectorErrorCode.Disconnected, request, 0);
        }

        var response = await _clientNetwork!.RequestAsync(request, stageId: 0, isAuthenticate: true);
        _stageId = _clientNetwork.StageId;
        if (TryExtractAuthStageType(response.Payload.DataSpan, out var stageType))
        {
            _stageType = stageType;
        }
        return response;
    }

    #endregion

    #region Send/Request

    /// <summary>
    /// 메시지 전송 (응답 없음)
    /// </summary>
    /// <param name="packet">전송할 패킷</param>
    public void Send(IPacket packet)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(_stageId, _stageType, (ushort)ConnectorErrorCode.Disconnected, packet);
            return;
        }

        _clientNetwork!.Send(packet, _stageId);
    }

    /// <summary>
    /// 요청 전송 (콜백 방식)
    /// </summary>
    /// <param name="request">요청 패킷</param>
    /// <param name="callback">응답 콜백</param>
    public void Request(IPacket request, Action<IPacket> callback)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(_stageId, _stageType, (ushort)ConnectorErrorCode.Disconnected, request);
            return;
        }

        _clientNetwork!.Request(request, callback, _stageId);
    }

    /// <summary>
    /// 요청 전송 (async/await 방식)
    /// </summary>
    /// <param name="request">요청 패킷</param>
    /// <returns>응답 패킷</returns>
    public async Task<IPacket> RequestAsync(IPacket request)
    {
        if (!IsConnected())
        {
            throw new ConnectorException(_stageId, (ushort)ConnectorErrorCode.Disconnected, request, 0);
        }

        return await _clientNetwork!.RequestAsync(request, _stageId);
    }

    #endregion

    #region Unity Support

    /// <summary>
    /// 메인 스레드에서 콜백 실행 (Unity Update에서 호출)
    /// </summary>
    public void MainThreadAction()
    {
        _clientNetwork?.MainThreadAction();
    }

    #endregion

    #region IAsyncDisposable

    /// <summary>
    /// 비동기 리소스 정리
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_clientNetwork != null)
        {
            await _clientNetwork.DisposeAsync();
            _clientNetwork = null;
        }
    }

    private static bool TryExtractAuthStageType(ReadOnlySpan<byte> payload, out string stageType)
    {
        stageType = string.Empty;
        int offset = 0;

        while (offset < payload.Length)
        {
            if (!TryReadVarint(payload, ref offset, out var tag))
            {
                return false;
            }

            var wireType = (int)(tag & 0x07);
            var fieldNumber = (int)(tag >> 3);

            if (fieldNumber == 6 && wireType == 2)
            {
                if (!TryReadVarint(payload, ref offset, out var len))
                {
                    return false;
                }

                if (offset + (int)len > payload.Length)
                {
                    return false;
                }

                stageType = System.Text.Encoding.UTF8.GetString(payload.Slice(offset, (int)len));
                return true;
            }

            if (!SkipField(payload, ref offset, wireType))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int offset, out ulong value)
    {
        value = 0;
        int shift = 0;
        while (offset < data.Length && shift < 64)
        {
            var b = data[offset++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }
            shift += 7;
        }

        return false;
    }

    private static bool SkipField(ReadOnlySpan<byte> data, ref int offset, int wireType)
    {
        return wireType switch
        {
            0 => TryReadVarint(data, ref offset, out _),
            1 => SkipBytes(data, ref offset, 8),
            2 => SkipLengthDelimited(data, ref offset),
            5 => SkipBytes(data, ref offset, 4),
            _ => false
        };
    }

    private static bool SkipLengthDelimited(ReadOnlySpan<byte> data, ref int offset)
    {
        if (!TryReadVarint(data, ref offset, out var len))
        {
            return false;
        }

        return SkipBytes(data, ref offset, (int)len);
    }

    private static bool SkipBytes(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        if (offset + count > data.Length)
        {
            return false;
        }

        offset += count;
        return true;
    }

    #endregion

}
