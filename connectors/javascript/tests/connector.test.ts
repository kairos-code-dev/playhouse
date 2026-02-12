/**
 * PlayHouse JavaScript Connector - Unit Tests
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { Connector, Packet, ConnectorConfig, DefaultConfig, ErrorCode, PacketConst, ConnectorError, isError, getErrorMessage } from '../src/index.js';

describe('Packet', () => {
    describe('empty', () => {
        it('creates a packet with empty payload', () => {
            const packet = Packet.empty('TestMessage');

            expect(packet.msgId).toBe('TestMessage');
            expect(packet.msgSeq).toBe(0);
            expect(packet.stageId).toBe(0n);
            expect(packet.errorCode).toBe(0);
            expect(packet.payload).toEqual(new Uint8Array(0));
            expect(packet.isPush).toBe(true);
            expect(packet.hasError).toBe(false);
        });
    });

    describe('fromBytes', () => {
        it('creates a packet with byte payload', () => {
            const payload = new Uint8Array([1, 2, 3, 4, 5]);
            const packet = Packet.fromBytes('BinaryMessage', payload);

            expect(packet.msgId).toBe('BinaryMessage');
            expect(packet.payload).toEqual(payload);
        });
    });

    describe('dispose', () => {
        it('marks packet as disposed', () => {
            const packet = Packet.empty('Test');

            expect(packet.isDisposed).toBe(false);
            packet.dispose();
            expect(packet.isDisposed).toBe(true);
        });

        it('is idempotent', () => {
            const packet = Packet.empty('Test');

            packet.dispose();
            packet.dispose(); // Should not throw
            expect(packet.isDisposed).toBe(true);
        });
    });
});

describe('ConnectorConfig', () => {
    it('has correct default values', () => {
        expect(DefaultConfig.heartbeatIntervalMs).toBe(10000);
        expect(DefaultConfig.heartbeatTimeoutMs).toBe(30000);
        expect(DefaultConfig.requestTimeoutMs).toBe(30000);
        expect(DefaultConfig.connectionIdleTimeoutMs).toBe(30000);
        expect(DefaultConfig.debugMode).toBe(false);
    });
});

describe('ErrorCode', () => {
    it('defines all expected error codes', () => {
        expect(ErrorCode.Success).toBe(0);
        // Connection errors (1xxx)
        expect(ErrorCode.ConnectionFailed).toBe(1001);
        expect(ErrorCode.ConnectionTimeout).toBe(1002);
        expect(ErrorCode.ConnectionClosed).toBe(1003);
        expect(ErrorCode.HeartbeatTimeout).toBe(1004);
        // Request errors (2xxx)
        expect(ErrorCode.RequestTimeout).toBe(2001);
        expect(ErrorCode.InvalidResponse).toBe(2002);
        expect(ErrorCode.InvalidRequest).toBe(2003);
        // Authentication errors (3xxx)
        expect(ErrorCode.AuthenticationFailed).toBe(3001);
        expect(ErrorCode.AuthenticationRequired).toBe(3002);
        // C# compatible error codes (60xxx)
        expect(ErrorCode.Disconnected).toBe(60201);
        expect(ErrorCode.RequestTimeoutLegacy).toBe(60202);
        expect(ErrorCode.Unauthenticated).toBe(60203);
    });
});

describe('PacketConst', () => {
    it('defines packet constants', () => {
        expect(PacketConst.MsgIdLimit).toBe(256);
        expect(PacketConst.MaxBodySize).toBe(1024 * 1024 * 2);
        expect(PacketConst.HeartBeat).toBe('@Heart@Beat@');
    });
});

describe('Connector', () => {
    let connector: Connector;

    beforeEach(() => {
        connector = new Connector();
    });

    describe('init', () => {
        it('initializes with default config', () => {
            connector.init();

            expect(connector.config.heartbeatIntervalMs).toBe(10000);
            expect(connector.config.requestTimeoutMs).toBe(30000);
        });

        it('initializes with custom config', () => {
            connector.init({
                heartbeatIntervalMs: 5000,
                requestTimeoutMs: 15000,
            });

            expect(connector.config.heartbeatIntervalMs).toBe(5000);
            expect(connector.config.requestTimeoutMs).toBe(15000);
            // Other values should remain default
            expect(connector.config.heartbeatTimeoutMs).toBe(30000);
        });
    });

    describe('isConnected', () => {
        it('returns false when not connected', () => {
            connector.init();
            expect(connector.isConnected).toBe(false);
        });
    });

    describe('isAuthenticated', () => {
        it('returns false when not authenticated', () => {
            connector.init();
            expect(connector.isAuthenticated).toBe(false);
        });
    });

    describe('stageId', () => {
        it('returns 0n by default', () => {
            connector.init();
            expect(connector.stageId).toBe(0n);
        });
    });

    describe('disconnect', () => {
        it('can be called when not connected', () => {
            connector.init();
            expect(() => connector.disconnect()).not.toThrow();
        });
    });

    describe('send', () => {
        it('triggers error callback when not connected', () => {
            connector.init();

            const errorCallback = vi.fn();
            connector.onError = errorCallback;

            const packet = Packet.empty('Test');
            connector.send(packet);
            connector.mainThreadAction(); // Process queued actions

            expect(errorCallback).toHaveBeenCalledWith(
                0n, // stageId
                ErrorCode.Disconnected,
                'Not connected',
                packet
            );
        });
    });

    describe('request', () => {
        it('rejects when not connected', async () => {
            connector.init();

            await expect(connector.request(Packet.empty('Test'))).rejects.toThrow(
                'Not connected'
            );
        });
    });

    describe('authenticate', () => {
        it('rejects when not connected', async () => {
            connector.init();

            await expect(
                connector.authenticate('service', 'account')
            ).rejects.toThrow('Not connected');
        });
    });

    describe('mainThreadAction', () => {
        it('processes queued actions', () => {
            connector.init();

            const actions: string[] = [];
            connector.onError = () => actions.push('error');

            connector.send(Packet.empty('Test')); // Will queue error action
            expect(actions).toHaveLength(0);

            connector.mainThreadAction();
            expect(actions).toHaveLength(1);
            expect(actions[0]).toBe('error');
        });
    });
});

describe('Packet Encoding', () => {
    // Test packet encoding by importing the codec directly
    // This requires additional test setup for internal modules

    it('encodes message ID correctly', async () => {
        // Import the codec dynamically for testing
        const { encodePacket } = await import('../src/internal/packet-codec.js');

        const packet = Packet.empty('TestMsg');
        const encoded = encodePacket(packet, 0, 0n);

        // Verify structure:
        // ContentSize(4) + MsgIdLen(1) + MsgId(7) + MsgSeq(2) + Payload(0)
        // Total: 4 + 1 + 7 + 2 + 0 = 14 bytes
        expect(encoded.length).toBe(14);

        // Check content size (little-endian)
        const view = new DataView(encoded.buffer);
        const contentSize = view.getInt32(0, true);
        expect(contentSize).toBe(10); // 1 + 7 + 2 + 0

        // Check msgIdLen
        expect(encoded[4]).toBe(7);

        // Check msgId
        const textDecoder = new TextDecoder('utf-8');
        const msgId = textDecoder.decode(encoded.subarray(5, 12));
        expect(msgId).toBe('TestMsg');

        // Check msgSeq (should be 0)
        const msgSeq = view.getUint16(12, true);
        expect(msgSeq).toBe(0);

    });

    it('encodes with non-zero msgSeq and stageId', async () => {
        const { encodePacket } = await import('../src/internal/packet-codec.js');

        const packet = Packet.fromBytes('Msg', new Uint8Array([1, 2, 3]));
        const encoded = encodePacket(packet, 42, 123456789n);

        const view = new DataView(encoded.buffer);

        // MsgSeq at offset 4 + 1 + 3 = 8
        const msgSeq = view.getUint16(8, true);
        expect(msgSeq).toBe(42);

        // Payload at offset 10
        expect(encoded[10]).toBe(1);
        expect(encoded[11]).toBe(2);
        expect(encoded[12]).toBe(3);
    });
});

describe('Packet Decoding', () => {
    it('decodes response packet correctly', async () => {
        const { decodePacket } = await import('../src/internal/packet-codec.js');

        // Build a response packet manually
        // MsgIdLen(1) + MsgId(4) + MsgSeq(2) + ErrorCode(2) + OriginalSize(4) + Payload
        const msgId = 'Test';
        const msgIdBytes = new TextEncoder().encode(msgId);
        const payload = new Uint8Array([10, 20, 30]);

        const totalSize = 1 + msgIdBytes.length + 2 + 2 + 4 + payload.length;
        const buffer = new ArrayBuffer(totalSize);
        const view = new DataView(buffer);
        const bytes = new Uint8Array(buffer);

        let offset = 0;

        // MsgIdLen
        view.setUint8(offset++, msgIdBytes.length);

        // MsgId
        bytes.set(msgIdBytes, offset);
        offset += msgIdBytes.length;

        // MsgSeq
        view.setUint16(offset, 123, true);
        offset += 2;

        // ErrorCode
        view.setUint16(offset, 0, true);
        offset += 2;

        // OriginalSize
        view.setInt32(offset, 0, true);
        offset += 4;

        // Payload
        bytes.set(payload, offset);

        // Decode
        const packet = decodePacket(bytes);

        expect(packet.msgId).toBe('Test');
        expect(packet.msgSeq).toBe(123);
        expect(packet.stageId).toBe(0n);
        expect(packet.errorCode).toBe(0);
        expect(packet.originalSize).toBe(0);
        expect(packet.payload).toEqual(payload);
    });

    it('decodes packet with error code', async () => {
        const { decodePacket } = await import('../src/internal/packet-codec.js');

        const msgId = 'Err';
        const msgIdBytes = new TextEncoder().encode(msgId);

        const totalSize = 1 + msgIdBytes.length + 2 + 2 + 4;
        const buffer = new ArrayBuffer(totalSize);
        const view = new DataView(buffer);
        const bytes = new Uint8Array(buffer);

        let offset = 0;

        view.setUint8(offset++, msgIdBytes.length);
        bytes.set(msgIdBytes, offset);
        offset += msgIdBytes.length;
        view.setUint16(offset, 1, true);
        offset += 2;
        view.setUint16(offset, 2001, true); // RequestTimeout error
        offset += 2;
        view.setInt32(offset, 0, true);

        const packet = decodePacket(bytes);

        expect(packet.msgId).toBe('Err');
        expect(packet.errorCode).toBe(2001);
        expect(packet.hasError).toBe(true);
    });
});
