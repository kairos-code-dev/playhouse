/**
 * Test Protocol Messages Helper
 *
 * Provides simple protobuf wire format serialization for test messages.
 * Similar to Java's TestMessages.java implementation.
 */

/**
 * Protobuf wire format helper - writes a varint
 */
function writeVarint(buffer: number[], value: number): void {
    while ((value & ~0x7f) !== 0) {
        buffer.push((value & 0x7f) | 0x80);
        value >>>= 7;
    }
    buffer.push(value);
}

/**
 * Protobuf wire format helper - writes a string field
 */
function writeString(buffer: number[], fieldNumber: number, value: string | undefined): void {
    if (value === undefined || value === null || value === '') {
        return;
    }

    const bytes = new TextEncoder().encode(value);
    const tag = (fieldNumber << 3) | 2; // Wire type 2 = length-delimited

    writeVarint(buffer, tag);
    writeVarint(buffer, bytes.length);
    buffer.push(...bytes);
}

/**
 * Protobuf wire format helper - writes a varint field
 */
function writeVarintField(buffer: number[], fieldNumber: number, value: number): void {
    if (value === 0) {
        return; // Skip default value
    }

    const tag = (fieldNumber << 3) | 0; // Wire type 0 = varint
    writeVarint(buffer, tag);
    writeVarint(buffer, value);
}

/**
 * Protobuf wire format helper - writes a bytes field
 */
function writeBytes(buffer: number[], fieldNumber: number, value: Uint8Array | undefined): void {
    if (value === undefined || value === null || value.length === 0) {
        return;
    }

    const tag = (fieldNumber << 3) | 2; // Wire type 2 = length-delimited
    writeVarint(buffer, tag);
    writeVarint(buffer, value.length);
    buffer.push(...value);
}

/**
 * AuthenticateRequest message
 */
export interface AuthenticateRequest {
    userId: string;
    token: string;
    metadata?: Record<string, string>;
}

/**
 * Serialize AuthenticateRequest to protobuf wire format
 */
export function serializeAuthenticateRequest(request: AuthenticateRequest): Uint8Array {
    const buffer: number[] = [];

    // Field 1: user_id (string)
    writeString(buffer, 1, request.userId);

    // Field 2: token (string)
    writeString(buffer, 2, request.token);

    // Field 3: metadata (map<string, string>) - skip for now

    return new Uint8Array(buffer);
}

/**
 * AuthenticateReply message
 */
export interface AuthenticateReply {
    accountId: string;
    stageId: bigint;
    success: boolean;
    receivedUserId: string;
    receivedToken: string;
}

/**
 * Parse AuthenticateReply from protobuf wire format
 */
export function parseAuthenticateReply(data: Uint8Array): AuthenticateReply {
    const reply: AuthenticateReply = {
        accountId: '',
        stageId: 0n,
        success: false,
        receivedUserId: '',
        receivedToken: ''
    };

    let offset = 0;
    while (offset < data.length) {
        const [tag, newOffset] = readVarint(data, offset);
        offset = newOffset;

        const fieldNumber = tag >>> 3;
        const wireType = tag & 0x07;

        switch (fieldNumber) {
            case 1: // account_id (string)
                if (wireType === 2) {
                    const [str, nextOffset] = readString(data, offset);
                    reply.accountId = str;
                    offset = nextOffset;
                }
                break;
            case 2: // stage_id (int64 as varint)
                if (wireType === 0) {
                    const [value, nextOffset] = readVarint(data, offset);
                    reply.stageId = BigInt(value);
                    offset = nextOffset;
                }
                break;
            case 3: // received_user_id (string)
                if (wireType === 2) {
                    const [str, nextOffset] = readString(data, offset);
                    reply.receivedUserId = str;
                    offset = nextOffset;
                }
                break;
            case 4: // received_token (string)
                if (wireType === 2) {
                    const [str, nextOffset] = readString(data, offset);
                    reply.receivedToken = str;
                    offset = nextOffset;
                }
                break;
            case 5: // success (bool as varint)
                if (wireType === 0) {
                    const [value, nextOffset] = readVarint(data, offset);
                    reply.success = value !== 0;
                    offset = nextOffset;
                }
                break;
            default:
                // Skip unknown field
                offset = skipField(data, offset, wireType);
        }
    }

    return reply;
}

