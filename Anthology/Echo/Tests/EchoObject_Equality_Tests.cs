// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Test;

public class EchoObject_Equality_Tests
{
    [Fact]
    public void TestNullEquality()
    {
        var echo = new EchoObject();
        Assert.False(echo.Equals(null));
        Assert.False(echo == null);
        Assert.True(echo != null);
    }

    [Fact]
    public void TestReferenceEquality()
    {
        var echo = new EchoObject(42);
        Assert.True(echo.Equals(echo));
        Assert.True(echo == echo);
    }

    [Fact]
    public void TestPrimitiveEquality()
    {
        // Integer tests
        var int1 = new EchoObject(42);
        var int2 = new EchoObject(42);
        var int3 = new EchoObject(43);
        Assert.True(int1.Equals(int2));
        Assert.False(int1.Equals(int3));

        // String tests
        var str1 = new EchoObject("test");
        var str2 = new EchoObject("test");
        var str3 = new EchoObject("different");
        Assert.True(str1.Equals(str2));
        Assert.False(str1.Equals(str3));

        // Boolean tests
        var bool1 = new EchoObject(true);
        var bool2 = new EchoObject(true);
        var bool3 = new EchoObject(false);
        Assert.True(bool1.Equals(bool2));
        Assert.False(bool1.Equals(bool3));

        // Float tests (using exact equality)
        var float1 = new EchoObject(3.14f);
        var float2 = new EchoObject(3.14f);
        var float3 = new EchoObject(3.15f);
        Assert.True(float1.Equals(float2));
        Assert.False(float1.Equals(float3));

        // Byte array tests
        var bytes1 = new EchoObject(new byte[] { 1, 2, 3 });
        var bytes2 = new EchoObject(new byte[] { 1, 2, 3 });
        var bytes3 = new EchoObject(new byte[] { 1, 2, 4 });
        Assert.True(bytes1.Equals(bytes2));
        Assert.False(bytes1.Equals(bytes3));
    }

    [Fact]
    public void TestListEquality()
    {
        // Empty lists
        var list1 = EchoObject.NewList();
        var list2 = EchoObject.NewList();
        Assert.True(list1.Equals(list2));

        // Lists with same elements
        list1 = new EchoObject(new List<EchoObject> { new EchoObject(1), new EchoObject(2) });
        list2 = new EchoObject(new List<EchoObject> { new EchoObject(1), new EchoObject(2) });
        Assert.True(list1.Equals(list2));

        // Lists with different elements
        var list3 = new EchoObject(new List<EchoObject> { new EchoObject(1), new EchoObject(3) });
        Assert.False(list1.Equals(list3));

        // Lists with different lengths
        var list4 = new EchoObject(new List<EchoObject> { new EchoObject(1) });
        Assert.False(list1.Equals(list4));
    }

    [Fact]
    public void TestCompoundEquality()
    {
        // Empty compounds
        var compound1 = EchoObject.NewCompound();
        var compound2 = EchoObject.NewCompound();
        Assert.True(compound1.Equals(compound2));

        // Compounds with same key-value pairs
        var dict1 = new Dictionary<string, EchoObject> {
            { "key1", new EchoObject(42) },
            { "key2", new EchoObject("value") }
        };
        var dict2 = new Dictionary<string, EchoObject> {
            { "key1", new EchoObject(42) },
            { "key2", new EchoObject("value") }
        };
        compound1 = new EchoObject(EchoType.Compound, dict1);
        compound2 = new EchoObject(EchoType.Compound, dict2);
        Assert.True(compound1.Equals(compound2));

        // Compounds with different values
        var dict3 = new Dictionary<string, EchoObject> {
            { "key1", new EchoObject(43) },
            { "key2", new EchoObject("value") }
        };
        var compound3 = new EchoObject(EchoType.Compound, dict3);
        Assert.False(compound1.Equals(compound3));

        // Compounds with different keys
        var dict4 = new Dictionary<string, EchoObject> {
            { "key1", new EchoObject(42) },
            { "different", new EchoObject("value") }
        };
        var compound4 = new EchoObject(EchoType.Compound, dict4);
        Assert.False(compound1.Equals(compound4));
    }

    [Fact]
    public void TestNestedEquality()
    {
        // Create nested compound objects
        var nested1 = EchoObject.NewCompound();
        nested1.SetValue(new Dictionary<string, EchoObject> {
            { "list", new EchoObject(new List<EchoObject> {
                new EchoObject(1),
                new EchoObject("test")
            })},
            { "compound", new EchoObject(EchoType.Compound, new Dictionary<string, EchoObject> {
                { "nested", new EchoObject(true) }
            })}
        });

        var nested2 = EchoObject.NewCompound();
        nested2.SetValue(new Dictionary<string, EchoObject> {
            { "list", new EchoObject(new List<EchoObject> {
                new EchoObject(1),
                new EchoObject("test")
            })},
            { "compound", new EchoObject(EchoType.Compound, new Dictionary<string, EchoObject> {
                { "nested", new EchoObject(true) }
            })}
        });

        Assert.True(nested1.Equals(nested2));

        // Modify a deeply nested value
        ((Dictionary<string, EchoObject>)nested2.Value!)["compound"]
            .Get("nested").Value = false;

        Assert.False(nested1.Equals(nested2));
    }

