# LIBZLINK 재현 시나리오 (bench_ss 기준)

## 1) 목적
bench_ss `send` 모드에서 발생하는 `0 msg/s` / `Host unreachable`를 PlayHouse 통합 코드 영향과 분리해서,
`ROUTER <-> ROUTER` 자체 동작(연결 flap, 라우팅 ID 유지)을 LIBZLINK 레벨에서 재현/검증하기 위한 시나리오입니다.

## 2) 현재 관찰된 실패 시그니처
- API는 `SSEchoRequest`를 대량 수신(`dropped=0`)하는데,
  반대 방향 송신에서 `Failed to send to <peer>: Host unreachable`가 대량 발생.
- 혹은 `Host unreachable` 없이도 monitor 상에서
  `ConnectionReady -> Disconnected(value=4) -> Connected -> ConnectionReady` 반복.
- 애플리케이션 레벨 `Disconnect()` 호출 없이 소켓 레벨에서 연결이 반복적으로 끊김.

## 3) 공통 토폴로지
- 프로세스 A (play-1)
  - `serverSocket` (ROUTER): bind `tcp://127.0.0.1:16100`, routingId=`play-1`
  - `clientSocket` (ROUTER): routingId=`play-1`, connect 대상:
    - self: `tcp://127.0.0.1:16100` (옵션 시나리오)
    - peer: `tcp://127.0.0.1:16201`
- 프로세스 B (api-1)
  - `serverSocket` (ROUTER): bind `tcp://127.0.0.1:16201`, routingId=`api-1`
  - `clientSocket` (ROUTER): routingId=`api-1`, connect 대상:
    - self: `tcp://127.0.0.1:16201` (옵션 시나리오)
    - peer: `tcp://127.0.0.1:16100`

소켓 옵션(기본):
- `RouterMandatory=1`
- `RouterHandover=1`
- `Immediate=0`
- `SndHwm/RcvHwm`은 충분히 크게(예: 100000)

프레임 포맷:
- Frame0: target serverId (`api-1` 또는 `play-1`)
- Frame1: route header bytes (`From`, `StageId` 포함)
- Frame2: payload bytes

## 4) 시나리오 A (bench_ss send와 동일한 핵심 패턴)
### 절차
1. A/B 서버 소켓 bind 후 client 소켓 connect.
2. Warmup 1초:
   - A client -> B server로 연속 전송 (64B payload, batch 10)
   - B server 수신 시 B client -> A server로 echo 전송
3. 측정 4초:
   - 위 패턴 동일 반복
4. monitor 이벤트 전체 수집.

### 파라미터
- payload: 64B
- 가상 동시성: 50 (또는 libzlink 벤치에서는 sender 루프 50개)
- inflight: 10
- warmup: 1s
- measure: 4s

### 실패 판정
- `ConnectionReady` 후 동일 peer에 대해 `Disconnected(value=3|4)`가 주기적으로 반복
- 또는 `Host unreachable` 발생률이 높고 처리량이 0 또는 급락

## 5) 시나리오 B (self-connect 제거 비교)
시나리오 A와 동일하되 각 프로세스에서 self-connect를 제거.
- A client는 `16201`만 connect
- B client는 `16100`만 connect

목표: self-connect 유무가 flap에 영향 주는지 분리.

## 6) 시나리오 C (단방향 안정성)
- A client -> B server만 송신 (B는 reply 미송신)
- 동일 시간 동안 monitor 관찰

목표: 양방향 트래픽이 있을 때만 끊기는지 확인.

## 7) 시나리오 D (시작 순서 비교)
A/B 순서를 바꿔서 시나리오 A 반복:
- D1: play 먼저, api 나중
- D2: api 먼저, play 나중

목표: startup race로 인한 일시적 reconnect가 지속 flap으로 이어지는지 확인.

## 8) 로그/메트릭 수집 권장
- Monitor 이벤트: `Connected`, `ConnectionReady`, `Disconnected`, `ConnectRetried`, `Closed`, `Accepted`
- 송신 실패 카운트: `Host unreachable`
- 처리량: 측정 구간 총 수신/응답 수
- 권장: 소켓별 peer(local/remote endpoint, routing id) 포함 기록

## 9) 재현시 핵심 확인 포인트
- `Disconnect()` 호출 없이도 transport-level disconnect가 발생하는지
- disconnect 직후 재접속 포트가 바뀌며(`remote=tcp://127.0.0.1:<ephemeral>`) 반복되는지
- 동일 peer id(`play-1`/`api-1`) 라우팅이 지속 유지되는지
