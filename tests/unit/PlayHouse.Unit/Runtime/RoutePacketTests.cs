using FluentAssertions;
using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;
using Xunit;

namespace PlayHouse.Unit.Runtime;

/// <summary>
/// RuntimeRoutePacket 단위 테스트
/// </summary>
public class RoutePacketTests
{
    #region 패킷 생성

    [Fact(DisplayName = "FromFrames로 수신된 바이트에서 패킷을 생성할 수 있다")]
    public void RuntimeRoutePacket_FromFrames_CreatesPacketFromBytes()
    {
        // Given
        var header = new RouteHeader
        {
            MsgSeq = 100,
            ServiceId = 1,
            MsgId = "TestMessage",
            From = "1:1",
            StageId = "42",
            AccountId = 12345
        };
        var headerBytes = header.ToByteArray();
        var payloadBytes = new byte[] { 1, 2, 3, 4, 5 };

        // When
        var packet = RoutePacket.FromFrames(headerBytes, payloadBytes);

        // Then
        packet.MsgSeq.Should().Be(100);
        packet.MsgId.Should().Be("TestMessage");
        packet.From.Should().Be("1:1");
        packet.StageId.Should().Be("42");
        packet.AccountId.Should().Be(12345);
        packet.Payload.DataSpan.ToArray().Should().BeEquivalentTo(payloadBytes);
    }

    [Fact(DisplayName = "Of로 RouteHeader와 payload로 패킷을 생성할 수 있다")]
    public void RuntimeRoutePacket_Of_CreatesPacketFromHeaderAndPayload()
    {
        // Given
        var header = new RouteHeader
        {
            MsgSeq = 50,
            ServiceId = 2,
            MsgId = "CustomMsg"
        };
        var payloadBytes = new byte[] { 10, 20, 30 };

        // When
        var packet = RoutePacket.Of(header, payloadBytes);

        // Then
        packet.Header.Should().Be(header);
        packet.Payload.DataSpan.ToArray().Should().BeEquivalentTo(payloadBytes);
    }

    [Fact(DisplayName = "Empty로 payload 없는 패킷을 생성할 수 있다")]
    public void RuntimeRoutePacket_Empty_CreatesPacketWithoutPayload()
    {
        // Given
        var header = new RouteHeader
        {
            MsgSeq = 1,
            ErrorCode = 500
        };

        // When
        var packet = RoutePacket.Empty(header);

        // Then
        packet.Payload.Length.Should().Be(0);
        packet.ErrorCode.Should().Be(500);
    }

    #endregion

    #region Reply 생성

    [Fact(DisplayName = "CreateErrorReply로 에러 응답 패킷을 생성할 수 있다")]
    public void RuntimeRoutePacket_CreateErrorReply_CreatesErrorResponse()
    {
        // Given
        var header = new RouteHeader
        {
            MsgSeq = 123,
            ServiceId = 1,
            MsgId = "Request"
        };
        var request = RoutePacket.Of(header, Array.Empty<byte>());

        // When
        var reply = request.CreateErrorReply(404);

        // Then
        reply.MsgSeq.Should().Be(123, "응답은 요청과 동일한 MsgSeq를 가져야 한다");
        reply.ErrorCode.Should().Be(404);
        reply.IsError.Should().BeTrue();
    }

    #endregion

    #region 직렬화

    [Fact(DisplayName = "SerializeHeader로 헤더를 바이트로 변환할 수 있다")]
    public void RuntimeRoutePacket_SerializeHeader_ReturnsByteArray()
    {
        // Given
        var header = new RouteHeader
        {
            MsgSeq = 999,
            MsgId = "TestSerialization"
        };
        var packet = RoutePacket.Of(header, Array.Empty<byte>());

        // When
        var bytes = packet.SerializeHeader();

        // Then
        bytes.Should().NotBeEmpty();

        // Verify roundtrip
        var parsed = RouteHeader.Parser.ParseFrom(bytes);
        parsed.MsgSeq.Should().Be(999);
        parsed.MsgId.Should().Be("TestSerialization");
    }

