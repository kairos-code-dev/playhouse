/**
 * A-01: WebSocket Advanced Tests
 *
 * Advanced WebSocket connection tests including connection events,
 * reconnection, parallel requests, and various edge cases.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Packet } from '../../../src/packet.js';
import { serializeBroadcastRequest, parseBroadcastNotify } from '../helpers/TestMessages.js';

describe('A-01: WebSocket Advanced', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('A-01-01: WebSocket connection succeeds', async () => {
        // Given: Stage is created
        const stageInfo = await testContext['testServer'].createStage();

        // When: Attempt WebSocket connection
        const wsUrl = testContext['testServer'].wsUrl;
        const connected = await testContext['connector']!.connect(wsUrl);

        // Then: Connection should succeed
        expect(connected).toBe(true);
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('A-01-01-01: WSS connection succeeds', async () => {
        // Given: Stage is created
        const stageInfo = await testContext['testServer'].createStage();

        // When: Attempt WSS connection
        const wssUrl = testContext['testServer'].wssUrl;
        const connected = await testContext['connector']!.connect(wssUrl);

        // Then: Connection should succeed
        expect(connected).toBe(true);
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('A-01-02: Authentication succeeds over WebSocket', async () => {
        // Given: Connected via WebSocket
        await testContext['createStageAndConnect']();

        // When: Authenticate
        const authReply = await testContext['authenticate']('ws-advanced-user-1');

        // Then: Authentication should succeed
        expect(testContext['connector']!.isAuthenticated).toBe(true);
        expect(authReply.success).toBe(true);
    });

    test('A-01-03: Echo request-response works over WebSocket', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('ws-advanced-user-2');

        // When: Send echo request
        const echoReply = await testContext['echo']('Hello WebSocket Advanced!', 42);

        // Then: Should receive correct echo response
        expect(echoReply.content).toBe('Hello WebSocket Advanced!');
        expect(echoReply.sequence).toBe(42);
    });

    test('A-01-04: Push messages are received over WebSocket', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('ws-advanced-user-3');

        let receivedPush = false;
        let pushContent = '';

        testContext['connector']!.onReceive = (packet: Packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                receivedPush = true;
                const notify = parseBroadcastNotify(packet.payload);
                pushContent = notify.data;
            }
        };

        // When: Send broadcast request (triggers push)
        const payload = serializeBroadcastRequest({ content: 'Test broadcast' });
        const broadcastRequest = Packet.fromBytes('BroadcastRequest', payload);
        testContext['connector']!.send(broadcastRequest);

        // Wait for push message
        const received = await testContext['waitForConditionWithMainThread'](
            () => receivedPush,
            5000
        );

        // Then: Should receive push notification
        expect(received).toBe(true);
        expect(receivedPush).toBe(true);
    });

    test('A-01-05: Reconnection works after disconnect', async () => {
        // Given: First connection
        await testContext['createStageAndConnect']();
        expect(testContext['connector']!.isConnected).toBe(true);

        // When: Disconnect
        testContext['connector']!.disconnect();
        await testContext['delay'](500);
        expect(testContext['connector']!.isConnected).toBe(false);

        // And: Reconnect to new stage
        const newStage = await testContext['testServer'].createStage();
        const wsUrl = testContext['testServer'].wsUrl;
        const reconnected = await testContext['connector']!.connect(wsUrl);

        // Then: Reconnection should succeed
        expect(reconnected).toBe(true);
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('A-01-06: Parallel requests are handled correctly', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('ws-advanced-user-4');

        // When: Send 10 parallel echo requests
        const requests = Array.from({ length: 10 }, (_, i) => {
            return testContext['echo'](`Echo ${i}`, i);
        });

        // Then: All requests should complete successfully
        const responses = await Promise.all(requests);
        expect(responses).toHaveLength(10);
        responses.forEach((response, i) => {
            expect(response).toBeDefined();
            expect(response.content).toBe(`Echo ${i}`);
            expect(response.sequence).toBe(i);
        });
    });

    test('A-01-07: OnConnect event fires on successful connection', { timeout: 10000 }, async () => {
        // Given: Stage is created
        const stageInfo = await testContext['testServer'].createStage();

        let connectEventFired = false;

        testContext['connector']!.onConnect = () => {
            connectEventFired = true;
        };

        // When: Connect using callback-based method
        const wsUrl = testContext['testServer'].wsUrl;
        testContext['connector']!.connect(wsUrl);

        // Wait for OnConnect event
        const completed = await testContext['waitForConditionWithMainThread'](
            () => connectEventFired,
            5000
        );

        // Then: Event should fire
        expect(completed).toBe(true);
        expect(connectEventFired).toBe(true);
        expect(testContext['connector']!.isConnected).toBe(true);
    });
});
