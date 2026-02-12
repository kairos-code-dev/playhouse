# ZeroMQ -> Zlink 마이그레이션 작업 계획

- 작성일: 2026-02-12
- 대상 브랜치: `refactor/zeromq-to-zlnk`
- 1차 목표: 서버 간 통신(ServerMesh) `Net.Zmq` -> `Zlink` 전환
- 2차 목표: 1차 성능 게이트 통과 후 CS 소켓 경로를 `Zlink Stream`으로 전환
- Zlink 로컬 경로: `/home/hep7/project/kairos/zlink/bindings/dotnet/src/Zlink`
- 계획/결과 기록 폴더: `doc/plan/zlink-migration`

## 1. 배경과 목표

현재 PlayHouse의 서버 간 통신은 `ROUTER↔ROUTER` 기반의 `Net.Zmq` 구현(`ZmqPlaySocket`)을 사용한다.  
이번 작업은 서버 간 통신부터 `Zlink`로 교체하고, 성능과 안정성이 확인되면 클라이언트 소켓(CS)도 `Zlink Stream`으로 교체하는 2단계 마이그레이션으로 진행한다.

핵심 목표는 다음과 같다.

- 기존 라우팅 프로토콜(3프레임: `ServerId`, `RouteHeader`, `Payload`)과 동작 의미를 유지한다.
- 처리량/지연/안정성을 기존 대비 동등 이상으로 유지한다.
- 초기에는 로컬 프로젝트 참조로 붙이고, 안정화 후 NuGet 패키지로 전환한다.

## 2. 범위

### 2.1 포함 범위 (1차: ServerMesh)

- `src/PlayHouse/Runtime/ServerMesh/PlaySocket/ZmqPlaySocket.cs` 대체 구현
- `src/PlayHouse/Runtime/ServerMesh/Communicator/PlayCommunicator.cs` 전환
- `src/PlayHouse/Runtime/ServerMesh/Communicator/XServerCommunicator.cs` 예외/종료 처리 전환
- `src/PlayHouse/PlayHouse.csproj` 의존성 교체
- 서버 간 통신 관련 테스트/벤치/문서 업데이트

### 2.2 포함 범위 (2차: CS Stream, 1차 성능검증 통과 후)

- Connector/Client transport 경로에 `Zlink Stream` 적용
- CS 벤치마크(`benchmark_cs`) 기준 성능/호환성 검증

### 2.3 제외 범위 (현 단계)

- 1차 완료 전 클라이언트 프로토콜 자체 변경
- 1차 완료 전 TLS/WSS 신규 기능 도입
- 1차 완료 전 Registry/Discovery/Gateway/Spot 등 Zlink 상위 서비스로의 구조 변경

## 3. 현재 구현 요약 (AS-IS)

- 서버 간 통신 구현:
  - `IPlaySocket` 인터페이스 + `ZmqPlaySocket` 구현
  - `PlayCommunicator`가 서버/클라이언트용 소켓 2개를 생성해 송수신 분리
  - `XClientCommunicator`는 채널 기반 송신 큐 처리
  - `XServerCommunicator`는 수신 루프 + `ZmqException(ETERM)` 기반 종료 처리
- 패키지 의존성:
  - `src/PlayHouse/PlayHouse.csproj` -> `Net.Zmq` 0.4.1
  - `tests/PlayHouse.Tests.Performance/Test.csproj` -> `Net.Zmq` 0.4.1
- 성능 측정 자산:
  - `tests/benchmark/benchmark_cs/run-single.sh`
  - `tests/benchmark/benchmark_ss/run-single.sh`
  - `tests/benchmark/benchmark_cs/*` (CS 벤치 실행 경로)
  - `tests/benchmark/benchmark_ss/*` (SS 벤치 실행 경로)

## 4. 목표 아키텍처 (TO-BE)

### 4.1 기본 원칙

- `IPlaySocket` 추상화는 유지한다.
- 프레임 포맷(3-frame)과 `RouteHeader` protobuf 구조는 유지한다.
- 전환 1차에서는 서버 간 통신 패턴을 계속 `ROUTER↔ROUTER`로 유지한다.

### 4.2 API 매핑 가이드 (Net.Zmq -> Zlink)

