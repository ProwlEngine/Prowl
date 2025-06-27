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
            DateTime.UtcNow,
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
        var datetime = original.ComplexObjectArray[2];
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ClassWithObjectArrays>(serialized);

        Assert.NotNull(deserialized.ComplexObjectArray);
        Assert.Equal(4, deserialized.ComplexObjectArray.Length);

        Assert.IsType<SimpleObject>(deserialized.ComplexObjectArray[0]);
        Assert.Equal("Hello", ((SimpleObject)deserialized.ComplexObjectArray[0]).StringField);

        Assert.IsType<Vector3>(deserialized.ComplexObjectArray[1]);
        Assert.Equal(1, ((Vector3)deserialized.ComplexObjectArray[1]).X);
        Assert.Equal(2, ((Vector3)deserialized.ComplexObjectArray[1]).Y);
        Assert.Equal(3, ((Vector3)deserialized.ComplexObjectArray[1]).Z);

        Assert.Equal(datetime, deserialized.ComplexObjectArray[2]);

        Assert.Equal("mixed", deserialized.ComplexObjectArray[3]);
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

    #region Multi Dimensional

    [Fact]
    public void TestSimpleMultiDimensionalObjectArray()
    {
        // 2D object array with primitive values
        var original = new object[,]
        {
            { 1, "two", 3.14f },
            { true, 42, "test" }
        };

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<object[,]>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.GetLength(0));
        Assert.Equal(3, deserialized.GetLength(1));

        Assert.Equal(1, deserialized[0, 0]);
        Assert.Equal("two", deserialized[0, 1]);
        Assert.Equal(3.14f, deserialized[0, 2]);
        Assert.Equal(true, deserialized[1, 0]);
        Assert.Equal(42, deserialized[1, 1]);
        Assert.Equal("test", deserialized[1, 2]);
    }

    [Fact]
    public void TestMultiDimensionalObjectArrayWithComplexTypes()
    {
        // 2D object array with complex objects
        var original = new object[2, 2];
        original[0, 0] = new SimpleObject { StringField = "Object_0_0" };
        original[0, 1] = new Vector3 { X = 1, Y = 2, Z = 3 };
        original[1, 0] = new CircularObject { Name = "Circular" };
        original[1, 1] = new SimpleInheritedObject { InheritedField = "Inherited" };

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<object[,]>(serialized);

        Assert.NotNull(deserialized);

        var obj00 = deserialized[0, 0] as SimpleObject;
        Assert.NotNull(obj00);
        Assert.Equal("Object_0_0", obj00.StringField);

        var vec01 = deserialized[0, 1] as Vector3?;
        Assert.NotNull(vec01);
        Assert.Equal(1, vec01.Value.X);
        Assert.Equal(2, vec01.Value.Y);
        Assert.Equal(3, vec01.Value.Z);

        var circ10 = deserialized[1, 0] as CircularObject;
        Assert.NotNull(circ10);
        Assert.Equal("Circular", circ10.Name);

        var inh11 = deserialized[1, 1] as SimpleInheritedObject;
        Assert.NotNull(inh11);
        Assert.Equal("Inherited", inh11.InheritedField);
    }

    [Fact]
    public void TestMultiDimensionalObjectArrayWithNulls()
    {
        // 2D object array with null values
        var original = new object[2, 2];
        original[0, 0] = "not null";
        original[0, 1] = null;
        original[1, 0] = null;
        original[1, 1] = 42;

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<object[,]>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal("not null", deserialized[0, 0]);
        Assert.Null(deserialized[0, 1]);
        Assert.Null(deserialized[1, 0]);
        Assert.Equal(42, deserialized[1, 1]);
    }

    [Fact]
    public void TestThreeDimensionalObjectArray()
    {
        // 3D object array
        var original = new object[2, 2, 2];
        original[0, 0, 0] = 1;
        original[0, 0, 1] = "string";
        original[0, 1, 0] = true;
        original[0, 1, 1] = 3.14f;
        original[1, 0, 0] = new SimpleObject();
        original[1, 0, 1] = new Vector3 { X = 1, Y = 2, Z = 3 };
        original[1, 1, 0] = null;
        original[1, 1, 1] = DateTime.Now.Date;

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<object[,,]>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.GetLength(0));
        Assert.Equal(2, deserialized.GetLength(1));
        Assert.Equal(2, deserialized.GetLength(2));

        Assert.Equal(1, deserialized[0, 0, 0]);
        Assert.Equal("string", deserialized[0, 0, 1]);
        Assert.Equal(true, deserialized[0, 1, 0]);
        Assert.Equal(3.14f, deserialized[0, 1, 1]);

        Assert.IsType<SimpleObject>(deserialized[1, 0, 0]);
        Assert.IsType<Vector3>(deserialized[1, 0, 1]);

        Assert.Null(deserialized[1, 1, 0]);
        Assert.IsType<DateTime>(deserialized[1, 1, 1]);
    }

    [Fact]
    public void TestMultiDimensionalObjectArrayWithTypedArrays()
    {
        // 2D object array containing typed arrays
        var original = new object[2, 2];
        original[0, 0] = new int[] { 1, 2, 3 };
        original[0, 1] = new string[] { "a", "b", "c" };
        original[1, 0] = new SimpleObject[] { new SimpleObject(), new SimpleObject { StringField = "Modified" } };
        original[1, 1] = new object[] { 1, "mixed", true };

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<object[,]>(serialized);

        Assert.NotNull(deserialized);

        // Check int array
        var intArray = deserialized[0, 0] as int[];
        Assert.NotNull(intArray);
        Assert.Equal(3, intArray.Length);
        Assert.Equal(1, intArray[0]);
        Assert.Equal(2, intArray[1]);
        Assert.Equal(3, intArray[2]);

        // Check string array
        var stringArray = deserialized[0, 1] as string[];
        Assert.NotNull(stringArray);
        Assert.Equal(3, stringArray.Length);
        Assert.Equal("a", stringArray[0]);
        Assert.Equal("b", stringArray[1]);
        Assert.Equal("c", stringArray[2]);

        // Check object array
        var objectArray = deserialized[1, 0] as SimpleObject[];
        Assert.NotNull(objectArray);
        Assert.Equal(2, objectArray.Length);
        Assert.Equal("Hello", objectArray[0].StringField);
        Assert.Equal("Modified", objectArray[1].StringField);

        // Check mixed object array
        var mixedArray = deserialized[1, 1] as object[];
        Assert.NotNull(mixedArray);
        Assert.Equal(3, mixedArray.Length);
        Assert.Equal(1, mixedArray[0]);
        Assert.Equal("mixed", mixedArray[1]);
        Assert.Equal(true, mixedArray[2]);
    }

    [Fact]
    public void TestMixedMultiDimensionalAndJaggedObjectArrays()
    {
        // Object array containing both multidimensional and jagged arrays
        var original = new object[2, 2];
        original[0, 0] = new int[,] { { 1, 2 }, { 3, 4 } };  // 2D int array
        original[0, 1] = new string[][] { new[] { "a", "b" }, new[] { "c", "d" } };  // Jagged string array
        original[1, 0] = new object[] { 1, "two", 3.0 };  // 1D object array
        original[1, 1] = new object[,] { { "nested", 42 }, { true, 3.14 } };  // 2D object array

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<object[,]>(serialized);

        Assert.NotNull(deserialized);

        // Check 2D int array
        var intArray = deserialized[0, 0] as int[,];
        Assert.NotNull(intArray);
        Assert.Equal(2, intArray.GetLength(0));
        Assert.Equal(2, intArray.GetLength(1));
        Assert.Equal(1, intArray[0, 0]);
        Assert.Equal(2, intArray[0, 1]);
        Assert.Equal(3, intArray[1, 0]);
        Assert.Equal(4, intArray[1, 1]);

        // Check jagged string array
        var stringArray = deserialized[0, 1] as string[][];
        Assert.NotNull(stringArray);
        Assert.Equal(2, stringArray.Length);
        Assert.Equal(2, stringArray[0].Length);
        Assert.Equal(2, stringArray[1].Length);
        Assert.Equal("a", stringArray[0][0]);
        Assert.Equal("b", stringArray[0][1]);
        Assert.Equal("c", stringArray[1][0]);
        Assert.Equal("d", stringArray[1][1]);

        // Check 1D object array
        var objectArray = deserialized[1, 0] as object[];
        Assert.NotNull(objectArray);
        Assert.Equal(3, objectArray.Length);
        Assert.Equal(1, objectArray[0]);
        Assert.Equal("two", objectArray[1]);
        Assert.Equal(3.0, objectArray[2]);

        // Check 2D object array
        var nestedArray = deserialized[1, 1] as object[,];
        Assert.NotNull(nestedArray);
        Assert.Equal(2, nestedArray.GetLength(0));
        Assert.Equal(2, nestedArray.GetLength(1));
        Assert.Equal("nested", nestedArray[0, 0]);
        Assert.Equal(42, nestedArray[0, 1]);
        Assert.Equal(true, nestedArray[1, 0]);
        Assert.Equal(3.14, nestedArray[1, 1]);
    }

    [Fact]
    public void TestEmptyMultiDimensionalObjectArray()
    {
        // Empty 2D object array
        var original = new object[0, 0];

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<object[,]>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.GetLength(0));
        Assert.Equal(0, deserialized.GetLength(1));
    }

    [Fact]
    public void TestAsymmetricMultiDimensionalObjectArray()
    {
        // 2D object array with different dimensions
        var original = new object[3, 2];
        original[0, 0] = 1;
        original[0, 1] = 2;
        original[1, 0] = "three";
        original[1, 1] = "four";
        original[2, 0] = true;
        original[2, 1] = false;

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<object[,]>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.GetLength(0));
        Assert.Equal(2, deserialized.GetLength(1));

        Assert.Equal(1, deserialized[0, 0]);
        Assert.Equal(2, deserialized[0, 1]);
        Assert.Equal("three", deserialized[1, 0]);
        Assert.Equal("four", deserialized[1, 1]);
        Assert.Equal(true, deserialized[2, 0]);
        Assert.Equal(false, deserialized[2, 1]);
    }

    public class ClassWithMultiDimensionalObjectArrays
    {
        public object[,] TwoDimensionalArray = new object[2, 2] {
            { 1, "two" },
            { true, 3.14 }
        };

        public object[,,] ThreeDimensionalArray = new object[2, 1, 2] {
            { { "a", "b" } },
            { { "c", "d" } }
        };

        public object[,] NullArray = null;
    }

    [Fact]
    public void TestClassWithMultiDimensionalObjectArrays()
    {
        var original = new ClassWithMultiDimensionalObjectArrays();
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<ClassWithMultiDimensionalObjectArrays>(serialized);

        Assert.NotNull(deserialized);

        // Check 2D array
        Assert.NotNull(deserialized.TwoDimensionalArray);
        Assert.Equal(2, deserialized.TwoDimensionalArray.GetLength(0));
        Assert.Equal(2, deserialized.TwoDimensionalArray.GetLength(1));
        Assert.Equal(1, deserialized.TwoDimensionalArray[0, 0]);
        Assert.Equal("two", deserialized.TwoDimensionalArray[0, 1]);
        Assert.Equal(true, deserialized.TwoDimensionalArray[1, 0]);
        Assert.Equal(3.14, deserialized.TwoDimensionalArray[1, 1]);

        // Check 3D array
        Assert.NotNull(deserialized.ThreeDimensionalArray);
        Assert.Equal(2, deserialized.ThreeDimensionalArray.GetLength(0));
        Assert.Equal(1, deserialized.ThreeDimensionalArray.GetLength(1));
        Assert.Equal(2, deserialized.ThreeDimensionalArray.GetLength(2));
        Assert.Equal("a", deserialized.ThreeDimensionalArray[0, 0, 0]);
        Assert.Equal("b", deserialized.ThreeDimensionalArray[0, 0, 1]);
        Assert.Equal("c", deserialized.ThreeDimensionalArray[1, 0, 0]);
        Assert.Equal("d", deserialized.ThreeDimensionalArray[1, 0, 1]);

        // Check null array
        Assert.Null(deserialized.NullArray);
    }

    #endregion
}