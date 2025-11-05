using Prowl.Echo;

namespace Tests.SourceGeneratorTests;

public class CollectionTests
{
    private T RoundTrip<T>(T value)
    {
        var serialized = Serializer.Serialize(value);
        return Serializer.Deserialize<T>(serialized);
    }

    [Fact]
    public void ObjectWithList_EmptyLists_SerializesCorrectly()
    {
        var original = new ObjectWithList();
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Empty(result.Numbers);
        Assert.Empty(result.Strings);
    }

    [Fact]
    public void ObjectWithList_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithList
        {
            Numbers = new List<int> { 1, 2, 3, 4, 5 },
            Strings = new List<string?> { "one", "two", null, "four" }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.Numbers, result.Numbers);
        Assert.Equal(original.Strings, result.Strings);
    }

    [Fact]
    public void ObjectWithArray_EmptyArrays_SerializesCorrectly()
    {
        var original = new ObjectWithArray();
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Empty(result.Numbers);
        Assert.Empty(result.Strings);
    }

    [Fact]
    public void ObjectWithArray_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithArray
        {
            Numbers = new[] { 10, 20, 30, 40, 50 },
            Strings = new[] { "a", "b", null, "d" }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.Numbers, result.Numbers);
        Assert.Equal(original.Strings, result.Strings);
    }

    [Fact]
    public void ObjectWithDictionary_Empty_SerializesCorrectly()
    {
        var original = new ObjectWithDictionary();
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Empty(result.StringToInt);
        Assert.Empty(result.IntToString);
    }

    [Fact]
    public void ObjectWithDictionary_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithDictionary
        {
            StringToInt = new Dictionary<string, int> { { "one", 1 }, { "two", 2 }, { "three", 3 } },
            IntToString = new Dictionary<int, string?> { { 1, "one" }, { 2, null }, { 3, "three" } }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.StringToInt, result.StringToInt);
        Assert.Equal(original.IntToString, result.IntToString);
    }

    [Fact]
    public void ObjectWithHashSet_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithHashSet
        {
            Numbers = new HashSet<int> { 1, 2, 3, 4, 5 },
            Strings = new HashSet<string?> { "a", "b", null, "d" }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.True(result.Numbers.SetEquals(original.Numbers));
        Assert.True(result.Strings.SetEquals(original.Strings));
    }

    [Fact]
    public void ObjectWithQueue_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithQueue();
        original.Numbers.Enqueue(1);
        original.Numbers.Enqueue(2);
        original.Numbers.Enqueue(3);
        original.Strings.Enqueue("first");
        original.Strings.Enqueue(null);
        original.Strings.Enqueue("third");

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.Numbers.ToList(), result.Numbers.ToList());
        Assert.Equal(original.Strings.ToList(), result.Strings.ToList());
    }

    [Fact]
    public void ObjectWithStack_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithStack();
        original.Numbers.Push(1);
        original.Numbers.Push(2);
        original.Numbers.Push(3);
        original.Strings.Push("first");
        original.Strings.Push(null);
        original.Strings.Push("third");

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.Numbers.ToList(), result.Numbers.ToList());
        Assert.Equal(original.Strings.ToList(), result.Strings.ToList());
    }

    [Fact]
    public void ObjectWithLinkedList_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithLinkedList();
        original.Numbers.AddLast(1);
        original.Numbers.AddLast(2);
        original.Numbers.AddLast(3);
        original.Strings.AddLast("first");
        original.Strings.AddLast((string?)null);
        original.Strings.AddLast("third");

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.Numbers.ToList(), result.Numbers.ToList());
        Assert.Equal(original.Strings.ToList(), result.Strings.ToList());
    }

    [Fact]
    public void ObjectWithNestedCollections_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithNestedCollections
        {
            NestedLists = new List<List<int>>
            {
                new List<int> { 1, 2, 3 },
                new List<int> { 4, 5, 6 },
                new List<int> { 7, 8, 9 }
            },
            DictOfLists = new Dictionary<string, List<int>>
            {
                { "first", new List<int> { 1, 2 } },
                { "second", new List<int> { 3, 4, 5 } }
            },
            ListOfDicts = new List<Dictionary<int, string>>
            {
                new Dictionary<int, string> { { 1, "one" }, { 2, "two" } },
                new Dictionary<int, string> { { 3, "three" } }
            }
        };

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.NestedLists.Count, result.NestedLists.Count);
        for (int i = 0; i < original.NestedLists.Count; i++)
        {
            Assert.Equal(original.NestedLists[i], result.NestedLists[i]);
        }
        Assert.Equal(original.DictOfLists, result.DictOfLists);
        Assert.Equal(original.ListOfDicts.Count, result.ListOfDicts.Count);
    }

    [Fact]
    public void ObjectWithJaggedArray_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithJaggedArray
        {
            JaggedArray = new int[][]
            {
                new int[] { 1, 2, 3 },
                new int[] { 4, 5 },
                new int[] { 6, 7, 8, 9 }
            },
            JaggedStringArray = new string?[][]
            {
                new string?[] { "a", null, "c" },
                new string?[] { "d", "e" }
            }
        };

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.JaggedArray.Length, result.JaggedArray.Length);
        for (int i = 0; i < original.JaggedArray.Length; i++)
        {
            Assert.Equal(original.JaggedArray[i], result.JaggedArray[i]);
        }
    }

    [Fact]
    public void ObjectWithMultidimensionalArray_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithMultidimensionalArray
        {
            TwoDArray = new int[,] { { 1, 2 }, { 3, 4 } },
            ThreeDArray = new string[,,]
            {
                { { "a", "b" }, { "c", "d" } },
                { { "e", "f" }, { "g", "h" } }
            }
        };

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.TwoDArray, result.TwoDArray);
        Assert.Equal(original.ThreeDArray, result.ThreeDArray);
    }

    [Fact]
    public void ObjectWithSortedCollections_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithSortedCollections
        {
            SortedNumbers = new SortedSet<int> { 5, 1, 3, 2, 4 },
            SortedDict = new SortedDictionary<string, int>
            {
                { "zebra", 26 },
                { "alpha", 1 },
                { "beta", 2 }
            }
        };

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.SortedNumbers, result.SortedNumbers);
        Assert.Equal(original.SortedDict, result.SortedDict);
    }

    [Fact]
    public void ObjectWithObservableCollection_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithObservableCollection
        {
            Numbers = new System.Collections.ObjectModel.ObservableCollection<int> { 1, 2, 3 },
            Strings = new System.Collections.ObjectModel.Collection<string> { "a", "b", "c" }
        };

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.Numbers.ToList(), result.Numbers.ToList());
        Assert.Equal(original.Strings.ToList(), result.Strings.ToList());
    }

    [Fact]
    public void ObjectWithEmptyCollections_AllEmpty_SerializesCorrectly()
    {
        var original = new ObjectWithEmptyCollections();
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Empty(result.EmptyList);
        Assert.Empty(result.EmptyDict);
        Assert.Empty(result.EmptySet);
    }

    [Fact]
    public void ObjectWithNullCollections_AllNull_SerializesCorrectly()
    {
        var original = new ObjectWithNullCollections();
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Null(result.NullableList);
        Assert.Null(result.NullableDict);
        Assert.Null(result.NullableArray);
    }

    [Fact]
    public void ObjectWithNullCollections_WithData_SerializesCorrectly()
    {
        var original = new ObjectWithNullCollections
        {
            NullableList = new List<int> { 1, 2, 3 },
            NullableDict = new Dictionary<string, int> { { "one", 1 } },
            NullableArray = new[] { 10, 20, 30 }
        };
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.NotNull(result.NullableList);
        Assert.Equal(original.NullableList, result.NullableList);
        Assert.NotNull(result.NullableDict);
        Assert.Equal(original.NullableDict, result.NullableDict);
        Assert.NotNull(result.NullableArray);
        Assert.Equal(original.NullableArray, result.NullableArray);
    }
}
