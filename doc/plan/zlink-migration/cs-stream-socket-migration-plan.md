# CS 소켓 Zlink Stream 전환 상세 계획

- 작성일: 2026-02-13
- 작성자: Codex
- 연계 문서: `doc/plan/zlink-migration/zeromq-to-zlink-migration-plan.md`
- 성능 비교 기준 문서: `doc/plan/zlink-migration/baseline-results.md`
- 목표: **호환성 제약 없이**, CS 전송 스택을 `Zlink Stream` 기반으로 재설계하여 성능과 유지보수성을 최적화

## 1. 배경 및 목표

현재 PlayHouse의 CS 경로는 서버 측 `Runtime/ClientTransport/Tcp/*` + 커넥터별 TCP/WebSocket 구현으로 동작한다.  
이번 단계는 서버 간 통신(SS) 전환 이후 2차 작업으로, **클라이언트 연결 경로(CS)도 Zlink Stream 기반으로 통일**하여 전송 스택을 단순화하고 성능/운영 일관성을 확보한다.

핵심 목표:

- 성능(throughput/latency/error rate) 최우선으로 구조 재설계
- 유지보수성(코드 단순성, 책임 분리, 테스트 용이성) 극대화
- 필요 시 기존 인터페이스/동작과 비호환 변경 허용
- TCP/TLS/WS/WSS 전 경로를 Zlink Stream 기반으로 통합
- 마이그레이션 완료 시 Legacy 엔진/플래그/코드 경로 완전 제거

확정 결정사항:

- Legacy 경로는 최종 산출물에서 완전 제거
- WS/WSS 경로도 이번 전환 범위에 포함
- 성능 비교는 테스트 오차를 반영해 10% 허용 오차 기준 적용

## 2. 비목표(이번 단계에서 하지 않음)

- 인증/게임 메시지 프로토콜 변경
- Stage/Actor 라우팅 모델 변경
- 신규 전송 프로토콜(QUIC 등) 추가

## 3. 작업 원칙

- 호환성 비고려:
  - 기존 서버/커넥터 API 및 기존 동작과의 완전 호환은 목표에서 제외
  - 더 나은 구조를 위해 breaking change를 허용
- 성능 최우선:
  - 데이터 경로 복사 최소화, 불필요한 추상화 제거, 병목 지점 계측을 기본 정책으로 적용
- 유지보수성 동시 최적화:
  - 단일 책임 구조, 테스트 가능한 경계, 명확한 에러 모델을 우선
  - 복잡도만 높이는 미세 최적화는 배제

## 4. 현행(AS-IS) 구조 요약

서버:

- `src/PlayHouse/Runtime/ClientTransport/Tcp/TcpTransportServer.cs`
- `src/PlayHouse/Runtime/ClientTransport/Tcp/TcpTransportSession.cs`
- `src/PlayHouse/Runtime/ClientTransport/TransportServerBuilder.cs`
- `src/PlayHouse/Core/Play/Bootstrap/PlayServerOption.cs` (TCP/WS 옵션 표면)

커넥터:

- C#: `connectors/csharp/src/PlayHouse.Connector/Network/TcpConnection.cs`
- C++: `connectors/cpp/src/tcp_connection.cpp`
- JavaScript: WebSocket 중심(`connectors/javascript/src/connector.ts`)
- Java: TCP/WS 선택(`connectors/java/...`)
- Unreal/Unity: 상위 커넥터 또는 브리지 경유

## 5. 목표(TO-BE) 아키텍처

### 5.1 서버 런타임

- `ZlinkStreamTransportServer`(신규): 리스닝/세션 수명 관리
- `ZlinkStreamTransportSession`(신규): 수신 파싱/송신 큐/종료 처리
- 필요 시 `ITransportServer`, `ITransportSession` 계약을 재정의
- `TransportServerBuilder`에서 TCP 계열 생성기를 내부적으로 Zlink Stream 구현으로 라우팅

### 5.2 커넥터

