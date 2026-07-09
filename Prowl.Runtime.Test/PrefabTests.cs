// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for the Runtime prefab surface: <see cref="PrefabAsset.Instantiate"/> (cloning, prefab-id
/// stamping, nested-prefab boundaries), GameObject prefab tracking, and prefab-data serialization.
/// The editor-side override engine (apply/revert/detect) lives in Prowl.Editor and is out of scope here.
/// </summary>
public class PrefabTests : RuntimeTestBase
{
    /// <summary>Build a PrefabAsset whose source is the given GameObject tree.</summary>
    private static PrefabAsset MakePrefab(GameObject source, Guid assetId)
    {
        EchoObject data = Serializer.Serialize(typeof(object), source);
        return new PrefabAsset { GameObjectData = data, AssetID = assetId };
    }

    private static T RoundTrip<T>(T value) => Serializer.Deserialize<T>(Serializer.Serialize(value));

    // ---------------------------------------------------------------------
    // Instantiate
    // ---------------------------------------------------------------------

    [Fact]
    public void Instantiate_NullData_ReturnsNull()
    {
        var prefab = new PrefabAsset { GameObjectData = null };
        Assert.Null(prefab.Instantiate());
    }

    [Fact]
    public void Instantiate_StampsPrefabAssetId_AndMarksInstance()
    {
        var id = Guid.NewGuid();
        var prefab = MakePrefab(CreateGameObject("Root"), id);

        var instance = prefab.Instantiate();

        Assert.NotNull(instance);
        Assert.Equal(id, instance!.PrefabAssetId);
        Assert.True(instance.IsPrefabInstance);
    }

    [Fact]
    public void Instantiate_ClonesComponentsWithData()
    {
        var source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>().IntField = 17;
        var prefab = MakePrefab(source, Guid.NewGuid());

        var instance = prefab.Instantiate();

        var comp = instance!.GetComponent<SerializableComponent>();
        Assert.NotNull(comp);
        Assert.Equal(17, comp!.IntField);
        Assert.Same(instance, comp.GameObject);
    }

    [Fact]
    public void Instantiate_ClonesChildren()
    {
        var source = CreateGameObject("Root");
        var child = CreateGameObject("Child");
        child.SetParent(source);
        var prefab = MakePrefab(source, Guid.NewGuid());

        var instance = prefab.Instantiate();

        Assert.Single(instance!.Children);
        Assert.Equal("Child", instance.Children[0].Name);
        Assert.Same(instance, instance.Children[0].Parent);
    }

    [Fact]
    public void Instantiate_ProducesIndependentCopies()
    {
        var source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>().IntField = 5;
        var sourceChild = CreateGameObject("Child");
        sourceChild.AddComponent<SerializableComponent>().IntField = 10;
        sourceChild.SetParent(source);
        var prefab = MakePrefab(source, Guid.NewGuid());

        var a = prefab.Instantiate()!;
        var b = prefab.Instantiate()!;

        // Mutate instance A.
        a.GetComponent<SerializableComponent>()!.IntField = 999;
        a.Children[0].GetComponent<SerializableComponent>()!.IntField = 888;

        // Instance B is untouched, and they are distinct object graphs.
        Assert.NotSame(a, b);
        Assert.Equal(5, b.GetComponent<SerializableComponent>()!.IntField);
        Assert.Equal(10, b.Children[0].GetComponent<SerializableComponent>()!.IntField);
    }

    [Fact]
    public void Instantiate_StampsComponentAndChildCounts()
    {
        var source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>();
        var child = CreateGameObject("Child");
        child.SetParent(source);
        var prefab = MakePrefab(source, Guid.NewGuid());

        var instance = prefab.Instantiate();

        Assert.Equal(1, instance!.PrefabComponentCount);
        Assert.Equal(1, instance.PrefabChildCount);
    }

    [Fact]
    public void Instantiate_StampsChildrenWithSamePrefabId()
    {
        var id = Guid.NewGuid();
        var source = CreateGameObject("Root");
        var child = CreateGameObject("Child");
        child.SetParent(source);
        var prefab = MakePrefab(source, id);

        var instance = prefab.Instantiate();

        Assert.Equal(id, instance!.Children[0].PrefabAssetId);
    }