    [Fact(DisplayName = "GetPayloadBytes로 페이로드를 바이트로 변환할 수 있다")]
    public void RuntimeRoutePacket_GetPayloadBytes_ReturnsPayloadData()
    {
        // Given
        var expectedPayload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var header = new RouteHeader { MsgSeq = 1 };
        var packet = RoutePacket.Of(header, expectedPayload);

        // When
        var bytes = packet.GetPayloadSpan().ToArray();

        // Then
        bytes.Should().BeEquivalentTo(expectedPayload);
    }

    #endregion

    #region 속성 검증

    [Fact(DisplayName = "IsError는 ErrorCode가 0이 아닐 때 true를 반환한다")]
    public void RuntimeRoutePacket_IsError_ReturnsTrueWhenErrorCodeNonZero()
    {
        // Given
        var header = new RouteHeader { ErrorCode = 100 };
        var packet = RoutePacket.Empty(header);

        // Then
        packet.IsError.Should().BeTrue();
    }

    [Fact(DisplayName = "IsError는 ErrorCode가 0일 때 false를 반환한다")]
    public void RuntimeRoutePacket_IsError_ReturnsFalseWhenErrorCodeZero()
    {
        // Given
        var header = new RouteHeader { ErrorCode = 0 };
        var packet = RoutePacket.Empty(header);

        // Then
        packet.IsError.Should().BeFalse();
    }

    #endregion

    #region Dispose

    [Fact(DisplayName = "Dispose 후에는  Payload는 접근하면 예외 발생")]
    public void RuntimeRoutePacket_AfterDispose_PayloadRemainsAccessible()
    {
        // Given
        var header = new RouteHeader { MsgSeq = 1 };
        var packet = RoutePacket.Of(header, new byte[] { 1, 2, 3 });

        // When
        packet.Dispose();

        // Then - should not throw (payload is already copied)
        var action = () => packet.Payload.Length;
        action.Should().Throw<Exception>();
    }

    #endregion
}

/// <summary>
/// RuntimePayload 단위 테스트
/// </summary>
public class RuntimePayloadTests
{
    #region MemoryPayload

    [Fact(DisplayName = "MemoryPayload는 바이트 배열을 래핑한다")]
    public void MemoryPayload_WrapsByteArray_ReturnsSameData()
    {
        // Given
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // When
        var payload = new MemoryPayload(data);

        // Then
        payload.DataSpan.ToArray().Should().BeEquivalentTo(data);
        ((IPayload)payload).Length.Should().Be(5);
    }

    [Fact(DisplayName = "MemoryPayload는 ReadOnlyMemory로부터 생성할 수 있다")]
    public void MemoryPayload_FromMemory_ReturnsCorrectData()
    {
        // Given
        ReadOnlyMemory<byte> memory = new byte[] { 10, 20, 30 };

        // When
        var payload = new MemoryPayload(memory);

        // Then
        payload.DataSpan.ToArray().Should().BeEquivalentTo(new byte[] { 10, 20, 30 });
    }

    #endregion

    #region EmptyPayload

    [Fact(DisplayName = "EmptyPayload는 싱글톤이다")]
    public void EmptyPayload_Instance_IsSingleton()
    {
        // When
        var first = EmptyPayload.Instance;
        var second = EmptyPayload.Instance;

        // Then
        first.Should().BeSameAs(second);
    }

    [Fact(DisplayName = "EmptyPayload의 길이는 0이다")]
    public void EmptyPayload_Length_IsZero()
    {
        // When
        var payload = EmptyPayload.Instance;

        // Then
        ((IPayload)payload).Length.Should().Be(0);
        payload.DataSpan.IsEmpty.Should().BeTrue();
    }

    #endregion
}
