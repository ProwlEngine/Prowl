// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Numerics;
using System.Runtime.Serialization;

namespace Prowl.Echo.Test;

#region Test Types

/// <summary>A vector that keeps its components in private SIMD storage and exposes them as properties.</summary>
public struct WrappedVector
{
    private Vector3 _v;

    public WrappedVector(float x, float y, float z) => _v = new Vector3(x, y, z);

    [DataMember] public float X { readonly get => _v.X; set => _v.X = value; }
    [DataMember] public float Y { readonly get => _v.Y; set => _v.Y = value; }
    [DataMember] public float Z { readonly get => _v.Z; set => _v.Z = value; }

    public float LengthSquared => _v.LengthSquared();
}

/// <summary>The same vector with plain public fields, which is how it used to be persisted.</summary>
public struct FieldVector
{
    public float X, Y, Z;
}

public class HolderOfWrapped
{
    public WrappedVector Position;
}

public class HolderOfFieldVector
{
    public FieldVector Position;
}

public class ObjectWithUnmarkedProperties
{
    [DataMember] public int ReadOnly => 5;
    public int NotMarked { get; set; } = 3;
    [DataMember] public int Marked { get; set; }
}

#endregion

public class DataMemberProperty_Tests
{
    [Fact]
    public void DataMemberProperties_RoundTrip()
    {
        var back = RoundtripTestHelpers.Roundtrip(new WrappedVector(1.5f, -2f, 3.25f));

        Assert.Equal(1.5f, back.X);
        Assert.Equal(-2f, back.Y);
        Assert.Equal(3.25f, back.Z);
    }

    [Fact]
    public void DataMemberProperties_UseThePropertyNamesAndSkipPrivateStorage()
    {
        EchoObject echo = Serializer.Serialize(new WrappedVector(1f, 2f, 3f));

        Assert.True(echo.TryGet("X", out _));
        Assert.True(echo.TryGet("Y", out _));
        Assert.True(echo.TryGet("Z", out _));
        Assert.False(echo.TryGet("_v", out _));
        Assert.False(echo.TryGet("LengthSquared", out _));
    }

    [Fact]
    public void DataStoredAsFields_LoadsIntoDataMemberProperties()
    {
        var old = new HolderOfFieldVector { Position = new FieldVector { X = 4f, Y = 5f, Z = 6f } };
        string text = Serializer.Serialize(old).WriteToString();

        var loaded = Serializer.Deserialize<HolderOfWrapped>(EchoObject.ReadFromString(text))!;

        Assert.Equal(4f, loaded.Position.X);
        Assert.Equal(5f, loaded.Position.Y);
        Assert.Equal(6f, loaded.Position.Z);
    }

    [Fact]
    public void DataMemberProperties_WriteTheSameFormatAsFields()
    {
        EchoObject fromFields = Serializer.Serialize(new HolderOfFieldVector { Position = new FieldVector { X = 1f, Y = 2f, Z = 3f } });
        EchoObject fromProperties = Serializer.Serialize(new HolderOfWrapped { Position = new WrappedVector(1f, 2f, 3f) });

        Assert.True(fromFields.TryGet("Position", out EchoObject? fieldPosition));
        Assert.True(fromProperties.TryGet("Position", out EchoObject? propertyPosition));
        Assert.Equal(fieldPosition!.WriteToString(), propertyPosition!.WriteToString());
    }

    [Fact]
    public void OnlyWritableMarkedPropertiesAreSerialized()
    {
        EchoObject echo = Serializer.Serialize(new ObjectWithUnmarkedProperties { NotMarked = 9, Marked = 7 });

        Assert.True(echo.TryGet("Marked", out EchoObject? marked));
        Assert.Equal(7, marked!.IntValue);
        Assert.False(echo.TryGet("NotMarked", out _));
        Assert.False(echo.TryGet("ReadOnly", out _));
    }
}
