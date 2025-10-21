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
}
