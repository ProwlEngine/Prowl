// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Test;

public class EchoObject_Query_Tests
{
    private static EchoObject CreateTestData()
    {
        var root = EchoObject.NewCompound();

        // Add player data
        var player = EchoObject.NewCompound();
        root["Player"] = player;

        // Add inventory
        var inventory = EchoObject.NewList();
        player["Inventory"] = inventory;

        // Add items
        var item1 = EchoObject.NewCompound();
        item1["Id"] = new EchoObject("gold_ingot");
        item1["Count"] = new EchoObject(64);
        item1["Value"] = new EchoObject(100);
        inventory.ListAdd(item1);

        var item2 = EchoObject.NewCompound();
        item2["Id"] = new EchoObject("iron_ingot");
        item2["Count"] = new EchoObject(32);
        item2["Value"] = new EchoObject(50);
        inventory.ListAdd(item2);

        // Add stats
        var stats = EchoObject.NewCompound();
        player["Stats"] = stats;
        stats["Health"] = new EchoObject(100);
        stats["Mana"] = new EchoObject(50);

        return root;
    }

    [Fact]
    public void GetValue_PrimitiveTypeConversions()
    {
        var echo = EchoObject.NewCompound();
        echo["int"] = new EchoObject(42);
        echo["float"] = new EchoObject(42.5f);
        echo["bool"] = new EchoObject(true);

        // Test various numeric conversions
        Assert.Equal(42, echo.GetValue<int>("int"));
        Assert.Equal(42L, echo.GetValue<long>("int"));
        Assert.Equal(42.0f, echo.GetValue<float>("int"));
        Assert.Equal(42.0, echo.GetValue<double>("int"));
        Assert.Equal(42.0m, echo.GetValue<decimal>("int"));

        // Test float conversions
        Assert.Equal(42.5f, echo.GetValue<float>("float"));
        Assert.Equal(42, echo.GetValue<int>("float"));

        // Test bool
        Assert.True(echo.GetValue<bool>("bool"));
    }

    [Fact]
    public void GetValue_CompoundAndList()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        var compound = EchoObject.NewCompound();

        root["list"] = list;
        root["compound"] = compound;

        // Test getting as EchoObject
        Assert.NotNull(root.GetValue<EchoObject>("list"));
        Assert.NotNull(root.GetValue<EchoObject>("compound"));

        // Test getting as specific collections
        Assert.NotNull(root.GetValue<List<EchoObject>>("list"));
        Assert.NotNull(root.GetValue<Dictionary<string, EchoObject>>("compound"));

