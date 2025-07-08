using Prowl.Echo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Echo.Test
{
    public class PolymorphismType_Tests
    {
        #region Test Classes and Interfaces

        public interface IShape
        {
            double Area { get; }
        }

        public abstract class Shape : IShape
        {
            public abstract double Area { get; }
            public string Name = "Shape";
        }

        public class Circle : Shape
        {
            public double Radius = 5.0;
            public override double Area => Math.PI * Radius * Radius;
        }

        public class Rectangle : Shape
        {
            public double Width = 10.0;
            public double Height = 5.0;
            public override double Area => Width * Height;
        }

        public class Square : Rectangle
        {
            public Square() { Width = Height = 3.0; }
            public new double Size { get => Width; set => Width = Height = value; }
        }

        public interface IContainer<T>
        {
            T Value { get; set; }
        }

        public class Container<T> : IContainer<T>
        {
            [SerializeField]
            private T? _value = default!;
            public T Value { get => _value; set { _value = value; } }
            public string ContainerType = typeof(T).Name;
        }

        public class StringContainer : Container<string>
        {
            public bool IsEmpty => string.IsNullOrEmpty(Value);
        }

        public class NestedContainer
        {
            public IContainer<object> ObjectContainer = new Container<object>();
            public IContainer<string> StringContainer = new StringContainer();
            public Container<IShape> ShapeContainer = new Container<IShape>();
        }

        public class ComplexPolymorphicObject
        {
            public object PrimitiveAsObject = 42;
            public object StringAsObject = "Hello";
            public object NullAsObject = null!;
            public IShape ShapeAsInterface = new Circle();
            public Shape ShapeAsAbstract = new Rectangle();
            public Rectangle RectangleAsBase = new Square();
            public object[] MixedArray = { 1, "test", new Circle(), null! };
            public List<object> MixedList = new() { true, 3.14, new Rectangle() };
            public Dictionary<string, object> MixedDict = new() {
                ["int"] = 100,
                ["shape"] = new Circle(),
                ["nested"] = new Container<string> { Value = "nested" }
            };
        }

        public class GenericHost<T>
        {
            public T Value = default!;
            public object ValueAsObject = default!;
            public List<T> Values = new();
            public Dictionary<string, T> NamedValues = new();
        }

        public struct ValueTypeContainer
        {
            public object BoxedInt = 42;
            public IShape ShapeInStruct = new Circle();

            public ValueTypeContainer()
            {
            }
        }

        public class CircularReference
        {
            public string Name = "";
            public CircularReference? Child;
            public CircularReference? Parent;
            public object AsObject = null!;
        }

        public enum TestEnum { Value1, Value2, Value3 }

        public class EnumContainer
        {
            public TestEnum DirectEnum = TestEnum.Value1;
            public object EnumAsObject = TestEnum.Value2;
            public Enum EnumAsEnum = TestEnum.Value3;
        }

        public class NullableContainer
        {
            public int? NullableInt = 42;
            public int? NullInt = null;
            public object NullableAsObject = (int?)123;
            public object NullNullableAsObject = (int?)null;
        }

        public class ArrayContainer
        {
            public object[] ObjectArray = { 1, "test", new Circle() };
            public IShape[] ShapeArray = { new Circle(), new Rectangle() };
            public Shape[] AbstractArray = { new Circle(), new Square() };
            public object SingleObject = new int[3] { 1, 2, 3 };
            public object JaggedArray = new int[][] { new[] { 1, 2 }, new[] { 3, 4, 5 } };
            public object MultiDimArray = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
        }

        #endregion

        #region Basic Polymorphism Tests

        [Fact]
        public void BasicPolymorphism_ObjectField_PreservesType()
        {
            // Arrange
            var obj = new { Value = (object)42 };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            var valueField = serialized.Get("Value");
            Assert.NotNull(valueField);

            // Should have type info since int is in object field
            Assert.True(HasTypeInfo(valueField), "Int in object field should preserve type");
        }

        [Fact]
        public void BasicPolymorphism_InterfaceField_PreservesConcreteType()
        {
            // Arrange
            var obj = new { Shape = (IShape)new Circle { Radius = 10 } };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            var shapeField = serialized.Get("Shape");
            Assert.True(HasTypeInfo(shapeField), "Interface field should preserve concrete type");
        }

        [Fact]
        public void BasicPolymorphism_AbstractField_PreservesConcreteType()
        {
            // Arrange
            var obj = new { Shape = (Shape)new Rectangle { Width = 5, Height = 3 } };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            var shapeField = serialized.Get("Shape");
            Assert.True(HasTypeInfo(shapeField), "Abstract field should preserve concrete type");
        }

        [Fact]
        public void BasicPolymorphism_ExactTypeMatch_NoTypePreservation()
        {
            // Arrange
            var obj = new { Value = new Circle { Radius = 7 } };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            var valueField = serialized.Get("Value");
            Assert.False(HasTypeInfo(valueField), "Exact type match should not preserve type");
        }

        #endregion

        #region Inheritance Hierarchy Tests

        [Fact]
        public void InheritanceHierarchy_DeepInheritance_PreservesCorrectType()
        {
            // Arrange
            var square = new Square();
            var asRectangle = (Rectangle)square;
            var asShape = (Shape)square;
            var asObject = (object)square;

            var obj = new {
                AsSquare = square,
                AsRectangle = asRectangle,
                AsShape = asShape,
                AsObject = asObject
            };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            Assert.False(HasTypeInfo(serialized.Get("AsSquare")), "Exact type should not preserve type");
            Assert.True(HasTypeInfo(serialized.Get("AsRectangle")), "Base class field should preserve derived type");
            Assert.True(HasTypeInfo(serialized.Get("AsShape")), "Abstract class field should preserve concrete type");
            Assert.True(HasTypeInfo(serialized.Get("AsObject")), "Object field should preserve concrete type");
        }

        [Fact]
        public void InheritanceHierarchy_MultipleInterfaceImplementations()
        {
            // Arrange
            var shapes = new IShape[] { new Circle(), new Rectangle(), new Square() };
            var obj = new { Shapes = shapes };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            var shapesArray = serialized.Get("Shapes");
            Assert.NotNull(shapesArray);
            var dataArray = shapesArray.Get("array");
            Assert.NotNull(dataArray);

            // Each element should preserve its concrete type
            for (int i = 0; i < shapes.Length; i++)
            {
                var element = dataArray.Get(i);
                Assert.True(HasTypeInfo(element), $"Array element {i} should preserve type");
            }
        }

        #endregion

        #region Collection Polymorphism Tests

        [Fact]
        public void CollectionPolymorphism_ObjectArray_PreservesElementTypes()
        {
            // Arrange
            var array = new object[] { 42, "hello", 3.14f, true, new Circle(), null! };

            // Act
            var serialized = Serializer.Serialize(array, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<object[]>(serialized);

            // Assert
            Assert.Equal(array.Length, deserialized.Length);
            Assert.IsType<int>(deserialized[0]);
            Assert.IsType<string>(deserialized[1]);
            Assert.IsType<float>(deserialized[2]);
            Assert.IsType<bool>(deserialized[3]);
            Assert.IsType<Circle>(deserialized[4]);
            Assert.Null(deserialized[5]);

            Assert.Equal(42, deserialized[0]);
            Assert.Equal("hello", deserialized[1]);
            Assert.Equal(3.14f, deserialized[2]);
            Assert.Equal(true, deserialized[3]);
        }

        [Fact]
        public void CollectionPolymorphism_ListOfObjects_PreservesElementTypes()
        {
            // Arrange
            var list = new List<object> { 100, "world", new Rectangle(), DateTime.Now };

            // Act
            var serialized = Serializer.Serialize(list, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<List<object>>(serialized);

            // Assert
            Assert.Equal(list.Count, deserialized.Count);
            Assert.IsType<int>(deserialized[0]);
            Assert.IsType<string>(deserialized[1]);
            Assert.IsType<Rectangle>(deserialized[2]);
            Assert.IsType<DateTime>(deserialized[3]);
        }

        [Fact]
        public void CollectionPolymorphism_ListOfObjects_WithNull()
        {
            // Arrange
            List<IShape> items = new() { new Rectangle(), null, new Circle() };

            // Act
            var serialized = Serializer.Serialize(items, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<List<IShape>>(serialized);

            // Assert
            Assert.Equal(items.Count, deserialized.Count);
            Assert.IsType<Rectangle>(deserialized[0]);
            Assert.Equal(deserialized[1], null);
            Assert.IsType<Circle>(deserialized[2]);
        }

        [Fact]
        public void CollectionPolymorphism_DictionaryWithObjectValues_PreservesTypes()
        {
            // Arrange
            var dict = new Dictionary<string, object> {
                ["number"] = 123,
                ["text"] = "test",
                ["shape"] = new Circle { Radius = 2 },
                ["flag"] = false
            };

            // Act
            var serialized = Serializer.Serialize(dict, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<Dictionary<string, object>>(serialized);

            // Assert
            Assert.Equal(dict.Count, deserialized.Count);
            Assert.IsType<int>(deserialized["number"]);
            Assert.IsType<string>(deserialized["text"]);
            Assert.IsType<Circle>(deserialized["shape"]);
            Assert.IsType<bool>(deserialized["flag"]);

            Assert.Equal(123, deserialized["number"]);
            Assert.Equal("test", deserialized["text"]);
            Assert.Equal(2, ((Circle)deserialized["shape"]).Radius);
            Assert.Equal(false, deserialized["flag"]);
        }

        [Fact]
        public void CollectionPolymorphism_NestedCollections_PreservesTypes()
        {
            // Arrange
            var nested = new List<object[]>
            {
            new object[] { 1, "a" },
            new object[] { new Circle(), true },
            new object[] { null!, 3.14 }
        };

            // Act
            var serialized = Serializer.Serialize(nested, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<List<object[]>>(serialized);

            // Assert
            Assert.Equal(3, deserialized.Count);

            // First array
            Assert.IsType<int>(deserialized[0][0]);
            Assert.IsType<string>(deserialized[0][1]);

            // Second array
            Assert.IsType<Circle>(deserialized[1][0]);
            Assert.IsType<bool>(deserialized[1][1]);

            // Third array
            Assert.Null(deserialized[2][0]);
            Assert.IsType<double>(deserialized[2][1]);
        }

        #endregion

        #region Generic Type Tests

        [Fact]
        public void GenericTypes_ConcreteGeneric_NoTypePreservation()
        {
            // Arrange
            var container = new Container<string> { Value = "test" };
            var obj = new { Container = container };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            var containerField = serialized.Get("Container");
            Assert.False(HasTypeInfo(containerField), "Exact generic type should not preserve type");
        }

        [Fact]
        public void GenericTypes_GenericAsInterface_PreservesConcreteType()
        {
            // Arrange
            var container = (IContainer<string>)new StringContainer { Value = "test" };
            var obj = new { Container = container };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            var containerField = serialized.Get("Container");
            Assert.True(HasTypeInfo(containerField), "Interface field should preserve concrete generic type");
        }

        [Fact]
        public void GenericTypes_OpenGenericInObject_PreservesType()
        {
            // Arrange
            var container = (object)new Container<Circle> { Value = new Circle() };
            var obj = new { Container = container };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            var containerField = serialized.Get("Container");
            Assert.True(HasTypeInfo(containerField), "Generic type in object field should preserve type");
        }

        [Fact]
        public void GenericTypes_ComplexGenericHierarchy()
        {
            // Arrange
            var host = new GenericHost<IShape> {
                Value = new Circle(),
                ValueAsObject = new Rectangle(),
                Values = { new Circle(), new Square() },
                NamedValues = { ["circle"] = new Circle(), ["square"] = new Square() }
            };

            // Act
            var serialized = Serializer.Serialize(host, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<GenericHost<IShape>>(serialized);

            // Assert
            Assert.IsType<Circle>(deserialized.Value);
            Assert.IsType<Rectangle>(deserialized.ValueAsObject);
            Assert.Equal(2, deserialized.Values.Count);
            Assert.IsType<Circle>(deserialized.Values[0]);
            Assert.IsType<Square>(deserialized.Values[1]);
            Assert.IsType<Circle>(deserialized.NamedValues["circle"]);
            Assert.IsType<Square>(deserialized.NamedValues["square"]);
        }

        #endregion

        #region Value Type and Boxing Tests

        [Fact]
        public void ValueTypes_BoxedPrimitives_PreservesTypes()
        {
            // Arrange
            var obj = new {
                BoxedInt = (object)42,
                BoxedFloat = (object)3.14f,
                BoxedBool = (object)true,
                BoxedChar = (object)'A'
            };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            Assert.True(HasTypeInfo(serialized.Get("BoxedInt")));
            Assert.True(HasTypeInfo(serialized.Get("BoxedFloat")));
            Assert.True(HasTypeInfo(serialized.Get("BoxedBool")));
            Assert.True(HasTypeInfo(serialized.Get("BoxedChar")));
        }

        [Fact]
        public void ValueTypes_BoxedStructs_PreservesTypes()
        {
            // Arrange
            var container = new ValueTypeContainer();
            var obj = new { Container = (object)container };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            var containerField = serialized.Get("Container");
            Assert.True(HasTypeInfo(containerField), "Boxed struct should preserve type");
        }

        [Fact]
        public void ValueTypes_NullableTypes_PreservesCorrectly()
        {
            // Arrange
            var container = new NullableContainer();

            // Act
            var serialized = Serializer.Serialize(container, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<NullableContainer>(serialized);

            // Assert
            Assert.Equal(42, deserialized.NullableInt);
            Assert.Null(deserialized.NullInt);
            Assert.IsType<int>(deserialized.NullableAsObject);
            Assert.Equal(123, deserialized.NullableAsObject);
            Assert.Null(deserialized.NullNullableAsObject);
        }

        #endregion

        #region Enum Tests

        [Fact]
        public void Enums_EnumAsObject_PreservesType()
        {
            // Arrange
            var container = new EnumContainer();

            // Act
            var serialized = Serializer.Serialize(container, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<EnumContainer>(serialized);

            // Assert
            Assert.Equal(TestEnum.Value1, deserialized.DirectEnum);
            Assert.IsType<TestEnum>(deserialized.EnumAsObject);
            Assert.Equal(TestEnum.Value2, deserialized.EnumAsObject);
            Assert.IsType<TestEnum>(deserialized.EnumAsEnum);
            Assert.Equal(TestEnum.Value3, deserialized.EnumAsEnum);
        }

        #endregion

        #region Array Edge Cases

        [Fact]
        public void Arrays_MultiDimensionalArrays_PreservesTypes()
        {
            // Arrange
            var container = new ArrayContainer();

            // Act
            var serialized = Serializer.Serialize(container, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<ArrayContainer>(serialized);

            // Assert
            // Object array elements should preserve types
            Assert.IsType<int>(deserialized.ObjectArray[0]);
            Assert.IsType<string>(deserialized.ObjectArray[1]);
            Assert.IsType<Circle>(deserialized.ObjectArray[2]);

            // Shape array should preserve concrete types
            Assert.IsType<Circle>(deserialized.ShapeArray[0]);
            Assert.IsType<Rectangle>(deserialized.ShapeArray[1]);

            // Abstract array should preserve concrete types
            Assert.IsType<Circle>(deserialized.AbstractArray[0]);
            Assert.IsType<Square>(deserialized.AbstractArray[1]);

            // Arrays as objects should preserve array types
            Assert.IsType<int[]>(deserialized.SingleObject);
            Assert.IsType<int[][]>(deserialized.JaggedArray);
            Assert.IsType<int[,]>(deserialized.MultiDimArray);
        }

        #endregion

        #region Circular Reference Tests

        [Fact]
        public void CircularReferences_WithPolymorphism_HandlesCorrectly()
        {
            // Arrange
            var parent = new CircularReference { Name = "Parent" };
            var child = new CircularReference { Name = "Child", Parent = parent };
            parent.Child = child;
            parent.AsObject = child;
            child.AsObject = parent;

            // Act
            var serialized = Serializer.Serialize(parent, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<CircularReference>(serialized);

            // Assert
            Assert.Equal("Parent", deserialized.Name);
            Assert.NotNull(deserialized.Child);
            Assert.Equal("Child", deserialized.Child.Name);
            Assert.Same(deserialized, deserialized.Child.Parent);
            Assert.Same(deserialized.Child, deserialized.AsObject);
            Assert.Same(deserialized, deserialized.Child.AsObject);
        }

        #endregion

        #region TypeMode Tests

        [Fact]
        public void TypeMode_Aggressive_AlwaysPreservesTypes()
        {
            // Arrange
            var obj = new { Value = new Circle() };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Aggressive);

            // Assert
            var valueField = serialized.Get("Value");
            Assert.True(HasTypeInfo(valueField), "Aggressive mode should always preserve type");
        }

        [Fact]
        public void TypeMode_None_NeverPreservesTypes()
        {
            // Arrange
            var obj = new { Shape = (IShape)new Circle() };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.None);

            // Assert
            var shapeField = serialized.Get("Shape");
            Assert.False(HasTypeInfo(shapeField), "None mode should never preserve type");
        }

        [Fact]
        public void TypeMode_Auto_OnlyPreservesWhenNeeded()
        {
            // Arrange
            var obj = new {
                ExactMatch = new Circle(),
                InterfaceField = (IShape)new Circle(),
                ObjectField = (object)new Circle()
            };

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);

            // Assert
            Assert.False(HasTypeInfo(serialized.Get("ExactMatch")), "Auto mode shouldn't preserve exact match");
            Assert.True(HasTypeInfo(serialized.Get("InterfaceField")), "Auto mode should preserve interface polymorphism");
            Assert.True(HasTypeInfo(serialized.Get("ObjectField")), "Auto mode should preserve object polymorphism");
        }

        #endregion

        #region Complex Integration Tests

        [Fact]
        public void ComplexPolymorphism_IntegrationTest()
        {
            // Arrange
            var obj = new ComplexPolymorphicObject();

            // Act
            var serialized = Serializer.Serialize(obj, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<ComplexPolymorphicObject>(serialized);

            // Assert
            // Primitive as object should preserve type
            Assert.IsType<int>(deserialized.PrimitiveAsObject);
            Assert.Equal(42, deserialized.PrimitiveAsObject);

            // String as object should preserve type
            Assert.IsType<string>(deserialized.StringAsObject);
            Assert.Equal("Hello", deserialized.StringAsObject);

            // Null should remain null
            Assert.Null(deserialized.NullAsObject);

            // Interface should preserve concrete type
            Assert.IsType<Circle>(deserialized.ShapeAsInterface);

            // Abstract should preserve concrete type
            Assert.IsType<Rectangle>(deserialized.ShapeAsAbstract);

            // Base class should preserve derived type
            Assert.IsType<Square>(deserialized.RectangleAsBase);

            // Mixed array should preserve all element types
            Assert.IsType<int>(deserialized.MixedArray[0]);
            Assert.IsType<string>(deserialized.MixedArray[1]);
            Assert.IsType<Circle>(deserialized.MixedArray[2]);
            Assert.Null(deserialized.MixedArray[3]);

            // Mixed list should preserve all element types
            Assert.IsType<bool>(deserialized.MixedList[0]);
            Assert.IsType<double>(deserialized.MixedList[1]);
            Assert.IsType<Rectangle>(deserialized.MixedList[2]);

            // Mixed dictionary should preserve all value types
            Assert.IsType<int>(deserialized.MixedDict["int"]);
            Assert.IsType<Circle>(deserialized.MixedDict["shape"]);
            Assert.IsType<Container<string>>(deserialized.MixedDict["nested"]);
        }

        [Fact]
        public void NestedGenericPolymorphism_ComplexScenario()
        {
            // Arrange
            var nested = new NestedContainer {
                ObjectContainer = new Container<object> { Value = new Circle() },
                StringContainer = new StringContainer { Value = "test" },
                ShapeContainer = new Container<IShape> { Value = new Rectangle() }
            };

            // Act
            var serialized = Serializer.Serialize(nested, TypeMode.Auto);
            var deserialized = Serializer.Deserialize<NestedContainer>(serialized);

            // Assert
            // ObjectContainer should preserve concrete type
            Assert.IsType<Container<object>>(deserialized.ObjectContainer);
            Assert.IsType<Circle>(deserialized.ObjectContainer.Value);

            // StringContainer should preserve derived type
            Assert.IsType<StringContainer>(deserialized.StringContainer);
            Assert.Equal("test", deserialized.StringContainer.Value);

            // ShapeContainer should preserve concrete shape type
            Assert.IsType<Container<IShape>>(deserialized.ShapeContainer);
            Assert.IsType<Rectangle>(deserialized.ShapeContainer.Value);
        }

        #endregion

        #region Helper Methods

        private static bool HasTypeInfo(EchoObject? obj)
        {
            if (obj?.TagType != EchoType.Compound) return false;

            // Check for both compact and full type info
            return obj.TryGet("$t", out _) ||    // Compact type info
                   obj.TryGet("$type", out _);   // Full type info
        }

        #endregion
    }
}