using Tests.Types;

namespace Prowl.Echo.Test;

public class ObjectArrayTests
{
    public class ClassWithObjectArrays
    {
        public object[] SimpleObjectArray = new object[] { 1, "two", 3.14f };

        public object[] EmptyObjectArray = new object[0];

        public object[] NullValuesArray = new object[] { "value", null, 42 };

        public object[] ComplexObjectArray = new object[] {
            new SimpleObject(),
            new Vector3 { X = 1, Y = 2, Z = 3 },
            "mixed"
        };

        public object[][] NestedObjectArrays = new object[][] {
            new object[] { 1, 2 },
            new object[] { "a", "b", "c" }
        };

        public object[] CircularObjectArray;

        public ClassWithObjectArrays()
        {
            // Create circular reference in one array
            var circular = new CircularObject { Name = "CircularInArray" };
            circular.Child = circular;
            CircularObjectArray = new object[] { circular, "other" };
        }
    }

    [Fact]
    public void TestSimpleObjectArray()
    {
        var original = new ClassWithObjectArrays();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ClassWithObjectArrays>(serialized);

        Assert.NotNull(deserialized.SimpleObjectArray);
        Assert.Equal(3, deserialized.SimpleObjectArray.Length);
        Assert.Equal(1, deserialized.SimpleObjectArray[0]);
        Assert.Equal("two", deserialized.SimpleObjectArray[1]);
        Assert.Equal(3.14f, deserialized.SimpleObjectArray[2]);
    }

    [Fact]
    public void TestEmptyObjectArray()
    {
        var original = new ClassWithObjectArrays();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ClassWithObjectArrays>(serialized);

        Assert.NotNull(deserialized.EmptyObjectArray);
        Assert.Empty(deserialized.EmptyObjectArray);
    }

    [Fact]
    public void TestNullValuesInObjectArray()
    {
        var original = new ClassWithObjectArrays();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ClassWithObjectArrays>(serialized);

        Assert.NotNull(deserialized.NullValuesArray);
        Assert.Equal(3, deserialized.NullValuesArray.Length);
        Assert.Equal("value", deserialized.NullValuesArray[0]);
        Assert.Null(deserialized.NullValuesArray[1]);
        Assert.Equal(42, deserialized.NullValuesArray[2]);
    }

    [Fact]
    public void TestComplexObjectArray()
    {
        var original = new ClassWithObjectArrays();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ClassWithObjectArrays>(serialized);

        Assert.NotNull(deserialized.ComplexObjectArray);
        Assert.Equal(3, deserialized.ComplexObjectArray.Length);

        Assert.IsType<SimpleObject>(deserialized.ComplexObjectArray[0]);
        Assert.Equal("Hello", ((SimpleObject)deserialized.ComplexObjectArray[0]).StringField);

        Assert.IsType<Vector3>(deserialized.ComplexObjectArray[1]);
        Assert.Equal(1, ((Vector3)deserialized.ComplexObjectArray[1]).X);
        Assert.Equal(2, ((Vector3)deserialized.ComplexObjectArray[1]).Y);
        Assert.Equal(3, ((Vector3)deserialized.ComplexObjectArray[1]).Z);

        Assert.Equal("mixed", deserialized.ComplexObjectArray[2]);
    }

    [Fact]
    public void TestNestedObjectArrays()
    {
        var original = new ClassWithObjectArrays();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ClassWithObjectArrays>(serialized);

        Assert.NotNull(deserialized.NestedObjectArrays);
        Assert.Equal(2, deserialized.NestedObjectArrays.Length);

        Assert.Equal(2, deserialized.NestedObjectArrays[0].Length);
        Assert.Equal(1, deserialized.NestedObjectArrays[0][0]);
        Assert.Equal(2, deserialized.NestedObjectArrays[0][1]);

        Assert.Equal(3, deserialized.NestedObjectArrays[1].Length);
        Assert.Equal("a", deserialized.NestedObjectArrays[1][0]);
        Assert.Equal("b", deserialized.NestedObjectArrays[1][1]);
        Assert.Equal("c", deserialized.NestedObjectArrays[1][2]);
    }