/**
 * EchoRequest message
 */
export interface EchoRequest {
    content: string;
    sequence: number;
}

/**
 * Serialize EchoRequest to protobuf wire format
 */
export function serializeEchoRequest(request: EchoRequest): Uint8Array {
    const buffer: number[] = [];

    // Field 1: content (string)
    writeString(buffer, 1, request.content);

    // Field 2: sequence (int32 as varint)
    if (request.sequence !== 0) {
        writeVarintField(buffer, 2, request.sequence);
    }

    return new Uint8Array(buffer);
}

/**
 * EchoReply message
 */
export interface EchoReply {
    content: string;
    sequence: number;
    processedAt: bigint;
}

/**
 * Parse EchoReply from protobuf wire format
 */
export function parseEchoReply(data: Uint8Array): EchoReply {
    const reply: EchoReply = {
        content: '',
        sequence: 0,
        processedAt: BigInt(0)
    };

    let offset = 0;
    while (offset < data.length) {
        const [tag, newOffset] = readVarint(data, offset);
        offset = newOffset;

        const fieldNumber = tag >>> 3;
        const wireType = tag & 0x07;

        switch (fieldNumber) {
            case 1: // content (string)
                if (wireType === 2) {
                    const [str, nextOffset] = readString(data, offset);
                    reply.content = str;
                    offset = nextOffset;
                }
                break;
            case 2: // sequence (int32)
                if (wireType === 0) {
                    const [value, nextOffset] = readVarint(data, offset);
                    reply.sequence = value;
                    offset = nextOffset;
                }
                break;
            case 3: // processed_at (int64)
                if (wireType === 0) {
                    const [value, nextOffset] = readVarint64(data, offset);
                    reply.processedAt = value;
                    offset = nextOffset;
                }
                break;
            default:
                offset = skipField(data, offset, wireType);
        }
    }

    return reply;
}

/**
 * FailRequest message
 */
export interface FailRequest {
    errorCode: number;
    errorMessage: string;
}

/**
 * Serialize FailRequest to protobuf wire format
 */
export function serializeFailRequest(request: FailRequest): Uint8Array {
    const buffer: number[] = [];

    // Field 1: error_code (int32)
    if (request.errorCode !== 0) {
        writeVarintField(buffer, 1, request.errorCode);
    }

    // Field 2: error_message (string)
    writeString(buffer, 2, request.errorMessage);

    return new Uint8Array(buffer);
}

/**
 * FailReply message
 */
export interface FailReply {
    errorCode: number;
    message: string;
}

/**
 * Parse FailReply from protobuf wire format
 */
export function parseFailReply(data: Uint8Array): FailReply {
    const reply: FailReply = {
        errorCode: 0,
        message: ''
    };

    let offset = 0;
    while (offset < data.length) {
        const [tag, newOffset] = readVarint(data, offset);
        offset = newOffset;

        const fieldNumber = tag >>> 3;
        const wireType = tag & 0x07;

        switch (fieldNumber) {
            case 1: // error_code (int32)
                if (wireType === 0) {
                    const [value, nextOffset] = readVarint(data, offset);
                    reply.errorCode = value;
                    offset = nextOffset;
                }
                break;
            case 2: // message (string)
                if (wireType === 2) {
                    const [str, nextOffset] = readString(data, offset);
                    reply.message = str;
                    offset = nextOffset;
                }
                break;
            default:
                offset = skipField(data, offset, wireType);
        }
    }

    return reply;
}

