# Connector Testing Guide

이 문서는 PlayHouse 커넥터 통합 테스트 실행 방법을 정리합니다.

## Prerequisites

- Docker, Docker Compose
- .NET SDK 8+
- Node.js 18+ (JavaScript 커넥터 테스트)
- JDK 17+ (Java 커넥터 테스트)

## TLS 인증서 준비

테스트 서버는 TLS 포트(HTTPS/WSS, TCP+TLS)를 사용하므로 인증서가 필요합니다.

```bash
bash connectors/test-server/certs/generate-certs.sh
```

생성 파일:

- `connectors/test-server/certs/server.crt`
- `connectors/test-server/certs/server.pfx`
- `connectors/test-server/certs/server.key` (Git 제외)

## Quick Start

전체 커넥터 테스트:

```bash
bash connectors/run-all-tests.sh
```

개별 커넥터 테스트:

```bash
bash connectors/csharp/run-tests.sh
bash connectors/javascript/run-tests.sh
bash connectors/java/run-tests.sh
```

## Port Map

- C#: HTTP `18080`, HTTPS `18443`, TCP `18001`, TCP+TLS `18002`
- Java: HTTP `28080`, HTTPS `28443`, TCP `28001`, TCP+TLS `28002`
- JavaScript: HTTP/WS `38080`, HTTPS/WSS `38443`, TCP `38001`, TCP+TLS `38002`

## Manual Run (선택)

### C# Integration Tests

```bash
TEST_SERVER_HOST=127.0.0.1 \
TEST_SERVER_HTTP_PORT=18080 \
TEST_SERVER_HTTPS_PORT=18443 \
TEST_SERVER_TCP_PORT=18001 \
TEST_SERVER_TCP_TLS_PORT=18002 \
dotnet test connectors/csharp/tests/PlayHouse.Connector.IntegrationTests
```

### JavaScript Integration Tests

```bash
cd connectors/javascript
TEST_SERVER_HOST=127.0.0.1 \
TEST_SERVER_HTTP_PORT=38080 \
TEST_SERVER_WS_PORT=38080 \
TEST_SERVER_HTTPS_PORT=38443 \
TEST_SERVER_WSS_PORT=38443 \
npm run test:integration
```

### Java Integration Tests

```bash
cd connectors/java
TEST_SERVER_HOST=127.0.0.1 \
TEST_SERVER_HTTP_PORT=28080 \
TEST_SERVER_HTTPS_PORT=28443 \
TEST_SERVER_TCP_PORT=28001 \
TEST_SERVER_TCP_TLS_PORT=28002 \
./gradlew integrationTest
```

## Troubleshooting

- `TLS enabled but certificate path is not set`
  - `connectors/test-server/certs/server.pfx`가 있는지 확인하고 인증서 생성 스크립트를 다시 실행하세요.
- 테스트 서버 로그 확인
  - 각 커넥터 스크립트 실패 시 자동으로 `docker-compose logs`를 출력합니다.
- 수동 정리
  - `docker-compose -f connectors/<connector>/docker-compose.test.yml down -v`
