// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for the Runtime prefab surface: <see cref="GameObject.InstantiateDetached"/> (cloning, prefab-id
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
        Assert.Null(GameObject.InstantiateDetached(prefab));
    }

    [Fact]
    public void Instantiate_StampsPrefabAssetId_AndMarksInstance()
    {
        var id = Guid.NewGuid();
        var prefab = MakePrefab(CreateGameObject("Root"), id);

        var instance = GameObject.InstantiateDetached(prefab);

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

        var instance = GameObject.InstantiateDetached(prefab);

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

        var instance = GameObject.InstantiateDetached(prefab);

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

        var a = GameObject.InstantiateDetached(prefab)!;
        var b = GameObject.InstantiateDetached(prefab)!;

        // Mutate instance A.
        a.GetComponent<SerializableComponent>()!.IntField = 999;
        a.Children[0].GetComponent<SerializableComponent>()!.IntField = 888;

        // Instance B is untouched, and they are distinct object graphs.
        Assert.NotSame(a, b);
        Assert.Equal(5, b.GetComponent<SerializableComponent>()!.IntField);
        Assert.Equal(10, b.Children[0].GetComponent<SerializableComponent>()!.IntField);
    }

    [Fact]
    public void Instantiate_InTheEditor_RecordsWhereEachComponentAndChildCameFrom()
    {
        var source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>();
        CreateGameObject("Child").SetParent(source);
        var prefab = MakePrefab(source, Guid.NewGuid());

        bool wasEditor = Application.IsEditor;
        Application.IsEditor = true;
        try
        {
            var instance = GameObject.InstantiateDetached(prefab)!;

            // What tells a prefab-provided component from one the instance adds later. Position is
            // not used, so reordering cannot reclassify anything.
            MonoBehaviour provided = instance.GetComponents<MonoBehaviour>().First();
            Assert.NotEqual(Guid.Empty, instance.GetComponentSourceIdentifier(provided));
            Assert.NotEqual(Guid.Empty, instance.Children[0].SourceIdentifier);
        }
        finally { Application.IsEditor = wasEditor; }
    }

    [Fact]
    public void Instantiate_OutsideTheEditor_RecordsOnlyWhichPrefabItIs()
    {
        var source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>();
        CreateGameObject("Child").SetParent(source);
        Guid assetId = Guid.NewGuid();
        var prefab = MakePrefab(source, assetId);

        bool wasEditor = Application.IsEditor;
        Application.IsEditor = false;
        try
        {
            var instance = GameObject.InstantiateDetached(prefab)!;

            // Which prefab an object came from is what a game can see and act on. Which prefab object
            // it was is bookkeeping for matching overrides, which nothing outside the editor does, and
            // which a built scene does not carry either.
            Assert.True(instance.IsPrefabInstance);
            Assert.Equal(assetId, instance.PrefabAssetId);
            Assert.Equal(assetId, instance.Children[0].PrefabAssetId);

            MonoBehaviour provided = instance.GetComponents<MonoBehaviour>().First();
            Assert.Equal(Guid.Empty, instance.GetComponentSourceIdentifier(provided));
            Assert.Equal(Guid.Empty, instance.Children[0].SourceIdentifier);
        }
        finally { Application.IsEditor = wasEditor; }
    }

    [Fact]
    public void Instantiate_OutsideTheEditor_StillGivesEveryInstanceItsOwnIdentifiers()
    {
        var source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>();
        var prefab = MakePrefab(source, Guid.NewGuid());

        bool wasEditor = Application.IsEditor;
        Application.IsEditor = false;
        try
        {
            var a = GameObject.InstantiateDetached(prefab)!;
            var b = GameObject.InstantiateDetached(prefab)!;

            // Skipping the bookkeeping must not mean two spawns wearing one identity.
            Assert.NotEqual(a.Identifier, b.Identifier);
            Assert.NotEqual(a.GetComponents<MonoBehaviour>().First().Identifier,
                            b.GetComponents<MonoBehaviour>().First().Identifier);
        }
        finally { Application.IsEditor = wasEditor; }
    }

    [Fact]
    public void Instantiate_ComponentAddedAfterwardsHasNoSource()
    {
        var source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>();
        var prefab = MakePrefab(source, Guid.NewGuid());

        var instance = GameObject.InstantiateDetached(prefab)!;
        MonoBehaviour added = instance.AddComponent<SerializableComponent>();

        Assert.Equal(Guid.Empty, instance.GetComponentSourceIdentifier(added));
    }

    [Fact]
    public void Instantiate_StampsChildrenWithSamePrefabId()
    {
        var id = Guid.NewGuid();
        var source = CreateGameObject("Root");
        var child = CreateGameObject("Child");
        child.SetParent(source);
        var prefab = MakePrefab(source, id);

        var instance = GameObject.InstantiateDetached(prefab);

        Assert.Equal(id, instance!.Children[0].PrefabAssetId);
    }

    /// <summary>
    /// Stamping stops at a child that already belongs to a different prefab, rather than overwriting
    /// every descendant. The editor flattens prefabs on import, so data reaching this is not something
    /// it writes any more, but the guard is what keeps stamping from running past a boundary it was
    /// handed and is worth pinning on its own.
    /// </summary>
    [Fact]
    public void Instantiate_StampingStopsAtAForeignPrefabId()
    {
        var outerId = Guid.NewGuid();
        var nestedId = Guid.NewGuid();

        var source = CreateGameObject("Root");
        var normal = CreateGameObject("Normal");
        normal.SetParent(source);
        var nested = CreateGameObject("Nested");
        nested.PrefabAssetId = nestedId;
        nested.SetParent(source);

        var prefab = MakePrefab(source, outerId);
        var instance = GameObject.InstantiateDetached(prefab);

        var normalClone = instance!.Children.Single(c => c.Name == "Normal");
        var nestedClone = instance.Children.Single(c => c.Name == "Nested");

        Assert.Equal(outerId, instance.PrefabAssetId);
        Assert.Equal(outerId, normalClone.PrefabAssetId);
        Assert.Equal(nestedId, nestedClone.PrefabAssetId);
    }

    [Fact]
    public void Instantiate_InstanceCanBeAddedToScene()
    {
        var prefab = MakePrefab(CreateGameObject("Root"), Guid.NewGuid());
        var scene = CreateScene(enable: true);

        var instance = GameObject.InstantiateDetached(prefab)!;
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
    public void AnOrdinaryGameObjectCarriesNoPrefabLink()
    {
        var go = CreateGameObject();

        // The whole reason the data sits behind a reference: the overwhelming majority of objects in a
        // scene are not prefab instances and should cost one null field.
        Assert.Null(go.PrefabLink);
        Assert.False(go.IsPrefabInstance);
        Assert.False(go.HasPrefabOverrides);
    }

    [Fact]
    public void AskingWhetherThereAreOverridesDoesNotAllocateALink()
    {
        var go = CreateGameObject();

        // PrefabOverrides itself is the mutable accessor, so reading it does create the link. The
        // cheap query is what callers sweeping a scene are meant to use, and it has to stay cheap or
        // every object in the scene grows one.
        Assert.False(go.HasPrefabOverrides);
        Assert.Null(go.PrefabLink);
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
        go.PrefabOverrides.Add(new PropertyOverride { Path = $"{Guid.NewGuid()}/$/TagIndex" });

        go.ClearPrefabData();

        Assert.False(go.IsPrefabInstance);
        Assert.Equal(Guid.Empty, go.PrefabAssetId);
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
        var sourceId = Guid.NewGuid();
        var go = CreateGameObject("Instance");
        go.PrefabAssetId = id;
        go.PrefabOverrides.Add(new PropertyOverride
        {
            Path = $"{sourceId}/$/TagIndex",
            Value = Serializer.Serialize(5)
        });

        var clone = RoundTrip(go);

        Assert.Equal(id, clone.PrefabAssetId);
        Assert.Single(clone.PrefabOverrides);
        Assert.Equal($"{sourceId}/$/TagIndex", clone.PrefabOverrides[0].Path);

        // The value has to survive too. A path with nothing behind it applies nothing.
        Assert.Equal(5, Serializer.Deserialize<int>(clone.PrefabOverrides[0].Value));
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
