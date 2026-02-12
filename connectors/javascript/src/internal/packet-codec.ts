/**
 * PlayHouse Connector - Packet Codec
 * Handles encoding and decoding of packets according to PlayHouse protocol
 */

import { Packet, ParsedPacket } from '../packet.js';
import { PacketConst } from '../types.js';

// Text encoder/decoder for UTF-8 strings
const textEncoder = new TextEncoder();
const textDecoder = new TextDecoder('utf-8');

/**
 * Request packet format:
 * ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + Payload(...)
 *
 * All integers are little-endian
 */

/**
 * Response packet format:
 * ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + ErrorCode(2) + OriginalSize(4) + Payload(...)
 *
 * All integers are little-endian
 */

/**
 * Encodes a packet for sending to the server
 * @param packet The packet to encode
 * @param msgSeq Message sequence number (0 for send, >0 for request)
 * @param _stageId Unused (kept for backwards call-site compatibility)
 * @returns Encoded binary data
 */
export function encodePacket(packet: Packet, msgSeq: number, _stageId: bigint): Uint8Array {
    const msgIdBytes = textEncoder.encode(packet.msgId);
    const msgIdLen = msgIdBytes.length;

    if (msgIdLen > 255) {
        throw new Error(`Message ID too long: ${packet.msgId} (${msgIdLen} bytes, max 255)`);
    }

    const payloadLen = packet.payload.length;
    if (payloadLen > PacketConst.MaxBodySize) {
        throw new Error(`Payload too large: ${payloadLen} bytes (max ${PacketConst.MaxBodySize})`);
    }

    // Calculate content size: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + Payload
    const contentSize = 1 + msgIdLen + 2 + payloadLen;
    const totalSize = 4 + contentSize; // ContentSize header + content

    const buffer = new ArrayBuffer(totalSize);
    const view = new DataView(buffer);
    const bytes = new Uint8Array(buffer);

    let offset = 0;

    // ContentSize (4 bytes, little-endian)
    view.setInt32(offset, contentSize, true);
    offset += 4;

    // MsgIdLen (1 byte)
    view.setUint8(offset, msgIdLen);
    offset += 1;

    // MsgId (N bytes)
    bytes.set(msgIdBytes, offset);
    offset += msgIdLen;

    // MsgSeq (2 bytes, little-endian)
    view.setUint16(offset, msgSeq & 0xffff, true);
    offset += 2;

    // Payload
    bytes.set(packet.payload, offset);

    return bytes;
}

/**
 * Decodes a response packet from the server
 * @param data Raw binary data (without the ContentSize prefix - already consumed)
 * @returns Parsed packet
 */
export function decodePacket(data: Uint8Array): ParsedPacket {
    const view = new DataView(data.buffer, data.byteOffset, data.byteLength);
    let offset = 0;

    // MsgIdLen (1 byte)
    const msgIdLen = view.getUint8(offset);
    offset += 1;

    if (msgIdLen > 255) {
        throw new Error(`Invalid message ID length: ${msgIdLen}`);
    }

    // MsgId (N bytes)
    const msgIdBytes = data.subarray(offset, offset + msgIdLen);
    const msgId = textDecoder.decode(msgIdBytes);
    offset += msgIdLen;

    // MsgSeq (2 bytes, little-endian)
    const msgSeq = view.getUint16(offset, true);
    offset += 2;

    // ErrorCode (2 bytes, little-endian)
    const errorCode = view.getUint16(offset, true);
    offset += 2;

    // OriginalSize (4 bytes, little-endian)
    const originalSize = view.getInt32(offset, true);
    offset += 4;

    // Payload (remaining bytes)
    let payload = data.subarray(offset);

    // Handle LZ4 compression if originalSize > 0
    // Note: LZ4 decompression would require an external library
    // For now, we return the raw (possibly compressed) payload
    // Users can decompress manually if needed
    if (originalSize > 0) {
        throw new Error(
            `Received compressed payload (originalSize=${originalSize}). LZ4 decompression is not implemented. ` +
            `Either disable compression on the server or implement LZ4 decompression.`
        );
    }

    return new ParsedPacket(msgId, msgSeq, 0n, errorCode, payload, originalSize);
}

/**
 * Reads a 4-byte little-endian integer from a buffer
 * Used for parsing the ContentSize header
 */
export function readContentSize(data: Uint8Array, offset: number = 0): number {
    const view = new DataView(data.buffer, data.byteOffset + offset, 4);
    return view.getInt32(0, true);
}

/**
 * Size of the content size header in bytes
 */
export const CONTENT_SIZE_HEADER = 4;
