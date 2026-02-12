package com.playhouse.connector.internal;

import com.playhouse.connector.Packet;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;

/**
 * 패킷 인코딩/디코딩
 * <p>
 * Request Packet: ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + Payload(...)
 * Response Packet: ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + ErrorCode(2) + OriginalSize(4) + Payload(...)
 * <p>
 * Byte Order: Little-endian
 */
public final class PacketCodec {

    private static final Logger logger = LoggerFactory.getLogger(PacketCodec.class);

    /**
     * Maximum allowed packet size (16MB) to prevent memory DoS attacks.
     * This should be configurable if larger packets are needed.
     */
    public static final int MAX_PACKET_SIZE = 16 * 1024 * 1024;

    /**
     * Maximum decompression ratio to prevent decompression bombs.
     * If originalSize > compressed size * MAX_DECOMPRESSION_RATIO, reject the packet.
     * A ratio of 100 means we allow up to 100x expansion during decompression.
     */
    public static final int MAX_DECOMPRESSION_RATIO = 100;

    /**
     * Minimum request header size: ContentSize(4) + MsgIdLen(1) + MsgId(1) + MsgSeq(2)
     */
    private static final int MIN_HEADER_SIZE = 8;

    private PacketCodec() {
        // Utility class
    }

    /**
     * 요청 패킷 인코딩
     *
     * @param packet  패킷
     * @param msgSeq  메시지 시퀀스
     * @param stageId Unused (kept for call-site compatibility)
     * @return 인코딩된 ByteBuffer
     */
    public static ByteBuffer encodeRequest(Packet packet, short msgSeq, long stageId) {
        byte[] msgIdBytes = packet.getMsgId().getBytes(StandardCharsets.UTF_8);
        byte[] payload = packet.getPayload();

        // ContentSize = MsgIdLen(1) + MsgId(N) + MsgSeq(2) + Payload(...)
        int contentSize = 1 + msgIdBytes.length + 2 + payload.length;
        int totalSize = 4 + contentSize;

        ByteBuffer buffer = ByteBuffer.allocate(totalSize).order(ByteOrder.LITTLE_ENDIAN);

        // Write
        buffer.putInt(contentSize);                      // ContentSize (4)
        buffer.put((byte) msgIdBytes.length);            // MsgIdLen (1)
        buffer.put(msgIdBytes);                          // MsgId (N)
        buffer.putShort(msgSeq);                         // MsgSeq (2)
        buffer.put(payload);                             // Payload (...)

        buffer.flip();

        if (logger.isDebugEnabled()) {
            logger.debug("Encoded request packet: msgId={}, msgSeq={}, payloadSize={}",
                packet.getMsgId(), msgSeq, payload.length);
        }

        return buffer;
    }

