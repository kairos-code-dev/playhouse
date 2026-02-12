#nullable enable

using System.Buffers;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using PlayHouse.Connector.Protocol;
using Xunit;

namespace PlayHouse.Unit.Connector;

/// <summary>
/// 단위 테스트: Payload 구현체들의 데이터 저장 및 직렬화 기능 검증
/// </summary>
public class PayloadTests
{
    #region EmptyPayload Tests

    [Fact(DisplayName = "EmptyPayload.Instance - 싱글톤 인스턴스를 반환한다")]
    public void EmptyPayload_Instance_ReturnsSingleton()
    {
        // Given (전제조건)
        // When (행동)
        var instance1 = EmptyPayload.Instance;
        var instance2 = EmptyPayload.Instance;

        // Then (결과)
        instance1.Should().BeSameAs(instance2, "싱글톤 인스턴스여야 함");
    }

    [Fact(DisplayName = "EmptyPayload.Data - 빈 메모리를 반환한다")]
    public void EmptyPayload_Data_ReturnsEmptyMemory()
    {
        // Given (전제조건)
        var payload = EmptyPayload.Instance;

        // When (행동)
        var data = payload.DataSpan;

        // Then (결과)
        data.Length.Should().Be(0, "빈 데이터여야 함");
        payload.DataMemory.Length.Should().Be(0, "빈 Memory여야 함");
    }

    [Fact(DisplayName = "EmptyPayload.Dispose - 여러 번 호출해도 예외가 발생하지 않는다")]
    public void EmptyPayload_Dispose_MultipleCalls_NoException()
    {
        // Given (전제조건)
        var payload = EmptyPayload.Instance;

        // When (행동)
        var action = () =>
        {
            payload.Dispose();
            payload.Dispose();
        };

        // Then (결과)
        action.Should().NotThrow("싱글톤은 Dispose해도 예외가 없어야 함");
    }

    #endregion

    #region BytePayload Tests

    [Fact(DisplayName = "BytePayload - 바이트 배열을 저장하고 반환한다")]
    public void BytePayload_StoresAndReturnsData()
    {
        // Given (전제조건)
        var testData = new byte[] { 1, 2, 3, 4, 5 };

        // When (행동)
        using var payload = new BytePayload(testData);
        var data = payload.DataSpan.ToArray();

        // Then (결과)
        data.Should().Equal(testData, "저장한 데이터가 동일해야 함");
    }

    [Fact(DisplayName = "BytePayload - 빈 배열도 저장한다")]
    public void BytePayload_EmptyArray_StoresCorrectly()
    {
        // Given (전제조건)
        var emptyData = Array.Empty<byte>();

        // When (행동)
        using var payload = new BytePayload(emptyData);

        // Then (결과)
        payload.DataSpan.Length.Should().Be(0, "빈 배열도 저장 가능해야 함");
    }

    [Fact(DisplayName = "BytePayload - null 배열은 예외를 발생시킨다")]
    public void BytePayload_NullArray_ThrowsException()
    {
        // Given (전제조건)
        byte[]? nullData = null;

        // When (행동)
        var action = () => new BytePayload(nullData!);

        // Then (결과)
        action.Should().Throw<ArgumentNullException>("null 배열은 허용되지 않아야 함");
    }

    [Fact(DisplayName = "BytePayload.DataSpan - Span으로 데이터에 접근한다")]
    public void BytePayload_DataSpan_ReturnsCorrectSpan()
    {
        // Given (전제조건)
        var testData = new byte[] { 10, 20, 30 };
        using IPayload payload = new BytePayload(testData);

        // When (행동)
        var span = payload.DataSpan;

        // Then (결과)
        span.Length.Should().Be(3, "Span 길이가 원본과 같아야 함");
        span[0].Should().Be(10);
        span[2].Should().Be(30);
        payload.DataMemory.Span.SequenceEqual(span).Should().BeTrue("DataMemory와 DataSpan은 같은 데이터를 가리켜야 함");
    }

    #endregion

    #region ProtoPayload Tests

