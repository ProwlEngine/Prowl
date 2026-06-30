using Prowl.Echo;

namespace Tests.SourceGeneratorTests;

public class NullableAndTupleTests
{
    private T RoundTrip<T>(T value)
    {
        var serialized = Serializer.Serialize(value);
        return Serializer.Deserialize<T>(serialized);
    }

    [Fact]
    public void ObjectWithNullables_AllNull_SerializesCorrectly()
    {
        var original = new ObjectWithNullables();
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Null(result.NullableInt);
        Assert.Null(result.NullableFloat);
        Assert.Null(result.NullableBool);
        Assert.Null(result.NullableDateTime);
        Assert.Null(result.NullableGuid);
    }

    [Fact]
    public void ObjectWithNullablesSet_AllSet_SerializesCorrectly()
    {
        var original = new ObjectWithNullablesSet
        {
            NullableInt = 99,
            NullableFloat = 2.718f,
            NullableBool = false,
            NullableDateTime = new DateTime(2025, 12, 31),
            NullableGuid = Guid.NewGuid()
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.NullableInt, result.NullableInt);
        Assert.Equal(original.NullableFloat, result.NullableFloat);
        Assert.Equal(original.NullableBool, result.NullableBool);
        Assert.Equal(original.NullableDateTime, result.NullableDateTime);
        Assert.Equal(original.NullableGuid, result.NullableGuid);
    }

    [Fact]
    public void ObjectWithNullables_MixedNullAndSet_SerializesCorrectly()
    {
        var original = new ObjectWithNullables
        {
            NullableInt = 42,
            NullableFloat = null,
            NullableBool = true,
            NullableDateTime = null,
            NullableGuid = Guid.Empty
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(42, result.NullableInt);
        Assert.Null(result.NullableFloat);
        Assert.True(result.NullableBool);
        Assert.Null(result.NullableDateTime);
        Assert.Equal(Guid.Empty, result.NullableGuid);
    }

    [Fact]
    public void ObjectWithTuples_Default_SerializesCorrectly()
    {
        var original = new ObjectWithTuples();
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.Tuple2, result.Tuple2);
        Assert.Equal(original.Tuple3, result.Tuple3);
        Assert.Equal(original.Tuple4, result.Tuple4);
    }

    [Fact]
    public void ObjectWithTuples_CustomValues_SerializesCorrectly()
    {
        var original = new ObjectWithTuples
        {
            Tuple2 = (99, "ninety-nine"),
            Tuple3 = (100, "hundred", false),
            Tuple4 = (200, "two hundred", true, 2.5f)
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.Tuple2, result.Tuple2);
        Assert.Equal(original.Tuple3, result.Tuple3);
        Assert.Equal(original.Tuple4, result.Tuple4);
    }

    [Fact]
    public void ObjectWithNamedTuples_SerializesCorrectly()
    {
        var original = new ObjectWithNamedTuples
        {
            Person = (42, "Alice"),
            Position = (10.5f, 20.3f, 30.7f)
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.Person.Id, result.Person.Id);
        Assert.Equal(original.Person.Name, result.Person.Name);
        Assert.Equal(original.Position.X, result.Position.X);
        Assert.Equal(original.Position.Y, result.Position.Y);
        Assert.Equal(original.Position.Z, result.Position.Z);
    }

    [Fact]
    public void ObjectWithNestedTuples_SerializesCorrectly()
    {
        var original = new ObjectWithNestedTuples
        {
            NestedTuple = ((10, 20), ("hello", "world")),
            TupleList = new List<(int Id, string Name)>
            {
                (1, "First"),
                (2, "Second"),
                (3, "Third")
            }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.NestedTuple, result.NestedTuple);
        Assert.Equal(original.TupleList.Count, result.TupleList.Count);
        for (int i = 0; i < original.TupleList.Count; i++)
        {
            Assert.Equal(original.TupleList[i], result.TupleList[i]);
        }
    }

    [Fact]
    public void ObjectWithNullableTuples_AllNull_SerializesCorrectly()
    {
        var original = new ObjectWithNullableTuples
        {
            NullableTuple = (null, null),
            CompletelyNullable = null
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.NullableTuple, result.NullableTuple);
        Assert.Null(result.CompletelyNullable);
    }

    [Fact]
    public void ObjectWithNullableTuples_WithValues_SerializesCorrectly()
    {
        var original = new ObjectWithNullableTuples
        {
            NullableTuple = (42, "test"),
            CompletelyNullable = (100, "value")
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.NullableTuple, result.NullableTuple);
        Assert.Equal(original.CompletelyNullable, result.CompletelyNullable);
    }

    [Fact]
    public void NullableStructWithFields_AllNull_SerializesCorrectly()
    {
        var original = new NullableStructWithFields
        {
            NullableInt = null,
            NullableString = null,
            NullableFloat = null
        };
        var result = RoundTrip(original);

        Assert.Null(result.NullableInt);
        Assert.Null(result.NullableString);
        Assert.Null(result.NullableFloat);
    }

    [Fact]
    public void NullableStructWithFields_WithValues_SerializesCorrectly()
    {
        var original = new NullableStructWithFields
        {
            NullableInt = 42,
            NullableString = "test",
            NullableFloat = 3.14f
        };
        var result = RoundTrip(original);

        Assert.Equal(original.NullableInt, result.NullableInt);
        Assert.Equal(original.NullableString, result.NullableString);
        Assert.Equal(original.NullableFloat, result.NullableFloat);
    }

    [Fact]
    public void NullableOfGeneratedStruct_Null_SerializesCorrectly()
    {
        NullableStructWithFields? nullStruct = null;
        var serialized = Serializer.Serialize(nullStruct);
        var result = Serializer.Deserialize<NullableStructWithFields?>(serialized);

        Assert.Null(result);
    }

    [Fact]
    public void NullableOfGeneratedStruct_WithValue_SerializesCorrectly()
    {
        NullableStructWithFields? original = new NullableStructWithFields
        {
            NullableInt = 100,
            NullableString = "nullable struct",
            NullableFloat = 1.5f
        };
        var serialized = Serializer.Serialize(original);
        var result = Serializer.Deserialize<NullableStructWithFields?>(serialized);

        Assert.NotNull(result);
        Assert.Equal(original.Value.NullableInt, result.Value.NullableInt);
        Assert.Equal(original.Value.NullableString, result.Value.NullableString);
        Assert.Equal(original.Value.NullableFloat, result.Value.NullableFloat);
    }
}