/**
 * NoResponseRequest message
 */
export interface NoResponseRequest {
    delayMs: number;
}

/**
 * Serialize NoResponseRequest to protobuf wire format
 */
export function serializeNoResponseRequest(request: NoResponseRequest): Uint8Array {
    const buffer: number[] = [];

    // Field 1: delay_ms (int32)
    if (request.delayMs !== 0) {
        writeVarintField(buffer, 1, request.delayMs);
    }

    return new Uint8Array(buffer);
}

/**
 * BroadcastRequest message
 */
export interface BroadcastRequest {
    content: string;
}

/**
 * Serialize BroadcastRequest to protobuf wire format
 */
export function serializeBroadcastRequest(request: BroadcastRequest): Uint8Array {
    const buffer: number[] = [];

    // Field 1: content (string)
    writeString(buffer, 1, request.content);

    return new Uint8Array(buffer);
}

/**
 * BroadcastNotify message
 */
export interface BroadcastNotify {
    eventType: string;
    data: string;
    fromAccountId: bigint;
    senderId: string;
}

/**
 * Parse BroadcastNotify from protobuf wire format
 */
export function parseBroadcastNotify(data: Uint8Array): BroadcastNotify {
    const notify: BroadcastNotify = {
        eventType: '',
        data: '',
        fromAccountId: BigInt(0),
        senderId: ''
    };

    let offset = 0;
    while (offset < data.length) {
        const [tag, newOffset] = readVarint(data, offset);
        offset = newOffset;

        const fieldNumber = tag >>> 3;
        const wireType = tag & 0x07;

        switch (fieldNumber) {
            case 1: // event_type (string)
                if (wireType === 2) {
                    const [str, nextOffset] = readString(data, offset);
                    notify.eventType = str;
                    offset = nextOffset;
                }
                break;
            case 2: // data (string)
                if (wireType === 2) {
                    const [str, nextOffset] = readString(data, offset);
                    notify.data = str;
                    offset = nextOffset;
                }
                break;
            case 3: // from_account_id (int64)
                if (wireType === 0) {
                    const [value, nextOffset] = readVarint64(data, offset);
                    notify.fromAccountId = value;
                    offset = nextOffset;
                }
                break;
            case 4: // sender_id (string)
                if (wireType === 2) {
                    const [str, nextOffset] = readString(data, offset);
                    notify.senderId = str;
                    offset = nextOffset;
                }
                break;
            default:
                offset = skipField(data, offset, wireType);
        }
    }

    return notify;
}

/**
 * LargePayloadRequest message
 */
export interface LargePayloadRequest {
    sizeBytes: number;
}

/**
 * Serialize LargePayloadRequest to protobuf wire format
 */
export function serializeLargePayloadRequest(request: LargePayloadRequest): Uint8Array {
    const buffer: number[] = [];

    // Field 1: size_bytes (int32)
    if (request.sizeBytes !== 0) {
        writeVarintField(buffer, 1, request.sizeBytes);
    }

    return new Uint8Array(buffer);
}

/**
 * BenchmarkReply message
 */
export interface BenchmarkReply {
    sequence: number;
    processedAt: bigint;
    payload: Uint8Array;
}

/**
 * Parse BenchmarkReply from protobuf wire format
 */
export function parseBenchmarkReply(data: Uint8Array): BenchmarkReply {
    const reply: BenchmarkReply = {
        sequence: 0,
        processedAt: BigInt(0),
        payload: new Uint8Array(0)
    };

    let offset = 0;
    while (offset < data.length) {
        const [tag, newOffset] = readVarint(data, offset);
        offset = newOffset;

        const fieldNumber = tag >>> 3;
        const wireType = tag & 0x07;

        switch (fieldNumber) {
            case 1: // sequence (int32)
                if (wireType === 0) {
                    const [value, nextOffset] = readVarint(data, offset);
                    reply.sequence = value;
                    offset = nextOffset;
                }
                break;
            case 2: // processed_at (int64)
                if (wireType === 0) {
                    const [value, nextOffset] = readVarint64(data, offset);
                    reply.processedAt = value;
                    offset = nextOffset;
                }
                break;
            case 3: // payload (bytes)
                if (wireType === 2) {
                    const [bytes, nextOffset] = readBytes(data, offset);
                    reply.payload = bytes;
                    offset = nextOffset;
                }
                break;
            default:
                offset = skipField(data, offset, wireType);
        }
    }

    return reply;
}