| Net.Zmq | Zlink |
|---|---|
| `Context` | `Zlink.Context` |
| `SocketType.Router` | `Zlink.SocketType.Router` |
| `SocketOption.Routing_Id` | `SocketOption.RoutingId` |
| `SocketOption.Router_Handover` | `SocketOption.RouterHandover` |
| `SocketOption.Router_Mandatory` | `SocketOption.RouterMandatory` |
| `SocketOption.Sndhwm` | `SocketOption.SndHwm` |
| `SocketOption.Rcvhwm` | `SocketOption.RcvHwm` |
| `SocketOption.Rcvtimeo` | `SocketOption.RcvTimeo` |
| `SocketOption.Tcp_Keepalive` | `SocketOption.TcpKeepalive` |
| `SocketOption.Tcp_Keepalive_Idle` | `SocketOption.TcpKeepaliveIdle` |
| `SocketOption.Tcp_Keepalive_Intvl` | `SocketOption.TcpKeepaliveIntvl` |
| `SendFlags.SendMore` | `SendFlags.SendMore` |
| `Recv(...)` + `HasMore` | `Receive(...)` + `GetOption(SocketOption.RcvMore)` |

### 4.3 종료 처리 기준

- 기존 `ZmqException + ETERM` 의존을 축소한다.
- `Context.Shutdown()` + 취소 토큰 기반 정상 종료 경로를 주 경로로 삼는다.
- Zlink 예외(`ZlinkException`)는 errno 기반으로 분류하되, 루프 종료 여부는 내부 상태(`_running`, cancellation) 우선으로 판단한다.

## 5. 단계별 실행 계획

### 단계 0: 사전 고정(Baseline)

- 기준 커밋 태깅 또는 기준 SHA 기록
- 현행 ZeroMQ 성능/안정성 수치 채집
- 결과 저장 위치 표준화: `doc/plan/zlink-migration/results/<timestamp>/`
- baseline 측정은 `benchmark_cs` + `benchmark_ss` 모두 수행
- baseline 메시지 크기: `64`, `1024`, `65536`
- 현 기준 최대 부하 프로파일:
  - CS: `./run-single.sh --ccu 10000 --size <64|1024|65536> --mode send --inflight 30`
  - SS: `./run-single.sh --ccu 10000 --size <64|1024|65536> --mode send --inflight 10`
- raw 출력 경로:
  - CS raw: `tests/benchmark/benchmark_cs/benchmark-results`
  - SS raw: `run-single.sh` 콘솔 출력(기본), baseline 실행 시 `doc/plan/zlink-migration/results/<timestamp>/ss/raw/*.log`로 저장

산출물:

- baseline 성능표(처리량, 평균/상위 지연, 오류율)
- 비교 기준 수치 문서화
- CS/SS 원본 결과를 표준 보관 루트(`doc/plan/zlink-migration/results/<timestamp>/`)로 아카이브
- 요약 결과는 `doc/plan/zlink-migration/baseline-results.md`에 기록

### 단계 1: 의존성 전환 준비 (로컬 참조 우선)

- `src/PlayHouse/PlayHouse.csproj`에 로컬 프로젝트 참조 추가
- 필요 시 벤치/테스트 프로젝트에도 동일하게 로컬 참조 추가
- 초기에는 `Net.Zmq`를 즉시 제거하지 않고 병행 가능 상태 유지

참고 경로:

- `src/PlayHouse/PlayHouse.csproj` -> `..\..\..\zlink\bindings\dotnet\src\Zlink\Zlink.csproj`
- `tests/PlayHouse.Tests.Performance/Test.csproj` -> `..\..\..\zlink\bindings\dotnet\src\Zlink\Zlink.csproj`

검증 포인트:

- 빌드 출력에 Zlink 네이티브 라이브러리(`runtimes/*/native/*`)가 포함되는지 확인
- 필요 시 `ZLINK_LIBRARY_PATH` 환경 변수 기반 fallback 동작 확인

### 단계 2: ServerMesh 소켓 구현 전환

- `ZlinkPlaySocket` 신규 구현 추가
- 기존 `ZmqPlaySocket`와 동등 동작 보장
  - 라우팅 ID 설정
  - 3프레임 송신/수신
  - self-connect(동일 ServerId) 시나리오
  - 버퍼 재사용/메모리 풀 정책 유지
- 수신 시 `RcvMore` 기반 프레임 경계 검증 추가
- `ReceiveDirect(level)` 경로도 동일 진단 의미를 유지

