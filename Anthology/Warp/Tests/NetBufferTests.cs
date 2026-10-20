// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Warp;

using Xunit;

namespace Prowl.Warp.Tests;

public class NetBufferTests
{
    [Fact]
    public void RoundTripsEveryType()
    {
        Guid guid = Guid.NewGuid();
        using NetWriter writer = new(initialCapacity: 1); // force growth

        writer.WriteBool(true);
        writer.WriteByte(200);
        writer.WriteSByte(-5);
        writer.WriteInt16(-1234);
        writer.WriteUInt16(65000);
        writer.WriteInt32(int.MinValue);
        writer.WriteUInt32(uint.MaxValue);
        writer.WriteInt64(long.MinValue);
        writer.WriteUInt64(ulong.MaxValue);
        writer.WriteSingle(3.25f);
        writer.WriteDouble(-1e300);
        writer.WriteVarUInt32(300);
        writer.WriteVarUInt64(ulong.MaxValue);
        writer.WriteVarInt32(-1);
        writer.WriteVarInt64(long.MinValue);
        writer.WriteGuid(guid);
        writer.WriteString("héllo wörld");
        writer.WriteString(null);
        writer.WriteBytesWithLength(new byte[] { 1, 2, 3 });
        writer.WriteBytes(new byte[] { 9, 8 });

        NetReader reader = new(writer.WrittenSpan);
        Assert.True(reader.ReadBool());
        Assert.Equal(200, reader.ReadByte());
        Assert.Equal(-5, reader.ReadSByte());
        Assert.Equal(-1234, reader.ReadInt16());
        Assert.Equal(65000, reader.ReadUInt16());
        Assert.Equal(int.MinValue, reader.ReadInt32());
        Assert.Equal(uint.MaxValue, reader.ReadUInt32());
        Assert.Equal(long.MinValue, reader.ReadInt64());
        Assert.Equal(ulong.MaxValue, reader.ReadUInt64());
        Assert.Equal(3.25f, reader.ReadSingle());
        Assert.Equal(-1e300, reader.ReadDouble());
        Assert.Equal(300u, reader.ReadVarUInt32());
        Assert.Equal(ulong.MaxValue, reader.ReadVarUInt64());
        Assert.Equal(-1, reader.ReadVarInt32());
        Assert.Equal(long.MinValue, reader.ReadVarInt64());
        Assert.Equal(guid, reader.ReadGuid());
        Assert.Equal("héllo wörld", reader.ReadString());
        Assert.Equal(string.Empty, reader.ReadString());
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.ReadBytesWithLength().ToArray());
        Assert.Equal(new byte[] { 9, 8 }, reader.ReadBytes(2).ToArray());
        Assert.True(reader.IsAtEnd);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 2)]
    [InlineData(16383, 2)]
    [InlineData(16384, 3)]
    [InlineData(uint.MaxValue, 5)]
    public void VarUIntUsesMinimalBytes(uint value, int expectedBytes)
    {
        using NetWriter writer = new();
        writer.WriteVarUInt32(value);
        Assert.Equal(expectedBytes, writer.Length);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(63, 1)]
    [InlineData(-64, 1)]
    [InlineData(64, 2)]
    public void ZigzagKeepsSmallNegativesSmall(int value, int expectedBytes)
    {
        using NetWriter writer = new();
        writer.WriteVarInt32(value);
        Assert.Equal(expectedBytes, writer.Length);
    }

    [Fact]
    public void ReadingPastTheEndThrows()
    {
        byte[] data = { 1, 2, 3 };
        Assert.Throws<EndOfStreamException>(() => new NetReader(data).ReadInt32());
    }

    [Fact]
    public void LengthPrefixLargerThanDataThrows()
    {
        using NetWriter writer = new();
        writer.WriteVarUInt32(1000);
        writer.WriteByte(1);
        byte[] data = writer.ToArray();

        Assert.Throws<EndOfStreamException>(() => new NetReader(data).ReadString());
        Assert.Throws<EndOfStreamException>(() => new NetReader(data).ReadBytesWithLength());
    }

    [Fact]
    public void OverlongVarIntThrows()
    {
        byte[] data = { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };
        Assert.Throws<InvalidDataException>(() => new NetReader(data).ReadVarUInt64());
    }

    [Fact]
    public void VarUInt32RejectsValuesAbove32Bits()
    {
        using NetWriter writer = new();
        writer.WriteVarUInt64((ulong)uint.MaxValue + 1);
        byte[] data = writer.ToArray();
        Assert.Throws<InvalidDataException>(() => new NetReader(data).ReadVarUInt32());
    }

    [Fact]
    public void ResetReusesTheWriter()
    {
        using NetWriter writer = new();
        writer.WriteInt64(1);
        writer.Reset();
        writer.WriteByte(7);
        Assert.Equal(new byte[] { 7 }, writer.ToArray());
    }
}