/**
 * LargePayloadReply message
 */
export interface LargePayloadReply {
    data: Uint8Array;
    originalSize: number;
    compressed: boolean;
}

/**
 * Parse LargePayloadReply from protobuf wire format
 */
export function parseLargePayloadReply(data: Uint8Array): LargePayloadReply {
    const reply: LargePayloadReply = {
        data: new Uint8Array(0),
        originalSize: 0,
        compressed: false
    };

    let offset = 0;
    while (offset < data.length) {
        const [tag, newOffset] = readVarint(data, offset);
        offset = newOffset;

        const fieldNumber = tag >>> 3;
        const wireType = tag & 0x07;

        switch (fieldNumber) {
            case 1: // data (bytes)
                if (wireType === 2) {
                    const [bytes, nextOffset] = readBytes(data, offset);
                    reply.data = bytes;
                    offset = nextOffset;
                }
                break;
            case 2: // original_size (int32)
                if (wireType === 0) {
                    const [value, nextOffset] = readVarint(data, offset);
                    reply.originalSize = value;
                    offset = nextOffset;
                }
                break;
            case 3: // compressed (bool)
                if (wireType === 0) {
                    const [value, nextOffset] = readVarint(data, offset);
                    reply.compressed = value !== 0;
                    offset = nextOffset;
                }
                break;
            default:
                offset = skipField(data, offset, wireType);
        }
    }

    return reply;
}

// ===== Protobuf Wire Format Helpers =====

function readVarint(data: Uint8Array, offset: number): [number, number] {
    let result = 0;
    let shift = 0;
    let byte: number;

    do {
        if (offset >= data.length) {
            throw new Error('Unexpected end of data while reading varint');
        }
        byte = data[offset++];
        result |= (byte & 0x7f) << shift;
        shift += 7;
    } while (byte >= 0x80);

    return [result >>> 0, offset];
}

function readVarint64(data: Uint8Array, offset: number): [bigint, number] {
    let result = BigInt(0);
    let shift = BigInt(0);
    let byte: number;

    do {
        if (offset >= data.length) {
            throw new Error('Unexpected end of data while reading varint64');
        }
        byte = data[offset++];
        result |= BigInt(byte & 0x7f) << shift;
        shift += BigInt(7);
    } while (byte >= 0x80);

    return [result, offset];
}

function readString(data: Uint8Array, offset: number): [string, number] {
    const [length, newOffset] = readVarint(data, offset);
    const strBytes = data.slice(newOffset, newOffset + length);
    const str = new TextDecoder().decode(strBytes);
    return [str, newOffset + length];
}

function readBytes(data: Uint8Array, offset: number): [Uint8Array, number] {
    const [length, newOffset] = readVarint(data, offset);
    const bytes = data.slice(newOffset, newOffset + length);
    return [bytes, newOffset + length];
}

function skipField(data: Uint8Array, offset: number, wireType: number): number {
    switch (wireType) {
        case 0: // Varint
            while (offset < data.length && data[offset] >= 0x80) {
                offset++;
            }
            return offset + 1;
        case 1: // 64-bit
            return offset + 8;
        case 2: // Length-delimited
            const [length, newOffset] = readVarint(data, offset);
            return newOffset + length;
        case 5: // 32-bit
            return offset + 4;
        default:
            throw new Error(`Unknown wire type: ${wireType}`);
    }
}
