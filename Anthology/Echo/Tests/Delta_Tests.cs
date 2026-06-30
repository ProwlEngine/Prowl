// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Test;

public class Player
{
    public string Name;
    public int Health;
    public int MaxHealth;
    public float PositionX;
    public float PositionY;
    public float PositionZ;
    public List<string> Inventory;
    public Dictionary<string, int> Stats;
}

public class DeltaComplexObject
{
    public int Id;
    public string Name;
    public List<int> Numbers;
    public Dictionary<string, string> Properties;
    public DeltaNestedObject Nested;
}

public class DeltaNestedObject
{
    public string Value;
    public int Count;
}

public class Delta_Tests
{
    #region Basic Delta Tests

    [Fact]
    public void TestDelta_NoChanges_ShouldBeEmpty()
    {
        var obj1 = new EchoObject(42);
        var obj2 = new EchoObject(42);

        var delta = EchoObject.CreateDelta(obj1, obj2);

        Assert.NotNull(delta);
        Assert.Equal(0, delta["Operations"].Count); // No operations
    }

    [Fact]
    public void TestDelta_PrimitiveChange_Int()
    {
        var obj1 = new EchoObject(42);
        var obj2 = new EchoObject(100);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(100, result.IntValue);
    }

    [Fact]
    public void TestDelta_PrimitiveChange_String()
    {
        var obj1 = new EchoObject("hello");
        var obj2 = new EchoObject("world");

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal("world", result.StringValue);
    }

    [Fact]
    public void TestDelta_PrimitiveChange_Float()
    {
        var obj1 = new EchoObject(3.14f);
        var obj2 = new EchoObject(2.71f);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(2.71f, result.FloatValue);
    }

    [Fact]
    public void TestDelta_PrimitiveChange_Bool()
    {
        var obj1 = new EchoObject(true);
        var obj2 = new EchoObject(false);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.False(result.BoolValue);
    }

    [Fact]
    public void TestDelta_NullToValue()
    {
        var obj1 = Serializer.Serialize((string)null);
        var obj2 = Serializer.Serialize("hello");

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal("hello", Serializer.Deserialize<string>(result));
    }

    [Fact]
    public void TestDelta_ValueToNull()
    {
        var obj1 = Serializer.Serialize("hello");
        var obj2 = Serializer.Serialize((string)null);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Null(Serializer.Deserialize<string>(result));
    }

    #endregion

    #region Compound Object Delta Tests

    [Fact]
    public void TestDelta_CompoundObject_SingleFieldChange()
    {
        var player1 = new Player { Name = "Alice", Health = 100, MaxHealth = 100 };
        var player2 = new Player { Name = "Alice", Health = 75, MaxHealth = 100 };

        var obj1 = Serializer.Serialize(player1);
        var obj2 = Serializer.Serialize(player2);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);
        var resultPlayer = Serializer.Deserialize<Player>(result);

