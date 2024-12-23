// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;
using Tests;
using Xunit;

namespace Prowl.Echo.Test;

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

    public class SimpleInheritedObject : SimpleObject
    {
        public string InheritedField = "Inherited";
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

        public EchoObject Serialize(SerializationContext ctx)
        {
            var compound = EchoObject.NewCompound();
            compound.Add("customValue", new EchoObject(EchoType.Int, Value));
            compound.Add("customText", new EchoObject(EchoType.String, Text));
            return compound;
        }

        public void Deserialize(EchoObject tag, SerializationContext ctx)
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

        public class NestedInheritedClass : NestedClass
        {
            public string InheritedValue = "Inherited";
        }

        public NestedClass NestedA = new NestedClass();
        public NestedClass NestedB = new NestedInheritedClass();
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

    #region Test Type Modes

    [Fact]
    public void TypeMode_Aggressive_AlwaysIncludesType()
    {
        // Arrange
        var obj = new SimpleObject();
        var context = new SerializationContext(TypeMode.Aggressive);

        // Act
        var result = Serializer.Serialize(obj, context);

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

    #region Key/Index Tracking

    [Fact]
    public void ListIndex_IsNull_WhenNotInList()
    {
        var item = EchoObject.NewCompound();
        Assert.Null(item.ListIndex);
    }

    [Fact]
    public void ListIndex_IsSet_WhenAddedToList()
    {
        var list = EchoObject.NewList();
        var item1 = new EchoObject("first");
        var item2 = new EchoObject("second");

        list.ListAdd(item1);
        list.ListAdd(item2);

        Assert.Equal(0, item1.ListIndex);
        Assert.Equal(1, item2.ListIndex);
    }

    [Fact]
    public void ListIndex_UpdatesOnRemoval()
    {
        var list = EchoObject.NewList();
        var item1 = new EchoObject("first");
        var item2 = new EchoObject("second");
        var item3 = new EchoObject("third");

        list.ListAdd(item1);
        list.ListAdd(item2);
        list.ListAdd(item3);

        list.ListRemove(item1);

        Assert.Null(item1.ListIndex); // Removed item
        Assert.Equal(0, item2.ListIndex); // Should shift down
        Assert.Equal(1, item3.ListIndex);
    }

    [Fact]
    public void CompoundKey_IsNull_WhenNotInCompound()
    {
        var item = new EchoObject("test");
        Assert.Null(item.CompoundKey);
    }

    [Fact]
    public void CompoundKey_IsSet_WhenAddedToCompound()
    {
        var compound = EchoObject.NewCompound();
        var item = new EchoObject("test");

        compound["myKey"] = item;

        Assert.Equal("myKey", item.CompoundKey);
    }

    [Fact]
    public void CompoundKey_UpdatesOnRemoval()
    {
        var compound = EchoObject.NewCompound();
        var item = new EchoObject("test");

        compound["myKey"] = item;
        Assert.Equal("myKey", item.CompoundKey);

        compound.Remove("myKey");
        Assert.Null(item.CompoundKey);
    }

    [Fact]
    public void CompoundKey_UpdatesOnReassignment()
    {
        var compound = EchoObject.NewCompound();
        var item = new EchoObject("test");

        compound["key1"] = item;
        Assert.Equal("key1", item.CompoundKey);

        item.Parent.Remove(item.CompoundKey);

        compound["key2"] = item; // Move to new key
        Assert.Equal("key2", item.CompoundKey);
    }

    [Fact]
    public void ListIndex_And_CompoundKey_AreExclusive()
    {
        var list = EchoObject.NewList();
        var compound = EchoObject.NewCompound();
        var item = new EchoObject("test");

        list.ListAdd(item);
        Assert.NotNull(item.ListIndex);
        Assert.Null(item.CompoundKey);

        list.ListRemove(item);
        compound["key"] = item;
        Assert.Null(item.ListIndex);
        Assert.NotNull(item.CompoundKey);
    }

    #endregion

    #region Query Tests

    private static EchoObject CreateTestData()
    {
        var root = EchoObject.NewCompound();

        // Add player data
        var player = EchoObject.NewCompound();
        root["Player"] = player;

        // Add inventory
        var inventory = EchoObject.NewList();
        player["Inventory"] = inventory;

        // Add items
        var item1 = EchoObject.NewCompound();
        item1["Id"] = new EchoObject("gold_ingot");
        item1["Count"] = new EchoObject(64);
        item1["Value"] = new EchoObject(100);
        inventory.ListAdd(item1);

        var item2 = EchoObject.NewCompound();
        item2["Id"] = new EchoObject("iron_ingot");
        item2["Count"] = new EchoObject(32);
        item2["Value"] = new EchoObject(50);
        inventory.ListAdd(item2);

        // Add stats
        var stats = EchoObject.NewCompound();
        player["Stats"] = stats;
        stats["Health"] = new EchoObject(100);
        stats["Mana"] = new EchoObject(50);

        return root;
    }

    [Fact]
    public void GetValue_PrimitiveTypeConversions()
    {
        var echo = EchoObject.NewCompound();
        echo["int"] = new EchoObject(42);
        echo["float"] = new EchoObject(42.5f);
        echo["bool"] = new EchoObject(true);

        // Test various numeric conversions
        Assert.Equal(42, echo.GetValue<int>("int"));
        Assert.Equal(42L, echo.GetValue<long>("int"));
        Assert.Equal(42.0f, echo.GetValue<float>("int"));
        Assert.Equal(42.0, echo.GetValue<double>("int"));
        Assert.Equal(42.0m, echo.GetValue<decimal>("int"));

        // Test float conversions
        Assert.Equal(42.5f, echo.GetValue<float>("float"));
        Assert.Equal(42, echo.GetValue<int>("float"));

        // Test bool
        Assert.True(echo.GetValue<bool>("bool"));
    }

    [Fact]
    public void GetValue_CompoundAndList()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        var compound = EchoObject.NewCompound();

        root["list"] = list;
        root["compound"] = compound;

        // Test getting as EchoObject
        Assert.NotNull(root.GetValue<EchoObject>("list"));
        Assert.NotNull(root.GetValue<EchoObject>("compound"));

        // Test getting as specific collections
        Assert.NotNull(root.GetValue<List<EchoObject>>("list"));
        Assert.NotNull(root.GetValue<Dictionary<string, EchoObject>>("compound"));

        // Test convenience methods
        Assert.NotNull(root.GetListAt("list"));
        Assert.NotNull(root.GetEchoAt("compound"));
        Assert.NotNull(root.GetDictionaryAt("compound"));
    }

    [Fact]
    public void GetValue_InvalidConversions()
    {
        var echo = EchoObject.NewCompound();
        echo["string"] = new EchoObject("not a number");

        // Should return default values for invalid conversions
        Assert.Equal(0, echo.GetValue<int>("string", 0));
        Assert.Equal(-1, echo.GetValue<int>("nonexistent", -1));
    }

    [Fact]
    public void GetValue_ByteArray()
    {
        var echo = EchoObject.NewCompound();
        var bytes = new byte[] { 1, 2, 3, 4 };
        echo["bytes"] = new EchoObject(bytes);

        var result = echo.GetValue<byte[]>("bytes");
        Assert.NotNull(result);
        Assert.Equal(bytes, result);
    }

    [Fact]
    public void GetValue_ComplexHierarchy()
    {
        var root = EchoObject.NewCompound();
        var players = EchoObject.NewList();
        root["players"] = players;

        var player = EchoObject.NewCompound();
        player["name"] = new EchoObject("Player1");
        players.ListAdd(player);

        // Test deep path with list index
        Assert.Equal("Player1", root.GetValue<string>("players/0/name"));

        // Test getting intermediate collections
        Assert.NotNull(root.GetListAt("players"));
        Assert.NotNull(root.GetEchoAt("players/0"));
        Assert.NotNull(root.GetDictionaryAt("players/0"));
    }

    [Theory]
    [InlineData(EchoType.Int, typeof(byte))]
    [InlineData(EchoType.Int, typeof(short))]
    [InlineData(EchoType.Int, typeof(long))]
    [InlineData(EchoType.Float, typeof(double))]
    [InlineData(EchoType.Double, typeof(float))]
    public void GetValue_NumericConversions(EchoType sourceType, Type targetType)
    {
        var echo = EchoObject.NewCompound();
        echo["value"] = new EchoObject(sourceType, 42);

        var method = typeof(EchoObject).GetMethod("GetValue")!.MakeGenericMethod(targetType);
        var result = method.Invoke(echo, new object?[] { "value", null });

        Assert.NotNull(result);
        Assert.Equal(42f, Convert.ChangeType(result, typeof(float)));
    }


    [Fact]
    public void Find_WithValidPath_ReturnsCorrectObject()
    {
        var root = CreateTestData();

        var result = root.Find("Player/Stats/Health");
        Assert.NotNull(result);
        Assert.Equal(100, result.IntValue);
    }

    [Fact]
    public void Find_WithListIndex_ReturnsCorrectObject()
    {
        var root = CreateTestData();

        var result = root.Find("Player/Inventory/0/Id");
        Assert.NotNull(result);
        Assert.Equal("gold_ingot", result.StringValue);
    }

    [Fact]
    public void Find_WithInvalidPath_ReturnsNull()
    {
        var root = CreateTestData();

        var result = root.Find("Player/NonExistent");
        Assert.Null(result);
    }

    [Fact]
    public void Find_WithInvalidListIndex_ReturnsNull()
    {
        var root = CreateTestData();

        var result = root.Find("Player/Inventory/999");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Find_WithEmptyPath_ReturnsSelf(string path)
    {
        var root = CreateTestData();

        var result = root.Find(path);
        Assert.Same(root, result);
    }

    [Fact]
    public void Where_OnList_ReturnsFilteredItems()
    {
        var root = CreateTestData();
        var inventory = root.Find("Player/Inventory");

        Assert.NotNull(inventory);

        var highValueItems = inventory.Where(item => item["Value"].IntValue > 75).ToList();

        Assert.Single(highValueItems);
        Assert.Equal("gold_ingot", highValueItems[0]["Id"]?.StringValue);
    }

    [Fact]
    public void Select_OnList_TransformsItems()
    {
        var root = CreateTestData();
        var inventory = root.Find("Player/Inventory");

        Assert.NotNull(inventory);

        var itemIds = inventory.Select(item => item["Id"]?.StringValue).ToList();

        Assert.Equal(2, itemIds.Count);
        Assert.Contains("gold_ingot", itemIds);
        Assert.Contains("iron_ingot", itemIds);
    }

    [Fact]
    public void FindAll_WithPredicate_ReturnsAllMatches()
    {
        var root = CreateTestData();

        var allInt = root.FindAll(tag => tag.TagType == EchoType.Int).ToList();

        Assert.Equal(6, allInt.Count);
    }

    [Fact]
    public void GetValue_WithValidPath_ReturnsTypedValue()
    {
        var root = CreateTestData();

        int health = root.GetValue<int>("Player/Stats/Health", -1);
        string? itemId = root.GetValue<string>("Player/Inventory/0/Id", "");

        Assert.Equal(100, health);
        Assert.Equal("gold_ingot", itemId);
    }

    [Fact]
    public void GetValue_WithInvalidPath_ReturnsDefault()
    {
        var root = CreateTestData();

        int value = root.GetValue<int>("NonExistent/Path", -1);
        string? text = root.GetValue<string>("NonExistent/Path", "default");

        Assert.Equal(-1, value);
        Assert.Equal("default", text);
    }

    [Fact]
    public void Exists_WithValidPath_ReturnsTrue()
    {
        var root = CreateTestData();

        Assert.True(root.Exists("Player/Stats/Health"));
        Assert.True(root.Exists("Player/Inventory/0"));
    }

    [Fact]
    public void Exists_WithInvalidPath_ReturnsFalse()
    {
        var root = CreateTestData();

        Assert.False(root.Exists("Player/NonExistent"));
        Assert.False(root.Exists("Player/Inventory/999"));
    }

    [Fact]
    public void GetPathsTo_FindsAllMatchingPaths()
    {
        var root = CreateTestData();

        var valuePaths = root.GetPathsTo(tag =>
            tag.TagType == EchoType.Int &&
            tag.IntValue > 75).ToList();

        Assert.Equal(2, valuePaths.Count);
        Assert.Contains("Player/Stats/Health", valuePaths);
        Assert.Contains("Player/Inventory/0/Value", valuePaths);
    }

    [Fact]
    public void ChainedQueries_WorkCorrectly()
    {
        var root = CreateTestData();

        var highValueItemIds = root.Find("Player/Inventory")?
            .Where(item => item.GetValue<int>("Value", 0) > 75)
            .Select(item => item.GetValue<string>("Id", ""))
            .ToList();

        Assert.NotNull(highValueItemIds);
        Assert.Single(highValueItemIds);
        Assert.Equal("gold_ingot", highValueItemIds[0]);
    }

    [Fact]
    public void DeepQuery_WithComplexConditions()
    {
        var root = CreateTestData();

        var results = root.FindAll(tag =>
            tag.TagType == EchoType.Int &&
            tag.Parent?.TagType == EchoType.Compound &&
            tag.Parent.Parent?.TagType == EchoType.List)
            .ToList();

        Assert.Equal(4, results.Count); // Should find Count and Value for both items
    }

    [Fact]
    public void ListOperations_WithModification()
    {
        var root = CreateTestData();
        var inventory = root.Find("Player/Inventory");
        Assert.NotNull(inventory);

        // Add new item
        var newItem = EchoObject.NewCompound();
        newItem["Id"] = new EchoObject("diamond");
        newItem["Count"] = new EchoObject(1);
        newItem["Value"] = new EchoObject(1000);
        inventory.ListAdd(newItem);

        // Verify through query
        var highestValue = inventory
            .Select(item => item.GetValue<int>("Value", 0))
            .Max();
        Assert.Equal(1000, highestValue);
    }

    [Fact]
    public void GetPath_ReturnsCorrectPath()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root["items"] = list;

        var item = EchoObject.NewCompound();
        list.ListAdd(item);
        item["name"] = new EchoObject("test");

        Assert.Equal("items", list.GetPath());
        Assert.Equal("items/0", item.GetPath());
        Assert.Equal("items/0/name", item["name"].GetPath());
    }

    [Fact]
    public void GetPath_WithMultipleLevels_ReturnsCorrectPath()
    {
        var root = EchoObject.NewCompound();
        var players = EchoObject.NewList();
        root["players"] = players;

        var player = EchoObject.NewCompound();
        players.ListAdd(player);

        var inventory = EchoObject.NewList();
        player["inventory"] = inventory;

        var item = EchoObject.NewCompound();
        inventory.ListAdd(item);
        item["name"] = new EchoObject("sword");

        Assert.Equal("players/0/inventory/0/name", item["name"].GetPath());
    }

    [Fact]
    public void GetRelativePath_MustExistInside()
    {
        var container = EchoObject.NewCompound();
        var subContainer = EchoObject.NewCompound();
        var item = new EchoObject("test");

        container.Add("sub", subContainer);
        subContainer.Add("item", item);

        // These should work
        Assert.Equal("sub", EchoObject.GetRelativePath(container, subContainer));
        Assert.Equal("sub/item", EchoObject.GetRelativePath(container, item));
        Assert.Equal("item", EchoObject.GetRelativePath(subContainer, item));

        // These should throw
        Assert.Throws<ArgumentException>(() => EchoObject.GetRelativePath(item, container));
        Assert.Throws<ArgumentException>(() => EchoObject.GetRelativePath(subContainer, container));
    }

    [Fact]
    public void CompoundKey_And_ListIndex_AreSetCorrectly()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root["items"] = list;

        var item1 = EchoObject.NewCompound();
        var item2 = EchoObject.NewCompound();
        list.ListAdd(item1);
        list.ListAdd(item2);

        // Test CompoundKey
        Assert.Equal("items", list.CompoundKey);

        // Test ListIndex
        Assert.Equal(0, item1.ListIndex);
        Assert.Equal(1, item2.ListIndex);

        // Test both with nested structure
        item1["name"] = new EchoObject("sword");
        Assert.Equal("name", item1["name"].CompoundKey);
        Assert.Equal(null, item1["name"].ListIndex); // Not in a list
    }

    [Fact]
    public void GetPath_Uses_CompoundKey_And_ListIndex()
    {
        var root = EchoObject.NewCompound();
        var players = EchoObject.NewList();
        root["players"] = players;  // CompoundKey = "players"

        var player = EchoObject.NewCompound();
        players.ListAdd(player);    // ListIndex = 0

        var items = EchoObject.NewList();
        player["inventory"] = items;  // CompoundKey = "inventory"

        var item = EchoObject.NewCompound();
        items.ListAdd(item);         // ListIndex = 0

        item["name"] = new EchoObject("sword");  // CompoundKey = "name"

        Assert.Equal("players/0/inventory/0/name", item["name"].GetPath());
    }

    #endregion

    #region Change Tracking

    [Fact]
    public void ChangeTracking_TracksValueChanges()
    {
        var root = EchoObject.NewCompound();
        EchoChangeEventArgs? capturedEvent = null;
        root.PropertyChanged += (s, e) => capturedEvent = e;

        root["value"] = new EchoObject("initial");
        Assert.NotNull(capturedEvent);
        Assert.Equal("value", capturedEvent.Path);
        Assert.Equal(ChangeType.TagAdded, capturedEvent.Type);
    }

    [Fact]
    public void ChangeTracking_TracksListChanges()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root["list"] = list;

        var events = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => events.Add(e);

        var item = new EchoObject("test");
        list.ListAdd(item);

        Assert.Contains(events, e =>
            e.Type == ChangeType.ListTagAdded &&
            e.Path == "list/0");
    }

    [Fact]
    public void ChangeTracking_TracksNestedChanges()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root["list"] = list;
        var item = new EchoObject("test");
        list.ListAdd(item);

        EchoChangeEventArgs? capturedEvent = null;
        root.PropertyChanged += (s, e) => capturedEvent = e;

        item.SetValue("changed");

        Assert.NotNull(capturedEvent);
        Assert.Equal("list/0", capturedEvent.Path);
        Assert.Equal("test", capturedEvent.OldValue);
        Assert.Equal("changed", capturedEvent.NewValue);
    }

    [Fact]
    public void ChangeTracking_CompoundAdd_TracksCorrectly()
    {
        var root = EchoObject.NewCompound();
        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        root.Add("test", new EchoObject("value"));

        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal(ChangeType.TagAdded, change.Type);
        Assert.Equal("test", change.Path);
        Assert.Equal("value", change.NewValue);
        Assert.Null(change.OldValue);
    }

    [Fact]
    public void ChangeTracking_NestedChanges_TrackCorrectPaths()
    {
        var root = EchoObject.NewCompound();
        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        // Create nested structure
        var player = EchoObject.NewCompound();
        root.Add("player", player);
        var inventory = EchoObject.NewList();
        player.Add("inventory", inventory);

        // Add item
        var item = new EchoObject("sword");
        inventory.ListAdd(item);

        // Change item value
        item.Value = "better sword";

        // Verify paths
        Assert.Contains(changes, e =>
            e.Path == "player" &&
            e.Type == ChangeType.TagAdded);

        Assert.Contains(changes, e =>
            e.Path == "player/inventory" &&
            e.Type == ChangeType.TagAdded);

        Assert.Contains(changes, e =>
            e.Path == "player/inventory/0" &&
            e.Type == ChangeType.ListTagAdded);

        Assert.Contains(changes, e =>
            e.Path == "player/inventory/0" &&
            e.Type == ChangeType.ValueChanged &&
            e.OldValue as string == "sword" &&
            e.NewValue as string == "better sword");
    }

    [Fact]
    public void ChangeTracking_ListOperations_TrackCorrectly()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root.Add("items", list);

        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        // Add items
        var item1 = new EchoObject("first");
        var item2 = new EchoObject("second");
        list.ListAdd(item1);
        list.ListAdd(item2);

        // Remove middle item
        list.ListRemove(item1);

        // Verify changes
        Assert.Contains(changes, e =>
            e.Path == "items/0" &&
            e.Type == ChangeType.ListTagAdded &&
            e.NewValue as string == "first");

        Assert.Contains(changes, e =>
            e.Path == "items/1" &&
            e.Type == ChangeType.ListTagAdded &&
            e.NewValue as string == "second");

        Assert.Contains(changes, e =>
            e.Path == "items/0" &&
            e.Type == ChangeType.ListTagRemoved &&
            e.OldValue as string == "first");

        // Verify index updates
        Assert.Contains(changes, e =>
            e.Type == ChangeType.ListTagMoved &&
            e.Path == "items/0" &&
            (int?)e.OldValue == 1 &&
            (int?)e.NewValue == 0);
    }

    [Fact]
    public void ChangeTracking_CompoundRename_TracksCorrectly()
    {
        var root = EchoObject.NewCompound();
        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        root.Add("oldName", new EchoObject("value"));
        root.Rename("oldName", "newName");

        Assert.Contains(changes, e =>
            e.Type == ChangeType.TagRenamed &&
            e.OldValue as string == "oldName" &&
            e.NewValue as string == "newName");
    }

    [Fact]
    public void ChangeTracking_DeepPathChanges()
    {
        var root = EchoObject.NewCompound();
        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        // Create deep structure
        root.Add("level1", EchoObject.NewCompound());
        root["level1"].Add("level2", EchoObject.NewCompound());
        root["level1"]["level2"].Add("level3", new EchoObject("initial"));

        // Change deep value
        root.Find("level1/level2/level3").Value = "changed";

        var lastChange = changes[^1];
        Assert.Equal("level1/level2/level3", lastChange.Path);
        Assert.Equal(ChangeType.ValueChanged, lastChange.Type);
        Assert.Equal("initial", lastChange.OldValue);
        Assert.Equal("changed", lastChange.NewValue);
    }

    [Fact]
    public void ChangeTracking_MultipleListeners()
    {
        var root = EchoObject.NewCompound();
        var player = EchoObject.NewCompound();
        root.Add("player", player);

        var rootChanges = new List<EchoChangeEventArgs>();
        var playerChanges = new List<EchoChangeEventArgs>();

        root.PropertyChanged += (s, e) => rootChanges.Add(e);
        player.PropertyChanged += (s, e) => playerChanges.Add(e);

        var steve = new EchoObject("Steve");
        player.Add("name", steve);
        
        Assert.Single(playerChanges);
        Assert.Single(rootChanges);

        Assert.Equal("name", playerChanges[0].RelativePath);
        Assert.Equal("player/name", rootChanges[0].RelativePath);
    }

    [Fact]
    public void ChangeTracking_RelativePaths()
    {
        var root = EchoObject.NewCompound();
        var player = EchoObject.NewCompound();
        root.Add("player", player);
        var inventory = EchoObject.NewList();
        player.Add("inventory", inventory);

        var changes = new List<EchoChangeEventArgs>();
        player.PropertyChanged += (s, e) => changes.Add(e);

        var item = new EchoObject("sword");
        inventory.ListAdd(item);
        item.Value = "better sword";

        Assert.Contains(changes, e =>
            e.RelativePath == "inventory/0" &&
            e.Path == "player/inventory/0");
    }

    [Fact]
    public void ChangeTracking_PreservesChangesAfterMove()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root.Add("items", list);

        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        // Add items and move them
        var item1 = new EchoObject("first");
        var item2 = new EchoObject("second");
        list.ListAdd(item1);
        list.ListAdd(item2);
        list.ListRemove(item1);

        // Change value of moved item
        item2.Value = "modified";

        var lastChange = changes[^1];
        Assert.Equal("items/0", lastChange.Path);
        Assert.Equal("second", lastChange.OldValue);
        Assert.Equal("modified", lastChange.NewValue);
    }

    [Fact]
    public void ChangeTracking_CloningDoesntTriggerEvents()
    {
        var root = EchoObject.NewCompound();
        root.Add("value", new EchoObject("test"));

        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        var clone = root.Clone();
        Assert.Empty(changes);
    }

    #endregion

}
