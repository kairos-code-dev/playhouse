# JavaScript Connector Integration Tests

This directory contains comprehensive integration tests for the PlayHouse JavaScript connector, matching the C# connector test structure.

## Quick Start

### Prerequisites
- Node.js >= 18.0.0
- Docker and docker-compose
- npm dependencies installed

### Running Tests

```bash
# Run all integration tests (includes docker-compose setup)
./run-tests.sh

# Or manually with test server running
docker-compose -f docker-compose.test.yml up -d
export TEST_SERVER_HOST=localhost
export TEST_SERVER_HTTP_PORT=38080
export TEST_SERVER_HTTPS_PORT=38443
export TEST_SERVER_WS_PORT=38001
export TEST_SERVER_WSS_PORT=38443
npm run test:integration
```

## Project Structure

```
tests/integration/
├── README.md                   # This file
├── IMPLEMENTATION_STATUS.md    # Implementation progress tracking
├── setup.ts                    # Global test setup
├── helpers/
│   ├── TestServerClient.ts     # HTTP API client for test server
│   └── BaseIntegrationTest.ts  # Base test class with helpers
├── core/                       # Core functionality tests (73 tests)
│   ├── C01_StageCreation.test.ts
│   ├── C02_WebSocketConnection.test.ts
│   ├── C03_AuthenticationSuccess.test.ts
│   ├── C05_EchoRequestResponse.test.ts
│   ├── C06_PushMessage.test.ts
│   ├── C07_Heartbeat.test.ts
│   ├── C08_Disconnection.test.ts
│   ├── C09_AuthenticationFailure.test.ts
│   ├── C10_RequestTimeout.test.ts
│   └── C11_ErrorResponse.test.ts
└── advanced/                   # Advanced tests (47 tests)
    ├── A01_WebSocketAdvanced.test.ts
    ├── A02_LargePayload.test.ts
    ├── A03_SendMethod.test.ts
    ├── A04_OnErrorEvent.test.ts
    ├── A05_MultipleConnector.test.ts
    └── A06_EdgeCase.test.ts
```

## Test Categories

### Core Tests (C-01 to C-11)

#### C-01: Stage Creation (3 tests)
Tests HTTP API for creating game stages on the test server.

#### C-02: WebSocket Connection (6 tests)
Tests WebSocket connection establishment and state management.

#### C-03: Authentication Success (6 tests)
Tests successful authentication scenarios with various methods.

#### C-05: Echo Request-Response (9 tests)
Tests basic request-response pattern with different payload types.

#### C-06: Push Message (6 tests)
Tests server-initiated push messages (BroadcastNotify).

#### C-07: Heartbeat (6 tests)
Tests automatic heartbeat mechanism for maintaining connections.

#### C-08: Disconnection (8 tests)
Tests graceful disconnection and reconnection scenarios.

#### C-09: Authentication Failure (9 tests)
Tests authentication failure cases with invalid credentials.

#### C-10: Request Timeout (7 tests)
Tests timeout behavior when server doesn't respond.

#### C-11: Error Response (8 tests)
Tests error response handling from server.

### Advanced Tests (A-01 to A-06)

#### A-01: WebSocket Advanced (7 tests)
Advanced WebSocket scenarios including path configuration.

#### A-02: Large Payload (5 tests)
Tests with large data payloads (1MB+) and compression.

#### A-03: Send Method (7 tests)
Tests fire-and-forget send method without response.

#### A-04: OnError Event (8 tests)
Tests error event handling in various failure scenarios.

#### A-05: Multiple Connector (7 tests)
Tests multiple simultaneous connector instances.

#### A-06: Edge Case (13 tests)
Tests edge cases and boundary conditions.

## Helper Classes

### TestServerClient

Provides HTTP API access to the test server:

```typescript
const testServer = new TestServerClient();

// Create a stage
const stageInfo = await testServer.createStage('TestStage');

// Check health
const isHealthy = await testServer.checkHealth();

// Get URLs
const httpUrl = testServer.httpBaseUrl;
const wsUrl = testServer.wsUrl;
```

### BaseIntegrationTest

Base class providing common test utilities:

```typescript
class MyTest extends BaseIntegrationTest {
    async myTest() {
        // Setup
        await this.beforeEach();

        // Connect to stage
        await this.createStageAndConnect();

        // Authenticate
        await this.authenticate('userId', 'token');

        // Send echo request
        const reply = await this.echo('test', 1);

        // Wait for condition
        await this.waitForCondition(() => someCondition);

        // Cleanup
        await this.afterEach();
    }
}
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `TEST_SERVER_HOST` | localhost | Test server hostname |
| `TEST_SERVER_HTTP_PORT` | 8080 | HTTP API port |
| `TEST_SERVER_HTTPS_PORT` | 8443 | HTTPS/WSS port |
| `TEST_SERVER_WS_PORT` | 38001 | WebSocket port |
| `TEST_SERVER_WSS_PORT` | 8443 | WebSocket TLS port |

## Test Server Endpoints

### HTTP API (Port 38080)
- `POST /api/stages` - Create a new stage
- `GET /api/health` - Health check endpoint

### WebSocket (Port 38001)
- `ws://localhost:38001` - WebSocket connection endpoint

## Writing New Tests

### Basic Template

```typescript
import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';

describe('My Test Suite', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('My test case', async () => {
        // Arrange
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('testUser');

        // Act
        const result = await testContext['echo']('test', 1);

        // Assert
        expect(result.content).toBe('test');
    });
});
```

## Common Patterns

### Testing Callback-Based APIs

```typescript
let callbackInvoked = false;
let result: any;

connector.someMethod((response) => {
    result = response;
    callbackInvoked = true;
});

// Wait with mainThreadAction
const completed = await testContext['waitForConditionWithMainThread'](
    () => callbackInvoked,
    5000
);

expect(completed).toBe(true);
expect(result).toBeDefined();
```

### Testing Multiple Connectors

```typescript
const connector1 = new Connector();
const connector2 = new Connector();

try {
    connector1.init();
    connector2.init();

    await connector1.connect(wsUrl);
    await connector2.connect(wsUrl);

    // Your tests here
} finally {
    connector1.disconnect();
    connector2.disconnect();
}
```

### Testing Error Conditions

```typescript
// Expect exception
await expect(async () => {
    await connector.someMethod();
}).rejects.toThrow();

// Expect specific error
await expect(async () => {
    await connector.authenticate(invalidPacket);
}).rejects.toThrow(/authentication failed/i);
```

## Debugging Tests

### View Test Server Logs

```bash
docker-compose -f docker-compose.test.yml logs -f
```

### Run Single Test File

```bash
npm run test:integration -- C01_StageCreation.test.ts
```

### Run Tests in Watch Mode

```bash
npm run test:integration:watch
```

### Enable Verbose Output

```bash
TEST_VERBOSE=true npm run test:integration
```

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-node@v3
        with:
          node-version: '18'
      - run: npm ci
      - run: cd connectors/javascript && ./run-tests.sh
```

## Troubleshooting

### Test Server Not Starting

```bash
# Check if ports are already in use
lsof -i :38080
lsof -i :38001

# View docker logs
docker-compose -f docker-compose.test.yml logs

# Rebuild containers
docker-compose -f docker-compose.test.yml up --build -d
```

### Tests Timing Out

- Increase timeout in test configuration
- Check test server health
- Ensure WebSocket port is accessible

### Connection Refused Errors

- Verify test server is running
- Check firewall settings
- Confirm port mappings in docker-compose.test.yml

## Additional Resources

- [C# Test Reference](../../csharp/tests/PlayHouse.Connector.IntegrationTests/)
- [Test Server Documentation](../../../test-server/README.md)
- [Connector API Documentation](../../README.md)