산출물:

- `src/PlayHouse/Runtime/ServerMesh/PlaySocket/ZlinkPlaySocket.cs`
- `PlayCommunicator`/`XServerCommunicator` 연동 변경

### 단계 3: Communicator 통합 및 호환 장치

- `PlayCommunicator`에서 `Zlink.Context` 사용
- 전환 안정화 전까지는 선택형 전략 권장
  - 예: 내부 플래그(기본 Zlink, 긴급시 ZMQ fallback)
- 운영 중 빠른 롤백 가능성 확보

산출물:

- 전송 스택 선택 지점(설정/분기) 추가
- 롤백 절차 문서화

### 단계 4: 테스트 전환

- `tests/unit/PlayHouse.Unit/ZmqSendRecvTest.cs`를 Zlink 기준으로 전환 또는 분리
- 최소 검증 항목:
  - 기본 송수신
  - self-routing
  - 대용량 payload
  - timeout/중단/Dispose 시나리오
  - 수신 루프 종료 안정성
- 통합/E2E 회귀 수행:
  - `tests/e2e/PlayHouse.E2E`
  - `tests/benchmark/benchmark_cs`
  - `tests/benchmark/benchmark_ss`

산출물:

- 단위/통합 테스트 통과 로그
- 실패 케이스 원인 및 수정 기록

### 단계 5: 성능 게이트 (1차 완료 조건)

실행 매트릭스:

- 모드: `request-async`, `request-callback`, `send`
- 메시지 크기: 최소 `64`, `1024`, `65536`
- 기준 스크립트:
  - CS Gate: `tests/benchmark/benchmark_cs/run-single.sh`
  - SS Gate: `tests/benchmark/benchmark_ss/run-single.sh`

권장 합격 기준:

- 처리량: CS/SS 각각 baseline 대비 95% 이상
- 평균 지연: CS/SS 각각 baseline 대비 10% 이내 증가
- 타임아웃/에러율: CS/SS 각각 baseline과 동등 수준
- 장시간(예: 30분+) soak 실행 시 크래시/메모리 누수/스레드 누수 없음

게이트 결과:

- Go: 단계 6 및 CS Stream 2차 진행
- No-Go: 병목 분석 후 단계 2~4 재실행

### 단계 6: 정리 및 NuGet 전환 준비

- Zlink 안정화 완료 후 `Net.Zmq` 의존 제거
- 코드/문서의 `ZMQ` 용어를 `S2S transport` 또는 `Zlink` 기준으로 정리
- NuGet 배포 전 체크:
  - `dotnet pack` 산출물 검증
  - 런타임별 네이티브 파일 포함 여부 검증
  - 로컬 프로젝트 참조 -> `PackageReference` 교체

산출물:

- 의존성 정리 PR
- NuGet 전환 PR

### 단계 7: 2차 계획 - CS 소켓을 Zlink Stream으로 전환

진행 조건:

- 단계 5 성능 게이트 통과
- 1차 운영 안정성 확인

핵심 작업:

- 클라이언트 transport 추상화 지점 식별 및 Stream adapter 추가
- Stream 연결 이벤트 처리(접속 이벤트 프레임/ID 프레임) 설계 반영
- 기존 인증/세션 lifecycle과 충돌 없는지 검증
- `benchmark_cs` 기준 성능 및 회귀 측정

권장 합격 기준:

- 기존 TCP 경로 대비 기능 회귀 없음
- 목표 부하에서 처리량/지연 수용 가능
- 연결 churn 및 재연결 시 안정성 확보

## 6. 파일 단위 작업 체크리스트

필수 변경:

- `src/PlayHouse/PlayHouse.csproj`
- `src/PlayHouse/Runtime/ServerMesh/PlaySocket/IPlaySocket.cs`
- `src/PlayHouse/Runtime/ServerMesh/PlaySocket/ZmqPlaySocket.cs` (제거 또는 fallback 유지)
- `src/PlayHouse/Runtime/ServerMesh/PlaySocket/ZlinkPlaySocket.cs` (신규)
- `src/PlayHouse/Runtime/ServerMesh/Communicator/PlayCommunicator.cs`
- `src/PlayHouse/Runtime/ServerMesh/Communicator/XServerCommunicator.cs`
- `tests/unit/PlayHouse.Unit/ZmqSendRecvTest.cs` (전환/개편)

검토 필요:

- `tests/PlayHouse.Tests.Performance/Test.csproj`
- `tests/PlayHouse.Tests.Performance/Program.cs`
- `docs/guides/configuration.md`
- `docs/internals/socket-transport.md`
- `docs/architecture/overview.md`

동기화 확인:

- `servers/dotnet/*` 미러 경로와의 반영 필요 여부 확인 (솔루션 사용 대상 기준으로 결정)

## 7. 리스크와 대응

리스크:

- 네이티브 라이브러리 로딩 실패 (`libzlink` 경로/RID 불일치)
- `RcvMore` 기반 멀티파트 처리 누락으로 프레임 정렬 깨짐
- 종료 처리 차이로 수신 스레드 hang 발생
- self-connect 또는 reconnect 타이밍 차이로 초기 패킷 유실
- 성능 열화(특정 모드/메시지 크기)

대응:

- 시작 시 네이티브 로딩 self-check 추가
- 멀티파트 처리 유닛 테스트 강화
- `Stop -> Context.Shutdown -> Join` 종료 순서 고정
- warm-up/재시도 로직 유지 및 계측 강화
- fallback 경로(한시적) 유지 후 단계적 제거

## 8. 의사결정 게이트 (Go/No-Go)

Go 조건:

- 단위/통합/E2E green
- 성능 기준 충족
- 장시간 안정성 검증 통과
- 롤백 절차 문서화 완료

No-Go 조건:

- 성능 기준 미달
- 수신 루프 종료 불안정
- 네이티브 로딩 이슈 반복

## 9. 실행 순서 제안

1. Baseline 측정 및 기록
2. 로컬 Zlink 의존성 연결
3. `ZlinkPlaySocket` 구현 + Communicator 연동
4. 단위/통합/E2E 회귀
5. `benchmark_cs` + `benchmark_ss` 성능 게이트
6. Net.Zmq 제거 및 문서 정리
7. NuGet 전환
8. CS Stream 2차 전환 착수

## 10. 실행 커맨드 기준

```bash
# 전체 빌드
dotnet build PlayHouse.sln -c Release

# 단위 테스트
dotnet test tests/unit/PlayHouse.Unit -c Release

# E2E 테스트
dotnet test tests/e2e/PlayHouse.E2E -c Release

# Baseline 아카이브 루트
REPO_ROOT="$(pwd)"
BASELINE_TAG="$(date +%Y%m%d_%H%M%S)"
BASELINE_ROOT="$REPO_ROOT/doc/plan/zlink-migration/results/$BASELINE_TAG"
mkdir -p "$BASELINE_ROOT/cs/raw" "$BASELINE_ROOT/ss/raw"
RESULT_NOTE="$REPO_ROOT/doc/plan/zlink-migration/baseline-results.md"

# Baseline (CS 최대 부하 프로파일, size: 64/1024/65536)
CS_STAMP="$(mktemp)"; touch "$CS_STAMP"
cd "$REPO_ROOT/tests/benchmark/benchmark_cs"
for size in 64 1024 65536; do
  ./run-single.sh --ccu 10000 --size "$size" --mode send --inflight 30
done
find "$REPO_ROOT/tests/benchmark/benchmark_cs/benchmark-results" -type f -newer "$CS_STAMP" -exec cp --parents {} "$BASELINE_ROOT/cs/raw" \;
rm -f "$CS_STAMP"

# Baseline (SS 최대 부하 프로파일, size: 64/1024/65536)
cd "$REPO_ROOT/tests/benchmark/benchmark_ss"
for size in 64 1024 65536; do
  ./run-single.sh --ccu 10000 --size "$size" --mode send --inflight 10 \
    | tee "$BASELINE_ROOT/ss/raw/ss_size_${size}.console.log"
done

# 결과 문서에 실행 이력 추가 (수치 표는 수동 입력)
{
  echo ""
  echo "## $BASELINE_TAG"
  echo "- Raw archive: \`doc/plan/zlink-migration/results/$BASELINE_TAG\`"
  echo "- CS command: \`./run-single.sh --ccu 10000 --size <64|1024|65536> --mode send --inflight 30\`"
  echo "- SS command: \`./run-single.sh --ccu 10000 --size <64|1024|65536> --mode send --inflight 10\`"
} >> "$RESULT_NOTE"

echo "Baseline archive: $BASELINE_ROOT"
```
