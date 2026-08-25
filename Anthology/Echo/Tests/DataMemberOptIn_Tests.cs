// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.Serialization;

namespace Prowl.Echo.Test;

#region Test Classes

/// <summary>
/// Stands in for a type from a library that wants to be serializable without referencing Echo, so it
/// reaches for the BCL attribute rather than <see cref="SerializeFieldAttribute"/>.
/// </summary>
public class ObjectWithDataMemberFields
{
    [DataMember] private int _optedIn;
    private int _notOptedIn;
    public int Public;

    public void Set(int optedIn, int notOptedIn, int publicValue)
    {
        _optedIn = optedIn;
        _notOptedIn = notOptedIn;
        Public = publicValue;
    }

    public int OptedIn => _optedIn;
    public int NotOptedIn => _notOptedIn;
}

public class ObjectWithExcludedDataMember
{
    [DataMember][SerializeIgnore] private int _ignored;
    [DataMember][NonSerialized] private int _nonSerialized;
    [DataMember] private int _kept;

    public void Set(int ignored, int nonSerialized, int kept)
    {
        _ignored = ignored;
        _nonSerialized = nonSerialized;
        _kept = kept;
    }

    public int Ignored => _ignored;
    public int NonSerialized => _nonSerialized;
    public int Kept => _kept;
}

public class ObjectWithDataMemberArray
{
    [DataMember] private float[]? _values;
    [DataMember] private int _count;

    public void Set(float[]? values, int count)
    {
        _values = values;
        _count = count;
    }

    public float[]? Values => _values;
    public int Count => _count;
}

public class ObjectWithRenamedDataMember
{
    [DataMember(Name = "SomethingElse")] private int _value;

    public void Set(int value) => _value = value;
    public int Value => _value;
}

#endregion

public class DataMemberOptIn_Tests
{
    [Fact]
    public void PrivateFieldWithDataMember_IsSerialized()
    {
        var original = new ObjectWithDataMemberFields();
        original.Set(42, 99, 7);

        EchoObject echo = Serializer.Serialize(original);
        var back = Serializer.Deserialize<ObjectWithDataMemberFields>(echo)!;

        Assert.Equal(42, back.OptedIn);
        Assert.Equal(7, back.Public);
    }

    [Fact]
    public void PrivateFieldWithoutDataMember_IsStillSkipped()
    {
        var original = new ObjectWithDataMemberFields();
        original.Set(42, 99, 7);

        var back = Serializer.Deserialize<ObjectWithDataMemberFields>(Serializer.Serialize(original))!;

        Assert.Equal(0, back.NotOptedIn);
    }

    [Fact]
    public void DataMemberField_UsesTheFieldNameAsTheKey()
    {
        var original = new ObjectWithDataMemberFields();
        original.Set(42, 0, 0);

        EchoObject echo = Serializer.Serialize(original);

        Assert.True(echo.TryGet("_optedIn", out EchoObject? value));
        Assert.Equal(42, value!.IntValue);
        Assert.False(echo.TryGet("_notOptedIn", out _));
    }

    [Fact]
    public void DataMemberName_IsIgnoredInFavourOfTheFieldName()
    {
        var original = new ObjectWithRenamedDataMember();
        original.Set(5);

        EchoObject echo = Serializer.Serialize(original);

        Assert.True(echo.TryGet("_value", out _));
        Assert.False(echo.TryGet("SomethingElse", out _));
        Assert.Equal(5, Serializer.Deserialize<ObjectWithRenamedDataMember>(echo)!.Value);
    }

    [Fact]
    public void ExclusionAttributes_StillWinOverDataMember()
    {
        var original = new ObjectWithExcludedDataMember();
        original.Set(1, 2, 3);

        EchoObject echo = Serializer.Serialize(original);
        var back = Serializer.Deserialize<ObjectWithExcludedDataMember>(echo)!;

        Assert.False(echo.TryGet("_ignored", out _));
        Assert.False(echo.TryGet("_nonSerialized", out _));
        Assert.Equal(0, back.Ignored);
        Assert.Equal(0, back.NonSerialized);
        Assert.Equal(3, back.Kept);
    }

    [Fact]
    public void DataMemberArrays_RoundTrip()
    {
        var original = new ObjectWithDataMemberArray();
        original.Set(new[] { 1f, 2f, 3f }, 3);

        var back = Serializer.Deserialize<ObjectWithDataMemberArray>(Serializer.Serialize(original))!;

        Assert.Equal(new[] { 1f, 2f, 3f }, back.Values);
        Assert.Equal(3, back.Count);
    }

    [Fact]
    public void NullDataMemberArray_RoundTripsAsNull()
    {
        var original = new ObjectWithDataMemberArray();
        original.Set(null, 0);

        var back = Serializer.Deserialize<ObjectWithDataMemberArray>(Serializer.Serialize(original))!;

        Assert.Null(back.Values);
    }

    [Fact]
    public void DataMemberFields_SurviveTheBinaryFormat()
    {
        var original = new ObjectWithDataMemberFields();
        original.Set(42, 0, 7);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            Serializer.Serialize(original).WriteToBinary(writer);

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var back = Serializer.Deserialize<ObjectWithDataMemberFields>(EchoObject.ReadFromBinary(reader))!;

        Assert.Equal(42, back.OptedIn);
        Assert.Equal(7, back.Public);
    }
}