    [Fact]
    public void TestHashCodeConsistency()
    {
        // Test primitive hash codes
        var int1 = new EchoObject(42);
        var int2 = new EchoObject(42);
        Assert.Equal(int1.GetHashCode(), int2.GetHashCode());

        // Test list hash codes
        var list1 = new EchoObject(new List<EchoObject> { new EchoObject(1), new EchoObject(2) });
        var list2 = new EchoObject(new List<EchoObject> { new EchoObject(1), new EchoObject(2) });
        Assert.Equal(list1.GetHashCode(), list2.GetHashCode());

        // Test compound hash codes
        var dict1 = new Dictionary<string, EchoObject> {
            { "key1", new EchoObject(42) },
            { "key2", new EchoObject("value") }
        };
        var dict2 = new Dictionary<string, EchoObject> {
            { "key1", new EchoObject(42) },
            { "key2", new EchoObject("value") }
        };
        var compound1 = new EchoObject(EchoType.Compound, dict1);
        var compound2 = new EchoObject(EchoType.Compound, dict2);
        Assert.Equal(compound1.GetHashCode(), compound2.GetHashCode());
    }

    [Fact]
    public void TestDifferentTypeEquality()
    {
        var intObj = new EchoObject(42);
        var stringObj = new EchoObject("42");
        var listObj = EchoObject.NewList();
        var compoundObj = EchoObject.NewCompound();

        Assert.False(intObj.Equals(stringObj));
        Assert.False(intObj.Equals(listObj));
        Assert.False(intObj.Equals(compoundObj));
        Assert.False(listObj.Equals(compoundObj));
    }

    [Fact]
    public void TestOperatorOverloads()
    {
        var obj1 = new EchoObject(42);
        var obj2 = new EchoObject(42);
        var obj3 = new EchoObject(43);

        Assert.True(obj1 == obj2);
        Assert.False(obj1 == obj3);
        Assert.False(obj1 != obj2);
        Assert.True(obj1 != obj3);

        // Test with null
        EchoObject? nullObj = null;
        Assert.False(obj1 == nullObj);
        Assert.True(obj1 != nullObj);
        Assert.False(nullObj == obj1);
        Assert.True(nullObj != obj1);
    }
    [Fact]
    public void TestNumericTypeConversion()
    {
        // Test numeric type conversions and equality
        var intObj = new EchoObject(42);
        var longObj = new EchoObject(42L);
        var doubleObj = new EchoObject(42.0);
        var floatObj = new EchoObject(42.0f);

        // Create new objects setting values across types
        var longAsInt = new EchoObject(EchoType.Int, 42L);
        var doubleAsInt = new EchoObject(EchoType.Int, 42.0);
        var floatAsInt = new EchoObject(EchoType.Int, 42.0f);

        // Verify all are equal when converted to same type
        Assert.True(intObj.Equals(longAsInt));
        Assert.True(intObj.Equals(doubleAsInt));
        Assert.True(intObj.Equals(floatAsInt));

        // Verify they're not equal when kept as different types
        Assert.False(intObj.Equals(longObj));
        Assert.False(intObj.Equals(doubleObj));
        Assert.False(intObj.Equals(floatObj));
    }

    [Fact]
    public void TestEdgeCaseValues()
    {
        // Test extreme numeric values
        var maxInt = new EchoObject(int.MaxValue);
        var minInt = new EchoObject(int.MinValue);
        var maxLong = new EchoObject(long.MaxValue);
        var minLong = new EchoObject(long.MinValue);

        Assert.NotEqual(maxInt.GetHashCode(), maxLong.GetHashCode());
        Assert.NotEqual(minInt.GetHashCode(), minLong.GetHashCode());

        // Test special floating point values
        var nanDouble = new EchoObject(double.NaN);
        var infDouble = new EchoObject(double.PositiveInfinity);
        var negInfDouble = new EchoObject(double.NegativeInfinity);

        Assert.False(nanDouble.Equals(new EchoObject(double.NaN))); // NaN should not equal NaN
        Assert.True(infDouble.Equals(new EchoObject(double.PositiveInfinity)));
        Assert.True(negInfDouble.Equals(new EchoObject(double.NegativeInfinity)));
    }