    /**
     * 응답 패킷 디코딩
     *
     * @param buffer 수신된 데이터 버퍼
     * @return 디코딩된 패킷
     * @throws IllegalArgumentException if packet structure is invalid
     */
    public static Packet decodeResponse(ByteBuffer buffer) {
        buffer.order(ByteOrder.LITTLE_ENDIAN);

        // Validate minimum buffer size (ContentSize + MsgIdLen + at least 1 byte for MsgId + rest of header)
        if (buffer.remaining() < MIN_HEADER_SIZE + 6) { // +6 for ErrorCode(2) + OriginalSize(4)
            throw new IllegalArgumentException("Buffer too small for response packet: " + buffer.remaining());
        }

        int startPosition = buffer.position();

        // Read ContentSize
        int contentSize = buffer.getInt();

        // Validate contentSize against buffer remaining
        if (contentSize < MIN_HEADER_SIZE - 4 || contentSize > buffer.remaining()) {
            throw new IllegalArgumentException(
                String.format("Invalid contentSize: %d (buffer remaining: %d, min: %d)",
                    contentSize, buffer.remaining(), MIN_HEADER_SIZE - 4));
        }

        // Calculate expected total size including the already-read ContentSize field
        int headerTotalSize = 4 + contentSize;
        int bufferTotal = buffer.position() - startPosition + buffer.remaining();

        if (bufferTotal < headerTotalSize) {
            throw new IllegalArgumentException(
                String.format("Buffer size mismatch: expected %d, got %d", headerTotalSize, bufferTotal));
        }

        // Read MsgIdLen
        int msgIdLen = buffer.get() & 0xFF;

        // Validate msgIdLen - it must fit within contentSize along with other fields
        // Response header after ContentSize: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + ErrorCode(2) + OriginalSize(4)
        // Minimum overhead = 1 + 2 + 2 + 4 = 9 bytes
        int minContentSizeForMsgId = 9 + msgIdLen;
        if (msgIdLen == 0 || msgIdLen > 255) {
            throw new IllegalArgumentException("Invalid msgIdLen: " + msgIdLen);
        }

        if (minContentSizeForMsgId > contentSize) {
            throw new IllegalArgumentException(
                String.format("msgIdLen %d too large for contentSize %d", msgIdLen, contentSize));
        }

        // Validate buffer has enough data for msgId
        if (buffer.remaining() < msgIdLen) {
            throw new IllegalArgumentException(
                String.format("Not enough data for msgId: need %d, have %d", msgIdLen, buffer.remaining()));
        }

        // Read MsgId
        byte[] msgIdBytes = new byte[msgIdLen];
        buffer.get(msgIdBytes);
        String msgId = new String(msgIdBytes, StandardCharsets.UTF_8);

        // Validate remaining fields can be read
        // Need: MsgSeq(2) + ErrorCode(2) + OriginalSize(4) = 8 bytes minimum
        if (buffer.remaining() < 8) {
            throw new IllegalArgumentException(
                String.format("Not enough data for remaining header fields: need 8, have %d", buffer.remaining()));
        }

        // Read MsgSeq
        short msgSeq = buffer.getShort();

        // Read ErrorCode
        short errorCode = buffer.getShort();

        // Read OriginalSize
        int originalSize = buffer.getInt();

        // Validate originalSize is reasonable (not negative, not exceeding MAX_PACKET_SIZE)
        if (originalSize < 0) {
            throw new IllegalArgumentException("Invalid originalSize: " + originalSize + " (negative)");
        }

        if (originalSize > MAX_PACKET_SIZE) {
            throw new IllegalArgumentException(
                String.format("originalSize %d exceeds maximum allowed size %d", originalSize, MAX_PACKET_SIZE));
        }

        // Validate decompression ratio to prevent decompression bombs
        // originalSize represents the decompressed size, payloadSize will be the compressed size
        // We'll check this after reading the payload
        int compressedSize = buffer.remaining();
        if (originalSize > 0 && compressedSize > 0 && originalSize > compressedSize * MAX_DECOMPRESSION_RATIO) {
            throw new IllegalArgumentException(
                String.format("Decompression ratio too high: originalSize=%d, compressedSize=%d, ratio=%.2f (max: %d)",
                    originalSize, compressedSize, (double) originalSize / compressedSize, MAX_DECOMPRESSION_RATIO));
        }

        // Read Payload
        int payloadSize = buffer.remaining();

        // Validate payload size is non-negative
        if (payloadSize < 0) {
            throw new IllegalArgumentException("Invalid payload size: " + payloadSize);
        }

        byte[] payload = new byte[payloadSize];
        buffer.get(payload);

        if (logger.isDebugEnabled()) {
            logger.debug("Decoded response packet: msgId={}, msgSeq={}, errorCode={}, originalSize={}, payloadSize={}",
                msgId, msgSeq, errorCode, originalSize, payloadSize);
        }

        return Packet.builder(msgId)
            .msgSeq(msgSeq)
            .stageId(0L)
            .errorCode(errorCode)
            .originalSize(originalSize)
            .payload(payload)
            .build();
    }

    /**
     * 다음 패킷 크기 읽기 (ContentSize만 읽음)
     *
     * @param buffer 수신된 데이터 버퍼
     * @return 패킷 크기 (헤더 4바이트 포함), 데이터 부족 시 -1, 유효하지 않은 크기 시 -2
     */
    public static int peekPacketSize(ByteBuffer buffer) {
        if (buffer.remaining() < 4) {
            return -1;
        }

        int position = buffer.position();
        buffer.order(ByteOrder.LITTLE_ENDIAN);
        int contentSize = buffer.getInt(position);

        // Validate contentSize to prevent memory DoS and buffer issues
        if (contentSize < MIN_HEADER_SIZE - 4) {
            // contentSize should be at least minimum header size minus ContentSize(4) field
            logger.error("Invalid packet: contentSize {} is too small (min: {})", contentSize, MIN_HEADER_SIZE - 4);
            return -2;
        }

        if (contentSize > MAX_PACKET_SIZE) {
            logger.error("Invalid packet: contentSize {} exceeds maximum allowed size ({})", contentSize, MAX_PACKET_SIZE);
            return -2;
        }

        // Total size = ContentSize (header) + ContentSize (body)
        return 4 + contentSize;
    }
}
