#nullable enable

using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using PlayHouse.Connector.Protocol;
using Xunit;

namespace PlayHouse.Unit.Connector;

/// <summary>
/// 단위 테스트: Packet 클래스의 생성 및 데이터 접근 기능 검증
/// </summary>
public class PacketTests
{
    [Fact(DisplayName = "Packet - MsgId와 Payload로 생성한다")]
    public void Packet_CreateWithMsgIdAndPayload()
    {
        // Given (전제조건)
        var msgId = "TestMessage";
        var payload = new BytePayload(new byte[] { 1, 2, 3 });

        // When (행동)
        using var packet = new Packet(msgId, payload);

        // Then (결과)
        packet.MsgId.Should().Be(msgId, "MsgId가 설정되어야 함");
        packet.Payload.Should().BeSameAs(payload, "Payload가 설정되어야 함");
    }

    [Fact(DisplayName = "Packet - Protobuf 메시지로 생성한다")]
    public void Packet_CreateWithProtobufMessage()
    {
        // Given (전제조건)
        var message = new StringValue { Value = "Hello" };
        var expectedMsgId = "StringValue"; // Descriptor.Name은 단순 이름만 반환

        // When (행동)
        using var packet = new Packet(message);

        // Then (결과)
        packet.MsgId.Should().Be(expectedMsgId, "MsgId는 Protobuf 메시지 Descriptor.Name이어야 함");
        packet.Payload.DataSpan.Length.Should().BeGreaterThan(0, "Payload 데이터가 있어야 함");
    }

    [Fact(DisplayName = "Packet - MsgId와 byte 배열로 생성한다")]
    public void Packet_CreateWithMsgIdAndByteArray()
    {
        // Given (전제조건)
        var msgId = "BinaryMessage";
        var data = new byte[] { 10, 20, 30, 40 };

        // When (행동)
        using var packet = new Packet(msgId, data);

        // Then (결과)
        packet.MsgId.Should().Be(msgId, "MsgId가 설정되어야 함");
        packet.Payload.DataSpan.ToArray().Should().Equal(data, "바이트 데이터가 저장되어야 함");
    }

    [Fact(DisplayName = "Packet - MsgId와 ReadOnlySpan으로 생성한다")]
    public void Packet_CreateWithMsgIdAndReadOnlySpan()
    {
        // Given (전제조건)
        var msgId = "SpanMessage";
        var source = new byte[] { 1, 2, 3, 4 };

        // When (행동)
        using var packet = new Packet(msgId, source.AsSpan());

        // Then (결과)
        packet.MsgId.Should().Be(msgId);
        packet.Payload.DataSpan.ToArray().Should().Equal(source);
    }

    [Fact(DisplayName = "Packet - MsgId와 ReadOnlyMemory로 생성하면 Zero-copy로 동작한다")]
    public void Packet_CreateWithMsgIdAndReadOnlyMemory_UsesZeroCopy()
    {
        // Given (전제조건)
        var msgId = "MemoryMessage";
        var source = new byte[] { 10, 20, 30 };

        // When (행동)
        using var packet = new Packet(msgId, source.AsMemory());
        source[1] = 99;

        // Then (결과)
        packet.MsgId.Should().Be(msgId);
        packet.Payload.DataSpan[1].Should().Be(99, "ReadOnlyMemory 생성자는 원본 메모리를 직접 참조해야 함");
    }

    [Fact(DisplayName = "Packet.Empty - 빈 Payload를 가진 Packet을 생성한다")]
    public void Packet_Empty_CreatesPacketWithEmptyPayload()
    {
        // Given (전제조건)
        var msgId = "EmptyMessage";

        // When (행동)
        using var packet = Packet.Empty(msgId);

        // Then (결과)
        packet.MsgId.Should().Be(msgId, "MsgId가 설정되어야 함");
        packet.Payload.DataSpan.Length.Should().Be(0, "빈 Payload여야 함");
    }

    [Fact(DisplayName = "Packet - Dispose 후 Payload도 정리된다")]
    public void Packet_Dispose_DisposesPayload()
    {
        // Given (전제조건)
        var packet = new Packet("Test", new byte[] { 1, 2, 3 });

        // When (행동)
        var action = () => packet.Dispose();

        // Then (결과)
        action.Should().NotThrow("Dispose가 정상적으로 완료되어야 함");
    }

    [Fact(DisplayName = "Packet - null MsgId는 예외를 발생시킨다")]
    public void Packet_NullMsgId_ThrowsException()
    {
        // Given (전제조건)
        string? nullMsgId = null;

        // When (행동)
        var action = () => new Packet(nullMsgId!, EmptyPayload.Instance);

        // Then (결과)
        action.Should().Throw<ArgumentNullException>("null MsgId는 허용되지 않아야 함");
    }

    [Fact(DisplayName = "Packet - null Payload는 예외를 발생시킨다")]
    public void Packet_NullPayload_ThrowsException()
    {
        // Given (전제조건)
        IPayload? nullPayload = null;

        // When (행동)
        var action = () => new Packet("Test", nullPayload!);

        // Then (결과)
        action.Should().Throw<ArgumentNullException>("null Payload는 허용되지 않아야 함");
    }

    [Fact(DisplayName = "Packet - 긴 MsgId도 정상 처리된다")]
    public void Packet_LongMsgId_HandledCorrectly()
    {
        // Given (전제조건)
        var longMsgId = new string('A', 200); // 200자 MsgId

        // When (행동)
        using var packet = Packet.Empty(longMsgId);

        // Then (결과)
        packet.MsgId.Should().Be(longMsgId, "긴 MsgId도 저장되어야 함");
    }

    [Fact(DisplayName = "Packet - IPacket 인터페이스를 구현한다")]
    public void Packet_ImplementsIPacketInterface()
    {
        // Given (전제조건)
        using IPacket packet = new Packet("Interface.Test", new byte[] { 1 });

        // When (행동)
        var msgId = packet.MsgId;
        var payload = packet.Payload;

        // Then (결과)
        msgId.Should().Be("Interface.Test");
        payload.Should().NotBeNull();
    }

    [Fact(DisplayName = "Packet - Protobuf 메시지 데이터가 역직렬화 가능하다")]
    public void Packet_ProtobufPayload_IsDeserializable()
    {
        // Given (전제조건)
        var original = new Int32Value { Value = 42 };
        using var packet = new Packet(original);

        // When (행동)
        var deserialized = Int32Value.Parser.ParseFrom(packet.Payload.DataSpan);

        // Then (결과)
        deserialized.Value.Should().Be(42, "역직렬화 값이 원본과 같아야 함");
    }
}