        Assert.Equal("Alice", resultPlayer.Name);
        Assert.Equal(75, resultPlayer.Health);
        Assert.Equal(100, resultPlayer.MaxHealth);
    }

    [Fact]
    public void TestDelta_CompoundObject_MultipleFieldChanges()
    {
        var player1 = new Player {
            Name = "Alice",
            Health = 100,
            MaxHealth = 100,
            PositionX = 0,
            PositionY = 0,
            PositionZ = 0
        };
        var player2 = new Player {
            Name = "Bob",
            Health = 75,
            MaxHealth = 150,
            PositionX = 10,
            PositionY = 5,
            PositionZ = 3
        };

        var obj1 = Serializer.Serialize(player1);
        var obj2 = Serializer.Serialize(player2);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);
        var resultPlayer = Serializer.Deserialize<Player>(result);

        Assert.Equal("Bob", resultPlayer.Name);
        Assert.Equal(75, resultPlayer.Health);
        Assert.Equal(150, resultPlayer.MaxHealth);
        Assert.Equal(10f, resultPlayer.PositionX);
        Assert.Equal(5f, resultPlayer.PositionY);
        Assert.Equal(3f, resultPlayer.PositionZ);
    }

    [Fact]
    public void TestDelta_CompoundObject_AddField()
    {
        var obj1 = EchoObject.NewCompound();
        obj1.Add("Name", new EchoObject("Alice"));
        obj1.Add("Health", new EchoObject(100));

        var obj2 = EchoObject.NewCompound();
        obj2.Add("Name", new EchoObject("Alice"));
        obj2.Add("Health", new EchoObject(100));
        obj2.Add("MaxHealth", new EchoObject(150));

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.True(result.Contains("MaxHealth"));
        Assert.Equal(150, result["MaxHealth"].IntValue);
    }

    [Fact]
    public void TestDelta_CompoundObject_RemoveField()
    {
        var obj1 = EchoObject.NewCompound();
        obj1.Add("Name", new EchoObject("Alice"));
        obj1.Add("Health", new EchoObject(100));
        obj1.Add("MaxHealth", new EchoObject(150));

        var obj2 = EchoObject.NewCompound();
        obj2.Add("Name", new EchoObject("Alice"));
        obj2.Add("Health", new EchoObject(100));

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.False(result.Contains("MaxHealth"));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TestDelta_CompoundObject_RenameField()
    {
        var obj1 = EchoObject.NewCompound();
        obj1.Add("OldName", new EchoObject("Value"));

        var obj2 = EchoObject.NewCompound();
        obj2.Add("NewName", new EchoObject("Value"));

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.False(result.Contains("OldName"));
        Assert.True(result.Contains("NewName"));
        Assert.Equal("Value", result["NewName"].StringValue);
    }

    #endregion

    #region List Delta Tests

    [Fact]
    public void TestDelta_List_NoChanges()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(3));

        var delta = EchoObject.CreateDelta(list1, list2);

        Assert.Equal(0, delta["Operations"].Count);
    }

    [Fact]
    public void TestDelta_List_ElementChange()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(99));
        list2.ListAdd(new EchoObject(3));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal(99, result[1].IntValue);
    }

    [Fact]
    public void TestDelta_List_AddElement()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(3));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal(3, result[2].IntValue);
    }

    [Fact]
    public void TestDelta_List_AddMultipleElements()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(3));
        list2.ListAdd(new EchoObject(4));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(4, result.Count);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
        Assert.Equal(3, result[2].IntValue);
        Assert.Equal(4, result[3].IntValue);
    }

    [Fact]
    public void TestDelta_List_RemoveElement()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(2));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
    }

    [Fact]
    public void TestDelta_List_RemoveMultipleElements()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));
        list1.ListAdd(new EchoObject(4));
        list1.ListAdd(new EchoObject(5));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(2));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void TestDelta_List_Clear()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));

        var list2 = EchoObject.NewList();

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void TestDelta_List_CompleteReplacement()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(99));
        list2.ListAdd(new EchoObject(88));
        list2.ListAdd(new EchoObject(77));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal(99, result[0].IntValue);
        Assert.Equal(88, result[1].IntValue);
        Assert.Equal(77, result[2].IntValue);
    }

    #endregion

    #region Nested Object Delta Tests

    [Fact]
    public void TestDelta_NestedObject_SingleChange()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Nested = new DeltaNestedObject { Value = "Hello", Count = 10 }
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Nested = new DeltaNestedObject { Value = "World", Count = 10 }
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.Equal("World", resultObj.Nested.Value);
        Assert.Equal(10, resultObj.Nested.Count);
    }

    [Fact]
    public void TestDelta_NestedObject_ReplaceNested()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Nested = new DeltaNestedObject { Value = "Hello", Count = 10 }
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Nested = new DeltaNestedObject { Value = "World", Count = 99 }
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.Equal("World", resultObj.Nested.Value);
        Assert.Equal(99, resultObj.Nested.Count);
    }

    [Fact]
    public void TestDelta_NestedObject_NullToObject()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Nested = null
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Nested = new DeltaNestedObject { Value = "World", Count = 99 }
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.NotNull(resultObj.Nested);
        Assert.Equal("World", resultObj.Nested.Value);
        Assert.Equal(99, resultObj.Nested.Count);
    }

    [Fact]
    public void TestDelta_NestedObject_ObjectToNull()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Nested = new DeltaNestedObject { Value = "World", Count = 99 }
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Nested = null
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.Null(resultObj.Nested);
    }

    #endregion

    #region Collection with Objects Delta Tests

    [Fact]
    public void TestDelta_ListWithObjects_AddItem()
    {
        var player1 = new Player
        {
            Name = "Alice",
            Inventory = new List<string> { "Sword", "Shield" }
        };

        var player2 = new Player
        {
            Name = "Alice",
            Inventory = new List<string> { "Sword", "Shield", "Potion" }
        };

        var echo1 = Serializer.Serialize(player1);
        var echo2 = Serializer.Serialize(player2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultPlayer = Serializer.Deserialize<Player>(result);

        Assert.Equal(3, resultPlayer.Inventory.Count);
        Assert.Equal("Potion", resultPlayer.Inventory[2]);
    }

    [Fact]
    public void TestDelta_Dictionary_AddEntry()
    {
        var player1 = new Player
        {
            Name = "Alice",
            Stats = new Dictionary<string, int>
            {
                { "Strength", 10 },
                { "Dexterity", 15 }
            }
        };

        var player2 = new Player
        {
            Name = "Alice",
            Stats = new Dictionary<string, int>
            {
                { "Strength", 10 },
                { "Dexterity", 15 },
                { "Intelligence", 20 }
            }
        };

        var echo1 = Serializer.Serialize(player1);
        var echo2 = Serializer.Serialize(player2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultPlayer = Serializer.Deserialize<Player>(result);

        Assert.Equal(3, resultPlayer.Stats.Count);
        Assert.Equal(20, resultPlayer.Stats["Intelligence"]);
    }

    [Fact]
    public void TestDelta_Dictionary_ModifyEntry()
    {
        var player1 = new Player
        {
            Name = "Alice",
            Stats = new Dictionary<string, int>
            {
                { "Strength", 10 },
                { "Dexterity", 15 }
            }
        };

        var player2 = new Player
        {
            Name = "Alice",
            Stats = new Dictionary<string, int>
            {
                { "Strength", 25 },
                { "Dexterity", 15 }
            }
        };

        var echo1 = Serializer.Serialize(player1);
        var echo2 = Serializer.Serialize(player2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultPlayer = Serializer.Deserialize<Player>(result);

        Assert.Equal(25, resultPlayer.Stats["Strength"]);
        Assert.Equal(15, resultPlayer.Stats["Dexterity"]);
    }

    [Fact]
    public void TestDelta_Dictionary_RemoveEntry()
    {
        var player1 = new Player
        {
            Name = "Alice",
            Stats = new Dictionary<string, int>
            {
                { "Strength", 10 },
                { "Dexterity", 15 },
                { "Intelligence", 20 }
            }
        };

        var player2 = new Player
        {
            Name = "Alice",
            Stats = new Dictionary<string, int>
            {
                { "Strength", 10 },
                { "Dexterity", 15 }
            }
        };

        var echo1 = Serializer.Serialize(player1);
        var echo2 = Serializer.Serialize(player2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultPlayer = Serializer.Deserialize<Player>(result);

        Assert.Equal(2, resultPlayer.Stats.Count);
        Assert.False(resultPlayer.Stats.ContainsKey("Intelligence"));
    }

    #endregion

    #region Type Change Delta Tests

    [Fact]
    public void TestDelta_TypeChange_IntToString()
    {
        var obj1 = new EchoObject(42);
        var obj2 = new EchoObject("hello");

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(EchoType.String, result.TagType);
        Assert.Equal("hello", result.StringValue);
    }

    [Fact]
    public void TestDelta_TypeChange_PrimitiveToCompound()
    {
        var obj1 = new EchoObject(42);
        var obj2 = EchoObject.NewCompound();
        obj2.Add("Value", new EchoObject(42));

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(EchoType.Compound, result.TagType);
        Assert.True(result.Contains("Value"));
    }

    [Fact]
    public void TestDelta_TypeChange_CompoundToPrimitive()
    {
        var obj1 = EchoObject.NewCompound();
        obj1.Add("Value", new EchoObject(42));
        var obj2 = new EchoObject(42);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(EchoType.Int, result.TagType);
        Assert.Equal(42, result.IntValue);
    }

    #endregion

    #region Delta Serialization Tests

    [Fact]
    public void TestDelta_Serialization_Binary()
    {
        var obj1 = new EchoObject(42);
        var obj2 = new EchoObject(100);

        var delta = EchoObject.CreateDelta(obj1, obj2);

        // Serialize delta to binary
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        delta.WriteToBinary(writer);

        // Deserialize delta from binary
        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var deserializedDelta = EchoObject.ReadFromBinary(reader);

        // Apply deserialized delta
        var result = EchoObject.ApplyDelta(obj1, deserializedDelta);

        Assert.Equal(100, result.IntValue);
    }

    [Fact]
    public void TestDelta_Serialization_String()
    {
        var obj1 = new EchoObject("hello");
        var obj2 = new EchoObject("world");

        var delta = EchoObject.CreateDelta(obj1, obj2);

        // Serialize delta to string
        string serialized = delta.WriteToString();

        // Deserialize delta from string
        var deserializedDelta = EchoObject.ReadFromString(serialized);

        // Apply deserialized delta
        var result = EchoObject.ApplyDelta(obj1, deserializedDelta);

        Assert.Equal("world", result.StringValue);
    }

    [Fact]
    public void TestDelta_Serialization_ComplexObject()
    {
        var player1 = new Player
        {
            Name = "Alice",
            Health = 100,
            Inventory = new List<string> { "Sword" },
            Stats = new Dictionary<string, int> { { "Strength", 10 } }
        };

        var player2 = new Player
        {
            Name = "Bob",
            Health = 75,
            Inventory = new List<string> { "Sword", "Shield" },
            Stats = new Dictionary<string, int> { { "Strength", 10 }, { "Dexterity", 15 } }
        };

        var echo1 = Serializer.Serialize(player1);
        var echo2 = Serializer.Serialize(player2);

        var delta = EchoObject.CreateDelta(echo1, echo2);

        // Serialize and deserialize
        string serialized = delta.WriteToString();
        var deserializedDelta = EchoObject.ReadFromString(serialized);

        // Apply
        var result = EchoObject.ApplyDelta(echo1, deserializedDelta);
        var resultPlayer = Serializer.Deserialize<Player>(result);

        Assert.Equal("Bob", resultPlayer.Name);
        Assert.Equal(75, resultPlayer.Health);
        Assert.Equal(2, resultPlayer.Inventory.Count);
        Assert.Equal(2, resultPlayer.Stats.Count);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void TestDelta_EmptyCompoundToEmptyCompound()
    {
        var obj1 = EchoObject.NewCompound();
        var obj2 = EchoObject.NewCompound();

        var delta = EchoObject.CreateDelta(obj1, obj2);

        Assert.Equal(0, delta["Operations"].Count);
    }

    [Fact]
    public void TestDelta_EmptyListToEmptyList()
    {
        var obj1 = EchoObject.NewList();
        var obj2 = EchoObject.NewList();

        var delta = EchoObject.CreateDelta(obj1, obj2);

        Assert.Equal(0, delta["Operations"].Count);
    }

    [Fact]
    public void TestDelta_DeepNesting()
    {
        var obj1 = EchoObject.NewCompound();
        var level1 = EchoObject.NewCompound();
        var level2 = EchoObject.NewCompound();
        level2.Add("Value", new EchoObject(42));
        level1.Add("Level2", level2);
        obj1.Add("Level1", level1);

        var obj2 = EchoObject.NewCompound();
        var level1_2 = EchoObject.NewCompound();
        var level2_2 = EchoObject.NewCompound();
        level2_2.Add("Value", new EchoObject(100));
        level1_2.Add("Level2", level2_2);
        obj2.Add("Level1", level1_2);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(100, result["Level1"]["Level2"]["Value"].IntValue);
    }

    [Fact]
    public void TestDelta_LargeList()
    {
        var list1 = EchoObject.NewList();
        for (int i = 0; i < 1000; i++)
            list1.ListAdd(new EchoObject(i));

        var list2 = EchoObject.NewList();
        for (int i = 0; i < 1000; i++)
            list2.ListAdd(new EchoObject(i * 2));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(1000, result.Count);
        Assert.Equal(0, result[0].IntValue);
        Assert.Equal(999 * 2, result[999].IntValue);
    }

    [Fact]
    public void TestDelta_SpecialFloatValues()
    {
        var obj1 = new EchoObject(float.NaN);
        var obj2 = new EchoObject(float.PositiveInfinity);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(float.PositiveInfinity, result.FloatValue);
    }

    [Fact]
    public void TestDelta_ByteArray()
    {
        var arr1 = new byte[] { 1, 2, 3, 4, 5 };
        var arr2 = new byte[] { 5, 4, 3, 2, 1 };

        var obj1 = new EchoObject(arr1);
        var obj2 = new EchoObject(arr2);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(arr2, result.ByteArrayValue);
    }

    #endregion

    #region Multiple Delta Application Tests

    [Fact]
    public void TestDelta_ApplyMultipleDeltas()
    {
        var obj1 = new EchoObject(10);
        var obj2 = new EchoObject(20);
        var obj3 = new EchoObject(30);

        var delta1 = EchoObject.CreateDelta(obj1, obj2);
        var delta2 = EchoObject.CreateDelta(obj2, obj3);

        var result = EchoObject.ApplyDelta(obj1, delta1);
        result = EchoObject.ApplyDelta(result, delta2);

        Assert.Equal(30, result.IntValue);
    }

    [Fact]
    public void TestDelta_ChainedComplexChanges()
    {
        var player1 = new Player { Name = "Alice", Health = 100 };
        var player2 = new Player { Name = "Alice", Health = 75 };
        var player3 = new Player { Name = "Bob", Health = 75 };
        var player4 = new Player { Name = "Bob", Health = 50 };

        var echo1 = Serializer.Serialize(player1);
        var echo2 = Serializer.Serialize(player2);
        var echo3 = Serializer.Serialize(player3);
        var echo4 = Serializer.Serialize(player4);

        var delta1 = EchoObject.CreateDelta(echo1, echo2);
        var delta2 = EchoObject.CreateDelta(echo2, echo3);
        var delta3 = EchoObject.CreateDelta(echo3, echo4);

        var result = EchoObject.ApplyDelta(echo1, delta1);
        result = EchoObject.ApplyDelta(result, delta2);
        result = EchoObject.ApplyDelta(result, delta3);

        var resultPlayer = Serializer.Deserialize<Player>(result);

        Assert.Equal("Bob", resultPlayer.Name);
        Assert.Equal(50, resultPlayer.Health);
    }

    #endregion

    #region List Stress Tests

    [Fact]
    public void TestDelta_List_InsertInMiddle()
    {
        // [1,2,3] -> [1,99,2,3] — the delta system treats this as positional changes + append
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(99));
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(3));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(4, result.Count);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(99, result[1].IntValue);
        Assert.Equal(2, result[2].IntValue);
        Assert.Equal(3, result[3].IntValue);
    }

    [Fact]
    public void TestDelta_List_RemoveFromMiddle()
    {
        // [1,2,3,4] -> [1,3,4] — positional: index 1 changes 2->3, index 2 changes 3->4, remove index 3
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));
        list1.ListAdd(new EchoObject(4));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(3));
        list2.ListAdd(new EchoObject(4));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(3, result[1].IntValue);
        Assert.Equal(4, result[2].IntValue);
    }

    [Fact]
    public void TestDelta_List_RemoveFromBeginning()
    {
        // [1,2,3,4] -> [3,4]
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));
        list1.ListAdd(new EchoObject(4));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(3));
        list2.ListAdd(new EchoObject(4));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].IntValue);
        Assert.Equal(4, result[1].IntValue);
    }

    [Fact]
    public void TestDelta_List_SwapElements()
    {
        // [1,2,3] -> [3,2,1]
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(3));
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(1));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal(3, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
        Assert.Equal(1, result[2].IntValue);
    }

    [Fact]
    public void TestDelta_List_ChangeAndGrow()
    {
        // [1,2] -> [99,88,77,66] — modify existing + append new
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(99));
        list2.ListAdd(new EchoObject(88));
        list2.ListAdd(new EchoObject(77));
        list2.ListAdd(new EchoObject(66));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(4, result.Count);
        Assert.Equal(99, result[0].IntValue);
        Assert.Equal(88, result[1].IntValue);
        Assert.Equal(77, result[2].IntValue);
        Assert.Equal(66, result[3].IntValue);
    }

    [Fact]
    public void TestDelta_List_ChangeAndShrink()
    {
        // [1,2,3,4] -> [99,88] — modify existing + remove tail
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));
        list1.ListAdd(new EchoObject(4));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(99));
        list2.ListAdd(new EchoObject(88));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal(99, result[0].IntValue);
        Assert.Equal(88, result[1].IntValue);
    }

    [Fact]
    public void TestDelta_List_EmptyToPopulated()
    {
        var list1 = EchoObject.NewList();

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(10));
        list2.ListAdd(new EchoObject(20));
        list2.ListAdd(new EchoObject(30));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal(10, result[0].IntValue);
        Assert.Equal(20, result[1].IntValue);
        Assert.Equal(30, result[2].IntValue);
    }

    [Fact]
    public void TestDelta_List_SingleElement_Change()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(99));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(1, result.Count);
        Assert.Equal(99, result[0].IntValue);
    }

    [Fact]
    public void TestDelta_List_SingleElement_Remove()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));

        var list2 = EchoObject.NewList();

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void TestDelta_List_SingleElement_Add()
    {
        var list1 = EchoObject.NewList();

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(42));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(1, result.Count);
        Assert.Equal(42, result[0].IntValue);
    }

    [Fact]
    public void TestDelta_List_MixedTypes()
    {
        // List with different primitive types
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject("hello"));
        list1.ListAdd(new EchoObject(3.14f));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject("world"));
        list2.ListAdd(new EchoObject(2.71f));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].IntValue);
        Assert.Equal("world", result[1].StringValue);
        Assert.Equal(2.71f, result[2].FloatValue);
    }

    [Fact]
    public void TestDelta_List_ElementTypeChange()
    {
        // Element changes type: int -> string
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject("two"));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal(EchoType.Int, result[0].TagType);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(EchoType.String, result[1].TagType);
        Assert.Equal("two", result[1].StringValue);
    }

    [Fact]
    public void TestDelta_List_ElementPrimitiveToCompound()
    {
        // List element changes from primitive to compound
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(42));

        var compound = EchoObject.NewCompound();
        compound.Add("X", new EchoObject(10));
        compound.Add("Y", new EchoObject(20));

        var list2 = EchoObject.NewList();
        list2.ListAdd(compound);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(1, result.Count);
        Assert.Equal(EchoType.Compound, result[0].TagType);
        Assert.Equal(10, result[0]["X"].IntValue);
        Assert.Equal(20, result[0]["Y"].IntValue);
    }

    [Fact]
    public void TestDelta_List_ElementCompoundToPrimitive()
    {
        // List element changes from compound to primitive
        var compound = EchoObject.NewCompound();
        compound.Add("X", new EchoObject(10));

        var list1 = EchoObject.NewList();
        list1.ListAdd(compound);

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(42));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(1, result.Count);
        Assert.Equal(EchoType.Int, result[0].TagType);
        Assert.Equal(42, result[0].IntValue);
    }

    #endregion

    #region Nested List Delta Tests

    [Fact]
    public void TestDelta_NestedList_InnerElementChange()
    {
        // [[1,2],[3,4]] -> [[1,2],[3,99]]
        var inner1a = EchoObject.NewList();
        inner1a.ListAdd(new EchoObject(1));
        inner1a.ListAdd(new EchoObject(2));
        var inner1b = EchoObject.NewList();
        inner1b.ListAdd(new EchoObject(3));
        inner1b.ListAdd(new EchoObject(4));

        var list1 = EchoObject.NewList();
        list1.ListAdd(inner1a);
        list1.ListAdd(inner1b);

        var inner2a = EchoObject.NewList();
        inner2a.ListAdd(new EchoObject(1));
        inner2a.ListAdd(new EchoObject(2));
        var inner2b = EchoObject.NewList();
        inner2b.ListAdd(new EchoObject(3));
        inner2b.ListAdd(new EchoObject(99));

        var list2 = EchoObject.NewList();
        list2.ListAdd(inner2a);
        list2.ListAdd(inner2b);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(2, result[1].Count);
        Assert.Equal(1, result[0][0].IntValue);
        Assert.Equal(2, result[0][1].IntValue);
        Assert.Equal(3, result[1][0].IntValue);
        Assert.Equal(99, result[1][1].IntValue);
    }

    [Fact]
    public void TestDelta_NestedList_InnerListGrows()
    {
        // [[1,2],[3]] -> [[1,2],[3,4,5]]
        var inner1a = EchoObject.NewList();
        inner1a.ListAdd(new EchoObject(1));
        inner1a.ListAdd(new EchoObject(2));
        var inner1b = EchoObject.NewList();
        inner1b.ListAdd(new EchoObject(3));

        var list1 = EchoObject.NewList();
        list1.ListAdd(inner1a);
        list1.ListAdd(inner1b);

        var inner2a = EchoObject.NewList();
        inner2a.ListAdd(new EchoObject(1));
        inner2a.ListAdd(new EchoObject(2));
        var inner2b = EchoObject.NewList();
        inner2b.ListAdd(new EchoObject(3));
        inner2b.ListAdd(new EchoObject(4));
        inner2b.ListAdd(new EchoObject(5));

        var list2 = EchoObject.NewList();
        list2.ListAdd(inner2a);
        list2.ListAdd(inner2b);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(3, result[1].Count);
        Assert.Equal(3, result[1][0].IntValue);
        Assert.Equal(4, result[1][1].IntValue);
        Assert.Equal(5, result[1][2].IntValue);
    }

    [Fact]
    public void TestDelta_NestedList_InnerListShrinks()
    {
        // [[1,2,3],[4,5,6]] -> [[1],[4,5,6]]
        var inner1a = EchoObject.NewList();
        inner1a.ListAdd(new EchoObject(1));
        inner1a.ListAdd(new EchoObject(2));
        inner1a.ListAdd(new EchoObject(3));
        var inner1b = EchoObject.NewList();
        inner1b.ListAdd(new EchoObject(4));
        inner1b.ListAdd(new EchoObject(5));
        inner1b.ListAdd(new EchoObject(6));

        var list1 = EchoObject.NewList();
        list1.ListAdd(inner1a);
        list1.ListAdd(inner1b);

        var inner2a = EchoObject.NewList();
        inner2a.ListAdd(new EchoObject(1));
        var inner2b = EchoObject.NewList();
        inner2b.ListAdd(new EchoObject(4));
        inner2b.ListAdd(new EchoObject(5));
        inner2b.ListAdd(new EchoObject(6));

        var list2 = EchoObject.NewList();
        list2.ListAdd(inner2a);
        list2.ListAdd(inner2b);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Count);
        Assert.Equal(1, result[0][0].IntValue);
        Assert.Equal(3, result[1].Count);
    }

    [Fact]
    public void TestDelta_NestedList_OuterListGrows()
    {
        // [[1],[2]] -> [[1],[2],[3]]
        var inner1a = EchoObject.NewList();
        inner1a.ListAdd(new EchoObject(1));
        var inner1b = EchoObject.NewList();
        inner1b.ListAdd(new EchoObject(2));

        var list1 = EchoObject.NewList();
        list1.ListAdd(inner1a);
        list1.ListAdd(inner1b);

        var inner2a = EchoObject.NewList();
        inner2a.ListAdd(new EchoObject(1));
        var inner2b = EchoObject.NewList();
        inner2b.ListAdd(new EchoObject(2));
        var inner2c = EchoObject.NewList();
        inner2c.ListAdd(new EchoObject(3));

        var list2 = EchoObject.NewList();
        list2.ListAdd(inner2a);
        list2.ListAdd(inner2b);
        list2.ListAdd(inner2c);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[2].Count);
        Assert.Equal(3, result[2][0].IntValue);
    }

    [Fact]
    public void TestDelta_NestedList_OuterListShrinks()
    {
        // [[1],[2],[3]] -> [[1]]
        var inner1a = EchoObject.NewList();
        inner1a.ListAdd(new EchoObject(1));
        var inner1b = EchoObject.NewList();
        inner1b.ListAdd(new EchoObject(2));
        var inner1c = EchoObject.NewList();
        inner1c.ListAdd(new EchoObject(3));

        var list1 = EchoObject.NewList();
        list1.ListAdd(inner1a);
        list1.ListAdd(inner1b);
        list1.ListAdd(inner1c);

        var inner2a = EchoObject.NewList();
        inner2a.ListAdd(new EchoObject(1));

        var list2 = EchoObject.NewList();
        list2.ListAdd(inner2a);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(1, result.Count);
        Assert.Equal(1, result[0].Count);
        Assert.Equal(1, result[0][0].IntValue);
    }

    [Fact]
    public void TestDelta_NestedList_ReplaceInnerList()
    {
        // [[1,2]] -> [[99,88,77]]
        var inner1 = EchoObject.NewList();
        inner1.ListAdd(new EchoObject(1));
        inner1.ListAdd(new EchoObject(2));

        var list1 = EchoObject.NewList();
        list1.ListAdd(inner1);

        var inner2 = EchoObject.NewList();
        inner2.ListAdd(new EchoObject(99));
        inner2.ListAdd(new EchoObject(88));
        inner2.ListAdd(new EchoObject(77));

        var list2 = EchoObject.NewList();
        list2.ListAdd(inner2);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(1, result.Count);
        Assert.Equal(3, result[0].Count);
        Assert.Equal(99, result[0][0].IntValue);
        Assert.Equal(88, result[0][1].IntValue);
        Assert.Equal(77, result[0][2].IntValue);
    }

    [Fact]
    public void TestDelta_ThreeLevelNestedList()
    {
        // [[[1]]] -> [[[99]]]
        var deepInner1 = EchoObject.NewList();
        deepInner1.ListAdd(new EchoObject(1));
        var mid1 = EchoObject.NewList();
        mid1.ListAdd(deepInner1);
        var outer1 = EchoObject.NewList();
        outer1.ListAdd(mid1);

        var deepInner2 = EchoObject.NewList();
        deepInner2.ListAdd(new EchoObject(99));
        var mid2 = EchoObject.NewList();
        mid2.ListAdd(deepInner2);
        var outer2 = EchoObject.NewList();
        outer2.ListAdd(mid2);

        var delta = EchoObject.CreateDelta(outer1, outer2);
        var result = EchoObject.ApplyDelta(outer1, delta);

        Assert.Equal(99, result[0][0][0].IntValue);
    }

    #endregion

    #region List in Compound Delta Tests

    [Fact]
    public void TestDelta_CompoundWithList_ListGrows()
    {
        var obj1 = EchoObject.NewCompound();
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        obj1.Add("Items", list1);
        obj1.Add("Name", new EchoObject("test"));

        var obj2 = EchoObject.NewCompound();
        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(3));
        list2.ListAdd(new EchoObject(4));
        obj2.Add("Items", list2);
        obj2.Add("Name", new EchoObject("test"));

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal("test", result["Name"].StringValue);
        Assert.Equal(4, result["Items"].Count);
        Assert.Equal(3, result["Items"][2].IntValue);
        Assert.Equal(4, result["Items"][3].IntValue);
    }

    [Fact]
    public void TestDelta_CompoundWithList_ListShrinks()
    {
        var obj1 = EchoObject.NewCompound();
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));
        obj1.Add("Items", list1);

        var obj2 = EchoObject.NewCompound();
        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        obj2.Add("Items", list2);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(1, result["Items"].Count);
        Assert.Equal(1, result["Items"][0].IntValue);
    }

    [Fact]
    public void TestDelta_CompoundWithList_ListCleared()
    {
        var obj1 = EchoObject.NewCompound();
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        obj1.Add("Items", list1);

        var obj2 = EchoObject.NewCompound();
        var list2 = EchoObject.NewList();
        obj2.Add("Items", list2);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(0, result["Items"].Count);
    }

    [Fact]
    public void TestDelta_CompoundWithList_BothChange()
    {
        // Both the list and other fields change simultaneously
        var obj1 = EchoObject.NewCompound();
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        obj1.Add("Items", list1);
        obj1.Add("Name", new EchoObject("old"));
        obj1.Add("Count", new EchoObject(1));

        var obj2 = EchoObject.NewCompound();
        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(3));
        obj2.Add("Items", list2);
        obj2.Add("Name", new EchoObject("new"));
        obj2.Add("Count", new EchoObject(3));

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal("new", result["Name"].StringValue);
        Assert.Equal(3, result["Count"].IntValue);
        Assert.Equal(3, result["Items"].Count);
        Assert.Equal(1, result["Items"][0].IntValue);
        Assert.Equal(2, result["Items"][1].IntValue);
        Assert.Equal(3, result["Items"][2].IntValue);
    }

    [Fact]
    public void TestDelta_CompoundWithList_ListAddedToCompound()
    {
        // Compound gains a new list field
        var obj1 = EchoObject.NewCompound();
        obj1.Add("Name", new EchoObject("test"));

        var obj2 = EchoObject.NewCompound();
        obj2.Add("Name", new EchoObject("test"));
        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(2));
        obj2.Add("Items", list2);

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.True(result.Contains("Items"));
        Assert.Equal(2, result["Items"].Count);
        Assert.Equal(1, result["Items"][0].IntValue);
        Assert.Equal(2, result["Items"][1].IntValue);
    }

    [Fact]
    public void TestDelta_CompoundWithList_ListRemovedFromCompound()
    {
        // Compound loses a list field
        var obj1 = EchoObject.NewCompound();
        obj1.Add("Name", new EchoObject("test"));
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        obj1.Add("Items", list1);

        var obj2 = EchoObject.NewCompound();
        obj2.Add("Name", new EchoObject("test"));

        var delta = EchoObject.CreateDelta(obj1, obj2);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.False(result.Contains("Items"));
    }

    #endregion

    #region List of Compounds Delta Tests

    [Fact]
    public void TestDelta_ListOfCompounds_ModifyField()
    {
        var c1a = EchoObject.NewCompound();
        c1a.Add("Name", new EchoObject("Alice"));
        c1a.Add("Score", new EchoObject(100));
        var c1b = EchoObject.NewCompound();
        c1b.Add("Name", new EchoObject("Bob"));
        c1b.Add("Score", new EchoObject(200));

        var list1 = EchoObject.NewList();
        list1.ListAdd(c1a);
        list1.ListAdd(c1b);

        var c2a = EchoObject.NewCompound();
        c2a.Add("Name", new EchoObject("Alice"));
        c2a.Add("Score", new EchoObject(150)); // changed
        var c2b = EchoObject.NewCompound();
        c2b.Add("Name", new EchoObject("Bob"));
        c2b.Add("Score", new EchoObject(200));

        var list2 = EchoObject.NewList();
        list2.ListAdd(c2a);
        list2.ListAdd(c2b);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alice", result[0]["Name"].StringValue);
        Assert.Equal(150, result[0]["Score"].IntValue);
        Assert.Equal("Bob", result[1]["Name"].StringValue);
        Assert.Equal(200, result[1]["Score"].IntValue);
    }

    [Fact]
    public void TestDelta_ListOfCompounds_AddCompound()
    {
        var c1a = EchoObject.NewCompound();
        c1a.Add("Name", new EchoObject("Alice"));

        var list1 = EchoObject.NewList();
        list1.ListAdd(c1a);

        var c2a = EchoObject.NewCompound();
        c2a.Add("Name", new EchoObject("Alice"));
        var c2b = EchoObject.NewCompound();
        c2b.Add("Name", new EchoObject("Bob"));

        var list2 = EchoObject.NewList();
        list2.ListAdd(c2a);
        list2.ListAdd(c2b);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alice", result[0]["Name"].StringValue);
        Assert.Equal("Bob", result[1]["Name"].StringValue);
    }

    [Fact]
    public void TestDelta_ListOfCompounds_RemoveCompound()
    {
        var c1a = EchoObject.NewCompound();
        c1a.Add("Name", new EchoObject("Alice"));
        var c1b = EchoObject.NewCompound();
        c1b.Add("Name", new EchoObject("Bob"));
        var c1c = EchoObject.NewCompound();
        c1c.Add("Name", new EchoObject("Charlie"));

        var list1 = EchoObject.NewList();
        list1.ListAdd(c1a);
        list1.ListAdd(c1b);
        list1.ListAdd(c1c);

        var c2a = EchoObject.NewCompound();
        c2a.Add("Name", new EchoObject("Alice"));
        var c2b = EchoObject.NewCompound();
        c2b.Add("Name", new EchoObject("Bob"));

        var list2 = EchoObject.NewList();
        list2.ListAdd(c2a);
        list2.ListAdd(c2b);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alice", result[0]["Name"].StringValue);
        Assert.Equal("Bob", result[1]["Name"].StringValue);
    }

    [Fact]
    public void TestDelta_ListOfCompounds_AddFieldToCompound()
    {
        // Compound inside list gains a new field
        var c1a = EchoObject.NewCompound();
        c1a.Add("Name", new EchoObject("Alice"));

        var list1 = EchoObject.NewList();
        list1.ListAdd(c1a);

        var c2a = EchoObject.NewCompound();
        c2a.Add("Name", new EchoObject("Alice"));
        c2a.Add("Score", new EchoObject(100));

        var list2 = EchoObject.NewList();
        list2.ListAdd(c2a);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(1, result.Count);
        Assert.Equal("Alice", result[0]["Name"].StringValue);
        Assert.True(result[0].Contains("Score"));
        Assert.Equal(100, result[0]["Score"].IntValue);
    }

    [Fact]
    public void TestDelta_ListOfCompounds_RemoveFieldFromCompound()
    {
        var c1a = EchoObject.NewCompound();
        c1a.Add("Name", new EchoObject("Alice"));
        c1a.Add("Score", new EchoObject(100));
        c1a.Add("Level", new EchoObject(5));

        var list1 = EchoObject.NewList();
        list1.ListAdd(c1a);

        var c2a = EchoObject.NewCompound();
        c2a.Add("Name", new EchoObject("Alice"));
        c2a.Add("Score", new EchoObject(100));

        var list2 = EchoObject.NewList();
        list2.ListAdd(c2a);

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(1, result.Count);
        Assert.False(result[0].Contains("Level"));
        Assert.Equal(2, result[0].Count);
    }

    #endregion

    #region List Delta Serialization Tests

    [Fact]
    public void TestDelta_ListSerialization_Binary()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(3));

        var delta = EchoObject.CreateDelta(list1, list2);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        delta.WriteToBinary(writer);

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var deserializedDelta = EchoObject.ReadFromBinary(reader);

        var result = EchoObject.ApplyDelta(list1, deserializedDelta);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
        Assert.Equal(3, result[2].IntValue);
    }

    [Fact]
    public void TestDelta_ListSerialization_String()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject("a"));
        list1.ListAdd(new EchoObject("b"));
        list1.ListAdd(new EchoObject("c"));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject("x"));
        list2.ListAdd(new EchoObject("b"));

        var delta = EchoObject.CreateDelta(list1, list2);
        string serialized = delta.WriteToString();
        var deserializedDelta = EchoObject.ReadFromString(serialized);

        var result = EchoObject.ApplyDelta(list1, deserializedDelta);

        Assert.Equal(2, result.Count);
        Assert.Equal("x", result[0].StringValue);
        Assert.Equal("b", result[1].StringValue);
    }

    [Fact]
    public void TestDelta_NestedListSerialization_String()
    {
        var inner1 = EchoObject.NewList();
        inner1.ListAdd(new EchoObject(1));
        var outer1 = EchoObject.NewList();
        outer1.ListAdd(inner1);

        var inner2 = EchoObject.NewList();
        inner2.ListAdd(new EchoObject(1));
        inner2.ListAdd(new EchoObject(2));
        var outer2 = EchoObject.NewList();
        outer2.ListAdd(inner2);

        var delta = EchoObject.CreateDelta(outer1, outer2);
        string serialized = delta.WriteToString();
        var deserializedDelta = EchoObject.ReadFromString(serialized);

        var result = EchoObject.ApplyDelta(outer1, deserializedDelta);

        Assert.Equal(1, result.Count);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(1, result[0][0].IntValue);
        Assert.Equal(2, result[0][1].IntValue);
    }

    #endregion

    #region Sequential List Delta Tests

    [Fact]
    public void TestDelta_List_SequentialGrowth()
    {
        // Start with empty, grow step by step
        var v1 = EchoObject.NewList();

        var v2 = EchoObject.NewList();
        v2.ListAdd(new EchoObject(1));

        var v3 = EchoObject.NewList();
        v3.ListAdd(new EchoObject(1));
        v3.ListAdd(new EchoObject(2));

        var v4 = EchoObject.NewList();
        v4.ListAdd(new EchoObject(1));
        v4.ListAdd(new EchoObject(2));
        v4.ListAdd(new EchoObject(3));

        var d1 = EchoObject.CreateDelta(v1, v2);
        var d2 = EchoObject.CreateDelta(v2, v3);
        var d3 = EchoObject.CreateDelta(v3, v4);

        var result = EchoObject.ApplyDelta(v1, d1);
        result = EchoObject.ApplyDelta(result, d2);
        result = EchoObject.ApplyDelta(result, d3);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
        Assert.Equal(3, result[2].IntValue);
    }

    [Fact]
    public void TestDelta_List_SequentialShrink()
    {
        // Start populated, shrink step by step
        var v1 = EchoObject.NewList();
        v1.ListAdd(new EchoObject(1));
        v1.ListAdd(new EchoObject(2));
        v1.ListAdd(new EchoObject(3));

        var v2 = EchoObject.NewList();
        v2.ListAdd(new EchoObject(1));
        v2.ListAdd(new EchoObject(2));

        var v3 = EchoObject.NewList();
        v3.ListAdd(new EchoObject(1));

        var v4 = EchoObject.NewList();

        var d1 = EchoObject.CreateDelta(v1, v2);
        var d2 = EchoObject.CreateDelta(v2, v3);
        var d3 = EchoObject.CreateDelta(v3, v4);

        var result = EchoObject.ApplyDelta(v1, d1);
        result = EchoObject.ApplyDelta(result, d2);
        result = EchoObject.ApplyDelta(result, d3);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void TestDelta_List_GrowThenShrink()
    {
        var v1 = EchoObject.NewList();
        v1.ListAdd(new EchoObject(1));

        var v2 = EchoObject.NewList();
        v2.ListAdd(new EchoObject(1));
        v2.ListAdd(new EchoObject(2));
        v2.ListAdd(new EchoObject(3));
        v2.ListAdd(new EchoObject(4));
        v2.ListAdd(new EchoObject(5));

        var v3 = EchoObject.NewList();
        v3.ListAdd(new EchoObject(1));
        v3.ListAdd(new EchoObject(2));

        var d1 = EchoObject.CreateDelta(v1, v2);
        var d2 = EchoObject.CreateDelta(v2, v3);

        var result = EchoObject.ApplyDelta(v1, d1);
        result = EchoObject.ApplyDelta(result, d2);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
    }

    [Fact]
    public void TestDelta_List_SequentialModifications()
    {
        // Repeatedly modify the same element
        var v1 = EchoObject.NewList();
        v1.ListAdd(new EchoObject(1));
        v1.ListAdd(new EchoObject(2));
        v1.ListAdd(new EchoObject(3));

        var v2 = EchoObject.NewList();
        v2.ListAdd(new EchoObject(10));
        v2.ListAdd(new EchoObject(2));
        v2.ListAdd(new EchoObject(3));

        var v3 = EchoObject.NewList();
        v3.ListAdd(new EchoObject(100));
        v3.ListAdd(new EchoObject(2));
        v3.ListAdd(new EchoObject(3));

        var d1 = EchoObject.CreateDelta(v1, v2);
        var d2 = EchoObject.CreateDelta(v2, v3);

        var result = EchoObject.ApplyDelta(v1, d1);
        result = EchoObject.ApplyDelta(result, d2);

        Assert.Equal(3, result.Count);
        Assert.Equal(100, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
        Assert.Equal(3, result[2].IntValue);
    }

    #endregion

    #region Serialized Object List Delta Tests

    [Fact]
    public void TestDelta_SerializedList_AddItem()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int> { 10, 20, 30 }
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int> { 10, 20, 30, 40, 50 }
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.Equal(5, resultObj.Numbers.Count);
        Assert.Equal(new List<int> { 10, 20, 30, 40, 50 }, resultObj.Numbers);
    }

    [Fact]
    public void TestDelta_SerializedList_RemoveItem()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int> { 10, 20, 30, 40, 50 }
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int> { 10, 20 }
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.Equal(2, resultObj.Numbers.Count);
        Assert.Equal(new List<int> { 10, 20 }, resultObj.Numbers);
    }

    [Fact]
    public void TestDelta_SerializedList_ModifyItem()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int> { 10, 20, 30 }
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int> { 10, 99, 30 }
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.Equal(new List<int> { 10, 99, 30 }, resultObj.Numbers);
    }

    [Fact]
    public void TestDelta_SerializedList_NullToList()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = null
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int> { 1, 2, 3 }
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.NotNull(resultObj.Numbers);
        Assert.Equal(new List<int> { 1, 2, 3 }, resultObj.Numbers);
    }

    [Fact]
    public void TestDelta_SerializedList_ListToNull()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int> { 1, 2, 3 }
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = null
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.Null(resultObj.Numbers);
    }

    [Fact]
    public void TestDelta_SerializedList_EmptyToPopulated()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int>()
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int> { 1, 2, 3 }
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.Equal(new List<int> { 1, 2, 3 }, resultObj.Numbers);
    }

    [Fact]
    public void TestDelta_SerializedList_PopulatedToEmpty()
    {
        var obj1 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int> { 1, 2, 3 }
        };

        var obj2 = new DeltaComplexObject
        {
            Id = 1,
            Name = "Test",
            Numbers = new List<int>()
        };

        var echo1 = Serializer.Serialize(obj1);
        var echo2 = Serializer.Serialize(obj2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultObj = Serializer.Deserialize<DeltaComplexObject>(result);

        Assert.NotNull(resultObj.Numbers);
        Assert.Empty(resultObj.Numbers);
    }

    [Fact]
    public void TestDelta_SerializedInventory_FullReplace()
    {
        var player1 = new Player
        {
            Name = "Alice",
            Inventory = new List<string> { "Sword", "Shield", "Potion" }
        };

        var player2 = new Player
        {
            Name = "Alice",
            Inventory = new List<string> { "Staff", "Robe", "Wand", "Scroll" }
        };

        var echo1 = Serializer.Serialize(player1);
        var echo2 = Serializer.Serialize(player2);

        var delta = EchoObject.CreateDelta(echo1, echo2);
        var result = EchoObject.ApplyDelta(echo1, delta);
        var resultPlayer = Serializer.Deserialize<Player>(result);

        Assert.Equal(4, resultPlayer.Inventory.Count);
        Assert.Equal("Staff", resultPlayer.Inventory[0]);
        Assert.Equal("Robe", resultPlayer.Inventory[1]);
        Assert.Equal("Wand", resultPlayer.Inventory[2]);
        Assert.Equal("Scroll", resultPlayer.Inventory[3]);
    }

    #endregion

    #region List Delta Idempotency Tests

    [Fact]
    public void TestDelta_List_ApplyTwiceProducesSameResult()
    {
        // Applying the same delta twice should not corrupt data
        // (Note: applying the same delta to the already-changed data may not be meaningful,
        //  but it should at least not crash)
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));
        list1.ListAdd(new EchoObject(3));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(1));
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(3));
        list2.ListAdd(new EchoObject(4));

        var delta = EchoObject.CreateDelta(list1, list2);

        var result1 = EchoObject.ApplyDelta(list1, delta);
        var result2 = EchoObject.ApplyDelta(list1, delta);

        // Both applications to the same baseline should produce identical results
        Assert.Equal(result1.Count, result2.Count);
        for (int i = 0; i < result1.Count; i++)
        {
            Assert.Equal(result1[i].IntValue, result2[i].IntValue);
        }
    }

    [Fact]
    public void TestDelta_List_EmptyDeltaNoChange()
    {
        var list = EchoObject.NewList();
        list.ListAdd(new EchoObject(1));
        list.ListAdd(new EchoObject(2));
        list.ListAdd(new EchoObject(3));

        var delta = EchoObject.CreateDelta(list, list.Clone());
        var result = EchoObject.ApplyDelta(list, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
        Assert.Equal(3, result[2].IntValue);
    }

    [Fact]
    public void TestDelta_List_DeltaDoesNotMutateBaseline()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(2));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(99));
        list2.ListAdd(new EchoObject(88));
        list2.ListAdd(new EchoObject(77));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        // Original should be untouched
        Assert.Equal(2, list1.Count);
        Assert.Equal(1, list1[0].IntValue);
        Assert.Equal(2, list1[1].IntValue);

        // Result should be the new values
        Assert.Equal(3, result.Count);
        Assert.Equal(99, result[0].IntValue);
    }

    #endregion

    #region List with String Elements Delta Tests

    [Fact]
    public void TestDelta_List_Strings_ModifyElements()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject("alpha"));
        list1.ListAdd(new EchoObject("beta"));
        list1.ListAdd(new EchoObject("gamma"));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject("alpha"));
        list2.ListAdd(new EchoObject("BETA"));
        list2.ListAdd(new EchoObject("gamma"));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal("alpha", result[0].StringValue);
        Assert.Equal("BETA", result[1].StringValue);
        Assert.Equal("gamma", result[2].StringValue);
    }

    [Fact]
    public void TestDelta_List_Strings_EmptyStrings()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject("hello"));
        list1.ListAdd(new EchoObject("world"));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(""));
        list2.ListAdd(new EchoObject(""));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(2, result.Count);
        Assert.Equal("", result[0].StringValue);
        Assert.Equal("", result[1].StringValue);
    }

    [Fact]
    public void TestDelta_List_DuplicateValues()
    {
        var list1 = EchoObject.NewList();
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(1));
        list1.ListAdd(new EchoObject(1));

        var list2 = EchoObject.NewList();
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(2));
        list2.ListAdd(new EchoObject(2));

        var delta = EchoObject.CreateDelta(list1, list2);
        var result = EchoObject.ApplyDelta(list1, delta);

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
        Assert.Equal(2, result[2].IntValue);
    }

    #endregion

    #region List Type Conversion Delta Tests

    [Fact]
    public void TestDelta_PrimitiveToList()
    {
        var obj1 = new EchoObject(42);

        var list = EchoObject.NewList();
        list.ListAdd(new EchoObject(1));
        list.ListAdd(new EchoObject(2));

        var delta = EchoObject.CreateDelta(obj1, list);
        var result = EchoObject.ApplyDelta(obj1, delta);

        Assert.Equal(EchoType.List, result.TagType);
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
    }

    [Fact]
    public void TestDelta_ListToPrimitive()
    {
        var list = EchoObject.NewList();
        list.ListAdd(new EchoObject(1));
        list.ListAdd(new EchoObject(2));

        var obj2 = new EchoObject(42);

        var delta = EchoObject.CreateDelta(list, obj2);
        var result = EchoObject.ApplyDelta(list, delta);

        Assert.Equal(EchoType.Int, result.TagType);
        Assert.Equal(42, result.IntValue);
    }

    [Fact]
    public void TestDelta_ListToCompound()
    {
        var list = EchoObject.NewList();
        list.ListAdd(new EchoObject(1));
        list.ListAdd(new EchoObject(2));

        var compound = EchoObject.NewCompound();
        compound.Add("Key", new EchoObject("Value"));

        var delta = EchoObject.CreateDelta(list, compound);
        var result = EchoObject.ApplyDelta(list, delta);

        Assert.Equal(EchoType.Compound, result.TagType);
        Assert.True(result.Contains("Key"));
        Assert.Equal("Value", result["Key"].StringValue);
    }

    [Fact]
    public void TestDelta_CompoundToList()
    {
        var compound = EchoObject.NewCompound();
        compound.Add("Key", new EchoObject("Value"));

        var list = EchoObject.NewList();
        list.ListAdd(new EchoObject(1));
        list.ListAdd(new EchoObject(2));

        var delta = EchoObject.CreateDelta(compound, list);
        var result = EchoObject.ApplyDelta(compound, delta);

        Assert.Equal(EchoType.List, result.TagType);
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].IntValue);
        Assert.Equal(2, result[1].IntValue);
    }

    #endregion
}