- 커넥터별 네트워크 추상화는 유지보수성 중심으로 재정의 가능
- 내부 네트워크 엔진은 Zlink Stream 중심으로 재구성
- `UseWebsocket` 경로를 포함해 TCP/TLS/WS/WSS 전 경로를 동일 원칙으로 전환

### 5.3 전환 스위치(필수)

- 서버/커넥터 모두 최종 상태에서 `SocketEngine` 이중화 제거
- 운영 롤백은 `Legacy` 전환이 아닌 이전 릴리즈 재배포(버전 롤백) 방식으로 운영

## 6. 성공 기준 (Must)

- 성능:
  - Throughput baseline 대비 10% 이내 하락까지 허용(>= baseline 90%)
  - Avg/P95 지연 baseline 대비 +10% 이내 허용
  - 장시간 soak(30분+) 무중단
- 유지보수성:
  - 핵심 전송 경로의 책임 분리 및 테스트 커버리지 확보
  - 신규 구조 기준 설계/운영 문서 업데이트 완료

## 7. 상세 실행 계획 (WBS)

## 단계 0. 사전 고정 및 설계 확정

작업:

- Zlink Stream API 확정(서버/클라이언트용 최소 API 표면 정의)
- 기존 TCP/TLS/WS/WSS 옵션과 ZlinkStream 옵션 매핑표 작성
- 기준 성능/안정성 재측정(CS):
  - `tests/benchmark/benchmark_cs/run-single.sh`
  - 메시지 크기 `64/1024/65536`
- 비교 기준:
  - 모든 성능 비교는 `doc/plan/zlink-migration/baseline-results.md`의 CS Baseline Summary 및 Run History를 기준으로 수행
  - 신규 측정 완료 후 baseline 대비 delta(throughput/avg/p95/error rate)를 동일 포맷으로 기록

산출물:

- `doc/plan/zlink-migration/cs-stream-option-mapping.md`
- baseline 결과 아카이브(`doc/plan/zlink-migration/results/<timestamp>/cs`)

## 단계 1. 서버 전송 엔진 추상화 정리

작업:

- `Runtime/ClientTransport`에 엔진 추상화 지점 추가
- `TransportServerBuilder`가 Zlink Stream 단일 경로로 동작하도록 변경
- 기존 `TcpTransportServer/Session` 제거를 위한 대체 경로와 삭제 계획 확정

산출물:

- Zlink Stream 단일 엔진 경로
- 삭제 대상 코드/설정 목록

## 단계 2. Zlink Stream 서버 구현 추가

작업:

- `ZlinkStreamTransportServer` 신규 구현
- `ZlinkStreamTransportSession` 신규 구현
- 기존 `MessageCodec`, `MessagePool` 재사용(중복 구현 금지)
- 송신/수신/종료 모델을 성능과 유지보수성 관점에서 재정의

검증:

- 단위 테스트: 패킷 경계, 부분 수신, 대용량, 비정상 길이 헤더
- 통합 테스트: PlayServer 부팅 후 인증/요청/푸시/해제

## 단계 3. TLS/WS/WSS 호환 계층 정리

작업:

- TCP/TLS/WS/WSS를 모두 Zlink Stream 기반으로 전환
- 전송별 공통 에러 모델/종료 시퀀스/관측 지표 포맷 통일
- 기존 WebSocket legacy 경로 제거

검증:

- TCP/TLS/WS/WSS E2E 회귀 통과

## 단계 4. 커넥터 전환 (언어별)

우선순위:

1. C# connector
2. C++ connector
3. Java connector
4. JavaScript connector
5. Unity/Unreal 브리지 영향 반영

공통 작업:

- 내부 network class만 교체
- 요청-응답/timeout/callback 정책을 신규 엔진 기준으로 재설계
- 각 런타임의 스레딩 모델은 단순성과 안정성을 기준으로 재정의
- WS/WSS 클라이언트 경로도 동일하게 Zlink Stream 원칙으로 전환

언어별 핵심 파일:

