// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Tests.Types;

namespace Prowl.Echo.Test;


public class TestObject
{
    public int Id;
    public string Name;
}

public class Collection_Tests
{
    T RoundTrip<T>(T value)
    {
        var serialized = Serializer.Serialize(value);
        return Serializer.Deserialize<T>(serialized);
    }

    [Fact]
    public void TestCollection_WithNulls_ShouldPreserveNulls()
    {
        var original = new System.Collections.ObjectModel.Collection<string?>
        {
            "first",
            null,
            "third",
            null,
            "fifth"
        };

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<System.Collections.ObjectModel.Collection<string?>>(serialized);

        Assert.Equal(original.Count, deserialized.Count);
        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i], deserialized[i]);
        }
    }

    [Fact]
    public void TestHashSet_WithNulls_ShouldPreserveNulls()
    {
        var original = new HashSet<string?> { "first", null, "third" };

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<HashSet<string?>>(serialized);

        Assert.Equal(original.Count, deserialized.Count);
        Assert.True(deserialized.SetEquals(original));
    }

    [Fact]
    public void TestQueue_WithNulls_ShouldPreserveNulls()
    {
        var original = new Queue<string?>();
        original.Enqueue("first");
        original.Enqueue(null);
        original.Enqueue("third");
        original.Enqueue(null);

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Queue<string?>>(serialized);

        Assert.Equal(original.Count, deserialized.Count);
        var originalList = original.ToList();
        var deserializedList = deserialized.ToList();
        for (int i = 0; i < originalList.Count; i++)
        {
            Assert.Equal(originalList[i], deserializedList[i]);
        }
    }

    [Fact]
    public void TestStack_WithNulls_ShouldPreserveNulls()
    {
        var original = new Stack<string?>();
        original.Push("first");
        original.Push(null);
        original.Push("third");
        original.Push(null);

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Stack<string?>>(serialized);

        Assert.Equal(original.Count, deserialized.Count);
        var originalList = original.ToList();
        var deserializedList = deserialized.ToList();
        for (int i = 0; i < originalList.Count; i++)
        {
            Assert.Equal(originalList[i], deserializedList[i]);
        }
    }

    [Fact]
    public void TestLinkedList_WithNulls_ShouldPreserveNulls()
    {
        var original = new LinkedList<string?>();
        original.AddLast("first");
        original.AddLast((string?)null);
        original.AddLast("third");
        original.AddLast((string?)null);

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<LinkedList<string?>>(serialized);

        Assert.Equal(original.Count, deserialized.Count);
        var originalList = original.ToList();
        var deserializedList = deserialized.ToList();
        for (int i = 0; i < originalList.Count; i++)
        {
            Assert.Equal(originalList[i], deserializedList[i]);
        }
    }

    [Fact]
    public void TestListOfLists()
    {
        var original = new List<List<int>>
        {
            new List<int> { 1, 2 },
            new List<int> { 3, 4, 5 }
        };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<List<List<int>>>(serialized);

        Assert.Equal(original.Count, deserialized.Count);
        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i], deserialized[i]);
        }
    }

    [Fact]
    public void TestQueue()
    {
        var original = new Queue<string>();
        original.Enqueue("first");
        original.Enqueue("second");
        original.Enqueue("third");

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Queue<string>>(serialized);

        Assert.Equal(original.Count, deserialized.Count);
        while (original.Count > 0)
        {
            Assert.Equal(original.Dequeue(), deserialized.Dequeue());
        }
    }

    [Fact]
    public void TestStack()
    {
        var original = new Stack<double>();
        original.Push(1.1);
        original.Push(2.2);
        original.Push(3.3);

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Stack<double>>(serialized);

        Assert.Equal(original.Count, deserialized.Count);
        while (original.Count > 0)
        {
            Assert.Equal(original.Pop(), deserialized.Pop());
        }
    }

    [Fact]
    public void TestLinkedList()
    {
        var original = new LinkedList<int>();
        original.AddLast(1);
        original.AddLast(2);
        original.AddLast(3);

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<LinkedList<int>>(serialized);

        Assert.Equal(original.Count, deserialized.Count);

        var originalNode = original.First;
        var deserializedNode = deserialized.First;

        while (originalNode != null)
        {
            Assert.Equal(originalNode.Value, deserializedNode.Value);
            originalNode = originalNode.Next;
            deserializedNode = deserializedNode.Next;
        }
    }

    [Fact]
    public void TestComplexTypes()
    {
        var original = new List<TestObject>
        {
            new TestObject { Id = 1, Name = "One" },
            new TestObject { Id = 2, Name = "Two" }
        };

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<List<TestObject>>(serialized);

        Assert.Equal(original.Count, deserialized.Count);
        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Id, deserialized[i].Id);
            Assert.Equal(original[i].Name, deserialized[i].Name);
        }
    }

    [Fact]
    public void TestNestedCollections()
    {
        var original = new Queue<List<Stack<int>>>();
        var stack1 = new Stack<int>();
        stack1.Push(1);
        stack1.Push(2);
        var stack2 = new Stack<int>();
        stack2.Push(3);
        stack2.Push(4);
        var list1 = new List<Stack<int>> { stack1 };
        var list2 = new List<Stack<int>> { stack2 };
        original.Enqueue(list1);
        original.Enqueue(list2);

        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Queue<List<Stack<int>>>>(serialized);

        // Convert to arrays for non-destructive comparison
        var originalArray = original.ToArray();
        var deserializedArray = deserialized.ToArray();

        Assert.Equal(originalArray.Length, deserializedArray.Length);

        for (int i = 0; i < originalArray.Length; i++)
        {
            var originalList = originalArray[i];
            var deserializedList = deserializedArray[i];
            Assert.Equal(originalList.Count, deserializedList.Count);

            for (int j = 0; j < originalList.Count; j++)
            {
                var originalStack = originalList[j].ToArray();
                var deserializedStack = deserializedList[j].ToArray();

                Assert.Equal(originalStack.Length, deserializedStack.Length);
                Assert.Equal(originalStack, deserializedStack);
            }
        }
    }

    [Fact]
    public void TestEmptyCollections()
    {
        // Test empty List
        var emptyList = new List<int>();
        var serializedList = Serializer.Serialize(emptyList);
        var deserializedList = Serializer.Deserialize<List<int>>(serializedList);
        Assert.Empty(deserializedList);

        // Test empty Queue
        var emptyQueue = new Queue<string>();
        var serializedQueue = Serializer.Serialize(emptyQueue);
        var deserializedQueue = Serializer.Deserialize<Queue<string>>(serializedQueue);
        Assert.Empty(deserializedQueue);

        // Test empty Stack
        var emptyStack = new Stack<double>();
        var serializedStack = Serializer.Serialize(emptyStack);
        var deserializedStack = Serializer.Deserialize<Stack<double>>(serializedStack);
        Assert.Empty(deserializedStack);

        // Test empty LinkedList
        var emptyLinkedList = new LinkedList<int>();
        var serializedLinkedList = Serializer.Serialize(emptyLinkedList);
        var deserializedLinkedList = Serializer.Deserialize<LinkedList<int>>(serializedLinkedList);
        Assert.Empty(deserializedLinkedList);
    }

    [Fact]
    public void TestArrays()
    {
        var original = new int[] { 1, 2, 3, 4, 5 };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<int[]>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestObjectArrays()
    {
        object[] objects = new object[]
        {
            1,
            "two",
            new TestObject { Id = 3, Name = "Three" }
        };

        var serialized = Serializer.Serialize(objects);
        var deserialized = Serializer.Deserialize<object[]>(serialized);

        Assert.Equal(objects.Length, deserialized.Length);
        for (int i = 0; i < objects.Length; i++)
        {
            var original = objects[i];
            var deserializedObject = deserialized[i];

            if (original is int)
            {
                Assert.IsType<int>(deserializedObject);
                Assert.Equal(original, deserializedObject);
            }
            else if (original is string)
            {
                Assert.IsType<string>(deserializedObject);
                Assert.Equal(original, deserializedObject);
            }
            else if (original is TestObject)
            {
                Assert.IsType<TestObject>(deserializedObject);
                var originalTestObject = (TestObject)original;
                var deserializedTestObject = (TestObject)deserializedObject;
                Assert.Equal(originalTestObject.Id, deserializedTestObject.Id);
                Assert.Equal(originalTestObject.Name, deserializedTestObject.Name);
            }
        }
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

    [Fact]
    public void TestObservableCollection()
    {
        var original = new System.Collections.ObjectModel.ObservableCollection<int> { 1, 2, 3 };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<System.Collections.ObjectModel.ObservableCollection<int>>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestSortedSet()
    {
        var original = new SortedSet<int> { 3, 1, 2 };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<SortedSet<int>>(serialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TestCollection()
    {
        var original = new System.Collections.ObjectModel.Collection<int> { 1, 2, 3 };
        var serialized = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<System.Collections.ObjectModel.Collection<int>>(serialized);
        Assert.Equal(original, deserialized);
    }
}