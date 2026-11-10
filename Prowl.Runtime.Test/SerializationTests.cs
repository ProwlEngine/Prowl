// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

#region Test types

/// <summary>A component with a single field, used to confirm component data flows through the
/// GameObject/Scene serializers (not to test Echo's field serialization itself).</summary>
public sealed class SerializableComponent : Component
{
    public int IntField;
}

/// <summary>A minimal runtime asset (EngineObject) used to test AssetRef serialization.</summary>
public sealed class TestAsset : Asset
{
    public int Value;
    public TestAsset() : base() { }
}

/// <summary>A component that references another component, to probe reference round-tripping.</summary>
public sealed class CrossRefComponent : Component
{
    public Component? Other;
}

public sealed class LinkComponent : Component
{
    public GameObject? Target;
}

public sealed class LifecycleCounter : Component
{
    public static int Callbacks;
    public override void OnAddedToScene() => Callbacks++;
    public override void OnEnable() => Callbacks++;
    public override void Start() => Callbacks++;
    public override void Update() => Callbacks++;
}

#endregion

/// <summary>
/// Tests for Prowl's wiring on top of Prowl.Echo - the hand-written ISerializable implementations
/// (GameObject, AssetRef, AnimationClip) and the ISerializationCallbackReceiver logic on Scene.
/// Echo's own field/format serialization is covered by Echo's test suite and not re-tested here.
/// </summary>
public class SerializationTests : RuntimeTestBase
{
    private static T RoundTrip<T>(T value) => Serializer.Deserialize<T>(Serializer.Serialize(value))!;

    private static SceneInstance RoundTrip(SceneInstance scene) => SceneInstance.Instantiate(scene.ActiveSection.Capture());

    // ---------------------------------------------------------------------
    // GameObject (custom ISerializable: GameObject.Serialize/Deserialize)
    // ---------------------------------------------------------------------

    [Fact]
    public void GameObject_RoundTrip_ReattachesComponents()
    {
        GameObject go = CreateGameObject();
        SerializableComponent comp = go.AddComponent<SerializableComponent>();
        comp.IntField = 123;

        GameObject clone = RoundTrip(go);

        // Deserialize must rebuild the component, its data, the cache (GetComponent) and the back-reference.
        SerializableComponent? cloneComp = clone.GetComponent<SerializableComponent>();
        Assert.NotNull(cloneComp);
        Assert.Equal(123, cloneComp.IntField);
        Assert.Same(clone, cloneComp.GameObject);
        Assert.Same(clone.Transform, cloneComp.Transform);
    }

    [Fact]
    public void GameObject_RoundTrip_ReconstructsChildParentLinks()
    {
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);

        GameObject clone = RoundTrip(parent);

