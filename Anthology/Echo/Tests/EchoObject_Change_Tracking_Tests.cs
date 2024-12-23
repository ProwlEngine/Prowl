// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Test;

public class EchoObject_Change_Tracking_Tests
{
    [Fact]
    public void ChangeTracking_TracksValueChanges()
    {
        var root = EchoObject.NewCompound();
        EchoChangeEventArgs? capturedEvent = null;
        root.PropertyChanged += (s, e) => capturedEvent = e;

        root["value"] = new EchoObject("initial");
        Assert.NotNull(capturedEvent);
        Assert.Equal("value", capturedEvent.Path);
        Assert.Equal(ChangeType.TagAdded, capturedEvent.Type);
    }

    [Fact]
    public void ChangeTracking_TracksListChanges()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root["list"] = list;

        var events = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => events.Add(e);

        var item = new EchoObject("test");
        list.ListAdd(item);

        Assert.Contains(events, e =>
            e.Type == ChangeType.ListTagAdded &&
            e.Path == "list/0");
    }

    [Fact]
    public void ChangeTracking_TracksNestedChanges()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root["list"] = list;
        var item = new EchoObject("test");
        list.ListAdd(item);

        EchoChangeEventArgs? capturedEvent = null;
        root.PropertyChanged += (s, e) => capturedEvent = e;

        item.SetValue("changed");

        Assert.NotNull(capturedEvent);
        Assert.Equal("list/0", capturedEvent.Path);
        Assert.Equal("test", capturedEvent.OldValue);
        Assert.Equal("changed", capturedEvent.NewValue);
    }

    [Fact]
    public void ChangeTracking_CompoundAdd_TracksCorrectly()
    {
        var root = EchoObject.NewCompound();
        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        root.Add("test", new EchoObject("value"));

        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal(ChangeType.TagAdded, change.Type);
        Assert.Equal("test", change.Path);
        Assert.Equal("value", change.NewValue);
        Assert.Null(change.OldValue);
    }

    [Fact]
    public void ChangeTracking_NestedChanges_TrackCorrectPaths()
    {
        var root = EchoObject.NewCompound();
        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        // Create nested structure
        var player = EchoObject.NewCompound();
        root.Add("player", player);
        var inventory = EchoObject.NewList();
        player.Add("inventory", inventory);

        // Add item
        var item = new EchoObject("sword");
        inventory.ListAdd(item);

        // Change item value
        item.Value = "better sword";

        // Verify paths
        Assert.Contains(changes, e =>
            e.Path == "player" &&
            e.Type == ChangeType.TagAdded);

        Assert.Contains(changes, e =>
            e.Path == "player/inventory" &&
            e.Type == ChangeType.TagAdded);

        Assert.Contains(changes, e =>
            e.Path == "player/inventory/0" &&
            e.Type == ChangeType.ListTagAdded);

        Assert.Contains(changes, e =>
            e.Path == "player/inventory/0" &&
            e.Type == ChangeType.ValueChanged &&
            e.OldValue as string == "sword" &&
            e.NewValue as string == "better sword");
    }

    [Fact]
    public void ChangeTracking_ListOperations_TrackCorrectly()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root.Add("items", list);

        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        // Add items
        var item1 = new EchoObject("first");
        var item2 = new EchoObject("second");
        list.ListAdd(item1);
        list.ListAdd(item2);

        // Remove middle item
        list.ListRemove(item1);

        // Verify changes
        Assert.Contains(changes, e =>
            e.Path == "items/0" &&
            e.Type == ChangeType.ListTagAdded &&
            e.NewValue as string == "first");

        Assert.Contains(changes, e =>
            e.Path == "items/1" &&
            e.Type == ChangeType.ListTagAdded &&
            e.NewValue as string == "second");

        Assert.Contains(changes, e =>
            e.Path == "items/0" &&
            e.Type == ChangeType.ListTagRemoved &&
            e.OldValue as string == "first");

        // Verify index updates
        Assert.Contains(changes, e =>
            e.Type == ChangeType.ListTagMoved &&
            e.Path == "items/0" &&
            (int?)e.OldValue == 1 &&
            (int?)e.NewValue == 0);
    }

    [Fact]
    public void ChangeTracking_CompoundRename_TracksCorrectly()
    {
        var root = EchoObject.NewCompound();
        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        root.Add("oldName", new EchoObject("value"));
        root.Rename("oldName", "newName");

        Assert.Contains(changes, e =>
            e.Type == ChangeType.TagRenamed &&
            e.OldValue as string == "oldName" &&
            e.NewValue as string == "newName");
    }

    [Fact]
    public void ChangeTracking_DeepPathChanges()
    {
        var root = EchoObject.NewCompound();
        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        // Create deep structure
        root.Add("level1", EchoObject.NewCompound());
        root["level1"].Add("level2", EchoObject.NewCompound());
        root["level1"]["level2"].Add("level3", new EchoObject("initial"));

        // Change deep value
        root.Find("level1/level2/level3").Value = "changed";

        var lastChange = changes[^1];
        Assert.Equal("level1/level2/level3", lastChange.Path);
        Assert.Equal(ChangeType.ValueChanged, lastChange.Type);
        Assert.Equal("initial", lastChange.OldValue);
        Assert.Equal("changed", lastChange.NewValue);
    }

    [Fact]
    public void ChangeTracking_MultipleListeners()
    {
        var root = EchoObject.NewCompound();
        var player = EchoObject.NewCompound();
        root.Add("player", player);

        var rootChanges = new List<EchoChangeEventArgs>();
        var playerChanges = new List<EchoChangeEventArgs>();

        root.PropertyChanged += (s, e) => rootChanges.Add(e);
        player.PropertyChanged += (s, e) => playerChanges.Add(e);

        var steve = new EchoObject("Steve");
        player.Add("name", steve);

        Assert.Single(playerChanges);
        Assert.Single(rootChanges);

        Assert.Equal("name", playerChanges[0].RelativePath);
        Assert.Equal("player/name", rootChanges[0].RelativePath);
    }

    [Fact]
    public void ChangeTracking_RelativePaths()
    {
        var root = EchoObject.NewCompound();
        var player = EchoObject.NewCompound();
        root.Add("player", player);
        var inventory = EchoObject.NewList();
        player.Add("inventory", inventory);

        var changes = new List<EchoChangeEventArgs>();
        player.PropertyChanged += (s, e) => changes.Add(e);

        var item = new EchoObject("sword");
        inventory.ListAdd(item);
        item.Value = "better sword";

        Assert.Contains(changes, e =>
            e.RelativePath == "inventory/0" &&
            e.Path == "player/inventory/0");
    }

    [Fact]
    public void ChangeTracking_PreservesChangesAfterMove()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        root.Add("items", list);

        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        // Add items and move them
        var item1 = new EchoObject("first");
        var item2 = new EchoObject("second");
        list.ListAdd(item1);
        list.ListAdd(item2);
        list.ListRemove(item1);

        // Change value of moved item
        item2.Value = "modified";

        var lastChange = changes[^1];
        Assert.Equal("items/0", lastChange.Path);
        Assert.Equal("second", lastChange.OldValue);
        Assert.Equal("modified", lastChange.NewValue);
    }

    [Fact]
    public void ChangeTracking_CloningDoesntTriggerEvents()
    {
        var root = EchoObject.NewCompound();
        root.Add("value", new EchoObject("test"));

        var changes = new List<EchoChangeEventArgs>();
        root.PropertyChanged += (s, e) => changes.Add(e);

        var clone = root.Clone();
        Assert.Empty(changes);
    }
}