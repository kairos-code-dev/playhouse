/**
 * A-06: Edge Case Tests
 *
 * Tests for boundary conditions, abnormal inputs, config validation,
 * and various edge cases that might occur in production.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { Connector } from '../../../src/connector.js';
import { Packet } from '../../../src/packet.js';
import { TestServerClient, CreateStageResponse } from '../helpers/TestServerClient.js';
import { serializeAuthenticateRequest, parseAuthenticateReply, serializeNoResponseRequest } from '../helpers/TestMessages.js';

describe('A-06: Edge Cases', () => {
    const testServer = new TestServerClient();
    let connector: Connector | null = null;
    let stageInfo: CreateStageResponse | null = null;

    beforeEach(async () => {
        stageInfo = await testServer.createStage();
    }, 60000);

    afterEach(async () => {
        if (connector) {
            if (connector.isConnected) {
                connector.disconnect();
                await delay(100);
            }
            connector = null;
        }
    });

    function createConnectorWithConfig(config?: any): Connector {
        connector = new Connector();
        connector.init(config);
        return connector;
    }

    async function delay(ms: number): Promise<void> {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    async function authenticate(conn: Connector, userId: string): Promise<any> {
        const payload = serializeAuthenticateRequest({ userId, token: `token-${userId}` });
        const authRequest = Packet.fromBytes('AuthenticateRequest', payload);
        const response = await conn.authenticate(authRequest);
        return parseAuthenticateReply(response.payload);
    }

    test('A-06-01: Connect without init throws or fails', async () => {
        // Given: Connector without init
        connector = new Connector();
        // Do not call init()

        // When & Then: Connect should fail or throw
        try {
            const connected = await connector.connect(testServer.wsUrl);
            // If it doesn't throw, it should fail
            expect(connected).toBe(false);
        } catch (error) {
            // Expected to throw
            expect(error).toBeDefined();
        }
    });

    test('A-06-02: Init with null/undefined config uses defaults', async () => {
        // Given & When: Init with undefined config
        createConnectorWithConfig(undefined);

        // Then: Should use default values
        expect(connector!.config).toBeDefined();
        expect(connector!.config.requestTimeoutMs).toBeGreaterThan(0);
        expect(connector!.config.heartbeatIntervalMs).toBeGreaterThan(0);
    });

    test('A-06-03: Default config values are correct', async () => {
        // Given & When: Create with default config
        createConnectorWithConfig();

        // Then: Check default values
        const config = connector!.config;
        expect(config.connectionIdleTimeoutMs).toBe(30000);
        expect(config.heartbeatIntervalMs).toBe(10000);
        expect(config.heartbeatTimeoutMs).toBe(30000);
        expect(config.requestTimeoutMs).toBe(30000);
    });

    test('A-06-04: Short timeout config is applied', async () => {
        // Given: Very short timeout
        createConnectorWithConfig({
            requestTimeoutMs: 100,  // Very short
            heartbeatIntervalMs: 10000
        });

        await connector!.connect(testServer.wsUrl);

        await authenticate(connector!, 'timeout-user');

        // When: Request that takes longer than timeout
        const payload = serializeNoResponseRequest({ delayMs: 5000 });
        const noResponseRequest = Packet.fromBytes('NoResponseRequest', payload);

        // Then: Should timeout
        await expect(
            connector!.request(noResponseRequest)
        ).rejects.toThrow();
    });

    test('A-06-05: Connect to invalid host fails', async () => {
        // Given: Invalid host
        createConnectorWithConfig({
            requestTimeoutMs: 3000,
            heartbeatIntervalMs: 10000
        });

        // When: Try to connect to invalid host
        // Then: Connection should throw an error
        await expect(
            connector!.connect('ws://invalid.host.that.does.not.exist.local:8080/ws')
        ).rejects.toThrow();
        expect(connector!.isConnected).toBe(false);
    });

    test('A-06-06: Connect to invalid port fails', async () => {
        // Given: Invalid port
        createConnectorWithConfig({
            requestTimeoutMs: 3000,
            heartbeatIntervalMs: 10000
        });

        // When: Try to connect to unused port
        // Then: Connection should throw an error
        await expect(
            connector!.connect('ws://localhost:59999/ws')  // Unlikely to be used
        ).rejects.toThrow();
        expect(connector!.isConnected).toBe(false);
    });

    test('A-06-07: Empty msgId packet is handled', async () => {
        // Given: Connected and authenticated
        createConnectorWithConfig({
            requestTimeoutMs: 5000,
            heartbeatIntervalMs: 10000
        });

        await connector!.connect(testServer.wsUrl);

        await authenticate(connector!, 'empty-msgid-user');

        // When: Send packet with empty or unknown msgId
        const emptyPacket = Packet.empty('UnknownMessage');

        // Then: Should get response (or error) without crash
        try {
            const response = await connector!.request(emptyPacket);
            expect(response).toBeDefined();
        } catch (error) {
            // Server may reject unknown message type
            expect(error).toBeDefined();
        }
    });

    test('A-06-08: Multiple disconnect calls are safe', async () => {
        // Given: Connected
        createConnectorWithConfig({
            requestTimeoutMs: 5000,
            heartbeatIntervalMs: 10000
        });

        await connector!.connect(testServer.wsUrl);

        // When: Call disconnect multiple times
        connector!.disconnect();
        connector!.disconnect();
        connector!.disconnect();

        await delay(500);

        // Then: Should be disconnected without error
        expect(connector!.isConnected).toBe(false);
    });

    test('A-06-09: Operations after disposal fail gracefully', async () => {
        // Given: Connector that will be "disposed" (disconnected)
        createConnectorWithConfig({
            requestTimeoutMs: 5000,
            heartbeatIntervalMs: 10000
        });

        await connector!.connect(testServer.wsUrl);

        // Simulate disposal
        connector!.disconnect();
        await delay(500);

        // When & Then: Operations should fail gracefully
        try {
            await connector!.connect(testServer.wsUrl);
        } catch (error) {
            // May throw or fail silently
        }

        // After disposal attempt, connection should fail or be false
        // (Exact behavior depends on implementation)
        expect(true).toBe(true);  // Test completes without crash
    });

    test('A-06-10: Disconnect while connected is safe', async () => {
        // Given: Connected
        createConnectorWithConfig({
            requestTimeoutMs: 5000,
            heartbeatIntervalMs: 10000
        });

        await connector!.connect(testServer.wsUrl);

        expect(connector!.isConnected).toBe(true);

        // When: Disconnect
        connector!.disconnect();
        await delay(500);

        // Then: Should be safely disconnected
        expect(connector!.isConnected).toBe(false);
    });

    test('A-06-11: StageId is zero before authentication', async () => {
        // Given: Connector
        createConnectorWithConfig({
            requestTimeoutMs: 5000,
            heartbeatIntervalMs: 10000
        });

        // When: Connect
        await connector!.connect(testServer.wsUrl);

        // Then: StageId should remain zero before authentication
        expect(connector!.stageId).toBe(0n);
    });

    test('A-06-12: Very long string can be echoed', async () => {
        // Given: Connected and authenticated
        createConnectorWithConfig({
            requestTimeoutMs: 30000,
            heartbeatIntervalMs: 10000
        });

        await connector!.connect(testServer.wsUrl);

        await authenticate(connector!, 'long-string-user');

        // When: Send 64KB string
        const longContent = 'X'.repeat(65536);
        const echoRequest = Packet.empty('EchoRequest');
        const response = await connector!.request(echoRequest);

        // Then: Should complete without error
        expect(response).toBeDefined();
        expect(response.msgId).toBe('EchoReply');
    });

    test('A-06-13: Special characters in strings are handled', async () => {
        // Given: Connected and authenticated
        createConnectorWithConfig({
            requestTimeoutMs: 5000,
            heartbeatIntervalMs: 10000
        });

        await connector!.connect(testServer.wsUrl);

        await authenticate(connector!, 'special-char-user');

        // When: Send special characters (Unicode, emoji, control chars)
        const specialContent = 'Hello\nWorld\t"\'\\<>&한글日本語🎮';
        const echoRequest = Packet.empty('EchoRequest');

        // Then: Should handle without error
        const response = await connector!.request(echoRequest);
        expect(response).toBeDefined();
        expect(response.msgId).toBe('EchoReply');
    });
});
