// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Tests.Types;

namespace Prowl.Echo.Test;

[FixedEchoStructure]
public struct NetworkPosition
{
    public float X;
    public float Y;
    public float Z;
}

public abstract class MonoBehaviour
{
    public string Name;
}

public class Component : MonoBehaviour
{
    public int Value;
}

public class GameObject
{
    public string Name;

    public List<MonoBehaviour> Components = new List<MonoBehaviour>();
}

public class NodeWithMultipleRefs : ISerializable
{
    public string Name;
    public NodeWithMultipleRefs Left;
    public NodeWithMultipleRefs Right;
    public NodeWithMultipleRefs Parent;

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        compound.Add("Name", new EchoObject(Name));
        compound.Add("Left", Serializer.Serialize(typeof(NodeWithMultipleRefs), Left, ctx));
        compound.Add("Right", Serializer.Serialize(typeof(NodeWithMultipleRefs), Right, ctx));
        compound.Add("Parent", Serializer.Serialize(typeof(NodeWithMultipleRefs), Parent, ctx));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Name = value["Name"].StringValue;
        Left = Serializer.Deserialize<NodeWithMultipleRefs>(value["Left"], ctx);
        Right = Serializer.Deserialize<NodeWithMultipleRefs>(value["Right"], ctx);
        Parent = Serializer.Deserialize<NodeWithMultipleRefs>(value["Parent"], ctx);

    }
}

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
    public void TestTimeSpan()
    {
        // Positive duration
        var duration = new TimeSpan(1, 2, 3, 4, 5);
        var serialized = Serializer.Serialize(duration);
        var deserialized = Serializer.Deserialize<TimeSpan>(serialized);
        Assert.Equal(duration, deserialized);

        // Minimum value
        var min = TimeSpan.MinValue;
        serialized = Serializer.Serialize(min);
        deserialized = Serializer.Deserialize<TimeSpan>(serialized);
        Assert.Equal(min, deserialized);

        // Maximum value
        var max = TimeSpan.MaxValue;
        serialized = Serializer.Serialize(max);
        deserialized = Serializer.Deserialize<TimeSpan>(serialized);
        Assert.Equal(max, deserialized);

        // Zero value
        var zero = TimeSpan.Zero;
        serialized = Serializer.Serialize(zero);
        deserialized = Serializer.Deserialize<TimeSpan>(serialized);
        Assert.Equal(zero, deserialized);

        // Negative duration
        var negative = new TimeSpan(-5, -30, -45);
        serialized = Serializer.Serialize(negative);
        deserialized = Serializer.Deserialize<TimeSpan>(serialized);
        Assert.Equal(negative, deserialized);

        // From days
        var days = TimeSpan.FromDays(7.5);
        serialized = Serializer.Serialize(days);
        deserialized = Serializer.Deserialize<TimeSpan>(serialized);
        Assert.Equal(days, deserialized);
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

    [Fact]
    public void TestDateTimeOffset()
    {
        // Current time
        var now = DateTimeOffset.Now;
        var serialized = Serializer.Serialize(now);
        var deserialized = Serializer.Deserialize<DateTimeOffset>(serialized);
        Assert.Equal(now, deserialized);

        // Minimum value
        var min = DateTimeOffset.MinValue;
        serialized = Serializer.Serialize(min);
        deserialized = Serializer.Deserialize<DateTimeOffset>(serialized);
        Assert.Equal(min, deserialized);

        // Maximum value
        var max = DateTimeOffset.MaxValue;
        serialized = Serializer.Serialize(max);
        deserialized = Serializer.Deserialize<DateTimeOffset>(serialized);
        Assert.Equal(max, deserialized);

        // UTC time
        var utc = DateTimeOffset.UtcNow;
        serialized = Serializer.Serialize(utc);
        deserialized = Serializer.Deserialize<DateTimeOffset>(serialized);
        Assert.Equal(utc, deserialized);

        // Specific date with offset
        var specific = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.FromHours(-5));
        serialized = Serializer.Serialize(specific);
        deserialized = Serializer.Deserialize<DateTimeOffset>(serialized);
        Assert.Equal(specific, deserialized);

        // Different timezone offset
        var withOffset = new DateTimeOffset(2024, 6, 15, 10, 30, 45, TimeSpan.FromHours(8));
        serialized = Serializer.Serialize(withOffset);
        deserialized = Serializer.Deserialize<DateTimeOffset>(serialized);
        Assert.Equal(withOffset, deserialized);
    }

    [Fact]
    public void TestUri()
    {
        // HTTP URL
        var http = new Uri("http://example.com");
        var serialized = Serializer.Serialize(http);
        var deserialized = Serializer.Deserialize<Uri>(serialized);
        Assert.Equal(http, deserialized);

        // HTTPS URL with path
        var https = new Uri("https://example.com/path/to/resource");
        serialized = Serializer.Serialize(https);
        deserialized = Serializer.Deserialize<Uri>(serialized);
        Assert.Equal(https, deserialized);

        // URL with query string
        var withQuery = new Uri("https://example.com/search?q=test&page=1");
        serialized = Serializer.Serialize(withQuery);
        deserialized = Serializer.Deserialize<Uri>(serialized);
        Assert.Equal(withQuery, deserialized);

        // File URI
        var file = new Uri("file:///C:/Users/test/file.txt");
        serialized = Serializer.Serialize(file);
        deserialized = Serializer.Deserialize<Uri>(serialized);
        Assert.Equal(file, deserialized);

        // Relative URI
        var relative = new Uri("/relative/path", UriKind.Relative);
        serialized = Serializer.Serialize(relative);
        deserialized = Serializer.Deserialize<Uri>(serialized);
        Assert.Equal(relative, deserialized);
    }

    [Fact]
    public void TestVersion()
    {
        // Two component version
        var twoComponent = new Version(1, 2);
        var serialized = Serializer.Serialize(twoComponent);
        var deserialized = Serializer.Deserialize<Version>(serialized);
        Assert.Equal(twoComponent, deserialized);

        // Three component version
        var threeComponent = new Version(1, 2, 3);
        serialized = Serializer.Serialize(threeComponent);
        deserialized = Serializer.Deserialize<Version>(serialized);
        Assert.Equal(threeComponent, deserialized);

        // Four component version
        var fourComponent = new Version(1, 2, 3, 4);
        serialized = Serializer.Serialize(fourComponent);
        deserialized = Serializer.Deserialize<Version>(serialized);
        Assert.Equal(fourComponent, deserialized);

        // Parse from string
        var parsed = Version.Parse("5.4.3.2");
        serialized = Serializer.Serialize(parsed);
        deserialized = Serializer.Deserialize<Version>(serialized);
        Assert.Equal(parsed, deserialized);
    }

    [Fact]
    public void TestTupleSerialization()
    {
        // ValueTuple with 2 elements
        var tuple2 = (42, "Hello");
        var serialized = Serializer.Serialize(tuple2);
        var deserialized = Serializer.Deserialize<(int, string)>(serialized);
        Assert.Equal(tuple2, deserialized);

        // ValueTuple with 3 elements
        var tuple3 = (1, "Two", 3.0f);
        serialized = Serializer.Serialize(tuple3);
        var deserialized3 = Serializer.Deserialize<(int, string, float)>(serialized);
        Assert.Equal(tuple3, deserialized3);

        // ValueTuple with 4 elements
        var tuple4 = (1, 2.0, "three", true);
        serialized = Serializer.Serialize(tuple4);
        var deserialized4 = Serializer.Deserialize<(int, double, string, bool)>(serialized);
        Assert.Equal(tuple4, deserialized4);

        // Nested tuple
        var nested = ((1, 2), ("a", "b"));
        serialized = Serializer.Serialize(nested);
        var deserializedNested = Serializer.Deserialize<((int, int), (string, string))>(serialized);
        Assert.Equal(nested, deserializedNested);

        // Classic Tuple
        var classicTuple = Tuple.Create(10, "Test");
        serialized = Serializer.Serialize(classicTuple);
        var deserializedClassic = Serializer.Deserialize<Tuple<int, string>>(serialized);
        Assert.Equal(classicTuple.Item1, deserializedClassic.Item1);
        Assert.Equal(classicTuple.Item2, deserializedClassic.Item2);
    }

    [Fact]
    public void TestAnonymousTypes()
    {
        // Simple anonymous type
        var simple = new { Name = "John", Age = 30 };
        var serialized = Serializer.Serialize(simple);

        // We can deserialize to a compound and check the values
        Assert.Equal(EchoType.Compound, serialized.TagType);
        Assert.True(serialized.TryGet("Name", out var nameTag));
        Assert.Equal("John", nameTag.StringValue);
        Assert.True(serialized.TryGet("Age", out var ageTag));
        Assert.Equal(30, ageTag.IntValue);

        // Nested anonymous type
        var nested = new
        {
            Person = new { Name = "Jane", Age = 25 },
            City = "New York",
            Timestamp = DateTime.Now
        };
        serialized = Serializer.Serialize(nested);
        Assert.Equal(EchoType.Compound, serialized.TagType);
        Assert.True(serialized.TryGet("Person", out var personTag));
        Assert.True(serialized.TryGet("City", out var cityTag));
        Assert.Equal("New York", cityTag.StringValue);

        // Anonymous type with collections
        var withCollections = new
        {
            Numbers = new List<int> { 1, 2, 3 },
            Names = new[] { "A", "B", "C" },
            Count = 42
        };
        serialized = Serializer.Serialize(withCollections);
        Assert.True(serialized.TryGet("Numbers", out var numbersTag));
        Assert.True(serialized.TryGet("Count", out var countTag));
        Assert.Equal(42, countTag.IntValue);
    }

    [Fact]
    public void TestComplexNestedObjectArrays()
    {
        // Create a complex nested structure: object[] containing object[] containing various int collections
        var complexArray = new object[]
        {
            // First inner array - primitives and basic collections
            new object[]
            {
                42,                                          // Plain int
                new List<int> { 1, 2, 3, 4, 5 },            // List<int>
                new LinkedList<int>(new[] { 10, 20, 30 }),  // LinkedList<int>
            },

            // Second inner array - more collection types
            new object[]
            {
                new Queue<int>(new[] { 100, 200, 300 }),    // Queue<int>
                new HashSet<int> { 5, 10, 15, 20 },         // HashSet<int>
                999,                                         // Another plain int
            },

            // Third inner array - mixed types
            new object[]
            {
                new List<int> { 7, 8, 9 },
                123,
                new HashSet<int> { 1, 2, 3 },
                new LinkedList<int>(new[] { 50, 60 }),
            },

            // Fourth inner array - edge cases
            new object[]
            {
                new List<int>(),                            // Empty list
                new Queue<int>(),                           // Empty queue
                0,                                          // Zero
                new HashSet<int> { 42 },                   // Single element
            }
        };

        // Serialize
        var serialized = Serializer.Serialize(complexArray);

        // Deserialize
        var deserialized = Serializer.Deserialize<object[]>(serialized);

        // Verify structure
        Assert.NotNull(deserialized);
        Assert.Equal(4, deserialized.Length);

        // Verify first inner array
        var firstInner = deserialized[0] as object[];
        Assert.NotNull(firstInner);
        Assert.Equal(3, firstInner.Length);
        Assert.Equal(42, firstInner[0]);
        var list1 = firstInner[1] as List<int>;
        Assert.NotNull(list1);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, list1);
        var linkedList1 = firstInner[2] as LinkedList<int>;
        Assert.NotNull(linkedList1);
        Assert.Equal(new[] { 10, 20, 30 }, linkedList1);

        // Verify second inner array
        var secondInner = deserialized[1] as object[];
        Assert.NotNull(secondInner);
        Assert.Equal(3, secondInner.Length);
        var queue = secondInner[0] as Queue<int>;
        Assert.NotNull(queue);
        Assert.Equal(new[] { 100, 200, 300 }, queue);
        var hashSet = secondInner[1] as HashSet<int>;
        Assert.NotNull(hashSet);
        Assert.True(hashSet.SetEquals(new[] { 5, 10, 15, 20 }));
        Assert.Equal(999, secondInner[2]);

        // Verify third inner array
        var thirdInner = deserialized[2] as object[];
        Assert.NotNull(thirdInner);
        Assert.Equal(4, thirdInner.Length);

        // Verify fourth inner array (edge cases)
        var fourthInner = deserialized[3] as object[];
        Assert.NotNull(fourthInner);
        Assert.Equal(4, fourthInner.Length);
        var emptyList = fourthInner[0] as List<int>;
        Assert.NotNull(emptyList);
        Assert.Empty(emptyList);
        var emptyQueue = fourthInner[1] as Queue<int>;
        Assert.NotNull(emptyQueue);
        Assert.Empty(emptyQueue);
        Assert.Equal(0, fourthInner[2]);
        var singleHashSet = fourthInner[3] as HashSet<int>;
        Assert.NotNull(singleHashSet);
        Assert.Single(singleHashSet);
        Assert.Contains(42, singleHashSet);
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

    [Fact]
    public void TestSerializeFieldOnPrivate()
    {
        var original = new ObjectWithAttributes();
        var serialized = Serializer.Serialize(original);
        Assert.True(serialized.Tags.ContainsKey("privateField"));
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
        object obj = new CustomSerializableObject { Value = 100, Text = "Test" };

        // Act
        var result = Serializer.Serialize(typeof(object), obj);

        // Assert
        Assert.True(result.TryGet("$type", out var typeTag));
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
        Assert.True(result.Get("NestedB").TryGet("$type", out _)); // Nested type does not match base type
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

    // Test GameObject structure
    [Fact]
    public void TestGameObjectStructure()
    {
        var gameObject = new GameObject { Name = "Test" };
        gameObject.Components.Add(new Component { Name = "Component", Value = 42 });
        var serialized = Serializer.Serialize(gameObject);
        var deserialized = Serializer.Deserialize<GameObject>(serialized);
        Assert.Equal(gameObject.Name, deserialized.Name);
        Assert.Single(deserialized.Components);
        Assert.Equal(gameObject.Components[0].Name, deserialized.Components[0].Name);
        Assert.Equal(typeof(Component), deserialized.Components[0].GetType());
    }

    [Fact]
    public void TestFixedAttribute()
    {
        var original = new NetworkPosition { X = 1.0f, Y = 2.0f, Z = 3.0f };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<NetworkPosition>(serialized);
        Assert.Equal(original.X, deserialized.X);
        Assert.Equal(original.Y, deserialized.Y);
        Assert.Equal(original.Z, deserialized.Z);

        // Convert to binary
        var stream = new MemoryStream();
        using var bw = new BinaryWriter(stream);
        serialized.WriteToBinary(bw, new BinarySerializationOptions() { EncodingMode = BinaryEncodingMode.Size });
        bw.Flush();

        int size = (int)stream.Length;

        stream.Position = 0;
        using var br = new BinaryReader(stream);
        var deserialized2 = Prowl.Echo.EchoObject.ReadFromBinary(br, new BinarySerializationOptions() { EncodingMode = BinaryEncodingMode.Size });
        var clone = (NetworkPosition)Prowl.Echo.Serializer.Deserialize(deserialized2, typeof(NetworkPosition));
        Assert.Equal(original.X, clone.X);
        Assert.Equal(original.Y, clone.Y);
        Assert.Equal(original.Z, clone.Z);
    }

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
    public void TestDiamondCircularReferences()
    {
        // Create a diamond-shaped reference pattern
        var root = new NodeWithMultipleRefs { Name = "Root" };
        var left = new NodeWithMultipleRefs { Name = "Left", Parent = root };
        var right = new NodeWithMultipleRefs { Name = "Right", Parent = root };
        var bottom = new NodeWithMultipleRefs { Name = "Bottom" };

        root.Left = left;
        root.Right = right;
        left.Right = bottom;
        right.Left = bottom;
        bottom.Parent = root;  // Complete the circle

        var serialized = Serializer.Serialize(root);
        var deserialized = Serializer.Deserialize<NodeWithMultipleRefs>(serialized);

        // Verify structure
        Assert.NotNull(deserialized);
        Assert.Equal("Root", deserialized.Name);
        Assert.Equal("Left", deserialized.Left.Name);
        Assert.Equal("Right", deserialized.Right.Name);

        // Verify object identity is preserved
        Assert.Same(deserialized.Left.Right, deserialized.Right.Left); // Bottom node should be the same instance
        Assert.Same(deserialized, deserialized.Left.Parent); // Parent references should point to root
        Assert.Same(deserialized, deserialized.Right.Parent);
        Assert.Same(deserialized, deserialized.Left.Right.Parent); // Bottom's parent should be root
    }

    [Fact]
    public void TestDelayedCircularReference()
    {
        // This test creates a situation where the circular reference isn't
        // encountered immediately during serialization
        var list = new List<NodeWithMultipleRefs>();
        var first = new NodeWithMultipleRefs { Name = "First" };
        var second = new NodeWithMultipleRefs { Name = "Second" };
        var third = new NodeWithMultipleRefs { Name = "Third" };

        list.Add(first);
        list.Add(second);
        list.Add(third);

        // Create circular references after adding to list
        first.Right = second;
        second.Right = third;
        third.Right = first; // Complete the circle

        var serialized = Serializer.Serialize(list);
        var deserialized = Serializer.Deserialize<List<NodeWithMultipleRefs>>(serialized);

        // Verify structure
        Assert.Equal(3, deserialized.Count);
        Assert.Equal("First", deserialized[0].Name);
        Assert.Equal("Second", deserialized[1].Name);
        Assert.Equal("Third", deserialized[2].Name);

        // Verify circular references
        Assert.Same(deserialized[1], deserialized[0].Right);
        Assert.Same(deserialized[2], deserialized[1].Right);
        Assert.Same(deserialized[0], deserialized[2].Right);
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
        original.Position = new Vector3 { X = 5, Y = 6, Z = 7 };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ConcreteClass>(serialized);
        Assert.Equal(((ConcreteClass)original).Value, deserialized.Value);
        Assert.Equal(original.Position.X, deserialized.Position.X);
        Assert.Equal(original.Position.Y, deserialized.Position.Y);
        Assert.Equal(original.Position.Z, deserialized.Position.Z);
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
    public void TestNullableTimeSpan()
    {
        // Non-null value
        TimeSpan? original = TimeSpan.FromHours(2.5);
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<TimeSpan?>(serialized);
        Assert.Equal(original, deserialized);

        // Null value
        TimeSpan? nullValue = null;
        serialized = Serializer.Serialize(nullValue);
        deserialized = Serializer.Deserialize<TimeSpan?>(serialized);
        Assert.Null(deserialized);
    }

    [Fact]
    public void TestNullableDateTimeOffset()
    {
        // Non-null value
        DateTimeOffset? original = DateTimeOffset.Now;
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<DateTimeOffset?>(serialized);
        Assert.Equal(original, deserialized);

        // Null value
        DateTimeOffset? nullValue = null;
        serialized = Serializer.Serialize(nullValue);
        deserialized = Serializer.Deserialize<DateTimeOffset?>(serialized);
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

    #region PolyMorphic Deserialize
    
    #region Test Models
    public interface IAnimal {  }

    public class Dog : IAnimal
    {
        public string Species;
        public string Breed;
    }

    public class Person
    {
        [FormerlySerializedAs("FULLNAME")]
        public string Name;

        public int Age;
    }
    #endregion

    [Fact]
    public void Deserialize_ShouldUseActualTypeFromTypeTag()
    {
        // Arrange
        var original = new Dog { Species = "Canine", Breed = "Golden Retriever" };

        // Serialize with type information
        var context = new SerializationContext(TypeMode.Aggressive);
        var serialized = Serializer.Serialize(original, context);

        // Act - Deserialize as base type
        var deserialized = Serializer.Deserialize<IAnimal>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.IsType<Dog>(deserialized);
        Assert.Equal("Golden Retriever", ((Dog)deserialized).Breed);
    }

    [Fact]
    public void Deserialize_ShouldHandleCaseInsensitiveFieldNames()
    {
        // Arrange
        var original = new Person { Name = "John", Age = 30 };
        var serialized = EchoObject.NewCompound();

        // Use different casing and former name
        serialized["NAME"] = new EchoObject(EchoType.String, "John"); // Lowercase
        serialized["AGE"] = new EchoObject(EchoType.Int, 30); // Uppercase

        // Act
        var deserialized = Serializer.Deserialize<Person>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("John", deserialized.Name);
        Assert.Equal(30, deserialized.Age);
    }

    [Fact]
    public void Deserialize_ShouldHandleFormerlySerializedNames()
    {
        // Arrange
        var serialized = EchoObject.NewCompound();

        // Use old field name from FormerlySerializedAs attribute
        serialized["FULLNAME"] = new EchoObject(EchoType.String, "Sarah");
        serialized["Age"] = new EchoObject(EchoType.Int, 28);

        // Act
        var deserialized = Serializer.Deserialize<Person>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("Sarah", deserialized.Name);
        Assert.Equal(28, deserialized.Age);
    }

    [Fact]
    public void Deserialize_ShouldUseActualTypeFormatterWhenAvailable()
    {
        // Arrange
        var original = new Dog { Species = "Canine", Breed = "Shiba Inu" };

        // Register a custom format handler for Dog
        Serializer.RegisterFormat(new DogFormat());

        var serialized = Serializer.Serialize(original, new SerializationContext(TypeMode.Aggressive));

        // Act
        var deserialized = Serializer.Deserialize<IAnimal>(serialized);

        // Assert
        Assert.IsType<Dog>(deserialized);
        Assert.Equal("Processed: Shiba Inu", ((Dog)deserialized).Breed);
    }

    // Custom format handler for Dog to test actual type handling
    private class DogFormat : ISerializationFormat
    {
        public bool CanHandle(Type type) => type == typeof(Dog);

        public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
        {
            var dog = (Dog)value;
            var compound = EchoObject.NewCompound();
            compound["Breed"] = new EchoObject(EchoType.String, dog.Breed);
            return compound;
        }

        public object Deserialize(EchoObject value, Type targetType, SerializationContext context)
        {
            return new Dog {
                Breed = "Processed: " + value["Breed"].StringValue,
                Species = "Canine"
            };
        }
    }

    #endregion

}
