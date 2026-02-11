/**
 * C-07: Heartbeat Automatic Handling Tests
 *
 * Tests that the Connector automatically sends heartbeats
 * and maintains connections over time.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Connector } from '../../../src/connector.js';
import { Packet } from '../../../src/packet.js';
import { serializeAuthenticateRequest } from '../helpers/TestMessages.js';

describe('C-07: Heartbeat Automatic Handling', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('C-07-01: Connection maintained over time without disconnection', { timeout: 60000 }, async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('heartbeatUser');

        expect(testContext['connector']!.isConnected).toBe(true);

        // When: Wait 5 seconds without any action (Heartbeat should be sent automatically)
        const startTime = Date.now();
        while (Date.now() - startTime < 5000) {
            testContext['connector']!.mainThreadAction();
            await testContext['delay'](50);
        }

        // Then: Connection should remain active
        expect(testContext['connector']!.isConnected).toBe(true);
        expect(testContext['connector']!.isAuthenticated).toBe(true);
    });

    test('C-07-02: Echo requests work correctly during heartbeat period', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('echoHeartbeatUser');

        // When: Wait 2 seconds then send echo request
        await testContext['delay'](2000);
        const echoReply = await testContext['echo']('After Heartbeat', 1);

        // Then: Echo should work normally
        expect(echoReply).toBeDefined();
        expect(echoReply.content).toBe('After Heartbeat');

        // Connection should remain active
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('C-07-03: Short heartbeat interval works correctly', { timeout: 10000 }, async () => {
        // Given: Short heartbeat interval (1 second)
        const connector = new Connector();
        connector.init({
            requestTimeoutMs: 5000,
            heartbeatIntervalMs: 1000 // 1 second heartbeat
        });
        testContext['connector'] = connector;

        // When: Connect and authenticate
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('shortHeartbeatUser');

        // Wait 3 seconds (3 heartbeats will be sent)
        const startTime = Date.now();
        while (Date.now() - startTime < 3000) {
            connector.mainThreadAction();
            await testContext['delay'](50);
        }

        // Then: Connection should be maintained
        expect(connector.isConnected).toBe(true);

        // Echo should work normally
        const echoReply = await testContext['echo']('Short Interval Test', 1);
        expect(echoReply.content).toBe('Short Interval Test');
    });

    test('C-07-04: Message transmission works correctly during heartbeat', { timeout: 15000 }, async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('transmitUser');

        // When: Send echo requests periodically for 10 seconds (along with heartbeat)
        for (let i = 1; i <= 5; i++) {
            const echoReply = await testContext['echo'](`Message ${i}`, i);
            expect(echoReply.content).toBe(`Message ${i}`);

            await testContext['delay'](2000); // 2 seconds between messages
        }

        // Then: All messages should be sent successfully and connection maintained
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('C-07-05: OnDisconnect event does not trigger during normal operation', { timeout: 15000 }, async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('noDisconnectUser');

        let disconnectTriggered = false;
        testContext['connector']!.onDisconnect = () => {
            disconnectTriggered = true;
        };

        // When: Wait 5 seconds
        const startTime = Date.now();
        while (Date.now() - startTime < 5000) {
            testContext['connector']!.mainThreadAction();
            await testContext['delay'](50);
        }

        // Then: OnDisconnect should not be triggered
        expect(disconnectTriggered).toBe(false);
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('C-07-06: Multiple connectors can maintain heartbeat simultaneously', { timeout: 30000 }, async () => {
        // Given: Create 3 connectors
        const stage1 = await testContext['testServer'].createStage('TestStage');
        const stage2 = await testContext['testServer'].createStage('TestStage');
        const stage3 = await testContext['testServer'].createStage('TestStage');

        const connector1 = new Connector();
        const connector2 = new Connector();
        const connector3 = new Connector();

        try {
            connector1.init({ requestTimeoutMs: 5000, heartbeatIntervalMs: 10000 });
            connector2.init({ requestTimeoutMs: 5000, heartbeatIntervalMs: 10000 });
            connector3.init({ requestTimeoutMs: 5000, heartbeatIntervalMs: 10000 });

            const wsUrl = testContext['testServer'].wsUrl;
            await connector1.connect(wsUrl);
            await connector2.connect(wsUrl);
            await connector3.connect(wsUrl);

            // Authenticate all connectors using protobuf format
            const authPayload1 = serializeAuthenticateRequest({ userId: 'user1', token: 'token1' });
            const authPayload2 = serializeAuthenticateRequest({ userId: 'user2', token: 'token2' });
            const authPayload3 = serializeAuthenticateRequest({ userId: 'user3', token: 'token3' });

            await connector1.authenticate(Packet.fromBytes('AuthenticateRequest', authPayload1));
            await connector2.authenticate(Packet.fromBytes('AuthenticateRequest', authPayload2));
            await connector3.authenticate(Packet.fromBytes('AuthenticateRequest', authPayload3));

            expect(connector1.isAuthenticated).toBe(true);
            expect(connector2.isAuthenticated).toBe(true);
            expect(connector3.isAuthenticated).toBe(true);

            // When: Wait 5 seconds (all connectors' heartbeats should work)
            const startTime = Date.now();
            while (Date.now() - startTime < 5000) {
                connector1.mainThreadAction();
                connector2.mainThreadAction();
                connector3.mainThreadAction();
                await testContext['delay'](50);
            }

            // Then: All connectors should remain connected
            expect(connector1.isConnected).toBe(true);
            expect(connector2.isConnected).toBe(true);
            expect(connector3.isConnected).toBe(true);
        } finally {
            connector1.disconnect();
            connector2.disconnect();
            connector3.disconnect();
        }
    });
});
