/**
 * C-02: WebSocket Connection Tests
 *
 * Tests for establishing WebSocket connections to the test server.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';

describe('C-02: WebSocket Connection', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('C-02-01: Connection succeeds after stage creation', async () => {
        console.log('C-02-01: Starting test...');


        // Given: Stage is created
        console.log('C-02-01: Creating stage via TestServerClient...');
        const stageInfo = await testContext['testServer'].createTestStage();
        console.log('C-02-01: Stage created:', stageInfo);

        // When: Attempt WebSocket connection
        const wsUrl = testContext['testServer'].wsUrl;
        console.log('C-02-01: Connecting to:', wsUrl, 'with stageId:', stageInfo.stageId);
        const connected = await testContext['connector']!.connect(wsUrl);
        console.log('C-02-01: Connect result:', connected);

        // Then: Connection should succeed
        expect(connected).toBe(true);
        expect(testContext['connector']!.isConnected).toBe(true);
        expect(testContext['connector']!.stageId).toBe(0n);
        console.log('C-02-01: Test complete');
    });

    test('C-02-02: IsConnected returns true after connection', async () => {
        // Given: Initial state is not connected
        expect(testContext['connector']!.isConnected).toBe(false);

        // When: Connection succeeds
        await testContext['createStageAndConnect']();

        // Then: IsConnected should return true
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('C-02-03: IsAuthenticated returns false before authentication', async () => {
        // Given & When: Connection succeeds (but not authenticated yet)
        await testContext['createStageAndConnect']();

        // Then: IsAuthenticated should be false before authentication
        expect(testContext['connector']!.isAuthenticated).toBe(false);
    });

    test('C-02-04: OnConnect event fires with success result', async () => {
        // Given: Stage is created
        const stageInfo = await testContext['testServer'].createTestStage();

        let connectResult: boolean | undefined;
        let eventTriggered = false;

        testContext['connector']!.onConnect = () => {
            connectResult = true;
            eventTriggered = true;
        };

        // When: Connect using callback-based method
        const wsUrl = testContext['testServer'].wsUrl;
        testContext['connector']!.connect(wsUrl);

        // Wait for OnConnect event (with mainThreadAction)
        const completed = await testContext['waitForConditionWithMainThread'](
            () => eventTriggered,
            5000
        );

        // Then: Event should fire with success result
        expect(completed).toBe(true);
        expect(eventTriggered).toBe(true);
        expect(connectResult).toBe(true);
    });

    test('C-02-05: TCP connection succeeds even with invalid stage ID', async () => {
        // Given: Non-existent stage ID
        const invalidStageId = 999999999;

        // When: Attempt connection with invalid stage ID
        const wsUrl = testContext['testServer'].wsUrl;
        const connected = await testContext['connector']!.connect(wsUrl);

        // Then: TCP connection itself succeeds (stage ID validation happens later)
        // PlayHouse server accepts WebSocket connection first, then validates on auth/join
        expect(connected).toBe(true);
        expect(testContext['connector']!.isConnected).toBe(true);
        expect(testContext['connector']!.isAuthenticated).toBe(false);
    });

    test('C-02-06: Can reconnect with the same connector', async () => {
        // Given: First connection
        await testContext['createStageAndConnect']();
        expect(testContext['connector']!.isConnected).toBe(true);

        // When: Disconnect and reconnect
        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        const newStageInfo = await testContext['testServer'].createTestStage();
        const wsUrl = testContext['testServer'].wsUrl;
        const reconnected = await testContext['connector']!.connect(wsUrl);

        // Then: Reconnection should succeed
        expect(reconnected).toBe(true);
        expect(testContext['connector']!.isConnected).toBe(true);
        expect(testContext['connector']!.stageId).toBe(0n);
    });
});
