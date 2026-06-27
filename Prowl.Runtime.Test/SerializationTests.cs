// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

#region Test types

/// <summary>A component with a single field, used to confirm component data flows through the
/// GameObject/Scene serializers (not to test Echo's field serialization itself).</summary>
public sealed class SerializableComponent : MonoBehaviour
{
    public int IntField;
}

/// <summary>A minimal runtime asset (EngineObject) used to test AssetRef serialization.</summary>
public sealed class TestAsset : EngineObject
{
    public int Value;
    public TestAsset() : base() { }
}

#endregion

/// <summary>
/// Tests for Prowl's wiring on top of Prowl.Echo - the hand-written ISerializable implementations
/// (GameObject, AssetRef, AnimationClip) and the ISerializationCallbackReceiver logic on Scene.
/// Echo's own field/format serialization is covered by Echo's test suite and not re-tested here.
/// </summary>
public class SerializationTests : RuntimeTestBase
{
    private static T RoundTrip<T>(T value) => Serializer.Deserialize<T>(Serializer.Serialize(value));

    // ---------------------------------------------------------------------
    // GameObject (custom ISerializable: GameObject.Serialize/Deserialize)
    // ---------------------------------------------------------------------

    [Fact]
    public void GameObject_RoundTrip_ReattachesComponents()
    {
        var go = CreateGameObject();
        var comp = go.AddComponent<SerializableComponent>();
        comp.IntField = 123;

        var clone = RoundTrip(go);

        // Deserialize must rebuild the component, its data, the cache (GetComponent) and the back-reference.
        var cloneComp = clone.GetComponent<SerializableComponent>();
        Assert.NotNull(cloneComp);
        Assert.Equal(123, cloneComp.IntField);
        Assert.Same(clone, cloneComp.GameObject);
        Assert.Same(clone.Transform, cloneComp.Transform);
    }

    [Fact]
    public void GameObject_RoundTrip_ReconstructsChildParentLinks()
    {
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);

        var clone = RoundTrip(parent);

        Assert.Single(clone.Children);
        Assert.Equal("Child", clone.Children[0].Name);
        Assert.Same(clone, clone.Children[0].Parent);
    }

    [Fact]
    public void GameObject_RoundTrip_WiresTransformToGameObject()
    {
        var go = CreateGameObject();

        var clone = RoundTrip(go);

        Assert.Same(clone, clone.Transform.GameObject);
    }

    [Fact]
    public void GameObject_RoundTrip_PersistsHandWrittenState()
    {
        // GameObject.Serialize writes these fields by hand, so a regression there wouldn't be caught
        // by Echo's tests.
        var go = CreateGameObject("Hero");
        go.TagIndex = 2;
        go.LayerIndex = 3;
        go.IsStatic = true;
        go.HideFlags = HideFlags.DontSave;
        go.Enabled = false;

        var clone = RoundTrip(go);

        Assert.Equal("Hero", clone.Name);
        Assert.Equal(2, clone.TagIndex);
        Assert.Equal(3, clone.LayerIndex);
        Assert.True(clone.IsStatic);
        Assert.Equal(HideFlags.DontSave, clone.HideFlags);
        Assert.False(clone.Enabled);
    }

    [Fact]
    public void GameObject_RoundTrip_GeneratesFreshIdentifier()
    {
        // A raw GameObject deserialize intentionally mints a new identifier; only Scene restores them.
        var go = CreateGameObject();

        var clone = RoundTrip(go);

        Assert.NotEqual(go.Identifier, clone.Identifier);
    }

    // ---------------------------------------------------------------------
    // Scene (ISerializationCallbackReceiver: OnBeforeSerialize/OnAfterDeserialize)
    // ---------------------------------------------------------------------

    [Fact]
    public void Scene_RoundTrip_ReaddsObjectsAndHierarchy()
    {
        var scene = CreateScene();
        var parent = CreateGameObject("Parent");
        var child = CreateGameObject("Child");
        child.SetParent(parent);
        scene.Add(parent);

        var clone = RoundTrip(scene);

        Assert.Equal(2, clone.Count);
        var cloneParent = clone.RootObjects.Single();
        Assert.Equal("Parent", cloneParent.Name);
        Assert.Single(cloneParent.Children);
        Assert.Same(clone, cloneParent.Children[0].Scene);
    }

    [Fact]
    public void Scene_RoundTrip_RestoresIdentifiers()
    {
        // The parallel identifier arrays Scene maintains are the reason save/load keeps stable IDs.
        var scene = CreateScene();
        var go = CreateGameObject("Obj");
        var comp = go.AddComponent<SerializableComponent>();
        scene.Add(go);
        Guid goId = go.Identifier;
        Guid compId = comp.Identifier;

        var clone = RoundTrip(scene);

        var cloneGo = clone.FindObjectByIdentifier<GameObject>(goId);
        Assert.NotNull(cloneGo);
        Assert.Equal("Obj", cloneGo.Name);
        Assert.NotNull(clone.FindObjectByIdentifier<SerializableComponent>(compId));
    }

    // ---------------------------------------------------------------------
    // AssetRef (custom ISerializable: inline instance vs AssetID reference)
    // ---------------------------------------------------------------------

    [Fact]
    public void AssetRef_RuntimeInstance_RoundTripsInline()
    {
        var asset = new TestAsset { Value = 55, Name = "Asset" };
        AssetRef<TestAsset> aref = asset; // no AssetID -> serialized inline

        var clone = RoundTrip(aref);

        Assert.NotNull(clone.Res);
        Assert.Equal(55, clone.Res!.Value);
    }

    [Fact]
    public void AssetRef_AssetId_RoundTripsReference()
    {
        var id = Guid.NewGuid();
        var aref = new AssetRef<TestAsset>(id);

        var clone = RoundTrip(aref);

        Assert.Equal(id, clone.AssetID);
    }

    [Fact]
    public void AssetRef_Null_RoundTrips()
    {
        AssetRef<TestAsset> aref = default;

        var clone = RoundTrip(aref);

        Assert.True(clone.IsExplicitNull);
    }

    // ---------------------------------------------------------------------
    // AnimationClip (custom ISerializable: rebuilds its bone map on deserialize)
    // ---------------------------------------------------------------------

    [Fact]
    public void AnimationClip_RoundTrip_RebuildsBoneMap()
    {
        var clip = new AnimationClip { Name = "Walk", Duration = 2f, Wrap = AnimationWrapMode.Loop };
        clip.AddBone(new AnimationClip.AnimBone
        {
            BoneName = "Root",
            PosX = new AnimationCurve([new KeyFrame(0f, 5f), new KeyFrame(1f, 5f)]),
            PosY = new AnimationCurve([new KeyFrame(0f, 0f)]),
            PosZ = new AnimationCurve([new KeyFrame(0f, 0f)]),
        });

        var clone = RoundTrip(clip);

        Assert.Equal(AnimationWrapMode.Loop, clone.Wrap);
        // GetBone reads _boneMap, which AnimationClip.Deserialize rebuilds from the bone list.
        var bone = clone.GetBone("Root");
        Assert.NotNull(bone);
        Assert.Equal(5.0, bone!.EvaluatePositionAt(0.5f).X, 3);
    }
}
