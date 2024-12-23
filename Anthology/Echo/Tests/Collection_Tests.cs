// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Tests.Types;

namespace Prowl.Echo.Test;

public class Collection_Tests
{
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
}