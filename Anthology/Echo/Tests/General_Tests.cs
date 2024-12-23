// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Tests.Types;

namespace Prowl.Echo.Test;

public class General_Tests
{
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
        serialized.Add("oldName", new EchoObject(EchoType.String, "Updated"));
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

    #region Test Type Modes

    [Fact]
    public void TypeMode_Aggressive_AlwaysIncludesType()
    {
        // Arrange
        var obj = new SimpleObject();
        var context = new SerializationContext(TypeMode.Aggressive);

        // Act - No reason to include type here, since the target type is the same as the actual type
        var result = Serializer.Serialize(obj.GetType(), obj, context);

        // Assert
        Assert.True(result.TryGet("$type", out var typeTag));
        Assert.Equal(typeof(SimpleObject).FullName, typeTag.StringValue);
    }

    [Fact]
    public void TypeMode_None_NeverIncludesType()
    {
        // Arrange
        var obj = new ComplexObject();
        var context = new SerializationContext(TypeMode.None);

        // Act
        var result = Serializer.Serialize(typeof(object), obj, context);

        // Assert
        Assert.False(result.TryGet("$type", out _));
    }

    [Fact]
    public void TypeMode_Auto_IncludesTypeOnlyWhenNecessary()
    {
        // Test with matching type - should not include type info
        {
            // Arrange
            var simpleObj = new SimpleObject();
            var context = new SerializationContext(TypeMode.Auto);
            Type targetType = typeof(SimpleObject);

            // Act
            var result = Serializer.Serialize(targetType, simpleObj, context);

            // Assert
            Assert.False(result.TryGet("$type", out _), "Type info should not be included when type matches exactly");
        }

        // Test with different type - should include type info
        {
            // Arrange - using SimpleObject as a more specific type than object
            var simpleObj = new SimpleObject();
            var context = new SerializationContext(TypeMode.Auto);
            Type targetType = typeof(object);

            // Act
            var result = Serializer.Serialize(targetType, simpleObj, context);

            // Assert
            Assert.True(result.TryGet("$type", out var typeTag), "Type info should be included when actual type differs from target type");
            Assert.Equal(typeof(SimpleObject).FullName, typeTag.StringValue);
        }
    }

    [Fact]
    public void TypeMode_Auto_IncludesTypeForObjectType()
    {
        // Arrange
        object obj = new SimpleObject();
        var context = new SerializationContext(TypeMode.Auto);

        // Act, We are passing object as the target type to force it to serialize into the object type
        var result = Serializer.Serialize(typeof(object), obj, context);

        // Assert
        Assert.True(result.TryGet("$type", out var typeTag));
        Assert.Equal(typeof(SimpleObject).FullName, typeTag.StringValue);
    }

    [Fact]
    public void TypeMode_ComplexObjects_WithCollections()
    {
        // Arrange
        var complex = new ComplexObject {
            Object = new SimpleObject(),
            Numbers = new List<int> { 1, 2, 3 },
            Values = new Dictionary<string, float> { { "test", 1.0f } }
        };

        // Act
        var result = Serializer.Serialize(complex, TypeMode.Auto);

        // Assert
        Assert.False(result.TryGet("$type", out _)); // Base type matches
        Assert.False(result.Get("Object").TryGet("$type", out _)); // Nested object type matches

        // Arrange
        var complex2 = new ComplexObject {
            Object = new SimpleInheritedObject(),
            Numbers = new List<int> { 1, 2, 3 },
            Values = new Dictionary<string, float> { { "test", 1.0f } }
        };

        // Act
        var result2 = Serializer.Serialize(complex2, TypeMode.Auto);

        // Assert
        Assert.False(result2.TryGet("$type", out _)); // Base type matches
        Assert.True(result2.Get("Object").TryGet("$type", out var objectType)); // Nested object type does not match
        Assert.Equal(typeof(SimpleInheritedObject).FullName, objectType.StringValue);
    }

    [Fact]
    public void TypeMode_CircularReferences()
    {
        // Arrange
        var parent = new CircularObject { Name = "Parent" };
        var child = new CircularObject { Name = "Child" };
        parent.Child = child;
        child.Child = parent;

        var context = new SerializationContext(TypeMode.Auto);

        // Act
        var result = Serializer.Serialize(parent, context);

        // Assert
        Assert.True(result.TryGet("$id", out var parentId));
        Assert.True(result.TryGet("Child", out var childTag));
        Assert.True(childTag.TryGet("$id", out var childId));
        Assert.True(childTag.TryGet("Child", out var circularRef));
        Assert.True(circularRef.TryGet("$id", out var circularId));

        // The circular reference should point back to the first object
        Assert.Equal(parentId.IntValue, circularId.IntValue);
    }

    [Fact]
    public void TypeMode_CustomSerializable()
    {
        // Arrange
        var obj = new CustomSerializableObject { Value = 100, Text = "Test" };
        var context = new SerializationContext(TypeMode.Aggressive);

        // Act
        var result = Serializer.Serialize(obj, context);

        // Assert
        Assert.True(result.TryGet("$type", out var typeTag));
        Assert.Equal(typeof(CustomSerializableObject).FullName, typeTag.StringValue);
        Assert.Equal(100, result.Get("customValue").IntValue);
        Assert.Equal("Test", result.Get("customText").StringValue);
    }

    [Fact]
    public void TypeMode_NestedTypes()
    {
        // Arrange
        var obj = new ObjectWithNestedTypes();
        var context = new SerializationContext(TypeMode.Auto);

        // Act
        var result = Serializer.Serialize(obj, context);

        // Assert 
        Assert.False(result.Get("NestedA").TryGet("$type", out _)); // Nested type matches base type
        Assert.True(result.Get("NestedB").TryGet("$type", out var nestedType)); // Nested type does not match base type
        Assert.Equal(typeof(ObjectWithNestedTypes.NestedInheritedClass).FullName, nestedType.StringValue);
    }

    [Fact]
    public void TypeMode_GenericTypes()
    {
        // Arrange
        var obj = new ObjectWithGenericField<string> { Value = "test" };
        var context = new SerializationContext(TypeMode.Auto);

        // Act
        var result = Serializer.Serialize(typeof(object), obj, context);

        // Assert
        Assert.True(result.TryGet("$type", out var typeTag));
        Assert.Contains("ObjectWithGenericField", typeTag.StringValue);
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
    public void TestSimpleVector3Struct()
    {
        var original = new Vector3();
        original.X = 1.0f;
        original.Y = 2.0f;
        original.Z = 3.0f;
        var stream = new MemoryStream();
        using var bw = new BinaryWriter(stream);
        var serialized = Prowl.Echo.Serializer.Serialize(original);
        Assert.NotNull(serialized);

        serialized.WriteToBinary(bw);
        bw.Flush();

        stream.Position = 0;
        using var br = new BinaryReader(stream);
        var deserialized = Prowl.Echo.EchoObject.ReadFromBinary(br);
        Assert.NotNull(deserialized);
        Vector3 clone = (Vector3)Prowl.Echo.Serializer.Deserialize(deserialized, typeof(Vector3));

        Assert.Equal(original.X, clone.X);
        Assert.Equal(original.Y, clone.Y);
        Assert.Equal(original.Z, clone.Z);
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