    [Fact]
    public void Instantiate_PreservesNestedPrefabId()
    {
        // A child that is itself a different prefab instance must keep its own PrefabAssetId.
        var outerId = Guid.NewGuid();
        var nestedId = Guid.NewGuid();

        var source = CreateGameObject("Root");
        var normal = CreateGameObject("Normal");
        normal.SetParent(source);
        var nested = CreateGameObject("Nested");
        nested.PrefabAssetId = nestedId;
        nested.SetParent(source);

        var prefab = MakePrefab(source, outerId);
        var instance = prefab.Instantiate();

        var normalClone = instance!.Children.Single(c => c.Name == "Normal");
        var nestedClone = instance.Children.Single(c => c.Name == "Nested");

        Assert.Equal(outerId, instance.PrefabAssetId);
        Assert.Equal(outerId, normalClone.PrefabAssetId);
        Assert.Equal(nestedId, nestedClone.PrefabAssetId); // boundary respected
    }

    [Fact]
    public void Instantiate_InstanceCanBeAddedToScene()
    {
        var prefab = MakePrefab(CreateGameObject("Root"), Guid.NewGuid());
        var scene = CreateScene(enable: true);

        var instance = prefab.Instantiate()!;
        scene.Add(instance);

        Assert.Same(scene, instance.Scene);
        Assert.True(instance.IsPrefabInstance);
    }

    // ---------------------------------------------------------------------
    // GameObject prefab tracking
    // ---------------------------------------------------------------------

    [Fact]
    public void IsPrefabInstance_ReflectsPrefabAssetId()
    {
        var go = CreateGameObject();
        Assert.False(go.IsPrefabInstance);

        go.PrefabAssetId = Guid.NewGuid();
        Assert.True(go.IsPrefabInstance);

        go.PrefabAssetId = Guid.Empty;
        Assert.False(go.IsPrefabInstance);
    }

    [Fact]
    public void PrefabOverrides_IsNeverNull()
    {
        var go = CreateGameObject();
        Assert.NotNull(go.PrefabOverrides);
        Assert.Empty(go.PrefabOverrides);
    }

    [Fact]
    public void ClearPrefabData_ResetsAllTracking()
    {
        var go = CreateGameObject();
        go.PrefabAssetId = Guid.NewGuid();
        go.PrefabComponentCount = 3;
        go.PrefabChildCount = 2;
        go.PrefabOverrides.Add(new PropertyOverride { Path = "$.X" });

        go.ClearPrefabData();

        Assert.False(go.IsPrefabInstance);
        Assert.Equal(Guid.Empty, go.PrefabAssetId);
        Assert.Equal(-1, go.PrefabComponentCount);
        Assert.Equal(-1, go.PrefabChildCount);
        Assert.Empty(go.PrefabOverrides);
    }

    [Fact]
    public void ClearPrefabDataRecursive_ClearsDescendants()
    {
        var id = Guid.NewGuid();
        var root = CreateGameObject("Root");
        var child = CreateGameObject("Child");
        var grandchild = CreateGameObject("Grandchild");
        child.SetParent(root);
        grandchild.SetParent(child);
        foreach (var go in new[] { root, child, grandchild })
            go.PrefabAssetId = id;

        root.ClearPrefabDataRecursive();

        Assert.False(root.IsPrefabInstance);
        Assert.False(child.IsPrefabInstance);
        Assert.False(grandchild.IsPrefabInstance);
    }

    [Fact]
    public void ClearPrefabData_NonRecursive_LeavesChildren()
    {
        var id = Guid.NewGuid();
        var root = CreateGameObject("Root");
        var child = CreateGameObject("Child");
        child.SetParent(root);
        root.PrefabAssetId = id;
        child.PrefabAssetId = id;

        root.ClearPrefabData();

        Assert.False(root.IsPrefabInstance);
        Assert.True(child.IsPrefabInstance); // untouched
    }

    // ---------------------------------------------------------------------
    // Prefab-data serialization (GameObject.Serialize writes prefab fields)
    // ---------------------------------------------------------------------

    [Fact]
    public void PrefabInstance_RoundTrip_PreservesPrefabData()
    {
        var id = Guid.NewGuid();
        var go = CreateGameObject("Instance");
        go.PrefabAssetId = id;
        go.PrefabComponentCount = 2;
        go.PrefabChildCount = 1;
        go.PrefabOverrides.Add(new PropertyOverride { Path = "$.TagIndex", Value = Serializer.Serialize(5) });

        var clone = RoundTrip(go);

        Assert.Equal(id, clone.PrefabAssetId);
        Assert.Equal(2, clone.PrefabComponentCount);
        Assert.Equal(1, clone.PrefabChildCount);
        Assert.Single(clone.PrefabOverrides);
        Assert.Equal("$.TagIndex", clone.PrefabOverrides[0].Path);
    }

    [Fact]
    public void NonPrefab_RoundTrip_CarriesNoPrefabData()
    {
        var go = CreateGameObject("Plain");

        var clone = RoundTrip(go);

        Assert.False(clone.IsPrefabInstance);
        Assert.Equal(Guid.Empty, clone.PrefabAssetId);
    }
}
