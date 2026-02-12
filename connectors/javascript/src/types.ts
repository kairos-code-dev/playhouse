/**
 * PlayHouse Connector - Common Type Definitions
 */

/**
 * Error codes returned by the connector
 */
export const ErrorCode = {
    /** Success - no error */
    Success: 0,

    // Connection errors (1xxx)
    /** Connection failed */
    ConnectionFailed: 1001,
    /** Connection timeout during initial connection */
    ConnectionTimeout: 1002,
    /** Connection closed unexpectedly */
    ConnectionClosed: 1003,
    /** Heartbeat timeout - no response from server */
    HeartbeatTimeout: 1004,

    // Request errors (2xxx)
    /** Request timeout */
    RequestTimeout: 2001,
    /** Invalid response received */
    InvalidResponse: 2002,
    /** Invalid request format */
    InvalidRequest: 2003,

    // Authentication errors (3xxx)
    /** Authentication failed */
    AuthenticationFailed: 3001,
    /** Authentication required but not performed */
    AuthenticationRequired: 3002,

    // C# compatible error codes (PlayHouse standard - 60xxx)
    /** Disconnected before operation - matches C# ConnectorErrorCode.Disconnected */
    Disconnected: 60201,
    /** Request timeout - matches C# ConnectorErrorCode.RequestTimeout */
    RequestTimeoutLegacy: 60202,
    /** Unauthenticated state - matches C# ConnectorErrorCode.Unauthenticated */
    Unauthenticated: 60203,
} as const;

export type ErrorCodeType = (typeof ErrorCode)[keyof typeof ErrorCode];

/**
 * Internal packet constants
 */
export const PacketConst = {
    /** Maximum message ID length in bytes */
    MsgIdLimit: 256,
    /** Maximum payload size (2MB) */
    MaxBodySize: 1024 * 1024 * 2,
    /** Minimum response size with 1-byte MsgId: MsgIdLen(1) + MsgId(1) + MsgSeq(2) + ErrorCode(2) + OriginalSize(4) = 10 */
    MinHeaderSize: 10,
    /** Heartbeat message ID */
    HeartBeat: '@Heart@Beat@',
    /** Debug message ID */
    Debug: '@Debug@',
    /** Timeout message ID */
    Timeout: '@Timeout@',
} as const;

/**
 * Protobuf message interface (compatible with protobufjs)
 */
export interface IProtoMessage {
    /** Returns the message type name */
    $type?: { name: string };
    /** Encodes the message to bytes */
    encode?(): { finish(): Uint8Array };
}

/**
 * Protobuf decoder interface
 */
export interface IProtoDecoder<T> {
    decode(data: Uint8Array): T;
}

/**
 * Disposable interface for resource cleanup
 */
export interface IDisposable {
    dispose(): void;
}

/**
 * Connector error class with error code
 */
export class ConnectorError extends Error {
    constructor(
        message: string,
        public readonly code: ErrorCodeType,
        public readonly request?: unknown
    ) {
        super(message);
        this.name = 'ConnectorError';
        // Restore prototype chain
        Object.setPrototypeOf(this, ConnectorError.prototype);
    }
}

/**
 * Type guard to check if a value is an Error
 */
export function isError(value: unknown): value is Error {
    return value instanceof Error;
}

/**
 * Safely extracts error message from unknown catch value
 */
export function getErrorMessage(error: unknown): string {
    if (isError(error)) {
        return error.message;
    }
    if (typeof error === 'string') {
        return error;
    }
    return String(error);
}
