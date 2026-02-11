/**
 * A-05: Multiple Connector Tests
 *
 * Tests for using multiple Connector instances simultaneously.
 * Verifies independent connections and communication.
 * Useful for multi-server connections or simulating multiple clients in tests.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { Connector } from '../../../src/connector.js';
import { Packet } from '../../../src/packet.js';
import { TestServerClient, CreateStageResponse } from '../helpers/TestServerClient.js';
import { serializeAuthenticateRequest, parseAuthenticateReply, serializeEchoRequest, parseEchoReply, serializeBroadcastRequest, parseBroadcastNotify } from '../helpers/TestMessages.js';

describe('A-05: Multiple Connectors', () => {
    const testServer = new TestServerClient();
    const connectors: Connector[] = [];
    const stages: CreateStageResponse[] = [];

    beforeEach(async () => {
        // Create 5 independent stages
        for (let i = 0; i < 5; i++) {
            stages.push(await testServer.createStage());
        }
    }, 60000);

    afterEach(async () => {
        // Cleanup all connectors
        for (const connector of connectors) {
            if (connector.isConnected) {
                connector.disconnect();
                await delay(50);
            }
        }
        connectors.length = 0;
        stages.length = 0;
    });

    function createConnector(requestTimeoutMs = 10000): Connector {
        const connector = new Connector();
        connector.init({
            requestTimeoutMs,
            heartbeatIntervalMs: 10000
        });
        connectors.push(connector);
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

    test('A-05-01: Multiple connectors can connect simultaneously', async () => {
        // Given: 5 connectors
        const testConnectors = Array.from({ length: 5 }, () => createConnector());

        // When: Connect all simultaneously
        const connectPromises = testConnectors.map((connector, index) =>
            connector.connect(testServer.wsUrl)
        );

        const results = await Promise.all(connectPromises);

        // Then: All connections should succeed
        expect(results.every(r => r === true)).toBe(true);
        expect(testConnectors.every(c => c.isConnected)).toBe(true);
    });

    test('A-05-02: Multiple connectors can authenticate independently', async () => {
        // Given: 3 connected connectors
        const testConnectors = Array.from({ length: 3 }, () => createConnector());

        for (let i = 0; i < testConnectors.length; i++) {
            await testConnectors[i].connect(testServer.wsUrl);
        }

        // When: Authenticate each with different user
        const authPromises = testConnectors.map(async (connector, index) => {
            return await authenticate(connector, `multi-user-${index}`);
        });

        const authResults = await Promise.all(authPromises);

        // Then: All authentications should succeed
        expect(testConnectors.every(c => c.isAuthenticated)).toBe(true);
        authResults.forEach((result, index) => {
            expect(result).toBeDefined();
            expect(result.success).toBe(true);
        });
    });

    test('A-05-03: Multiple connectors can send requests simultaneously', async () => {
        // Given: 3 connected and authenticated connectors
        const testConnectors = Array.from({ length: 3 }, () => createConnector());

        for (let i = 0; i < testConnectors.length; i++) {
            await testConnectors[i].connect(testServer.wsUrl);

            await authenticate(testConnectors[i], `multi-user-${i}`);
        }

        // When: Send echo requests simultaneously
        const requestPromises = testConnectors.map(async (connector, index) => {
            const payload = serializeEchoRequest({ content: `Echo ${index}`, sequence: index });
            const echoRequest = Packet.fromBytes('EchoRequest', payload);
            return await connector.request(echoRequest);
        });

        const responses = await Promise.all(requestPromises);

        // Then: All requests should complete
        expect(responses).toHaveLength(3);
        responses.forEach((response, index) => {
            expect(response.msgId).toBe('EchoReply');
            const reply = parseEchoReply(response.payload);
            expect(reply.content).toBe(`Echo ${index}`);
            expect(reply.sequence).toBe(index);
        });
    });

    test('A-05-04: Disconnecting one connector does not affect others', async () => {
        // Given: 3 connected and authenticated connectors
        const testConnectors = Array.from({ length: 3 }, () => createConnector());

        for (let i = 0; i < testConnectors.length; i++) {
            await testConnectors[i].connect(testServer.wsUrl);

            await authenticate(testConnectors[i], `multi-user-${i}`);
        }

        // When: Disconnect first connector
        testConnectors[0].disconnect();
        await delay(500);

        // Then: First is disconnected, others remain connected
        expect(testConnectors[0].isConnected).toBe(false);
        expect(testConnectors[1].isConnected).toBe(true);
        expect(testConnectors[2].isConnected).toBe(true);

        // Others can still make requests
        for (let i = 1; i < testConnectors.length; i++) {
            const payload = serializeEchoRequest({ content: `Echo ${i}`, sequence: i });
            const echoRequest = Packet.fromBytes('EchoRequest', payload);
            const response = await testConnectors[i].request(echoRequest);
            expect(response.msgId).toBe('EchoReply');
            const reply = parseEchoReply(response.payload);
            expect(reply.content).toBe(`Echo ${i}`);
        }
    });

    test('A-05-05: Multiple connectors can connect to same stage', async () => {
        // Given: Same stage for all connectors
        const sharedStage = stages[0];
        const testConnectors = Array.from({ length: 3 }, () => createConnector());

        // When: Connect all to same stage
        for (let i = 0; i < testConnectors.length; i++) {
            await testConnectors[i].connect(testServer.wsUrl);

            await authenticate(testConnectors[i], `multi-user-${i}`);
        }

        // Then: All can make independent requests
        for (let i = 0; i < testConnectors.length; i++) {
            const payload = serializeEchoRequest({ content: `Echo ${i}`, sequence: i });
            const echoRequest = Packet.fromBytes('EchoRequest', payload);
            const response = await testConnectors[i].request(echoRequest);
            expect(response.msgId).toBe('EchoReply');
            const reply = parseEchoReply(response.payload);
            expect(reply.content).toBe(`Echo ${i}`);
        }
    });

    test('A-05-06: Stress test with many connectors', async () => {
        // Given: 10 connectors
        const connectorCount = 10;
        const testConnectors: Connector[] = [];
        const extraStages: CreateStageResponse[] = [];

        // Create additional stages
        for (let i = 0; i < connectorCount; i++) {
            extraStages.push(await testServer.createStage());
        }

        // When: Connect all simultaneously
        const connectPromises: Promise<boolean>[] = [];
        for (let i = 0; i < connectorCount; i++) {
            const connector = createConnector();
            testConnectors.push(connector);

            const stage = extraStages[i];
            connectPromises.push(
                connector.connect(testServer.wsUrl)
            );
        }

        const connectResults = await Promise.all(connectPromises);

        // Then: All connections should succeed
        expect(connectResults.every(r => r === true)).toBe(true);

        // Disconnect all
        testConnectors.forEach(c => c.disconnect());
        await delay(500);

        // All should be disconnected
        expect(testConnectors.every(c => !c.isConnected)).toBe(true);
    });

    test('A-05-07: Each connector\'s events are independent', async () => {
        // Given: 2 connectors with separate event handlers
        const connector1 = createConnector();
        const connector2 = createConnector();

        const connector1Received: string[] = [];
        const connector2Received: string[] = [];

        connector1.onReceive = (packet: Packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                const notify = parseBroadcastNotify(packet.payload);
                connector1Received.push(notify.data);
            }
        };

        connector2.onReceive = (packet: Packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                const notify = parseBroadcastNotify(packet.payload);
                connector2Received.push(notify.data);
            }
        };

        // Connect to different stages
        await connector1.connect(testServer.wsUrl);
        await connector2.connect(testServer.wsUrl);

        await authenticate(connector1, 'connector1-user');
        await authenticate(connector2, 'connector2-user');

        // When: Each sends broadcast request
        const payload1 = serializeBroadcastRequest({ content: 'From Connector 1' });
        const broadcast1 = Packet.fromBytes('BroadcastRequest', payload1);
        connector1.send(broadcast1);

        const payload2 = serializeBroadcastRequest({ content: 'From Connector 2' });
        const broadcast2 = Packet.fromBytes('BroadcastRequest', payload2);
        connector2.send(broadcast2);

        // Wait for push messages
        const deadline = Date.now() + 5000;
        while ((connector1Received.length === 0 || connector2Received.length === 0) && Date.now() < deadline) {
            connector1.mainThreadAction();
            connector2.mainThreadAction();
            await delay(10);
        }

        // Then: Each connector receives its own messages
        expect(connector1Received.length).toBeGreaterThan(0);
        expect(connector2Received.length).toBeGreaterThan(0);
    });
});