    [Fact]
    public void TestStringEdgeCases()
    {
        // Test empty and whitespace strings
        var emptyString = new EchoObject(string.Empty);
        var whitespaceString = new EchoObject("   ");
        var nullString = new EchoObject((string)null);

        Assert.True(emptyString.Equals(new EchoObject(string.Empty)));
        Assert.False(emptyString.Equals(whitespaceString));
        Assert.True(emptyString.Equals(nullString)); // null strings should be converted to empty

        // Test strings with special characters
        var specialChars = new EchoObject("Hello\n\t\r\0World");
        var specialChars2 = new EchoObject("Hello\n\t\r\0World");
        Assert.True(specialChars.Equals(specialChars2));
    }

    [Fact]
    public void TestByteArrayEdgeCases()
    {
        // Test empty byte arrays
        var emptyArray1 = new EchoObject(new byte[0]);
        var emptyArray2 = new EchoObject(new byte[0]);
        Assert.True(emptyArray1.Equals(emptyArray2));

        // Test arrays with zero bytes
        var zeroArray1 = new EchoObject(new byte[] { 0, 0, 0 });
        var zeroArray2 = new EchoObject(new byte[] { 0, 0, 0 });
        Assert.True(zeroArray1.Equals(zeroArray2));

        // Test large arrays
        var largeArray1 = new EchoObject(new byte[10000]);
        var largeArray2 = new EchoObject(new byte[10000]);
        Assert.True(largeArray1.Equals(largeArray2));
    }

    [Fact]
    public void TestCompoundEdgeCases()
    {
        // Test nested empty structures
        var emptyCompound1 = EchoObject.NewCompound();
        var emptyCompound2 = EchoObject.NewCompound();
        Assert.True(emptyCompound1.Equals(emptyCompound2));

        // Test compound with empty nested structures
        var nestedEmpty1 = EchoObject.NewCompound();
        nestedEmpty1.SetValue(new Dictionary<string, EchoObject> {
            { "empty_list", EchoObject.NewList() },
            { "empty_compound", EchoObject.NewCompound() }
        });

        var nestedEmpty2 = EchoObject.NewCompound();
        nestedEmpty2.SetValue(new Dictionary<string, EchoObject> {
            { "empty_list", EchoObject.NewList() },
            { "empty_compound", EchoObject.NewCompound() }
        });

        Assert.True(nestedEmpty1.Equals(nestedEmpty2));
    }

    [Fact]
    public void TestListEdgeCases()
    {
        // Test list with duplicate values
        var list1 = new EchoObject(new List<EchoObject> {
            new EchoObject(1),
            new EchoObject(1),
            new EchoObject(1)
        });

        var list2 = new EchoObject(new List<EchoObject> {
            new EchoObject(1),
            new EchoObject(1),
            new EchoObject(1)
        });

        Assert.True(list1.Equals(list2));

        // Test list with mixed types
        var mixedList1 = new EchoObject(new List<EchoObject> {
            new EchoObject(1),
            new EchoObject("string"),
            new EchoObject(true),
            EchoObject.NewCompound(),
            EchoObject.NewList()
        });

        var mixedList2 = new EchoObject(new List<EchoObject> {
            new EchoObject(1),
            new EchoObject("string"),
            new EchoObject(true),
            EchoObject.NewCompound(),
            EchoObject.NewList()
        });

        Assert.True(mixedList1.Equals(mixedList2));
    }

    [Fact]
    public void TestDeepNestedStructures()
    {
        // Create a deeply nested structure
        var deepNested1 = CreateDeepNestedStructure(5);
        var deepNested2 = CreateDeepNestedStructure(5);
        Assert.True(deepNested1.Equals(deepNested2));

        // Modify their deep values
        ModifyDeepValue(deepNested2);
        Assert.False(deepNested1.Equals(deepNested2));
        ModifyDeepValue(deepNested1);
        Assert.True(deepNested1.Equals(deepNested2));
    }

    private EchoObject CreateDeepNestedStructure(int depth)
    {
        if (depth == 0)
        {
            // Create a compound with a string value instead of just a string
            var leaf = EchoObject.NewCompound();
            leaf.Add("leaf", new EchoObject("NotModified"));
            return leaf;
        }

        var compound = EchoObject.NewCompound();
        compound["nested"] = CreateDeepNestedStructure(depth - 1);
        var list = EchoObject.NewList();
        list.ListAdd(CreateDeepNestedStructure(depth - 1));
        compound["list"] = list;
        return compound;
    }
    private void ModifyDeepValue(EchoObject obj)
    {
        if(obj.TagType == EchoType.String)
            obj.StringValue = "Modified";

        // Handle compound type
        if (obj.TagType == EchoType.Compound)
        {
            foreach(var tag in obj.Tags)
                ModifyDeepValue(tag.Value);
        }
        // Handle list type
        else if (obj.TagType == EchoType.List)
            foreach (var tag in obj.List)
                ModifyDeepValue(tag);
    }

}