/**
 * C-11: Error Response Tests
 *
 * Tests for handling error responses from the server.
 * FailRequest causes the server to intentionally return an error response.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Connector } from '../../../src/connector.js';
import { Packet } from '../../../src/packet.js';
import { serializeFailRequest, parseFailReply, serializeAuthenticateRequest } from '../helpers/TestMessages.js';

describe('C-11: Error Response', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('C-11-01: Can receive server error response', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('errorUser');

        const failRequest = {
            errorCode: 1000,
            errorMessage: 'Test Error'
        };

        // When: Send request that causes error
        const payload = serializeFailRequest(failRequest);
        const requestPacket = Packet.fromBytes('FailRequest', payload);
        const responsePacket = await testContext['connector']!.request(requestPacket);

        // Then: Should receive error response
        expect(responsePacket).toBeDefined();
        expect(responsePacket.msgId).toBe('FailReply');

        const failReply = parseFailReply(responsePacket.payload);
        expect(failReply.errorCode).toBe(1000);
        expect(failReply.message).toContain('Test Error');
    });

    test('C-11-02: Can handle different error codes', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('multiErrorUser');

        const errorCodes = [1000, 1001, 1002, 1003, 1004];

        // When: Request with various error codes
        for (const errorCode of errorCodes) {
            const failRequest = {
                errorCode: errorCode,
                errorMessage: `Error ${errorCode}`
            };

            const payload = serializeFailRequest(failRequest);
            const requestPacket = Packet.fromBytes('FailRequest', payload);
            const responsePacket = await testContext['connector']!.request(requestPacket);

            // Then: Each error code should be returned correctly
            const failReply = parseFailReply(responsePacket.payload);
            expect(failReply.errorCode).toBe(errorCode);
            expect(failReply.message).toContain(`Error ${errorCode}`);
        }
    });

    test('C-11-03: Connection remains after error response', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('connectionErrorUser');

        const failRequest = {
            errorCode: 1000,
            errorMessage: 'Connection Test Error'
        };

        // When: Receive error response
        const payload = serializeFailRequest(failRequest);
        const requestPacket = Packet.fromBytes('FailRequest', payload);
        await testContext['connector']!.request(requestPacket);

        // Then: Connection and authentication should remain
        expect(testContext['connector']!.isConnected).toBe(true);
        expect(testContext['connector']!.isAuthenticated).toBe(true);

        // Other requests should work normally
        const echoReply = await testContext['echo']('After Error', 1);
        expect(echoReply.content).toBe('After Error');
    });

    test('C-11-04: Callback-based request can handle error response', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('callbackErrorUser');

        const failRequest = {
            errorCode: 2000,
            errorMessage: 'Callback Error Test'
        };

        let failReply: any;
        let callbackInvoked = false;

        // When: Callback-based error request
        const payload = serializeFailRequest(failRequest);
        const requestPacket = Packet.fromBytes('FailRequest', payload);
        testContext['connector']!.requestWithCallback(requestPacket, (responsePacket) => {
            failReply = parseFailReply(responsePacket.payload);
            callbackInvoked = true;
        });

        // Wait for callback (with mainThreadAction, max 5 seconds)
        const completed = await testContext['waitForConditionWithMainThread'](
            () => callbackInvoked,
            5000
        );

        // Then: Callback should be invoked and receive error information
        expect(completed).toBe(true);
        expect(failReply.errorCode).toBe(2000);
        expect(failReply.message).toContain('Callback Error Test');
    });

    test('C-11-05: Can handle mixed error and success responses', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('mixedUser');

        // When: Send alternating success and error requests
        const echo1 = await testContext['echo']('Success 1', 1);
        expect(echo1.content).toBe('Success 1');

        const failRequest1 = {
            errorCode: 3001,
            errorMessage: 'Error 1'
        };
        const payload1 = serializeFailRequest(failRequest1);
        const failPacket1 = Packet.fromBytes('FailRequest', payload1);
        const failResponse1 = await testContext['connector']!.request(failPacket1);
        const failReply1 = parseFailReply(failResponse1.payload);
        expect(failReply1.errorCode).toBe(3001);

        const echo2 = await testContext['echo']('Success 2', 2);
        expect(echo2.content).toBe('Success 2');

        const failRequest2 = {
            errorCode: 3002,
            errorMessage: 'Error 2'
        };
        const payload2 = serializeFailRequest(failRequest2);
        const failPacket2 = Packet.fromBytes('FailRequest', payload2);
        const failResponse2 = await testContext['connector']!.request(failPacket2);
        const failReply2 = parseFailReply(failResponse2.payload);
        expect(failReply2.errorCode).toBe(3002);

        const echo3 = await testContext['echo']('Success 3', 3);
        expect(echo3.content).toBe('Success 3');

        // Then: All requests should be handled correctly
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('C-11-06: Can handle error response with empty message', { timeout: 10000 }, async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('emptyErrorUser');

        const failRequest = {
            errorCode: 4000,
            errorMessage: '' // Empty error message
        };

        // When: Request with empty error message
        const payload = serializeFailRequest(failRequest);
        const requestPacket = Packet.fromBytes('FailRequest', payload);
        const responsePacket = await testContext['connector']!.request(requestPacket);

        // Then: Should receive error response
        const failReply = parseFailReply(responsePacket.payload);
        expect(failReply.errorCode).toBe(4000);
        // Error message may be empty
    });

    test('C-11-07: Multiple clients can each receive their own errors', { timeout: 15000 }, async () => {
        // Given: Create 2 connectors
        const stage1 = await testContext['testServer'].createStage('TestStage');
        const stage2 = await testContext['testServer'].createStage('TestStage');

        const connector1 = new Connector();
        const connector2 = new Connector();

        try {
            connector1.init({ requestTimeoutMs: 5000, heartbeatIntervalMs: 10000 });
            connector2.init({ requestTimeoutMs: 5000, heartbeatIntervalMs: 10000 });

            const wsUrl = testContext['testServer'].wsUrl;
            await connector1.connect(wsUrl);
            await connector2.connect(wsUrl);

            // Use protobuf format authentication
            const authPayload1 = serializeAuthenticateRequest({ userId: 'user1', token: 'token1' });
            const authPayload2 = serializeAuthenticateRequest({ userId: 'user2', token: 'token2' });
            await connector1.authenticate(Packet.fromBytes('AuthenticateRequest', authPayload1));
            await connector2.authenticate(Packet.fromBytes('AuthenticateRequest', authPayload2));

            // When: Each client requests with different error codes
            const fail1 = {
                errorCode: 5001,
                errorMessage: 'Error from Client 1'
            };
            const fail2 = {
                errorCode: 5002,
                errorMessage: 'Error from Client 2'
            };

            const payload1 = serializeFailRequest(fail1);
            const failPacket1 = Packet.fromBytes('FailRequest', payload1);
            const payload2 = serializeFailRequest(fail2);
            const failPacket2 = Packet.fromBytes('FailRequest', payload2);

            const response1 = await connector1.request(failPacket1);
            const response2 = await connector2.request(failPacket2);

            // Then: Each client should receive their own error
            const reply1 = parseFailReply(response1.payload);
            const reply2 = parseFailReply(response2.payload);

            expect(reply1.errorCode).toBe(5001);
            expect(reply2.errorCode).toBe(5002);

            expect(reply1.message).toContain('Client 1');
            expect(reply2.message).toContain('Client 2');
        } finally {
            connector1.disconnect();
            connector2.disconnect();
        }
    });

    test('C-11-08: Error response message type is correct', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('typeCheckUser');

        const failRequest = {
            errorCode: 6000,
            errorMessage: 'Type Check'
        };

        // When: Send error request
        const payload = serializeFailRequest(failRequest);
        const requestPacket = Packet.fromBytes('FailRequest', payload);
        const responsePacket = await testContext['connector']!.request(requestPacket);

        // Then: Response message type should be FailReply
        expect(responsePacket.msgId).toBe('FailReply');
    });
});
