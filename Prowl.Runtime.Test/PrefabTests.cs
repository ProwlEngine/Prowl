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
        var prefab = new PrefabAsset { GameObjectData = data };
        prefab.SetIdentity(assetId, "");
        return prefab;
    }

    private static T RoundTrip<T>(T value) => Serializer.Deserialize<T>(Serializer.Serialize(value))!;

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
    public void Instantiate_ClonesComponentsWithData()
    {
        GameObject source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>().IntField = 17;
        PrefabAsset prefab = MakePrefab(source, Guid.NewGuid());

        var instance = GameObject.InstantiateDetached(prefab);

        SerializableComponent? comp = instance!.GetComponent<SerializableComponent>();
        Assert.NotNull(comp);
        Assert.Equal(17, comp!.IntField);
        Assert.Same(instance, comp.GameObject);
    }

    [Fact]
    public void Instantiate_ClonesChildren()
    {
        GameObject source = CreateGameObject("Root");
        GameObject child = CreateGameObject("Child");
        child.SetParent(source);
        PrefabAsset prefab = MakePrefab(source, Guid.NewGuid());

        var instance = GameObject.InstantiateDetached(prefab);

        Assert.Single(instance!.Children);
        Assert.Equal("Child", instance.Children[0].Name);
        Assert.Same(instance, instance.Children[0].Parent);
    }

    [Fact]
    public void Instantiate_ProducesIndependentCopies()
    {
        GameObject source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>().IntField = 5;
        GameObject sourceChild = CreateGameObject("Child");
        sourceChild.AddComponent<SerializableComponent>().IntField = 10;
        sourceChild.SetParent(source);
        PrefabAsset prefab = MakePrefab(source, Guid.NewGuid());

        GameObject a = GameObject.InstantiateDetached(prefab)!;
        GameObject b = GameObject.InstantiateDetached(prefab)!;

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
        GameObject source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>();
        CreateGameObject("Child").SetParent(source);
        PrefabAsset prefab = MakePrefab(source, Guid.NewGuid());

        bool wasEditor = Application.IsEditor;
        Application.IsEditor = true;
        try
        {
            GameObject instance = GameObject.InstantiateDetached(prefab)!;

            // What tells a prefab-provided component from one the instance adds later. Position is
            // not used, so reordering cannot reclassify anything.
            Component provided = instance.GetComponents<Component>().First();
            Assert.NotEqual(Guid.Empty, provided.SourceIdentifier);
            Assert.NotEqual(Guid.Empty, instance.Children[0].SourceIdentifier);
        }
        finally { Application.IsEditor = wasEditor; }
    }

    [Fact]
    public void Instantiate_OutsideTheEditor_RecordsOnlyWhichPrefabItIs()
    {
        GameObject source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>();
        CreateGameObject("Child").SetParent(source);
        Guid assetId = Guid.NewGuid();
        PrefabAsset prefab = MakePrefab(source, assetId);

        bool wasEditor = Application.IsEditor;
        Application.IsEditor = false;
        try
        {
            GameObject instance = GameObject.InstantiateDetached(prefab)!;

            // Which prefab an object came from is what a game can see and act on. Which prefab object
            // it was is bookkeeping for matching overrides, which nothing outside the editor does, and
            // which a built scene does not carry either.
            Assert.True(instance.IsPrefabInstance);
            Assert.Equal(assetId, instance.PrefabAssetId);
            Assert.Equal(assetId, instance.Children[0].PrefabAssetId);

            Component provided = instance.GetComponents<Component>().First();
            Assert.Equal(Guid.Empty, provided.SourceIdentifier);
            Assert.Equal(Guid.Empty, instance.Children[0].SourceIdentifier);
        }
        finally { Application.IsEditor = wasEditor; }
    }

    [Fact]
    public void Instantiate_OutsideTheEditor_StillGivesEveryInstanceItsOwnIdentifiers()
    {
        GameObject source = CreateGameObject("Root");
        source.AddComponent<SerializableComponent>();
        PrefabAsset prefab = MakePrefab(source, Guid.NewGuid());

        bool wasEditor = Application.IsEditor;
        Application.IsEditor = false;
        try
        {
            GameObject a = GameObject.InstantiateDetached(prefab)!;
            GameObject b = GameObject.InstantiateDetached(prefab)!;

            // Skipping the bookkeeping must not mean two spawns wearing one identity.
            Assert.NotEqual(a.Identifier, b.Identifier);
            Assert.NotEqual(a.GetComponents<Component>().First().Identifier,
                            b.GetComponents<Component>().First().Identifier);
        }
        finally { Application.IsEditor = wasEditor; }
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

        GameObject source = CreateGameObject("Root");
        GameObject normal = CreateGameObject("Normal");
        normal.SetParent(source);
        GameObject nested = CreateGameObject("Nested");
        nested.PrefabAssetId = nestedId;
        nested.SetParent(source);

        PrefabAsset prefab = MakePrefab(source, outerId);
        var instance = GameObject.InstantiateDetached(prefab);

        GameObject normalClone = instance!.Children.Single(c => c.Name == "Normal");
        GameObject nestedClone = instance.Children.Single(c => c.Name == "Nested");

        Assert.Equal(outerId, instance.PrefabAssetId);
        Assert.Equal(outerId, normalClone.PrefabAssetId);
        Assert.Equal(nestedId, nestedClone.PrefabAssetId);
    }

    // ---------------------------------------------------------------------
    // GameObject prefab tracking
    // ---------------------------------------------------------------------

    [Fact]
    public void IsPrefabInstance_ReflectsPrefabAssetId()
    {
        GameObject go = CreateGameObject();
        Assert.False(go.IsPrefabInstance);

        go.PrefabAssetId = Guid.NewGuid();
        Assert.True(go.IsPrefabInstance);

        go.PrefabAssetId = Guid.Empty;
        Assert.False(go.IsPrefabInstance);
    }

    [Fact]
    public void AnOrdinaryGameObjectCarriesNoPrefabLink()
    {
        GameObject go = CreateGameObject();

        // The whole reason the data sits behind a reference: the overwhelming majority of objects in a
        // scene are not prefab instances and should cost one null field.
        Assert.Null(go.PrefabLink);
        Assert.False(go.IsPrefabInstance);
        Assert.False(go.HasPrefabOverrides);
    }

    [Fact]
    public void AskingWhetherThereAreOverridesDoesNotAllocateALink()
    {
        GameObject go = CreateGameObject();

        // PrefabOverrides itself is the mutable accessor, so reading it does create the link. The
        // cheap query is what callers sweeping a scene are meant to use, and it has to stay cheap or
        // every object in the scene grows one.
        Assert.False(go.HasPrefabOverrides);
        Assert.Null(go.PrefabLink);
    }

    [Fact]
    public void ClearPrefabData_ResetsAllTracking()
    {
        GameObject go = CreateGameObject();
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
        GameObject root = CreateGameObject("Root");
        GameObject child = CreateGameObject("Child");
        GameObject grandchild = CreateGameObject("Grandchild");
        child.SetParent(root);
        grandchild.SetParent(child);
        foreach (GameObject? go in new[] { root, child, grandchild })
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
        GameObject root = CreateGameObject("Root");
        GameObject child = CreateGameObject("Child");
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
        GameObject go = CreateGameObject("Instance");
        go.PrefabAssetId = id;
        go.PrefabOverrides.Add(new PropertyOverride
        {
            Path = $"{sourceId}/$/TagIndex",
            Value = Serializer.Serialize(5)
        });

        GameObject clone = RoundTrip(go);

        Assert.Equal(id, clone.PrefabAssetId);
        Assert.Single(clone.PrefabOverrides);
        Assert.Equal($"{sourceId}/$/TagIndex", clone.PrefabOverrides[0].Path);

        // The value has to survive too. A path with nothing behind it applies nothing.
        Assert.Equal(5, Serializer.Deserialize<int>(clone.PrefabOverrides[0].Value));
    }

    [Fact]
    public void NonPrefab_RoundTrip_CarriesNoPrefabData()
    {
        GameObject go = CreateGameObject("Plain");

        GameObject clone = RoundTrip(go);

        Assert.False(clone.IsPrefabInstance);
        Assert.Equal(Guid.Empty, clone.PrefabAssetId);
    }
}
