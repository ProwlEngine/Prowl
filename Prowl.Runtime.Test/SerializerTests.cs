// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Runtime.Test;

public class SerializerTests
{
    #region Classes

    // Test classes
    public enum TestEnum2
    {
        None = 0,
        One = 1,
        Two = 2,
        Large = 1000000,
        Negative = -1
    }

    public class SimpleObject
    {
        public string StringField = "Hello";
        public int IntField = 42;
        public float FloatField = 3.14f;
        public bool BoolField = true;
    }

    public class ComplexObject
    {
        public SimpleObject Object = new();
        public List<int> Numbers = new() { 1, 2, 3 };
        public Dictionary<string, float> Values = new() { { "one", 1.0f }, { "two", 2.0f } };
    }

    public class CircularObject
    {
        public string Name = "Parent";
        public CircularObject? Child;
    }

    public class CustomSerializableObject : ISerializable
    {
        public int Value = 42;
        public string Text = "Custom";

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            var compound = SerializedProperty.NewCompound();
            compound.Add("customValue", new SerializedProperty(PropertyType.Int, Value));
            compound.Add("customText", new SerializedProperty(PropertyType.String, Text));
            return compound;
        }

        public void Deserialize(SerializedProperty tag, Serializer.SerializationContext ctx)
        {
            Value = tag.Get("customValue").IntValue;
            Text = tag.Get("customText").StringValue;
        }
    }

    private class ObjectWithAttributes
    {
        [FormerlySerializedAs("oldName")]
        public string NewName = "Test";

        [IgnoreOnNull]
        public string? OptionalField = null;
    }

    private class ObjectWithReadOnlyFields
    {
        public readonly string ReadOnlyField = "Readonly";
        public const string ConstField = "Const";
        public string NormalField = "Normal";
    }

    private abstract class AbstractClass
    {
        public string Name = "Abstract";
    }

    private class ConcreteClass : AbstractClass
    {
        public int Value = 42;
    }

    private struct TestStruct
    {
        public int X;
        public int Y;
    }

    private class ObjectWithNestedTypes
    {
        public class NestedClass
        {
            public string Value = "Nested";
        }

        public NestedClass Nested = new();
    }

    private class ObjectWithTuple
    {
        public (int, string) SimpleTuple = (1, "One");
        public ValueTuple<int, string, float> NamedTuple = (1, "One", 1.0f);
    }

    private class ObjectWithGenericField<T>
    {
        public T? Value;
    }

    private class ObjectWithEvent
    {
        public event EventHandler? TestEvent;
        public string Name = "EventTest";
    }

    private record TestRecord(string Name, int Value);

    private class ObjectWithIndexer
    {
        private readonly Dictionary<string, object> _storage = new();
        public object this[string key]
        {
            get => _storage[key];
            set => _storage[key] = value;
        }
    }

    #endregion

    #region Basic Tests

    [Fact]
    public void TestPrimitives()
    {
        // String
        Assert.Equal("test", Serializer.Deserialize<string>(Serializer.Serialize("test")));
        Assert.Equal(string.Empty, Serializer.Deserialize<string>(Serializer.Serialize(string.Empty)));

        // Numeric types
        Assert.Equal((byte)255, Serializer.Deserialize<byte>(Serializer.Serialize((byte)255)));
        Assert.Equal((sbyte)-128, Serializer.Deserialize<sbyte>(Serializer.Serialize((sbyte)-128)));
        Assert.Equal((short)-32768, Serializer.Deserialize<short>(Serializer.Serialize((short)-32768)));
        Assert.Equal((ushort)65535, Serializer.Deserialize<ushort>(Serializer.Serialize((ushort)65535)));
        Assert.Equal(42, Serializer.Deserialize<int>(Serializer.Serialize(42)));
        Assert.Equal(42u, Serializer.Deserialize<uint>(Serializer.Serialize(42u)));
        Assert.Equal(42L, Serializer.Deserialize<long>(Serializer.Serialize(42L)));
        Assert.Equal(42uL, Serializer.Deserialize<ulong>(Serializer.Serialize(42uL)));
        Assert.Equal(3.14f, Serializer.Deserialize<float>(Serializer.Serialize(3.14f)));
        Assert.Equal(3.14159, Serializer.Deserialize<double>(Serializer.Serialize(3.14159)));
        Assert.Equal(3.14159m, Serializer.Deserialize<decimal>(Serializer.Serialize(3.14159m)));

        // Boolean
        Assert.True(Serializer.Deserialize<bool>(Serializer.Serialize(true)));
        Assert.False(Serializer.Deserialize<bool>(Serializer.Serialize(false)));

        // Byte array
        var byteArray = new byte[] { 1, 2, 3, 4, 5 };
        var deserializedArray = Serializer.Deserialize<byte[]>(Serializer.Serialize(byteArray));
        Assert.Equal(byteArray, deserializedArray);
    }

    [Fact]
    public void TestNullValues()
    {
        string? nullString = null;
        var serialized = Serializer.Serialize(nullString);
        var deserialized = Serializer.Deserialize<string>(serialized);
        Assert.Null(deserialized);
    }

    [Fact]
    public void TestDateTime()
    {
        // Current time
        var now = DateTime.Now;
        var serialized = Serializer.Serialize(now);
        var deserialized = Serializer.Deserialize<DateTime>(serialized);
        Assert.Equal(now, deserialized);

        // Minimum value
        var min = DateTime.MinValue;
        serialized = Serializer.Serialize(min);
        deserialized = Serializer.Deserialize<DateTime>(serialized);
        Assert.Equal(min, deserialized);

        // Maximum value
        var max = DateTime.MaxValue;
        serialized = Serializer.Serialize(max);
        deserialized = Serializer.Deserialize<DateTime>(serialized);
        Assert.Equal(max, deserialized);

        // UTC time
        var utc = DateTime.UtcNow;
        serialized = Serializer.Serialize(utc);
        deserialized = Serializer.Deserialize<DateTime>(serialized);
        Assert.Equal(utc, deserialized);

        // Specific date
        var specific = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);
        serialized = Serializer.Serialize(specific);
        deserialized = Serializer.Deserialize<DateTime>(serialized);
        Assert.Equal(specific, deserialized);
    }

    [Fact]
    public void TestGuid()
    {
        // Empty Guid
        var empty = Guid.Empty;
        var serialized = Serializer.Serialize(empty);
        var deserialized = Serializer.Deserialize<Guid>(serialized);
        Assert.Equal(empty, deserialized);

        // New Guid
        var guid = Guid.NewGuid();
        serialized = Serializer.Serialize(guid);
        deserialized = Serializer.Deserialize<Guid>(serialized);
        Assert.Equal(guid, deserialized);

        // Specific Guid
        var specific = new Guid("A1A2A3A4-B1B2-C1C2-D1D2-E1E2E3E4E5E6");
        serialized = Serializer.Serialize(specific);
        deserialized = Serializer.Deserialize<Guid>(serialized);
        Assert.Equal(specific, deserialized);
    }

    [Flags]
    public enum TestFlags
    {
        None = 0,
        Flag1 = 1,
        Flag2 = 2,
        Flag3 = 4,
        All = Flag1 | Flag2 | Flag3
    }

    [Fact]
    public void TestEnum()
    {
        // Basic enum values
        var none = TestEnum2.None;
        var serialized = Serializer.Serialize(none);
        var deserialized = Serializer.Deserialize<TestEnum2>(serialized);
        Assert.Equal(none, deserialized);

        var large = TestEnum2.Large;
        serialized = Serializer.Serialize(large);
        deserialized = Serializer.Deserialize<TestEnum2>(serialized);
        Assert.Equal(large, deserialized);

        var negative = TestEnum2.Negative;
        serialized = Serializer.Serialize(negative);
        deserialized = Serializer.Deserialize<TestEnum2>(serialized);
        Assert.Equal(negative, deserialized);
    }

    [Fact]
    public void TestFlagsEnum()
    {
        // Single flag
        var flag1 = TestFlags.Flag1;
        var serialized = Serializer.Serialize(flag1);
        var deserialized = Serializer.Deserialize<TestFlags>(serialized);
        Assert.Equal(flag1, deserialized);

        // Combined flags
        var combined = TestFlags.Flag1 | TestFlags.Flag2;
        serialized = Serializer.Serialize(combined);
        deserialized = Serializer.Deserialize<TestFlags>(serialized);
        Assert.Equal(combined, deserialized);

        // All flags
        var all = TestFlags.All;
        serialized = Serializer.Serialize(all);
        deserialized = Serializer.Deserialize<TestFlags>(serialized);
        Assert.Equal(all, deserialized);

        // No flags
        var none = TestFlags.None;
        serialized = Serializer.Serialize(none);
        deserialized = Serializer.Deserialize<TestFlags>(serialized);
        Assert.Equal(none, deserialized);
    }

    #endregion

    #region Attribute Tests
    [Fact]
    public void TestFormerlySerializedAs()
    {
        var original = new ObjectWithAttributes { NewName = "Updated" };
        var serialized = Serializer.Serialize(original);
        serialized.Remove("NewName");
        serialized.Add("oldName", new SerializedProperty(PropertyType.String, "Updated"));
        var deserialized = Serializer.Deserialize<ObjectWithAttributes>(serialized);
        Assert.Equal(original.NewName, deserialized.NewName);
    }

    [Fact]
    public void TestIgnoreOnNull()
    {
        var original = new ObjectWithAttributes { OptionalField = null };
        var serialized = Serializer.Serialize(original);
        Assert.False(serialized.Tags.ContainsKey("OptionalField"));
    }
    #endregion

    #region Collection Tests
    [Fact]
    public void TestArrays()
    {
        var original = new int[] { 1, 2, 3, 4, 5 };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<int[]>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestJaggedArrays()
    {
        var original = new int[][]
        {
                new int[] { 1, 2 },
                new int[] { 3, 4, 5 }
        };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<int[][]>(serialized);

        Assert.Equal(original.Length, deserialized.Length);
        for (int i = 0; i < original.Length; i++)
        {
            Assert.Equal(original[i], deserialized[i]);
        }
    }

    [Fact]
    public void TestMultidimensionalArrays()
    {
        var original = new int[,] { { 1, 2 }, { 3, 4 } };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<int[,]>(serialized);
        Assert.Equal(original, deserialized);

    }

    [Fact]
    public void TestHashSet()
    {
        var original = new HashSet<int> { 1, 2, 3 };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<HashSet<int>>(serialized);
        Assert.Equal(original, deserialized);
    }
    #endregion

    [Fact]
    public void TestSimpleObject()
    {
        var original = new SimpleObject();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<SimpleObject>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(original.StringField, deserialized.StringField);
        Assert.Equal(original.IntField, deserialized.IntField);
        Assert.Equal(original.FloatField, deserialized.FloatField);
        Assert.Equal(original.BoolField, deserialized.BoolField);
    }

    [Fact]
    public void TestComplexObject()
    {
        var original = new ComplexObject();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ComplexObject>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Object.StringField, deserialized.Object.StringField);
        Assert.Equal(original.Numbers, deserialized.Numbers);
        Assert.Equal(original.Values, deserialized.Values);
    }

    [Fact]
    public void TestCircularReferences()
    {
        var original = new CircularObject();
        original.Child = new CircularObject { Name = "Child" };
        original.Child.Child = original; // Create circular reference

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<CircularObject>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal("Parent", deserialized.Name);
        Assert.NotNull(deserialized.Child);
        Assert.Equal("Child", deserialized.Child.Name);
        Assert.Same(deserialized, deserialized.Child.Child); // Verify circular reference is preserved
    }

    [Fact]
    public void TestCustomSerializable()
    {
        var original = new CustomSerializableObject();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<CustomSerializableObject>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Value, deserialized.Value);
        Assert.Equal(original.Text, deserialized.Text);
    }

    [Fact]
    public void TestDictionary()
    {
        var original = new Dictionary<string, int>
            {
                { "one", 1 },
                { "two", 2 },
                { "three", 3 }
            };

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Dictionary<string, int>>(serialized);

        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestStringKeyDictionary()
    {
        var original = new Dictionary<string, int>
    {
        { "one", 1 },
        { "two", 2 },
        { "three", 3 }
    };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Dictionary<string, int>>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestIntKeyDictionary()
    {
        var original = new Dictionary<int, string>
    {
        { 1, "one" },
        { 2, "two" },
        { 3, "three" }
    };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Dictionary<int, string>>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestEnumKeyDictionary()
    {
        var original = new Dictionary<DayOfWeek, int>
    {
        { DayOfWeek.Monday, 1 },
        { DayOfWeek.Wednesday, 3 },
        { DayOfWeek.Friday, 5 }
    };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Dictionary<DayOfWeek, int>>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestGuidKeyDictionary()
    {
        var original = new Dictionary<Guid, string>
    {
        { Guid.NewGuid(), "first" },
        { Guid.NewGuid(), "second" },
        { Guid.NewGuid(), "third" }
    };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Dictionary<Guid, string>>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestNestedDictionary()
    {
        var original = new Dictionary<int, Dictionary<string, bool>>
    {
        {
            1, new Dictionary<string, bool>
            {
                { "true", true },
                { "false", false }
            }
        },
        {
            2, new Dictionary<string, bool>
            {
                { "yes", true },
                { "no", false }
            }
        }
    };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Dictionary<int, Dictionary<string, bool>>>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestEmptyDictionary()
    {
        var original = new Dictionary<int, string>();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Dictionary<int, string>>(serialized);
        Assert.Empty(deserialized);
    }

    [Fact]
    public void TestDictionaryWithNullValues()
    {
        var original = new Dictionary<int, string?>
    {
        { 1, "one" },
        { 2, null },
        { 3, "three" }
    };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Dictionary<int, string?>>(serialized);
        Assert.Equal(original, deserialized);
    }

    // Test with a custom type as key
    public class CustomKey
    {
        public int Id;
        public string Name = "";

        public override bool Equals(object? obj)
        {
            if (obj is CustomKey other)
                return Id == other.Id && Name == other.Name;
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name);
        }
    }

    [Fact]
    public void TestCustomTypeKeyDictionary()
    {
        var original = new Dictionary<CustomKey, int>
        {
            { new CustomKey { Id = 1, Name = "first" }, 1 },
            { new CustomKey { Id = 2, Name = "second" }, 2 },
            { new CustomKey { Id = 3, Name = "third" }, 3 }
        };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Dictionary<CustomKey, int>>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestMixedNestedDictionaries()
    {
        var original = new Dictionary<int, Dictionary<string, Dictionary<Guid, bool>>>
    {
        {
            1, new Dictionary<string, Dictionary<Guid, bool>>
            {
                {
                    "first", new Dictionary<Guid, bool>
                    {
                        { Guid.NewGuid(), true },
                        { Guid.NewGuid(), false }
                    }
                }
            }
        }
    };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Dictionary<int, Dictionary<string, Dictionary<Guid, bool>>>>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestList()
    {
        var original = new List<string> { "one", "two", "three" };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<List<string>>(serialized);

        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestAbstractClass()
    {
        AbstractClass original = new ConcreteClass { Name = "Test", Value = 42 };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ConcreteClass>(serialized);
        Assert.Equal(((ConcreteClass)original).Value, deserialized.Value);
    }

    [Fact]
    public void TestIndexer()
    {
        var original = new ObjectWithIndexer();
        original["test"] = "value";
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ObjectWithIndexer>(serialized);
        // Indexers aren't serialized by default
        Assert.Throws<KeyNotFoundException>(() => deserialized["test"]);
    }

    [Fact]
    public void TestDeepNesting()
    {
        var obj = new CircularObject();
        var current = obj;
        // Create a deeply nested structure
        for (int i = 0; i < 1000; i++)
        {
            current.Child = new CircularObject { Name = $"Level {i}" };
            current = current.Child;
        }

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<CircularObject>(serialized);

        // Verify a few levels
        Assert.Equal("Level 0", deserialized.Child?.Name);
        Assert.Equal("Level 1", deserialized.Child?.Child?.Name);
    }

    [Fact]
    public void TestLargeData()
    {
        var largeString = new string('a', 1_000_000);
        var serialized = Serializer.Serialize(largeString);
        var deserialized = Serializer.Deserialize<string>(serialized);
        Assert.Equal(largeString, deserialized);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void TestIntegerValues(int value)
    {
        var serialized = Serializer.Serialize(value);
        var deserialized = Serializer.Deserialize<int>(serialized);
        Assert.Equal(value, deserialized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hello")]
    [InlineData("Special\nCharacters\t\r")]
    [InlineData("Unicode 🎮 Characters")]
    public void TestStringValues(string value)
    {
        var serialized = Serializer.Serialize(value);
        var deserialized = Serializer.Deserialize<string>(serialized);
        Assert.Equal(value, deserialized);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.Epsilon)]
    public void TestSpecialFloatingPointValues(double value)
    {
        var serialized = Serializer.Serialize(value);
        var deserialized = Serializer.Deserialize<double>(serialized);
        Assert.Equal(value, deserialized);
    }

    [Fact]
    public void TestNullableInt()
    {
        // Non-null value
        int? original = 42;
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<int?>(serialized);
        Assert.Equal(original, deserialized);

        // Null value
        int? nullValue = null;
        serialized = Serializer.Serialize(nullValue);
        deserialized = Serializer.Deserialize<int?>(serialized);
        Assert.Null(deserialized);
    }

    [Fact]
    public void TestNullableDateTime()
    {
        // Non-null value
        DateTime? original = DateTime.Now;
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<DateTime?>(serialized);
        Assert.Equal(original, deserialized);

        // Null value
        DateTime? nullValue = null;
        serialized = Serializer.Serialize(nullValue);
        deserialized = Serializer.Deserialize<DateTime?>(serialized);
        Assert.Null(deserialized);
    }

    [Fact]
    public void TestNullableGuid()
    {
        // Non-null value
        Guid? original = Guid.NewGuid();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Guid?>(serialized);
        Assert.Equal(original, deserialized);

        // Null value
        Guid? nullValue = null;
        serialized = Serializer.Serialize(nullValue);
        deserialized = Serializer.Deserialize<Guid?>(serialized);
        Assert.Null(deserialized);
    }

    [Fact]
    public void TestNullableEnum()
    {
        // Non-null value
        TestEnum2? original = TestEnum2.Two;
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<TestEnum2?>(serialized);
        Assert.Equal(original, deserialized);

        // Null value
        TestEnum2? nullValue = null;
        serialized = Serializer.Serialize(nullValue);
        deserialized = Serializer.Deserialize<TestEnum2?>(serialized);
        Assert.Null(deserialized);
    }

    private class NullableTestClass
    {
        public int? NullableInt;
        public DateTime? NullableDateTime;
        public Guid? NullableGuid;
        public TestEnum2? NullableEnum;

        public int? NullIntValue;
        public DateTime? NullDateTimeValue;
        public Guid? NullGuidValue;
        public TestEnum2? NullEnumValue;
    }

    [Fact]
    public void TestComplexNullableTypes()
    {
        var testClass = new NullableTestClass
        {
            NullableInt = 42,
            NullableDateTime = DateTime.Now,
            NullableGuid = Guid.NewGuid(),
            NullableEnum = TestEnum2.One,
            NullIntValue = null,
            NullDateTimeValue = null,
            NullGuidValue = null,
            NullEnumValue = null
        };

        var serialized = Serializer.Serialize(testClass);
        var deserialized = Serializer.Deserialize<NullableTestClass>(serialized);

        // Check non-null values
        Assert.Equal(testClass.NullableInt, deserialized.NullableInt);
        Assert.Equal(testClass.NullableDateTime, deserialized.NullableDateTime);
        Assert.Equal(testClass.NullableGuid, deserialized.NullableGuid);
        Assert.Equal(testClass.NullableEnum, deserialized.NullableEnum);

        // Check null values
        Assert.Null(deserialized.NullIntValue);
        Assert.Null(deserialized.NullDateTimeValue);
        Assert.Null(deserialized.NullGuidValue);
        Assert.Null(deserialized.NullEnumValue);
    }

    [Fact]
    public void TestNullablePrimitives()
    {
        // Test all primitive nullable types
        byte? byteValue = 255;
        Assert.Equal(byteValue, Serializer.Deserialize<byte?>(Serializer.Serialize(byteValue)));

        sbyte? sbyteValue = -128;
        Assert.Equal(sbyteValue, Serializer.Deserialize<sbyte?>(Serializer.Serialize(sbyteValue)));

        short? shortValue = -32768;
        Assert.Equal(shortValue, Serializer.Deserialize<short?>(Serializer.Serialize(shortValue)));

        ushort? ushortValue = 65535;
        Assert.Equal(ushortValue, Serializer.Deserialize<ushort?>(Serializer.Serialize(ushortValue)));

        long? longValue = long.MaxValue;
        Assert.Equal(longValue, Serializer.Deserialize<long?>(Serializer.Serialize(longValue)));

        ulong? ulongValue = ulong.MaxValue;
        Assert.Equal(ulongValue, Serializer.Deserialize<ulong?>(Serializer.Serialize(ulongValue)));

        float? floatValue = 3.14159f;
        Assert.Equal(floatValue, Serializer.Deserialize<float?>(Serializer.Serialize(floatValue)));

        double? doubleValue = 3.14159265359;
        Assert.Equal(doubleValue, Serializer.Deserialize<double?>(Serializer.Serialize(doubleValue)));

        decimal? decimalValue = 3.14159265359m;
        Assert.Equal(decimalValue, Serializer.Deserialize<decimal?>(Serializer.Serialize(decimalValue)));

        bool? boolValue = true;
        Assert.Equal(boolValue, Serializer.Deserialize<bool?>(Serializer.Serialize(boolValue)));
    }
}
