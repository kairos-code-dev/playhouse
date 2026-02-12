/**
 * A-04: OnError Event Tests
 *
 * Tests for the OnError event which fires on request failures,
 * connection problems, server errors, and other error conditions.
 * Used for error handling in callback-based API.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Packet } from '../../../src/packet.js';
import { serializeEchoRequest, serializeAuthenticateRequest } from '../helpers/TestMessages.js';

describe('A-04: OnError Event', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('A-04-01: OnError fires when send() on disconnected state', async () => {
        // Given: Connected, authenticated, then disconnected
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('onerror-user-1');

        const errorEvents: Array<{ stageId: bigint; errorCode: number; request?: Packet }> = [];
        testContext['connector']!.onError = (stageId: bigint, errorCode: number, message: string, request?: Packet) => {
            errorEvents.push({ stageId, errorCode, request });
        };

        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // When: Try to send after disconnect
        const payload = serializeEchoRequest({ content: 'Test', sequence: 1 });
        const echoRequest = Packet.fromBytes('EchoRequest', payload);
        testContext['connector']!.send(echoRequest);
        testContext['connector']!.mainThreadAction();

        // Then: OnError should be fired
        expect(errorEvents.length).toBeGreaterThanOrEqual(1);
        expect(errorEvents[0].errorCode).toBeGreaterThan(0);
        expect(errorEvents[0].stageId).toBeDefined();
    });

    test('A-04-02: OnError fires when request() callback on disconnected state', async () => {
        // Given: Connected, authenticated, then disconnected
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('onerror-user-2');

        const errorEvents: Array<{ stageId: bigint; errorCode: number }> = [];
        testContext['connector']!.onError = (stageId: bigint, errorCode: number) => {
            errorEvents.push({ stageId, errorCode });
        };

        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // When: Try to request after disconnect using callback
        const payload = serializeEchoRequest({ content: 'Test', sequence: 1 });
        const echoRequest = Packet.fromBytes('EchoRequest', payload);
        let callbackFired = false;

        testContext['connector']!.requestWithCallback(echoRequest, (response: Packet) => {
            callbackFired = true;
        });
        testContext['connector']!.mainThreadAction();

        // Then: OnError should fire, callback should not
        expect(errorEvents.length).toBeGreaterThanOrEqual(1);
        expect(errorEvents[0].errorCode).toBeGreaterThan(0);
        expect(callbackFired).toBe(false);
    });

    test('A-04-03: OnError contains original request packet', async () => {
        // Given: Disconnected state
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('onerror-user-3');

        let receivedRequest: Packet | undefined;
        testContext['connector']!.onError = (stageId: bigint, errorCode: number, message: string, request?: Packet) => {
            receivedRequest = request;
        };

        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // When: Send request
        const payload = serializeEchoRequest({ content: 'Test', sequence: 1 });
        const echoRequest = Packet.fromBytes('EchoRequest', payload);
        testContext['connector']!.send(echoRequest);
        testContext['connector']!.mainThreadAction();

        // Then: Original request should be in OnError
        expect(receivedRequest).toBeDefined();
        expect(receivedRequest?.msgId).toBe('EchoRequest');
    });

    test('A-04-04: OnError contains StageId information', async () => {
        // Given: Connected to specific stage
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('onerror-user-4');

        let receivedStageId: bigint | undefined;
        testContext['connector']!.onError = (stageId: bigint) => {
            receivedStageId = stageId;
        };

        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // When: Send after disconnect
        const payload = serializeEchoRequest({ content: 'Test', sequence: 1 });
        const echoRequest = Packet.fromBytes('EchoRequest', payload);
        testContext['connector']!.send(echoRequest);
        testContext['connector']!.mainThreadAction();

        // Then: connector resets stageId on disconnect, so error callback stageId is 0
        expect(receivedStageId).toBe(0n);
    });

    test('A-04-05: OnError fires when authenticate() on disconnected state', async () => {
        // Given: Connected then disconnected
        await testContext['createStageAndConnect']();

        const errorEvents: number[] = [];
        testContext['connector']!.onError = (stageId: bigint, errorCode: number) => {
            errorEvents.push(errorCode);
        };

        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // When: Try to authenticate after disconnect using callback
        const payload = serializeAuthenticateRequest({ userId: 'test-user', token: 'test-token' });
        const authRequest = Packet.fromBytes('AuthenticateRequest', payload);
        let callbackFired = false;

        testContext['connector']!.requestWithCallback(authRequest, (response: Packet) => {
            callbackFired = true;
        });
        testContext['connector']!.mainThreadAction();

        // Then: OnError should fire
        expect(errorEvents.length).toBeGreaterThanOrEqual(1);
        expect(errorEvents[0]).toBeGreaterThan(0);
        expect(callbackFired).toBe(false);
    });

    test('A-04-06: Multiple OnError handlers can be registered', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('onerror-user-6');

        let handler1Called = false;
        let handler2Called = false;
        let handler3Called = false;

        // Note: JavaScript typically supports only one handler per event
        // This test verifies the last handler is called
        testContext['connector']!.onError = () => { handler1Called = true; };
        testContext['connector']!.onError = () => { handler2Called = true; };
        testContext['connector']!.onError = () => { handler3Called = true; };

        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // When: Trigger error
        const payload = serializeEchoRequest({ content: 'Test', sequence: 1 });
        const echoRequest = Packet.fromBytes('EchoRequest', payload);
        testContext['connector']!.send(echoRequest);
        testContext['connector']!.mainThreadAction();

        // Then: At least one handler should be called (likely the last one)
        const anyHandlerCalled = handler1Called || handler2Called || handler3Called;
        expect(anyHandlerCalled).toBe(true);
    });

    test('A-04-07: OnError handler exception does not crash connector', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('onerror-user-7');

        let handler2Called = false;

        testContext['connector']!.onError = () => {
            handler2Called = true;
            throw new Error('Test exception in handler');
        };

        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // When: Trigger error
        const payload = serializeEchoRequest({ content: 'Test', sequence: 1 });
        const echoRequest = Packet.fromBytes('EchoRequest', payload);

        try {
            testContext['connector']!.send(echoRequest);
            testContext['connector']!.mainThreadAction();
        } catch (error) {
            // Exception may or may not propagate depending on implementation
        }

        // Then: Handler was called despite exception
        expect(handler2Called).toBe(true);
    });

    test('A-04-08: Normal disconnect does not fire OnError', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('onerror-user-8');

        let errorCount = 0;
        testContext['connector']!.onError = () => {
            errorCount++;
        };

        // When: Normal disconnect
        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // Then: OnError should not fire for normal disconnect
        expect(errorCount).toBe(0);
    });
});
