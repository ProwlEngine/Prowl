// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Test;

public class EchoObject_KeyIndex_Tracking_Tests
{
    [Fact]
    public void ListIndex_IsNull_WhenNotInList()
    {
        var item = EchoObject.NewCompound();
        Assert.Null(item.ListIndex);
    }

    [Fact]
    public void ListIndex_IsSet_WhenAddedToList()
    {
        var list = EchoObject.NewList();
        var item1 = new EchoObject("first");
        var item2 = new EchoObject("second");

        list.ListAdd(item1);
        list.ListAdd(item2);

        Assert.Equal(0, item1.ListIndex);
        Assert.Equal(1, item2.ListIndex);
    }

    [Fact]
    public void ListIndex_UpdatesOnRemoval()
    {
        var list = EchoObject.NewList();
        var item1 = new EchoObject("first");
        var item2 = new EchoObject("second");
        var item3 = new EchoObject("third");

        list.ListAdd(item1);
        list.ListAdd(item2);
        list.ListAdd(item3);

        list.ListRemove(item1);

        Assert.Null(item1.ListIndex); // Removed item
        Assert.Equal(0, item2.ListIndex); // Should shift down
        Assert.Equal(1, item3.ListIndex);
    }

    [Fact]
    public void CompoundKey_IsNull_WhenNotInCompound()
    {
        var item = new EchoObject("test");
        Assert.Null(item.CompoundKey);
    }

    [Fact]
    public void CompoundKey_IsSet_WhenAddedToCompound()
    {
        var compound = EchoObject.NewCompound();
        var item = new EchoObject("test");

        compound["myKey"] = item;

        Assert.Equal("myKey", item.CompoundKey);
    }

    [Fact]
    public void CompoundKey_UpdatesOnRemoval()
    {
        var compound = EchoObject.NewCompound();
        var item = new EchoObject("test");

        compound["myKey"] = item;
        Assert.Equal("myKey", item.CompoundKey);

        compound.Remove("myKey");
        Assert.Null(item.CompoundKey);
    }

    [Fact]
    public void CompoundKey_UpdatesOnReassignment()
    {
        var compound = EchoObject.NewCompound();
        var item = new EchoObject("test");

        compound["key1"] = item;
        Assert.Equal("key1", item.CompoundKey);

        item.Parent.Remove(item.CompoundKey);

        compound["key2"] = item; // Move to new key
        Assert.Equal("key2", item.CompoundKey);
    }

    [Fact]
    public void ListIndex_And_CompoundKey_AreExclusive()
    {
        var list = EchoObject.NewList();
        var compound = EchoObject.NewCompound();
        var item = new EchoObject("test");

        list.ListAdd(item);
        Assert.NotNull(item.ListIndex);
        Assert.Null(item.CompoundKey);

        list.ListRemove(item);
        compound["key"] = item;
        Assert.Null(item.ListIndex);
        Assert.NotNull(item.CompoundKey);
    }
}