        Assert.Single(clone.Children);
        Assert.Equal("Child", clone.Children[0].Name);
        Assert.Same(clone, clone.Children[0].Parent);
    }

    [Fact]
    public void GameObject_RoundTrip_WiresTransformToGameObject()
    {
        GameObject go = CreateGameObject();

        GameObject clone = RoundTrip(go);

        Assert.Same(clone, clone.Transform.GameObject);
    }

    [Fact]
    public void GameObject_RoundTrip_PersistsHandWrittenState()
    {
        // GameObject.Serialize writes these fields by hand, so a regression there wouldn't be caught
        // by Echo's tests.
        GameObject go = CreateGameObject("Hero");
        go.TagIndex = 2;
        go.LayerIndex = 3;
        go.IsStatic = true;
        go.HideFlags = HideFlags.DontSave;
        go.Enabled = false;

        GameObject clone = RoundTrip(go);

        Assert.Equal("Hero", clone.Name);
        Assert.Equal(2, clone.TagIndex);
        Assert.Equal(3, clone.LayerIndex);
        Assert.True(clone.IsStatic);
        Assert.Equal(HideFlags.DontSave, clone.HideFlags);
        Assert.False(clone.Enabled);
    }

    [Fact]
    public void GameObject_RoundTrip_OneWayComponentReference_Rewires()
    {
        GameObject root = CreateGameObject("Root");
        CrossRefComponent refComp = root.AddComponent<CrossRefComponent>();
        GameObject child = CreateGameObject("Child");
        SerializableComponent target = child.AddComponent<SerializableComponent>();
        child.SetParent(root);
        refComp.Other = target;

        GameObject clone = RoundTrip(root);

        // A one-way reference to another object in the same graph rewires to the cloned target.
        Assert.Same(clone.Children[0].GetComponent<SerializableComponent>(), clone.GetComponent<CrossRefComponent>()!.Other);
    }

    [Fact]
    public void GameObject_RoundTrip_CyclicComponentReferences_ArePreserved()
    {
        // Two components referencing each other across the parent/child boundary must both survive a
        // round trip. This relies on GameObject.Deserialize visiting Components before Children to
        // match the serialization order (Echo's reference encoding is single-pass, definition-first).
        GameObject root = CreateGameObject("Root");
        CrossRefComponent a = root.AddComponent<CrossRefComponent>();
        GameObject child = CreateGameObject("Child");
        CrossRefComponent b = child.AddComponent<CrossRefComponent>();
        child.SetParent(root);
        a.Other = b;
        b.Other = a;

        GameObject clone = RoundTrip(root);

        CrossRefComponent ca = clone.GetComponent<CrossRefComponent>()!;
        CrossRefComponent cb = clone.Children[0].GetComponent<CrossRefComponent>()!;
        Assert.Same(cb, ca.Other);
        Assert.Same(ca, cb.Other);
    }

    [Fact]
    public void GameObject_RoundTrip_GeneratesFreshIdentifier()
    {
        // A raw GameObject deserialize intentionally mints a new identifier; only Scene restores them.
        GameObject go = CreateGameObject();

        GameObject clone = RoundTrip(go);

        Assert.NotEqual(go.Identifier, clone.Identifier);
    }

    // ---------------------------------------------------------------------
    // Scene content: Capture, ReadContent and Instantiate
    // ---------------------------------------------------------------------

    [Fact]
    public void Capture_ThenInstantiate_KeepsEveryObjectAndIdentifier()
    {
        SceneInstance scene = CreateScene();
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);
        LinkComponent link = parent.AddComponent<LinkComponent>();
        link.Target = child;
        scene.Add(parent);
        scene.Lighting.LightmapScale = 3f;
        scene.LightmapBake.Bounces = 7;

        SceneInstance copy = SceneInstance.Instantiate(scene.ActiveSection.Capture());

        Assert.Equal(
            scene.Objects.Select(o => (o.Name, o.Identifier)).OrderBy(o => o.Name),
            copy.Objects.Select(o => (o.Name, o.Identifier)).OrderBy(o => o.Name));
        LinkComponent copied = copy.FindObject<LinkComponent>(link.Identifier)!;
        Assert.Same(copy.FindObject<GameObject>(child.Identifier), copied.Target);
        Assert.Equal(3f, copy.Lighting.LightmapScale);
        Assert.Equal(7, copy.LightmapBake.Bounces);
    }

    [Fact]
    public void ReadContent_DeliversNoLifecycleCallback()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject("Counted");
        go.AddComponent<LifecycleCounter>();
        scene.Add(go);
        var stored = new Scene { Data = scene.ActiveSection.CaptureTree() };
        LifecycleCounter.Callbacks = 0;

        SceneContent content = stored.ReadContent();

        Assert.Equal(0, LifecycleCounter.Callbacks);
        GameObject read = Assert.Single(content.Objects);
        Assert.Equal(go.Identifier, read.Identifier);
        Assert.True(read.Scene.IsNotValid());
    }

    [Fact]
    public void Capture_SavesAReferenceOutOfTheWorldAsNull_AndNeverCopiesItsTarget()
    {
        SceneInstance scene = CreateScene();
        SceneInstance other = CreateScene();
        GameObject foreign = CreateGameObject("ForeignObject");
        other.Add(foreign);
        GameObject holder = CreateGameObject("Holder");
        holder.AddComponent<LinkComponent>().Target = foreign;
        scene.Add(holder);

        EchoObject tree = scene.ActiveSection.CaptureTree();
        SceneInstance copy = SceneInstance.Instantiate(SceneContent.Read(tree));

        Assert.DoesNotContain("ForeignObject", tree.WriteToString());
        Assert.Null(copy.Roots.Single().GetComponent<LinkComponent>()!.Target);
    }

    // ---------------------------------------------------------------------
    // Scene round trips
    // ---------------------------------------------------------------------

    [Fact]
    public void Scene_RoundTrip_ReaddsObjectsAndHierarchy()
    {
        SceneInstance scene = CreateScene();
        GameObject parent = CreateGameObject("Parent");
        GameObject child = CreateGameObject("Child");
        child.SetParent(parent);
        scene.Add(parent);

        SceneInstance clone = RoundTrip(scene);

        Assert.Equal(2, clone.ObjectCount);
        GameObject cloneParent = clone.Roots.Single();
        Assert.Equal("Parent", cloneParent.Name);
        Assert.Single(cloneParent.Children);
        Assert.Same(clone, cloneParent.Children[0].Scene);
    }

    // A GameObject whose serialized Transform can't be restored (an unresolved forward $id reference,
    // which some scenes' flat-array + nested-children encoding produces) must not throw - it has to
    // fall back to a fresh Transform. Before the fix this NREs on `_transform.GameObject = this`.
    [Fact]
    public void GameObject_Deserialize_WithUnresolvableTransform_FallsBackAndDoesNotThrow()
    {
        GameObject go = CreateGameObject("Obj");
        EchoObject echo = Serializer.Serialize(go);
        echo.Remove("Transform"); // Transform now deserializes to null

        GameObject? clone = Serializer.Deserialize<GameObject>(echo);

        Assert.NotNull(clone);
        Assert.NotNull(clone!.Transform);                 // a GameObject must always have a Transform
        Assert.Same(clone, clone.Transform.GameObject);   // and it must be wired back to the object
    }

    // The real-world failure this hunts: one GameObject with an unrestorable Transform used to make the
    // ENTIRE scene deserialize to zero objects, because the NRE propagated out of the serializeObj array
    // and Echo dropped the whole field. The healthy objects (and the recovered one) must survive.
    [Fact]
    public void Scene_RoundTrip_OneObjectWithUnresolvableTransform_DoesNotWipeScene()
    {
        SceneInstance scene = CreateScene();
        scene.Add(CreateGameObject("Healthy1"));
        scene.Add(CreateGameObject("Broken"));
        scene.Add(CreateGameObject("Healthy2"));

        EchoObject echo = scene.ActiveSection.CaptureTree();

        // Drop the "Broken" object's Transform in the serialized array to simulate the unresolved ref.
        foreach (EchoObject el in StoredObjects(echo).List)
            if (el.TryGet("Name", out EchoObject? n) && n!.StringValue == "Broken")
                el.Remove("Transform");

        SceneInstance clone = SceneInstance.Instantiate(SceneContent.Read(echo));

        Assert.Equal(3, clone.Objects.Count());
        Assert.Contains(clone.Objects, g => g.Name == "Healthy1");
        Assert.Contains(clone.Objects, g => g.Name == "Healthy2");
        Assert.All(clone.Objects, g => Assert.NotNull(g.Transform));
    }

    // ---------------------------------------------------------------------
    // AssetRef (a GUID that neither loads nor holds)
    // ---------------------------------------------------------------------

    private sealed class Holder
    {
        public TestAsset? Asset;
    }

    [Fact]
    public void ARuntimeAssetInAField_RoundTripsInline()
    {
        var holder = new Holder { Asset = new TestAsset { Value = 55, Name = "Asset" } };

        Holder clone = RoundTrip(holder);

        Assert.NotNull(clone.Asset);
        Assert.Equal(55, clone.Asset!.Value);
    }

    [Fact]
    public void AssetRef_RoundTripsItsGuid()
    {
        var id = Guid.NewGuid();
        var aref = new AssetRef<TestAsset>(id);

        AssetRef<TestAsset> clone = RoundTrip(aref);

        Assert.Equal(id, clone.AssetID);
    }

    [Fact]
    public void AssetRef_Empty_RoundTrips()
    {
        AssetRef<TestAsset> aref = default;

        AssetRef<TestAsset> clone = RoundTrip(aref);

        Assert.True(clone.IsEmpty);
    }

    // ---------------------------------------------------------------------
    // Rigs and clips (the payload is Motion's own binary, carried as bytes)
    // ---------------------------------------------------------------------

    private static Motion.Skeleton TwoBoneSkeleton() => new(
        new[] { new Motion.StringID("Root"), new Motion.StringID("Child") },
        new[] { Motion.Skeleton.InvalidIndex, 0 },
        new[]
        {
            Vector.Spatial.Transform3D.Identity,
            new Vector.Spatial.Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
        });

    [Fact]
    public void Avatar_RoundTrip_KeepsItsSkeleton()
    {
        Avatar avatar = Avatar.CreateGeneric(TwoBoneSkeleton(), 0, "Rig");

        Avatar clone = RoundTrip(avatar);

        Assert.NotNull(clone.Skeleton);
        Assert.Equal(2, clone.Skeleton!.BoneCount);
        Assert.Equal("Child", clone.Skeleton.GetBoneID(1).DebugName);
        Assert.NotNull(clone.Runtime);
    }

    [Fact]
    public void AnimationClip_RoundTrip_SamplesTheSamePose()
    {
        Motion.Skeleton skeleton = TwoBoneSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");

        var first = new Motion.Pose(skeleton);
        first.SetToReferencePose();
        var last = new Motion.Pose(skeleton);
        last.SetToReferencePose();
        last.SetTransform(1, new Vector.Spatial.Transform3D(new Float3(0f, 3f, 0f), Quaternion.Identity, Float3.One));

        var source = new Motion.AnimationClip(skeleton, new[] { first, last }, 1f);
        AnimationClip clip = AnimationClip.FromSkeletal(source, avatar, "Walk");

        AnimationClip clone = RoundTrip(clip);
        clone.Avatar = avatar;

        Assert.Equal(AnimationClipKind.Skeletal, clone.Kind);
        Assert.Equal(1f, clone.Duration, 3);

        Motion.AnimationClipBase? decoded = clone.GetClip(avatar.Runtime);
        Assert.NotNull(decoded);
        var sampled = new Motion.Pose(skeleton);
        decoded!.GetPose(1f, sampled);
        Assert.Equal(3.0, sampled.GetTransform(1).position.Y, 2);
    }

    /// <summary>
    /// A clip's events are not part of the frames Motion stores, so the asset keeps them itself and hands
    /// them back when the clip is decoded, in the clip's normalized time.
    /// </summary>
    [Fact]
    public void AnimationClip_RoundTrip_KeepsItsEvents()
    {
        Motion.Skeleton skeleton = TwoBoneSkeleton();
        Avatar avatar = Avatar.CreateGeneric(skeleton, 0, "Rig");
        var pose = new Motion.Pose(skeleton);
        pose.SetToReferencePose();

        var source = new Motion.AnimationClip(skeleton, new[] { pose, pose }, 2f);
        AnimationClip clip = AnimationClip.FromSkeletal(source, avatar, "Walk", new[]
        {
            new ClipEvent { Kind = ClipEventKind.Foot, Time = 0.5f, Option = (int)Motion.FootPhase.RightFootDown },
            new ClipEvent { Kind = ClipEventKind.Named, Time = 1f, Length = 0.5f, Name = "Swing" },
        });

        AnimationClip clone = RoundTrip(clip);
        clone.Avatar = avatar;
        IReadOnlyList<Motion.AnimationEvent> events = clone.GetClip(avatar.Runtime)!.Events;

        Assert.Equal(2, events.Count);
        Motion.FootEvent foot = Assert.IsType<Motion.FootEvent>(events[0]);
        Assert.Equal(Motion.FootPhase.RightFootDown, foot.Phase);
        Assert.Equal(0.25f, foot.StartTime, 3);

        Motion.IdEvent named = Assert.IsType<Motion.IdEvent>(events[1]);
        Assert.Equal(new Motion.StringID("Swing"), named.Id);
        Assert.Equal(0.5f, named.StartTime, 3);
        Assert.Equal(0.25f, named.Duration, 3);
    }
}
