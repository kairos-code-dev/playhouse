# PlayHouse Unity Connector

Unity connector package for PlayHouse real-time game server framework (Unity 2022 LTS).

## Install (Git URL)

Unity Package Manager -> Add package from git URL...

```
https://github.com/ulalax/playhouse.git?path=connectors/unity#v0.1.0
```

## Package Structure

```
connectors/unity/
├── Runtime/
│   ├── PlayHouse.Connector.Runtime.asmdef
│   └── Scripts/
│       └── PlayHouse.Connector/  (core connector sources)
├── Samples~/
│   └── Basic/
│       └── BasicConnectorExample.cs
├── Documentation~/
│   └── index.md
├── package.json
├── CHANGELOG.md
└── LICENSE.md
```

## Basic Usage

```csharp
using PlayHouse.Connector;
using UnityEngine;

public class PlayHouseClient : MonoBehaviour
{
    private Connector _connector;

    private void Start()
    {
        _connector = new Connector();
        _connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 10000,
            HeartbeatIntervalMs = 5000
        });

        _connector.OnConnect += success => Debug.Log($"Connected: {success}");
        _connector.OnReceive += (stageId, stageType, packet) =>
            Debug.Log($"Recv({stageType}/{stageId}): {packet.MsgId}");
        _connector.OnError += (stageId, stageType, errorCode, request) =>
            Debug.LogError($"Error {errorCode} on {stageType}/{stageId}");
        _connector.OnDisconnect += () => Debug.Log("Disconnected");

        _connector.Connect("127.0.0.1", 34001);
    }

    private void Update()
    {
        _connector?.MainThreadAction();
    }

    private void OnDestroy()
    {
        _connector?.Disconnect();
    }
}
```

## Notes
- Call `MainThreadAction()` from `Update()` to dispatch callbacks on the Unity main thread.
- Use TCP/WebSocket/TLS according to your server configuration.