    [Fact]
    public void TestCircularObjectArrays()
    {
        var original = new ClassWithObjectArrays();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ClassWithObjectArrays>(serialized);

        Assert.NotNull(deserialized.CircularObjectArray);
        Assert.Equal(2, deserialized.CircularObjectArray.Length);

        var circular = deserialized.CircularObjectArray[0] as CircularObject;
        Assert.NotNull(circular);
        Assert.Equal("CircularInArray", circular.Name);
        Assert.Same(circular, circular.Child); // Should maintain the circular reference

        Assert.Equal("other", deserialized.CircularObjectArray[1]);
    }

    [Fact]
    public void TestDirectObjectArray()
    {
        // Test serializing an object[] directly rather than as a field
        object[] original = new object[] { 1, "string", true, new SimpleObject() };

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<object[]>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(4, deserialized.Length);
        Assert.Equal(1, deserialized[0]);
        Assert.Equal("string", deserialized[1]);
        Assert.Equal(true, deserialized[2]);
        Assert.IsType<SimpleObject>(deserialized[3]);
    }

    [Fact]
    public void TestMixedTypedObjectArrays()
    {
        // Object array with a mix of primitive arrays and object arrays
        var mixed = new object[] {
            new int[] { 1, 2, 3 },
            new string[] { "a", "b" },
            new object[] { 1, "mixed", true }
        };

        var serialized = Serializer.Serialize(mixed);
        var deserialized = Serializer.Deserialize<object[]>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.Length);

        var intArray = deserialized[0] as int[];
        Assert.NotNull(intArray);
        Assert.Equal(3, intArray.Length);
        Assert.Equal(1, intArray[0]);
        Assert.Equal(2, intArray[1]);
        Assert.Equal(3, intArray[2]);

        var stringArray = deserialized[1] as string[];
        Assert.NotNull(stringArray);
        Assert.Equal(2, stringArray.Length);
        Assert.Equal("a", stringArray[0]);
        Assert.Equal("b", stringArray[1]);

        var nestedArray = deserialized[2] as object[];
        Assert.NotNull(nestedArray);
        Assert.Equal(3, nestedArray.Length);
        Assert.Equal(1, nestedArray[0]);
        Assert.Equal("mixed", nestedArray[1]);
        Assert.Equal(true, nestedArray[2]);
    }

    [Fact]
    public void TestObjectArrayWithTypeInfo()
    {
        // Class with an array field that would need type information preserved
        var original = new object[] {
            new SimpleObject(),
            new SimpleInheritedObject(),  // Derived type
            new ConcreteClass()           // Implementation of abstract class
        };

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<object[]>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.Length);

        Assert.IsType<SimpleObject>(deserialized[0]);
        Assert.IsType<SimpleInheritedObject>(deserialized[1]);
        Assert.IsType<ConcreteClass>(deserialized[2]);

        // Verify the inherited field is present in the second object
        var inheritedObj = deserialized[1] as SimpleInheritedObject;
        Assert.Equal("Inherited", inheritedObj.InheritedField);

        // Verify abstract base members are deserialized
        var concreteObj = deserialized[2] as ConcreteClass;
        Assert.Equal("Abstract", concreteObj.Name);
        Assert.Equal(42, concreteObj.Value);
    }

    [Fact]
    public void TestNullObjectArrayField()
    {
        // Test class with a null array field
        var classWithNullArray = new ClassWithNullArray();
        var serialized = Serializer.Serialize(classWithNullArray);
        var deserialized = Serializer.Deserialize<ClassWithNullArray>(serialized);

        Assert.Null(deserialized.NullArray);
    }

    public class ClassWithNullArray
    {
        public object[] NullArray = null;
    }

    [Fact]
    public void TestLargeObjectArray()
    {
        // Test with a large object array to check for any size-related issues
        object[] largeArray = new object[1000];
        for (int i = 0; i < largeArray.Length; i++)
        {
            largeArray[i] = i % 2 == 0 ? i : i.ToString();
        }

        var serialized = Serializer.Serialize(largeArray);
        var deserialized = Serializer.Deserialize<object[]>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(1000, deserialized.Length);

        for (int i = 0; i < largeArray.Length; i++)
        {
            if (i % 2 == 0)
                Assert.Equal(i, deserialized[i]);
            else
                Assert.Equal(i.ToString(), deserialized[i]);
        }
    }
}