    [Fact(DisplayName = "ProtoPayload - Protobuf 메시지를 직렬화하여 저장한다")]
    public void ProtoPayload_SerializesProtobufMessage()
    {
        // Given (전제조건)
        var message = new StringValue { Value = "테스트 메시지" };

        // When (행동)
        using var payload = new ProtoPayload(message);
        var data = payload.DataSpan;

        // Then (결과)
        data.Length.Should().BeGreaterThan(0, "직렬화된 데이터가 있어야 함");
        payload.DataMemory.Length.Should().Be(data.Length, "DataMemory 길이는 DataSpan과 같아야 함");
    }

    [Fact(DisplayName = "MemoryPayload - DataMemory와 DataSpan이 같은 데이터를 반환한다")]
    public void MemoryPayload_DataMemoryAndDataSpan_ReturnSameData()
    {
        // Given (전제조건)
        var source = new byte[] { 1, 2, 3, 4 };
        using var payload = new MemoryPayload(source.AsMemory());

        // When (행동)
        var span = payload.DataSpan;
        var memory = payload.DataMemory;

        // Then (결과)
        memory.Length.Should().Be(span.Length);
        memory.Span.SequenceEqual(span).Should().BeTrue();
    }

    [Fact(DisplayName = "ArrayPoolPayload - DataMemory는 DataSpan과 같은 길이를 반환한다")]
    public void ArrayPoolPayload_DataMemory_HasSameLengthAsSpan()
    {
        // Given (전제조건)
        var rented = ArrayPool<byte>.Shared.Rent(16);
        rented[0] = 10;
        rented[1] = 20;
        rented[2] = 30;
        using var payload = new ArrayPoolPayload(rented, 3);

        // When (행동)
        var span = payload.DataSpan;
        var memory = payload.DataMemory;

        // Then (결과)
        memory.Length.Should().Be(span.Length);
        memory.Span.SequenceEqual(span).Should().BeTrue();
    }

    [Fact(DisplayName = "ProtoPayload - 직렬화된 데이터가 역직렬화 가능하다")]
    public void ProtoPayload_SerializedData_IsDeserializable()
    {
        // Given (전제조건)
        var originalMessage = new StringValue { Value = "Hello, Protobuf!" };
        using var payload = new ProtoPayload(originalMessage);

        // When (행동)
        var deserializedMessage = StringValue.Parser.ParseFrom(payload.DataSpan);

        // Then (결과)
        deserializedMessage.Value.Should().Be("Hello, Protobuf!", "역직렬화한 값이 원본과 같아야 함");
    }

    [Fact(DisplayName = "ProtoPayload - 복잡한 메시지도 직렬화한다")]
    public void ProtoPayload_ComplexMessage_SerializesCorrectly()
    {
        // Given (전제조건)
        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        using var payload = new ProtoPayload(timestamp);

        // When (행동)
        var deserialized = Timestamp.Parser.ParseFrom(payload.DataSpan);

        // Then (결과)
        deserialized.Seconds.Should().Be(timestamp.Seconds, "타임스탬프 초가 같아야 함");
        deserialized.Nanos.Should().Be(timestamp.Nanos, "타임스탬프 나노초가 같아야 함");
    }

    #endregion

    #region IPayload Interface Tests

    [Fact(DisplayName = "IPayload - 모든 구현체가 인터페이스를 만족한다")]
    public void IPayload_AllImplementations_SatisfyInterface()
    {
        // Given (전제조건)
        IPayload[] payloads =
        {
            EmptyPayload.Instance,
            new BytePayload(new byte[] { 1, 2, 3 }),
            new ProtoPayload(new StringValue { Value = "test" })
        };

        // When & Then
        foreach (var payload in payloads)
        {
            // DataSpan 속성 접근 가능
            var span = payload.DataSpan;
            span.Length.Should().BeGreaterThanOrEqualTo(0, "DataSpan 길이가 0 이상이어야 함");

            // Dispose 가능
            payload.Dispose();
        }
    }

    #endregion
}