        // Test convenience methods
        Assert.NotNull(root.GetListAt("list"));
        Assert.NotNull(root.GetEchoAt("compound"));
        Assert.NotNull(root.GetDictionaryAt("compound"));
    }

    [Fact]
    public void GetValue_InvalidConversions()
    {
        var echo = EchoObject.NewCompound();
        echo["string"] = new EchoObject("not a number");

        // Should return default values for invalid conversions
        Assert.Equal(0, echo.GetValue<int>("string", 0));
        Assert.Equal(-1, echo.GetValue<int>("nonexistent", -1));
    }

    [Fact]
    public void GetValue_ByteArray()
    {
        var echo = EchoObject.NewCompound();
        var bytes = new byte[] { 1, 2, 3, 4 };
        echo["bytes"] = new EchoObject(bytes);

        var result = echo.GetValue<byte[]>("bytes");
        Assert.NotNull(result);
        Assert.Equal(bytes, result);
    }

    [Fact]
    public void GetValue_ComplexHierarchy()
    {
        var root = EchoObject.NewCompound();
        var players = EchoObject.NewList();
        root["players"] = players;

        var player = EchoObject.NewCompound();
        player["name"] = new EchoObject("Player1");
        players.ListAdd(player);

        // Test deep path with list index
        Assert.Equal("Player1", root.GetValue<string>("players/0/name"));

        // Test getting intermediate collections
        Assert.NotNull(root.GetListAt("players"));
        Assert.NotNull(root.GetEchoAt("players/0"));
        Assert.NotNull(root.GetDictionaryAt("players/0"));
    }

    [Theory]
    [InlineData(EchoType.Int, typeof(byte))]
    [InlineData(EchoType.Int, typeof(short))]
    [InlineData(EchoType.Int, typeof(long))]
    [InlineData(EchoType.Float, typeof(double))]
    [InlineData(EchoType.Double, typeof(float))]
    public void GetValue_NumericConversions(EchoType sourceType, Type targetType)
    {
        var echo = EchoObject.NewCompound();
        echo["value"] = new EchoObject(sourceType, 42);

        var method = typeof(EchoObject).GetMethod("GetValue")!.MakeGenericMethod(targetType);
        var result = method.Invoke(echo, new object?[] { "value", null });

        Assert.NotNull(result);
        Assert.Equal(42f, Convert.ChangeType(result, typeof(float)));
    }


    [Fact]
    public void Find_WithValidPath_ReturnsCorrectObject()
    {
        var root = CreateTestData();

        var result = root.Find("Player/Stats/Health");
        Assert.NotNull(result);
        Assert.Equal(100, result.IntValue);
    }

    [Fact]
    public void Find_WithListIndex_ReturnsCorrectObject()
    {
        var root = CreateTestData();

        var result = root.Find("Player/Inventory/0/Id");
        Assert.NotNull(result);
        Assert.Equal("gold_ingot", result.StringValue);
    }

    [Fact]
    public void Find_WithInvalidPath_ReturnsNull()
    {
        var root = CreateTestData();

        var result = root.Find("Player/NonExistent");
        Assert.Null(result);
    }

    [Fact]
    public void Find_WithInvalidListIndex_ReturnsNull()
    {
        var root = CreateTestData();

        var result = root.Find("Player/Inventory/999");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Find_WithEmptyPath_ReturnsSelf(string path)
    {
        var root = CreateTestData();

        var result = root.Find(path);
        Assert.Same(root, result);
    }

    [Fact]
    public void Where_OnList_ReturnsFilteredItems()
    {
        var root = CreateTestData();
        var inventory = root.Find("Player/Inventory");

        Assert.NotNull(inventory);

        var highValueItems = inventory.Where(item => item["Value"].IntValue > 75).ToList();

        Assert.Single(highValueItems);
        Assert.Equal("gold_ingot", highValueItems[0]["Id"]?.StringValue);
    }

    [Fact]
    public void Select_OnList_TransformsItems()
    {
        var root = CreateTestData();
        var inventory = root.Find("Player/Inventory");

        Assert.NotNull(inventory);

        var itemIds = inventory.Select(item => item["Id"]?.StringValue).ToList();

        Assert.Equal(2, itemIds.Count);
        Assert.Contains("gold_ingot", itemIds);
        Assert.Contains("iron_ingot", itemIds);
    }

    [Fact]
    public void FindAll_WithPredicate_ReturnsAllMatches()
    {
        var root = CreateTestData();

        var allInt = root.FindAll(tag => tag.TagType == EchoType.Int).ToList();

        Assert.Equal(6, allInt.Count);
    }

    [Fact]
    public void GetValue_WithValidPath_ReturnsTypedValue()
    {
        var root = CreateTestData();

        int health = root.GetValue<int>("Player/Stats/Health", -1);
        string? itemId = root.GetValue<string>("Player/Inventory/0/Id", "");

        Assert.Equal(100, health);
        Assert.Equal("gold_ingot", itemId);
    }

    [Fact]
    public void GetValue_WithInvalidPath_ReturnsDefault()
    {
        var root = CreateTestData();

        int value = root.GetValue<int>("NonExistent/Path", -1);
        string? text = root.GetValue<string>("NonExistent/Path", "default");

        Assert.Equal(-1, value);
        Assert.Equal("default", text);
    }

    [Fact]
    public void Exists_WithValidPath_ReturnsTrue()
    {
        var root = CreateTestData();

        Assert.True(root.Exists("Player/Stats/Health"));
        Assert.True(root.Exists("Player/Inventory/0"));
    }

    [Fact]
    public void Exists_WithInvalidPath_ReturnsFalse()
    {
        var root = CreateTestData();

        Assert.False(root.Exists("Player/NonExistent"));
        Assert.False(root.Exists("Player/Inventory/999"));
    }

    [Fact]
    public void GetPathsTo_FindsAllMatchingPaths()
    {
        var root = CreateTestData();

        var valuePaths = root.GetPathsTo(tag =>
            tag.TagType == EchoType.Int &&
            tag.IntValue > 75).ToList();

        Assert.Equal(2, valuePaths.Count);
        Assert.Contains("Player/Stats/Health", valuePaths);
        Assert.Contains("Player/Inventory/0/Value", valuePaths);
    }

    [Fact]
    public void ChainedQueries_WorkCorrectly()
    {
        var root = CreateTestData();

        var highValueItemIds = root.Find("Player/Inventory")?
            .Where(item => item.GetValue<int>("Value", 0) > 75)
            .Select(item => item.GetValue<string>("Id", ""))
            .ToList();

        Assert.NotNull(highValueItemIds);
        Assert.Single(highValueItemIds);
        Assert.Equal("gold_ingot", highValueItemIds[0]);
    }

    [Fact]
    public void DeepQuery_WithComplexConditions()
    {
        var root = CreateTestData();

        var results = root.FindAll(tag =>
            tag.TagType == EchoType.Int &&
            tag.Parent?.TagType == EchoType.Compound &&
            tag.Parent.Parent?.TagType == EchoType.List)
            .ToList();

        Assert.Equal(4, results.Count); // Should find Count and Value for both items
    }

    [Fact]
    public void ListOperations_WithModification()
    {
        var root = CreateTestData();
        var inventory = root.Find("Player/Inventory");
        Assert.NotNull(inventory);

        // Add new item
        var newItem = EchoObject.NewCompound();
        newItem["Id"] = new EchoObject("diamond");
        newItem["Count"] = new EchoObject(1);
        newItem["Value"] = new EchoObject(1000);
        inventory.ListAdd(newItem);

        // Verify through query
        var highestValue = inventory
            .Select(item => item.GetValue<int>("Value", 0))
            .Max();
        Assert.Equal(1000, highestValue);
    }

    [Fact]
    public void GetPath_ReturnsCorrectPath()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root["items"] = list;

        var item = EchoObject.NewCompound();
        list.ListAdd(item);
        item["name"] = new EchoObject("test");

        Assert.Equal("items", list.GetPath());
        Assert.Equal("items/0", item.GetPath());
        Assert.Equal("items/0/name", item["name"].GetPath());
    }

    [Fact]
    public void GetPath_WithMultipleLevels_ReturnsCorrectPath()
    {
        var root = EchoObject.NewCompound();
        var players = EchoObject.NewList();
        root["players"] = players;

        var player = EchoObject.NewCompound();
        players.ListAdd(player);

        var inventory = EchoObject.NewList();
        player["inventory"] = inventory;

        var item = EchoObject.NewCompound();
        inventory.ListAdd(item);
        item["name"] = new EchoObject("sword");

        Assert.Equal("players/0/inventory/0/name", item["name"].GetPath());
    }

    [Fact]
    public void GetRelativePath_MustExistInside()
    {
        var container = EchoObject.NewCompound();
        var subContainer = EchoObject.NewCompound();
        var item = new EchoObject("test");

        container.Add("sub", subContainer);
        subContainer.Add("item", item);

        // These should work
        Assert.Equal("sub", EchoObject.GetRelativePath(container, subContainer));
        Assert.Equal("sub/item", EchoObject.GetRelativePath(container, item));
        Assert.Equal("item", EchoObject.GetRelativePath(subContainer, item));

        // These should throw
        Assert.Throws<ArgumentException>(() => EchoObject.GetRelativePath(item, container));
        Assert.Throws<ArgumentException>(() => EchoObject.GetRelativePath(subContainer, container));
    }

    [Fact]
    public void CompoundKey_And_ListIndex_AreSetCorrectly()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root["items"] = list;

        var item1 = EchoObject.NewCompound();
        var item2 = EchoObject.NewCompound();
        list.ListAdd(item1);
        list.ListAdd(item2);

        // Test CompoundKey
        Assert.Equal("items", list.CompoundKey);

        // Test ListIndex
        Assert.Equal(0, item1.ListIndex);
        Assert.Equal(1, item2.ListIndex);

        // Test both with nested structure
        item1["name"] = new EchoObject("sword");
        Assert.Equal("name", item1["name"].CompoundKey);
        Assert.Equal(null, item1["name"].ListIndex); // Not in a list
    }

    [Fact]
    public void GetPath_Uses_CompoundKey_And_ListIndex()
    {
        var root = EchoObject.NewCompound();
        var players = EchoObject.NewList();
        root["players"] = players;  // CompoundKey = "players"

        var player = EchoObject.NewCompound();
        players.ListAdd(player);    // ListIndex = 0

        var items = EchoObject.NewList();
        player["inventory"] = items;  // CompoundKey = "inventory"

        var item = EchoObject.NewCompound();
        items.ListAdd(item);         // ListIndex = 0

        item["name"] = new EchoObject("sword");  // CompoundKey = "name"

        Assert.Equal("players/0/inventory/0/name", item["name"].GetPath());
    }
}