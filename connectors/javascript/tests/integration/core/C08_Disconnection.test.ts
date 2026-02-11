/**
 * C-08: Disconnection Tests
 *
 * Tests that the Connector can properly disconnect and that
 * the state is correct after disconnection.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Connector } from '../../../src/connector.js';
import { Packet } from '../../../src/packet.js';
import { ErrorCode } from '../../../src/types.js';
import { serializeEchoRequest, serializeAuthenticateRequest } from '../helpers/TestMessages.js';

describe('C-08: Disconnection', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('C-08-01: Disconnect closes the connection', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('disconnectUser');

        expect(testContext['connector']!.isConnected).toBe(true);
        expect(testContext['connector']!.isAuthenticated).toBe(true);

        // When: Disconnect
        testContext['connector']!.disconnect();
        await testContext['delay'](500); // Wait for disconnection to complete

        // Then: Should be disconnected
        expect(testContext['connector']!.isConnected).toBe(false);
        expect(testContext['connector']!.isAuthenticated).toBe(false);
    });

    test('C-08-02: OnDisconnect event does not trigger when client disconnects', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('clientDisconnectUser');

        let disconnectEventTriggered = false;
        testContext['connector']!.onDisconnect = () => {
            disconnectEventTriggered = true;
        };

        // When: Client disconnects intentionally
        testContext['connector']!.disconnect();
        await testContext['delay'](1000); // Wait for event

        // Then: OnDisconnect event should not be triggered (intentional disconnect)
        expect(disconnectEventTriggered).toBe(false);
    });

    test('C-08-03: Send fails after disconnect', async () => {
        // Given: Connected, authenticated, then disconnected
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('sendAfterDisconnectUser');
        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        let receivedErrorCode: number | undefined;
        testContext['connector']!.onError = (_stageId, errorCode, _message) => {
            receivedErrorCode = errorCode;
        };

        // When: Try to send after disconnection
        const echoRequest = {
            content: 'Test',
            sequence: 1
        };
        const payload = serializeEchoRequest(echoRequest);
        const packet = Packet.fromBytes('EchoRequest', payload);
        testContext['connector']!.send(packet);

        // Process pending callbacks
        testContext['connector']!.mainThreadAction();

        // Then: Disconnected error should occur
        expect(receivedErrorCode).toBe(ErrorCode.Disconnected);
    });

    test('C-08-04: RequestAsync throws exception after disconnect', async () => {
        // Given: Connected, authenticated, then disconnected
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('requestAfterDisconnectUser');
        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // When: Try to request after disconnection
        const echoRequest = {
            content: 'Test',
            sequence: 1
        };
        const payload = serializeEchoRequest(echoRequest);
        const packet = Packet.fromBytes('EchoRequest', payload);

        // Then: Should throw exception
        await expect(
            testContext['connector']!.request(packet)
        ).rejects.toThrow('Not connected');
    });

    test('C-08-05: Can reconnect after disconnect', async () => {
        // Given: First connection and disconnect
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('reconnectUser1');
        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        expect(testContext['connector']!.isConnected).toBe(false);

        // When: Reconnect to new stage
        const reconnected = await testContext['createStageAndConnect']();

        // Then: Reconnection should succeed
        expect(reconnected).toBe(true);
        expect(testContext['connector']!.isConnected).toBe(true);

        // Authentication should be possible
        const authReply = await testContext['authenticate']('reconnectUser2');
        expect(authReply).toBeDefined();
    });

    test('C-08-06: Multiple disconnect calls are safe', { timeout: 10000 }, async () => {
        // Given: Connected
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('multiDisconnectUser');

        // When: Call disconnect multiple times
        testContext['connector']!.disconnect();
        await testContext['delay'](200);

        // Should not throw exception
        expect(() => {
            testContext['connector']!.disconnect();
            testContext['connector']!.disconnect();
            testContext['connector']!.disconnect();
        }).not.toThrow();

        // Then: Should be disconnected
        expect(testContext['connector']!.isConnected).toBe(false);
    });

    test('C-08-07: Connector cleanup disconnects automatically', async () => {
        // Given: Create and connect new connector
        const tempConnector = new Connector();
        tempConnector.init({ requestTimeoutMs: 5000, heartbeatIntervalMs: 10000 });

        const stageInfo = await testContext['testServer'].createStage('TestStage');
        const wsUrl = testContext['testServer'].wsUrl;
        await tempConnector.connect(wsUrl);

        const authPayload = serializeAuthenticateRequest({ userId: 'disposeUser', token: 'valid_token' });
        const authPacket = Packet.fromBytes('AuthenticateRequest', authPayload);
        await tempConnector.authenticate(authPacket);
        expect(tempConnector.isAuthenticated).toBe(true);
        expect(tempConnector.isConnected).toBe(true);

        // When: Disconnect (cleanup)
        tempConnector.disconnect();
        await testContext['delay'](500);

        // Then: Should be disconnected
        expect(tempConnector.isConnected).toBe(false);
    });

    test('C-08-08: IsAuthenticated returns false after disconnect', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('authCheckUser');

        expect(testContext['connector']!.isAuthenticated).toBe(true);

        // When: Disconnect
        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // Then: IsAuthenticated should be false
        expect(testContext['connector']!.isAuthenticated).toBe(false);
    });
});
