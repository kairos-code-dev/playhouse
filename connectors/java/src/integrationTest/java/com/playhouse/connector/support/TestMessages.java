package com.playhouse.connector.support;

import com.google.protobuf.ByteString;
import com.google.protobuf.InvalidProtocolBufferException;

import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;

/**
 * 테스트용 프로토콜 메시지 헬퍼 클래스
 * <p>
 * 간단한 protobuf 메시지 직렬화/역직렬화를 지원합니다.
 * 실제 프로젝트에서는 protoc로 생성된 클래스를 사용해야 합니다.
 * </p>
 */
public class TestMessages {

    /**
     * AuthenticateRequest 메시지
     */
    public static class AuthenticateRequest {
        public String userId;
        public String token;
        public Map<String, String> metadata = new HashMap<>();

        public AuthenticateRequest(String userId, String token) {
            this.userId = userId;
            this.token = token;
        }

        public byte[] toByteArray() {
            // Simplified: protobuf wire format
            // Field 1 (userId): tag=10 (field 1, type 2=string)
            // Field 2 (token): tag=18 (field 2, type 2=string)
            int estimatedSize = 32 + (userId != null ? userId.length() * 3 : 0) + (token != null ? token.length() * 3 : 0);
            ByteBuffer buffer = ByteBuffer.allocate(Math.max(1024, estimatedSize));
            writeString(buffer, 1, userId);
            writeString(buffer, 2, token);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * AuthenticateReply 메시지
     */
    public static class AuthenticateReply {
        public String accountId = "";
        public long stageId;
        public boolean success;
        public String receivedUserId = "";
        public String receivedToken = "";

        public static AuthenticateReply parseFrom(byte[] data) throws InvalidProtocolBufferException {
            AuthenticateReply reply = new AuthenticateReply();
            ByteBuffer buffer = ByteBuffer.wrap(data);

            while (buffer.hasRemaining()) {
                int tag = readVarint(buffer);
                int fieldNumber = tag >>> 3;
                int wireType = tag & 0x7;

                switch (fieldNumber) {
                    case 1: // account_id
                        reply.accountId = readString(buffer);
                        break;
                    case 2: // stage_id
                        reply.stageId = readVarint64(buffer);
                        break;
                    case 3: // received_user_id
                        reply.receivedUserId = readString(buffer);
                        break;
                    case 4: // received_token
                        reply.receivedToken = readString(buffer);
                        break;
                    case 5: // success
                        reply.success = readVarint(buffer) != 0;
                        break;
                    default:
                        skipField(buffer, wireType);
                }
            }
            return reply;
        }
    }

    /**
     * EchoRequest 메시지
     */
    public static class EchoRequest {
        public String content;
        public int sequence;

        public EchoRequest(String content, int sequence) {
            this.content = content;
            this.sequence = sequence;
        }

        public byte[] toByteArray() {
            int estimatedSize = 32 + (content != null ? content.length() * 3 : 0);
            ByteBuffer buffer = ByteBuffer.allocate(Math.max(1024, estimatedSize));
            writeString(buffer, 1, content);
            writeVarint(buffer, 2, sequence);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * EchoReply 메시지
     */
    public static class EchoReply {
        public String content = "";
        public int sequence;
        public long processedAt;

        public static EchoReply parseFrom(byte[] data) throws InvalidProtocolBufferException {
            EchoReply reply = new EchoReply();
            ByteBuffer buffer = ByteBuffer.wrap(data);

            while (buffer.hasRemaining()) {
                int tag = readVarint(buffer);
                int fieldNumber = tag >>> 3;
                int wireType = tag & 0x7;

                switch (fieldNumber) {
                    case 1: // content
                        reply.content = readString(buffer);
                        break;
                    case 2: // sequence
                        reply.sequence = readVarint(buffer);
                        break;
                    case 3: // processed_at
                        reply.processedAt = readVarint64(buffer);
                        break;
                    default:
                        skipField(buffer, wireType);
                }
            }
            return reply;
        }
    }

    /**
     * BroadcastRequest 메시지
     */
    public static class BroadcastRequest {
        public String content;

        public BroadcastRequest(String content) {
            this.content = content;
        }

        public byte[] toByteArray() {
            int estimatedSize = 32 + (content != null ? content.length() * 3 : 0);
            ByteBuffer buffer = ByteBuffer.allocate(Math.max(1024, estimatedSize));
            writeString(buffer, 1, content);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * BroadcastNotify 메시지
     */
    public static class BroadcastNotify {
        public String eventType = "";
        public String data = "";
        public long fromAccountId;
        public String senderId = "";

        public static BroadcastNotify parseFrom(byte[] rawData) throws InvalidProtocolBufferException {
            BroadcastNotify notify = new BroadcastNotify();
            ByteBuffer buffer = ByteBuffer.wrap(rawData);

            while (buffer.hasRemaining()) {
                int tag = readVarint(buffer);
                int fieldNumber = tag >>> 3;
                int wireType = tag & 0x7;

                switch (fieldNumber) {
                    case 1: // event_type
                        notify.eventType = readString(buffer);
                        break;
                    case 2: // data
                        notify.data = readString(buffer);
                        break;
                    case 3: // from_account_id
                        notify.fromAccountId = readVarint64(buffer);
                        break;
                    case 4: // sender_id
                        notify.senderId = readString(buffer);
                        break;
                    default:
                        skipField(buffer, wireType);
                }
            }
            return notify;
        }
    }

    /**
     * FailRequest 메시지
     */
    public static class FailRequest {
        public int errorCode;
        public String errorMessage;

        public FailRequest(int errorCode, String errorMessage) {
            this.errorCode = errorCode;
            this.errorMessage = errorMessage;
        }

        public byte[] toByteArray() {
            int estimatedSize = 32 + (errorMessage != null ? errorMessage.length() * 3 : 0);
            ByteBuffer buffer = ByteBuffer.allocate(Math.max(1024, estimatedSize));
            writeVarint(buffer, 1, errorCode);
            writeString(buffer, 2, errorMessage);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * FailReply 메시지
     */
    public static class FailReply {
        public int errorCode;
        public String message = "";

        public static FailReply parseFrom(byte[] data) throws InvalidProtocolBufferException {
            FailReply reply = new FailReply();
            ByteBuffer buffer = ByteBuffer.wrap(data);

            while (buffer.hasRemaining()) {
                int tag = readVarint(buffer);
                int fieldNumber = tag >>> 3;
                int wireType = tag & 0x7;

                switch (fieldNumber) {
                    case 1: // error_code
                        reply.errorCode = readVarint(buffer);
                        break;
                    case 2: // message
                        reply.message = readString(buffer);
                        break;
                    default:
                        skipField(buffer, wireType);
                }
            }
            return reply;
        }
    }

    /**
     * NoResponseRequest 메시지
     */
    public static class NoResponseRequest {
        public int delayMs;

        public NoResponseRequest(int delayMs) {
            this.delayMs = delayMs;
        }

        public byte[] toByteArray() {
            ByteBuffer buffer = ByteBuffer.allocate(128);
            writeVarint(buffer, 1, delayMs);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * LargePayloadRequest 메시지
     */
    public static class LargePayloadRequest {
        public int sizeBytes;

        public LargePayloadRequest(int sizeBytes) {
            this.sizeBytes = sizeBytes;
        }

        public byte[] toByteArray() {
            ByteBuffer buffer = ByteBuffer.allocate(128);
            writeVarint(buffer, 1, sizeBytes);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * BenchmarkReply 메시지
     * Proto field numbers:
     * - sequence: 1 (int32)
     * - processed_at: 2 (int64)
     * - payload: 3 (bytes)
     */
    public static class BenchmarkReply {
        public int sequence;
        public long processedAt;
        public byte[] payload;

        public static BenchmarkReply parseFrom(byte[] data) throws InvalidProtocolBufferException {
            BenchmarkReply reply = new BenchmarkReply();
            reply.payload = new byte[0];
            ByteBuffer buffer = ByteBuffer.wrap(data);

            while (buffer.hasRemaining()) {
                int tag = readVarint(buffer);
                int fieldNumber = tag >>> 3;
                int wireType = tag & 0x7;

                switch (fieldNumber) {
                    case 1: // sequence (int32)
                        reply.sequence = readVarint(buffer);
                        break;
                    case 2: // processed_at (int64)
                        reply.processedAt = readVarint64(buffer);
                        break;
                    case 3: // payload (bytes)
                        if (wireType == 2) {
                            int length = readVarint(buffer);
                            if (length < 0 || length > buffer.remaining()) {
                                throw new IllegalArgumentException("Invalid payload length: " + length);
                            }
                            reply.payload = new byte[length];
                            buffer.get(reply.payload);
                        } else {
                            skipField(buffer, wireType);
                        }
                        break;
                    default:
                        skipField(buffer, wireType);
                }
            }
            return reply;
        }
    }

    // ===== Protobuf Wire Format Helpers =====

    private static void writeString(ByteBuffer buffer, int fieldNumber, String value) {
        if (value == null || value.isEmpty()) {
            return;
        }

        byte[] bytes = value.getBytes(StandardCharsets.UTF_8);
        int tag = (fieldNumber << 3) | 2; // Wire type 2 = length-delimited

        writeVarint(buffer, tag);
        writeVarint(buffer, bytes.length);
        buffer.put(bytes);
    }

    private static void writeVarint(ByteBuffer buffer, int fieldNumber, int value) {
        int tag = (fieldNumber << 3) | 0; // Wire type 0 = varint
        writeVarint(buffer, tag);
        writeVarint(buffer, value);
    }

    private static void writeVarint(ByteBuffer buffer, int value) {
        while ((value & ~0x7F) != 0) {
            buffer.put((byte) ((value & 0x7F) | 0x80));
            value >>>= 7;
        }
        buffer.put((byte) value);
    }

    // ===== Protobuf Read Helpers =====

    private static int readVarint(ByteBuffer buffer) {
        int result = 0;
        int shift = 0;
        while (buffer.hasRemaining()) {
            byte b = buffer.get();
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) {
                return result;
            }
            shift += 7;
            if (shift >= 32) {
                throw new IllegalArgumentException("Varint too long");
            }
        }
        throw new IllegalArgumentException("Truncated varint");
    }

    private static long readVarint64(ByteBuffer buffer) {
        long result = 0;
        int shift = 0;
        while (buffer.hasRemaining()) {
            byte b = buffer.get();
            result |= (long) (b & 0x7F) << shift;
            if ((b & 0x80) == 0) {
                return result;
            }
            shift += 7;
            if (shift >= 64) {
                throw new IllegalArgumentException("Varint64 too long");
            }
        }
        throw new IllegalArgumentException("Truncated varint64");
    }

    private static String readString(ByteBuffer buffer) {
        int length = readVarint(buffer);
        if (length < 0 || length > buffer.remaining()) {
            throw new IllegalArgumentException("Invalid string length: " + length);
        }
        byte[] bytes = new byte[length];
        buffer.get(bytes);
        return new String(bytes, StandardCharsets.UTF_8);
    }

    private static void skipField(ByteBuffer buffer, int wireType) {
        switch (wireType) {
            case 0: // Varint
                readVarint64(buffer);
                break;
            case 1: // 64-bit
                if (buffer.remaining() < 8) {
                    throw new IllegalArgumentException("Truncated 64-bit field");
                }
                buffer.position(buffer.position() + 8);
                break;
            case 2: // Length-delimited
                int length = readVarint(buffer);
                if (length < 0 || length > buffer.remaining()) {
                    throw new IllegalArgumentException("Invalid length-delimited field");
                }
                buffer.position(buffer.position() + length);
                break;
            case 5: // 32-bit
                if (buffer.remaining() < 4) {
                    throw new IllegalArgumentException("Truncated 32-bit field");
                }
                buffer.position(buffer.position() + 4);
                break;
            default:
                throw new IllegalArgumentException("Unknown wire type: " + wireType);
        }
    }
}
