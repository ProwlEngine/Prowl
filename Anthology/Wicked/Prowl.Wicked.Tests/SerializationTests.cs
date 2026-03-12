using System.Numerics;

namespace Prowl.Wicked.Tests;

public class SerializationTests
{
    private static NetworkReader WriteThenRead(Action<NetworkWriter> write)
    {
        var writer = new NetworkWriter();
        write(writer);
        return new NetworkReader(writer.ToArraySegment());
    }

    // ── Primitive round-trips ──

    [Fact]
    public void Byte_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteByte(0); w.WriteByte(127); w.WriteByte(255); });
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(127, reader.ReadByte());
        Assert.Equal(255, reader.ReadByte());
    }

    [Fact]
    public void SByte_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteSByte(sbyte.MinValue); w.WriteSByte(0); w.WriteSByte(sbyte.MaxValue); });
        Assert.Equal(sbyte.MinValue, reader.ReadSByte());
        Assert.Equal(0, reader.ReadSByte());
        Assert.Equal(sbyte.MaxValue, reader.ReadSByte());
    }

    [Fact]
    public void Short_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteShort(short.MinValue); w.WriteShort(0); w.WriteShort(short.MaxValue); });
        Assert.Equal(short.MinValue, reader.ReadShort());
        Assert.Equal(0, reader.ReadShort());
        Assert.Equal(short.MaxValue, reader.ReadShort());
    }

    [Fact]
    public void UShort_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteUShort(0); w.WriteUShort(ushort.MaxValue); });
        Assert.Equal((ushort)0, reader.ReadUShort());
        Assert.Equal(ushort.MaxValue, reader.ReadUShort());
    }

    [Fact]
    public void Int_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteInt(int.MinValue); w.WriteInt(0); w.WriteInt(int.MaxValue); });
        Assert.Equal(int.MinValue, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(int.MaxValue, reader.ReadInt());
    }

    [Fact]
    public void UInt_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteUInt(0); w.WriteUInt(uint.MaxValue); });
        Assert.Equal(0u, reader.ReadUInt());
        Assert.Equal(uint.MaxValue, reader.ReadUInt());
    }

    [Fact]
    public void Long_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteLong(long.MinValue); w.WriteLong(0); w.WriteLong(long.MaxValue); });
        Assert.Equal(long.MinValue, reader.ReadLong());
        Assert.Equal(0L, reader.ReadLong());
        Assert.Equal(long.MaxValue, reader.ReadLong());
    }

    [Fact]
    public void ULong_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteULong(0); w.WriteULong(ulong.MaxValue); });
        Assert.Equal(0UL, reader.ReadULong());
        Assert.Equal(ulong.MaxValue, reader.ReadULong());
    }

    [Fact]
    public void Float_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteFloat(0f); w.WriteFloat(-1.5f); w.WriteFloat(float.MaxValue); w.WriteFloat(float.Epsilon); });
        Assert.Equal(0f, reader.ReadFloat());
        Assert.Equal(-1.5f, reader.ReadFloat());
        Assert.Equal(float.MaxValue, reader.ReadFloat());
        Assert.Equal(float.Epsilon, reader.ReadFloat());
    }

    [Fact]
    public void Double_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteDouble(0.0); w.WriteDouble(-3.14); w.WriteDouble(double.MaxValue); });
        Assert.Equal(0.0, reader.ReadDouble());
        Assert.Equal(-3.14, reader.ReadDouble());
        Assert.Equal(double.MaxValue, reader.ReadDouble());
    }

    [Fact]
    public void Bool_RoundTrips()
    {
        var reader = WriteThenRead(w => { w.WriteBool(true); w.WriteBool(false); });
        Assert.True(reader.ReadBool());
        Assert.False(reader.ReadBool());
    }

    // ── String ──

    [Fact]
    public void String_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteString("hello"));
        Assert.Equal("hello", reader.ReadString());
    }

    [Fact]
    public void String_Empty_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteString(""));
        Assert.Equal("", reader.ReadString());
    }

    [Fact]
    public void String_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteString(null));
        Assert.Null(reader.ReadString());
    }

    [Fact]
    public void String_Unicode_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteString("\u00e9\u00e0\u00fc \ud83d\ude80"));
        Assert.Equal("\u00e9\u00e0\u00fc \ud83d\ude80", reader.ReadString());
    }

    // ── Vector2 ──

    [Fact]
    public void Vector2_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteVector2(new Vector2(3.5f, -7.25f)));
        Assert.Equal(new Vector2(3.5f, -7.25f), reader.ReadVector2());
    }

    // ── INetworkSerializable ──

    [Fact]
    public void Serializable_RoundTrips()
    {
        var reader = WriteThenRead(w => w.Write(new TestSerializable { X = 42, Name = "test" }));
        var result = reader.Read<TestSerializable>();
        Assert.Equal(42, result.X);
        Assert.Equal("test", result.Name);
    }

    [Fact]
    public void SerializableArray_RoundTrips()
    {
        var items = new[]
        {
            new TestSerializable { X = 1, Name = "a" },
            new TestSerializable { X = 2, Name = "b" }
        };
        var reader = WriteThenRead(w => w.WriteArray(items));
        var result = reader.ReadArray<TestSerializable>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal(1, result[0].X);
        Assert.Equal("a", result[0].Name);
        Assert.Equal(2, result[1].X);
        Assert.Equal("b", result[1].Name);
    }

    [Fact]
    public void SerializableArray_Empty_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteArray(Array.Empty<TestSerializable>()));
        var result = reader.ReadArray<TestSerializable>();
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void SerializableArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteArray<TestSerializable>(null));
        Assert.Null(reader.ReadArray<TestSerializable>());
    }

    // ── Primitive arrays ──

    [Fact]
    public void IntArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteIntArray(new[] { 1, -2, int.MaxValue }));
        Assert.Equal(new[] { 1, -2, int.MaxValue }, reader.ReadIntArray());
    }

    [Fact]
    public void IntArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteIntArray(null));
        Assert.Null(reader.ReadIntArray());
    }

    [Fact]
    public void IntArray_Empty_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteIntArray(Array.Empty<int>()));
        var result = reader.ReadIntArray();
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void UIntArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteUIntArray(new uint[] { 0, uint.MaxValue }));
        Assert.Equal(new uint[] { 0, uint.MaxValue }, reader.ReadUIntArray());
    }

    [Fact]
    public void UIntArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteUIntArray(null));
        Assert.Null(reader.ReadUIntArray());
    }

    [Fact]
    public void FloatArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteFloatArray(new[] { 1.5f, -0.5f }));
        Assert.Equal(new[] { 1.5f, -0.5f }, reader.ReadFloatArray());
    }

    [Fact]
    public void FloatArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteFloatArray(null));
        Assert.Null(reader.ReadFloatArray());
    }

    [Fact]
    public void DoubleArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteDoubleArray(new[] { 1.0, -2.5 }));
        Assert.Equal(new[] { 1.0, -2.5 }, reader.ReadDoubleArray());
    }

    [Fact]
    public void DoubleArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteDoubleArray(null));
        Assert.Null(reader.ReadDoubleArray());
    }

    [Fact]
    public void StringArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteStringArray(new[] { "a", null, "c" }));
        var result = reader.ReadStringArray();
        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal("a", result[0]);
        Assert.Null(result[1]);
        Assert.Equal("c", result[2]);
    }

    [Fact]
    public void StringArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteStringArray(null));
        Assert.Null(reader.ReadStringArray());
    }

    [Fact]
    public void ByteArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteByteArray(new byte[] { 0, 1, 255 }));
        Assert.Equal(new byte[] { 0, 1, 255 }, reader.ReadByteArray());
    }

    [Fact]
    public void ByteArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteByteArray(null));
        Assert.Null(reader.ReadByteArray());
    }

    [Fact]
    public void LongArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteLongArray(new[] { long.MinValue, 0L, long.MaxValue }));
        Assert.Equal(new[] { long.MinValue, 0L, long.MaxValue }, reader.ReadLongArray());
    }

    [Fact]
    public void LongArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteLongArray(null));
        Assert.Null(reader.ReadLongArray());
    }

    [Fact]
    public void ULongArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteULongArray(new ulong[] { 0, ulong.MaxValue }));
        Assert.Equal(new ulong[] { 0, ulong.MaxValue }, reader.ReadULongArray());
    }

    [Fact]
    public void ULongArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteULongArray(null));
        Assert.Null(reader.ReadULongArray());
    }

    [Fact]
    public void ShortArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteShortArray(new short[] { short.MinValue, 0, short.MaxValue }));
        Assert.Equal(new short[] { short.MinValue, 0, short.MaxValue }, reader.ReadShortArray());
    }

    [Fact]
    public void ShortArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteShortArray(null));
        Assert.Null(reader.ReadShortArray());
    }

    [Fact]
    public void UShortArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteUShortArray(new ushort[] { 0, ushort.MaxValue }));
        Assert.Equal(new ushort[] { 0, ushort.MaxValue }, reader.ReadUShortArray());
    }

    [Fact]
    public void UShortArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteUShortArray(null));
        Assert.Null(reader.ReadUShortArray());
    }

    [Fact]
    public void BoolArray_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteBoolArray(new[] { true, false, true }));
        Assert.Equal(new[] { true, false, true }, reader.ReadBoolArray());
    }

    [Fact]
    public void BoolArray_Null_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteBoolArray(null));
        Assert.Null(reader.ReadBoolArray());
    }

    // ── Enums ──

    [Fact]
    public void Enum_Int_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteEnum(IntEnum.Second));
        Assert.Equal(IntEnum.Second, reader.ReadEnum<IntEnum>());
    }

    [Fact]
    public void Enum_Byte_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteEnum(ByteEnum.High));
        Assert.Equal(ByteEnum.High, reader.ReadEnum<ByteEnum>());
    }

    [Fact]
    public void Enum_Short_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteEnum(ShortEnum.Negative));
        Assert.Equal(ShortEnum.Negative, reader.ReadEnum<ShortEnum>());
    }

    [Fact]
    public void Enum_Long_RoundTrips()
    {
        var reader = WriteThenRead(w => w.WriteEnum(LongEnum.Big));
        Assert.Equal(LongEnum.Big, reader.ReadEnum<LongEnum>());
    }

    // ── Raw bytes ──

    [Fact]
    public void RawBytes_RoundTrips()
    {
        var data = new byte[] { 10, 20, 30 };
        var reader = WriteThenRead(w => w.WriteBytes(new ArraySegment<byte>(data)));
        var result = reader.ReadBytes(3);
        Assert.Equal(data, result.ToArray());
    }

    [Fact]
    public void RawBytes_Empty_IsNoOp()
    {
        var reader = WriteThenRead(w => w.WriteBytes(new ArraySegment<byte>(Array.Empty<byte>())));
        Assert.Equal(0, reader.Remaining);
    }

    // ── Writer reset/reuse ──

    [Fact]
    public void Writer_Reset_ClearsData()
    {
        var writer = new NetworkWriter();
        writer.WriteInt(999);
        writer.Reset();
        writer.WriteByte(42);

        var reader = new NetworkReader(writer.ToArraySegment());
        Assert.Equal(42, reader.ReadByte());
        Assert.Equal(0, reader.Remaining);
    }

    // ── Read past end ──

    [Fact]
    public void ReadPastEnd_Throws()
    {
        var reader = WriteThenRead(w => w.WriteByte(1));
        reader.ReadByte(); // consume the one byte
        Assert.Throws<InvalidOperationException>(() => reader.ReadByte());
    }

    [Fact]
    public void ReadIntPastEnd_Throws()
    {
        var reader = WriteThenRead(w => w.WriteByte(1));
        Assert.Throws<InvalidOperationException>(() => reader.ReadInt());
    }

    // ── Remaining ──

    [Fact]
    public void Remaining_TracksUnreadBytes()
    {
        var reader = WriteThenRead(w => { w.WriteInt(1); w.WriteByte(2); });
        Assert.Equal(5, reader.Remaining); // 4 + 1
        reader.ReadInt();
        Assert.Equal(1, reader.Remaining);
        reader.ReadByte();
        Assert.Equal(0, reader.Remaining);
    }

    // ── Mixed types in sequence ──

    [Fact]
    public void MixedTypes_RoundTripInOrder()
    {
        var reader = WriteThenRead(w =>
        {
            w.WriteByte(0xFF);
            w.WriteInt(-42);
            w.WriteString("hello");
            w.WriteBool(true);
            w.WriteFloat(1.5f);
            w.WriteVector2(new Vector2(10, 20));
            w.WriteDouble(3.14);
            w.WriteLong(long.MaxValue);
        });

        Assert.Equal(0xFF, reader.ReadByte());
        Assert.Equal(-42, reader.ReadInt());
        Assert.Equal("hello", reader.ReadString());
        Assert.True(reader.ReadBool());
        Assert.Equal(1.5f, reader.ReadFloat());
        Assert.Equal(new Vector2(10, 20), reader.ReadVector2());
        Assert.Equal(3.14, reader.ReadDouble());
        Assert.Equal(long.MaxValue, reader.ReadLong());
        Assert.Equal(0, reader.Remaining);
    }

    // ── Buffer growth ──

    [Fact]
    public void Writer_GrowsBeyondInitialBuffer()
    {
        var writer = new NetworkWriter();
        // Write more than the initial 256-byte buffer
        for (int i = 0; i < 100; i++)
            writer.WriteInt(i);

        var reader = new NetworkReader(writer.ToArraySegment());
        for (int i = 0; i < 100; i++)
            Assert.Equal(i, reader.ReadInt());
        Assert.Equal(0, reader.Remaining);
    }
}

// ── Test helper types ──

public class TestSerializable : INetworkSerializable
{
    public int X { get; set; }
    public string? Name { get; set; }

    public void Serialize(NetworkWriter writer)
    {
        writer.WriteInt(X);
        writer.WriteString(Name);
    }

    public void Deserialize(NetworkReader reader)
    {
        X = reader.ReadInt();
        Name = reader.ReadString();
    }
}

public enum IntEnum { First = 0, Second = 1, Third = 2 }
public enum ByteEnum : byte { Low = 0, High = 255 }
public enum ShortEnum : short { Negative = -100, Zero = 0, Positive = 100 }
public enum LongEnum : long { Big = long.MaxValue }