- C#: `connectors/csharp/src/PlayHouse.Connector/Network/TcpConnection.cs`
- C++: `connectors/cpp/src/tcp_connection.cpp`
- Java: `connectors/java/src/main/...` (네트워크 계층)
- JS: `connectors/javascript/src/connector.ts` (필요 시 엔진 어댑터)

## 단계 5. 테스트/벤치 게이트

서버:

- `dotnet build PlayHouse.sln`
- `dotnet test`
- `dotnet test tests/PlayHouse.Tests.Unit`

커넥터:

- `connectors/run-all-tests.sh`
- 언어별 integration 테스트(도커 기반 포함)

성능 게이트:

- CS benchmark 반복 측정(최소 3회)
- 결과 저장: `doc/plan/zlink-migration/results/<timestamp>/cs-stream-gate`
- 비교/판정 문서화:
  - `doc/plan/zlink-migration/baseline-results.md`에 신규 run 섹션 추가
  - `Baseline Comparison` 표 형식으로 baseline 대비 delta를 기록하고 허용 오차(10%) 기준으로 Go/No-Go 판단
- 측정 범위:
  - `send`, `request`, `request-callback`, `auth`, `heartbeat/idle-timeout`
  - TCP/TLS/WS/WSS 전 경로 포함
- 합격 기준:
  - throughput >= baseline 90%
  - avg/p95 latency <= baseline +10%
  - error/timeout rate baseline 대비 +10%p 이내

## 단계 6. 점진 배포 및 기본값 전환

작업:

- Canary 환경에서 Zlink Stream 단일 경로 검증
- 장애 시 이전 버전 재배포(릴리즈 롤백)
- 안정화 후 Legacy 코드/설정/문서 완전 제거

완료 기준(DoD):

- 회귀/성능/soak 테스트 통과
- 운영 장애 없이 1주 이상 안정성 확보
- Legacy 제거 완료(코드/설정/운영 가이드)
- 문서/가이드 업데이트 완료

## 8. 리스크와 대응

- 리스크: Zlink Stream의 backpressure 동작이 기존 TCP와 달라 burst 트래픽에서 지연/드랍 발생
- 대응: 송신 큐 수위 계측, 수위 초과 시 정책(드랍/블로킹) 명시, 부하 시뮬레이션 사전 수행

- 리스크: TLS/WS 조합 경로에서 기능 축소/변경 영향
- 대응: TCP/TLS/WS/WSS를 동일 전환 범위로 묶고, 경로별 최소 동작/성능 기준을 동일하게 강제

- 리스크: 커넥터별 스레딩 모델 차이로 callback race 발생
- 대응: 언어별 main-thread dispatch 계약 테스트 추가

- 리스크: Legacy 완전 제거 후 회귀 발생 시 복구 시간 증가
- 대응: 이전 릴리즈 즉시 재배포 Runbook과 데이터/세션 영향 범위 문서화

## 9. 산출물 목록

- 설계/매핑:
  - `doc/plan/zlink-migration/cs-stream-option-mapping.md`
  - `doc/plan/zlink-migration/cs-stream-interface-change-matrix.md`
- 구현:
  - `src/PlayHouse/Runtime/ClientTransport/Zlink/*` (신규)
  - 커넥터별 네트워크 구현 교체 PR
- 검증:
  - 테스트 로그/성능 리포트/soak 리포트
  - baseline 비교 결과(`doc/plan/zlink-migration/baseline-results.md` 업데이트 포함)
- 운영:
  - 롤백 포함 전환 Runbook

## 10. 권장 일정(안)

- D1~D2: 단계 0~1
- D3~D5: 단계 2
- D6~D7: 단계 3
- D8~D11: 단계 4
- D12~D13: 단계 5
- D14: 단계 6 승인/반영

## 11. 즉시 착수 체크리스트

- Zlink Stream 서버/클라이언트 API 스펙 확정
- `TransportServerBuilder` 엔진 선택 설계 PR 생성
- C# connector PoC 브랜치 분리
- benchmark baseline 재측정 실행
