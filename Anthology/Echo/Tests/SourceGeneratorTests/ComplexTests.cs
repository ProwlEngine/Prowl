using Prowl.Echo;

namespace Tests.SourceGeneratorTests;

public class ComplexTests
{
    private T RoundTrip<T>(T value)
    {
        var serialized = Serializer.Serialize(value);
        return Serializer.Deserialize<T>(serialized);
    }

    [Fact]
    public void SimpleClass_RoundTrip()
    {
        var original = new SimpleClass { Value = 42, Name = "Test" };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(42, result.Value);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public void ObjectWithNestedGenerated_Null_SerializesCorrectly()
    {
        var original = new ObjectWithNestedGenerated { Id = 100 };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(100, result.Id);
        Assert.Null(result.Nested);
        Assert.Null(result.NestedList);
    }

    [Fact]
    public void ObjectWithNestedGenerated_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithNestedGenerated
        {
            Id = 200,
            Nested = new SimpleClass { Value = 1, Name = "Nested" },
            NestedList = new List<SimpleClass>
            {
                new SimpleClass { Value = 2, Name = "First" },
                new SimpleClass { Value = 3, Name = "Second" }
            }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(200, result.Id);
        Assert.NotNull(result.Nested);
        Assert.Equal(1, result.Nested.Value);
        Assert.Equal("Nested", result.Nested.Name);
        Assert.NotNull(result.NestedList);
        Assert.Equal(2, result.NestedList.Count);
        Assert.Equal(2, result.NestedList[0].Value);
        Assert.Equal(3, result.NestedList[1].Value);
    }

    [Fact]
    public void DeeplyNestedObject_FiveLevelsDeep_SerializesCorrectly()
    {
        var original = new DeeplyNestedObject
        {
            Level = 1,
            Child = new DeeplyNestedObject
            {
                Level = 2,
                Child = new DeeplyNestedObject
                {
                    Level = 3,
                    Child = new DeeplyNestedObject
                    {
                        Level = 4,
                        Child = new DeeplyNestedObject { Level = 5 }
                    }
                }
            }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(1, result.Level);
        Assert.NotNull(result.Child);
        Assert.Equal(2, result.Child!.Level);
        Assert.NotNull(result.Child.Child);
        Assert.Equal(3, result.Child.Child!.Level);
        Assert.NotNull(result.Child.Child.Child);
        Assert.Equal(4, result.Child.Child.Child!.Level);
        Assert.NotNull(result.Child.Child.Child.Child);
        Assert.Equal(5, result.Child.Child.Child.Child!.Level);
        Assert.Null(result.Child.Child.Child.Child.Child);
    }

    [Fact]
    public void GeneratedVector3_SerializesCorrectly()
    {
        var original = new GeneratedVector3 { X = 1.0f, Y = 2.0f, Z = 3.0f };
        var result = RoundTrip(original);

        Assert.Equal(1.0f, result.X);
        Assert.Equal(2.0f, result.Y);
        Assert.Equal(3.0f, result.Z);
    }

    [Fact]
    public void ObjectWithGeneratedStruct_SerializesCorrectly()
    {
        var original = new ObjectWithGeneratedStruct
        {
            Position = new GeneratedVector3 { X = 10, Y = 20, Z = 30 },
            Velocity = new GeneratedVector3 { X = 1, Y = 2, Z = 3 },
            Path = new List<GeneratedVector3>
            {
                new GeneratedVector3 { X = 0, Y = 0, Z = 0 },
                new GeneratedVector3 { X = 5, Y = 5, Z = 5 }
            }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(10, result.Position.X);
        Assert.Equal(20, result.Position.Y);
        Assert.Equal(30, result.Position.Z);
        Assert.Equal(1, result.Velocity.X);
        Assert.Equal(2, result.Path.Count);
        Assert.Equal(5, result.Path[1].X);
    }

    [Fact]
    public void ObjectWithMixedTypes_SerializesCorrectly()
    {
        var original = new ObjectWithMixedTypes();
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.IntValue, result.IntValue);
        Assert.Equal(original.StringValue, result.StringValue);
        Assert.Equal(original.FloatValue, result.FloatValue);
        Assert.Equal(original.BoolValue, result.BoolValue);
        Assert.Equal(original.DateValue.Ticks, result.DateValue.Ticks);
        Assert.Equal(original.GuidValue, result.GuidValue);
        Assert.Equal(original.TimeValue, result.TimeValue);
    }

    [Fact]
    public void ObjectWithEnums_SerializesCorrectly()
    {
        var original = new ObjectWithEnums
        {
            Day = DayOfWeek.Friday,
            Mode = FileMode.Create,
            Days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(DayOfWeek.Friday, result.Day);
        Assert.Equal(FileMode.Create, result.Mode);
        Assert.Equal(3, result.Days.Count);
        Assert.Equal(original.Days, result.Days);
    }

    [Fact]
    public void ObjectWithComplexNesting_SerializesCorrectly()
    {
        var original = new ObjectWithComplexNesting
        {
            Data = new Dictionary<string, List<SimpleClass>>
            {
                {
                    "group1", new List<SimpleClass>
                    {
                        new SimpleClass { Value = 1, Name = "A" },
                        new SimpleClass { Value = 2, Name = "B" }
                    }
                },
                {
                    "group2", new List<SimpleClass>
                    {
                        new SimpleClass { Value = 3, Name = "C" }
                    }
                }
            }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(2, result.Data.Count);
        Assert.Equal(2, result.Data["group1"].Count);
        Assert.Equal(1, result.Data["group1"][0].Value);
        Assert.Equal("A", result.Data["group1"][0].Name);
    }

    [Fact]
    public void ObjectWithCircularReference_SerializesCorrectly()
    {
        var node1 = new ObjectWithCircularReference { Name = "Node1" };
        var node2 = new ObjectWithCircularReference { Name = "Node2" };
        var node3 = new ObjectWithCircularReference { Name = "Node3" };

        node1.Next = node2;
        node2.Previous = node1;
        node2.Next = node3;
        node3.Previous = node2;

        var serialized = Serializer.Serialize(node1);
        var result = Serializer.Deserialize<ObjectWithCircularReference>(serialized);

        Assert.NotNull(result);
        Assert.Equal("Node1", result.Name);
        Assert.NotNull(result.Next);
        Assert.Equal("Node2", result.Next!.Name);
        Assert.NotNull(result.Next.Previous);
        Assert.Equal("Node1", result.Next.Previous!.Name);
        // Verify circular reference is preserved
        Assert.Same(result, result.Next.Previous);
    }

    [Fact]
    public void ObjectWithAllBuiltInTypes_SerializesCorrectly()
    {
        var original = new ObjectWithAllBuiltInTypes
        {
            ByteValue = 255,
            SByteValue = -128,
            ShortValue = -32768,
            UShortValue = 65535,
            IntValue = -2147483648,
            UIntValue = 4294967295,
            LongValue = -9223372036854775808,
            ULongValue = 18446744073709551615,
            FloatValue = 3.14f,
            DoubleValue = 3.14159265359,
            DecimalValue = 123.456m,
            CharValue = 'A',
            BoolValue = true,
            StringValue = "Test String"
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.ByteValue, result.ByteValue);
        Assert.Equal(original.SByteValue, result.SByteValue);
        Assert.Equal(original.ShortValue, result.ShortValue);
        Assert.Equal(original.UShortValue, result.UShortValue);
        Assert.Equal(original.IntValue, result.IntValue);
        Assert.Equal(original.UIntValue, result.UIntValue);
        Assert.Equal(original.LongValue, result.LongValue);
        Assert.Equal(original.ULongValue, result.ULongValue);
        Assert.Equal(original.FloatValue, result.FloatValue);
        Assert.Equal(original.DoubleValue, result.DoubleValue);
        Assert.Equal(original.DecimalValue, result.DecimalValue);
        Assert.Equal(original.CharValue, result.CharValue);
        Assert.Equal(original.BoolValue, result.BoolValue);
        Assert.Equal(original.StringValue, result.StringValue);
    }

    [Fact]
    public void ObjectWithNullableBuiltInTypes_AllNull_SerializesCorrectly()
    {
        var original = new ObjectWithNullableBuiltInTypes();
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Null(result.ByteValue);
        Assert.Null(result.IntValue);
        Assert.Null(result.FloatValue);
        Assert.Null(result.BoolValue);
    }

    [Fact]
    public void ObjectWithNullableBuiltInTypes_AllSet_SerializesCorrectly()
    {
        var original = new ObjectWithNullableBuiltInTypes
        {
            ByteValue = 128,
            SByteValue = -64,
            ShortValue = 1000,
            UShortValue = 2000,
            IntValue = -50000,
            UIntValue = 100000,
            LongValue = -1000000000,
            ULongValue = 2000000000,
            FloatValue = 2.71f,
            DoubleValue = 2.71828,
            DecimalValue = 99.99m,
            CharValue = 'Z',
            BoolValue = false
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.ByteValue, result.ByteValue);
        Assert.Equal(original.IntValue, result.IntValue);
        Assert.Equal(original.FloatValue, result.FloatValue);
        Assert.Equal(original.BoolValue, result.BoolValue);
    }

    [Fact]
    public void ObjectWithUriAndVersion_Null_SerializesCorrectly()
    {
        var original = new ObjectWithUriAndVersion();
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Null(result.WebsiteUri);
        Assert.Null(result.AppVersion);
    }

    [Fact]
    public void ObjectWithUriAndVersion_WithValues_SerializesCorrectly()
    {
        var original = new ObjectWithUriAndVersion
        {
            WebsiteUri = new Uri("https://example.com"),
            AppVersion = new Version(1, 2, 3, 4)
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.NotNull(result.WebsiteUri);
        Assert.Equal("https://example.com/", result.WebsiteUri!.ToString());
        Assert.NotNull(result.AppVersion);
        Assert.Equal(new Version(1, 2, 3, 4), result.AppVersion);
    }

    [Fact]
    public void ObjectWithDateTimeTypes_SerializesCorrectly()
    {
        var now = DateTime.Now;
        var offset = DateTimeOffset.Now;
        var span = TimeSpan.FromHours(5.5);

        var original = new ObjectWithDateTimeTypes
        {
            DateTime = now,
            DateTimeOffset = offset,
            TimeSpan = span
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(now.Ticks, result.DateTime.Ticks);
        Assert.Equal(offset, result.DateTimeOffset);
        Assert.Equal(span, result.TimeSpan);
    }

    [Fact]
    public void MultipleRoundTrips_PreservesData()
    {
        var original = new ObjectWithMixedTypes
        {
            IntValue = 12345,
            StringValue = "Round trip test",
            FloatValue = 9.87f,
            BoolValue = false
        };

        // Serialize and deserialize multiple times
        var tag1 = Serializer.Serialize(original);
        var obj1 = Serializer.Deserialize<ObjectWithMixedTypes>(tag1);
        var tag2 = Serializer.Serialize(obj1);
        var obj2 = Serializer.Deserialize<ObjectWithMixedTypes>(tag2);
        var tag3 = Serializer.Serialize(obj2);
        var obj3 = Serializer.Deserialize<ObjectWithMixedTypes>(tag3);

        Assert.NotNull(obj3);
        Assert.Equal(original.IntValue, obj3.IntValue);
        Assert.Equal(original.StringValue, obj3.StringValue);
        Assert.Equal(original.FloatValue, obj3.FloatValue);
        Assert.Equal(original.BoolValue, obj3.BoolValue);
    }

    [Fact]
    public void LargeCollectionOfGeneratedObjects_SerializesCorrectly()
    {
        var original = new List<SimpleClass>();
        for (int i = 0; i < 1000; i++)
        {
            original.Add(new SimpleClass { Value = i, Name = $"Object{i}" });
        }

        var serialized = Serializer.Serialize(original);
        var result = Serializer.Deserialize<List<SimpleClass>>(serialized);

        Assert.NotNull(result);
        Assert.Equal(1000, result.Count);
        Assert.Equal(500, result[500].Value);
        Assert.Equal("Object500", result[500].Name);
    }
}
