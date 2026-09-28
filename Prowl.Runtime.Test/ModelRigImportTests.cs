// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Prowl.Motion;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Xunit;

using MotionClip = Prowl.Motion.AnimationClipBase;

namespace Prowl.Runtime.Test;

/// <summary>
/// The rig a model import produces: a skeleton covering the hierarchy, and clips sampled from the
/// file's curves onto it.
/// </summary>
public class ModelRigImportTests
{
    // A model with a root and one child bone, and an animation sliding that bone from y=1 to y=3
    // over one second. Everything lives in a base64 buffer so the test carries its own asset.
    private static string RiggedGltf(float lastKeyTime = 1f, float endZ = 0f)
    {
        var bytes = new byte[8 * sizeof(float)];
        var span = bytes.AsSpan();
        Write(span, 0, 0f); Write(span, 1, lastKeyTime);              // key times
        Write(span, 2, 0f); Write(span, 3, 1f); Write(span, 4, 0f);   // key 0: (0, 1, 0)
        Write(span, 5, 0f); Write(span, 6, 3f); Write(span, 7, endZ); // key 1: (0, 3, endZ)

        string buffer = Convert.ToBase64String(bytes);
        return $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          "nodes": [
            { "name": "Root", "children": [ 1 ] },
            { "name": "Bone", "translation": [ 0, 1, 0 ] }
          ],
          "buffers": [ { "byteLength": {{bytes.Length}}, "uri": "data:application/octet-stream;base64,{{buffer}}" } ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 8 },
            { "buffer": 0, "byteOffset": 8, "byteLength": 24 }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 2, "type": "SCALAR", "min": [ 0 ], "max": [ {{lastKeyTime}} ] },
            { "bufferView": 1, "componentType": 5126, "count": 2, "type": "VEC3" }
          ],
          "animations": [ {
            "name": "Slide",
            "samplers": [ { "input": 0, "output": 1, "interpolation": "LINEAR" } ],
            "channels": [ { "sampler": 0, "target": { "node": 1, "path": "translation" } } ]
          } ]
        }
        """;

        static void Write(Span<byte> target, int index, float value)
            => BitConverter.TryWriteBytes(target[(index * sizeof(float))..], value);
    }

    private static ModelImportResult Import(ModelRigType rig = ModelRigType.Generic, float sampleRate = 30f, float lastKeyTime = 1f,
        float endZ = 0f, Dictionary<string, ModelClipSettings>? clips = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(RiggedGltf(lastKeyTime, endZ)));
        return ClayBackedImporter.Import(stream, "rig.gltf", new ModelImporterSettings
        {
            RigType = rig,
            AnimationSampleRate = sampleRate,
            ClipOverrides = clips,
        });
    }

    [Fact]
    public void TheSkeletonCoversEveryNode()
    {
        ModelImportResult result = Import();

        Assert.NotNull(result.Avatar);
        Skeleton skeleton = result.Avatar!.Skeleton!;

        // One bone per node, including the model root the importer builds above the file's own nodes.
        int root = skeleton.GetBoneIndex(new StringID("Root"));
        int bone = skeleton.GetBoneIndex(new StringID("Bone"));
        Assert.NotEqual(Skeleton.InvalidIndex, root);
        Assert.NotEqual(Skeleton.InvalidIndex, bone);
        Assert.Equal(root, skeleton.GetParentBoneIndex(bone));
        Assert.Equal(Skeleton.InvalidIndex, skeleton.GetParentBoneIndex(skeleton.GetParentBoneIndex(root)));
        Assert.Equal(1.0, skeleton.GetBoneParentSpaceTransform(bone).position.Y, 3);
    }

    // The point of resampling: the clip has to play back what the curve described.
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0.5f, 2f)]
    [InlineData(1f, 3f)]
    public void TheClipSamplesWhatTheCurveDescribed(float time, float expectedY)
    {
        ModelImportResult result = Import();
        Assert.Single(result.Animations);

        AnimationClip asset = result.Animations[0];
        Skeleton skeleton = result.Avatar!.Skeleton!;
        MotionClip? clip = asset.GetClip(result.Avatar.Runtime);
        Assert.NotNull(clip);

        var pose = new Pose(skeleton);
        clip!.GetPose(time, pose);

        int bone = skeleton.GetBoneIndex(new StringID("Bone"));
        Assert.Equal(expectedY, pose.GetTransform(bone).position.Y, 1);
    }

    [Fact]
    public void TheSampleRateSetsTheFrameCount()
    {
        Assert.Equal(31, Import(sampleRate: 30f).Animations[0].FrameCount);
        Assert.Equal(61, Import(sampleRate: 60f).Animations[0].FrameCount);
    }

    [Fact]
    public void AClipCarriesTheRigItWasAuthoredOn()
    {
        ModelImportResult result = Import();

        AnimationClip clip = result.Animations[0];
        Assert.Equal(AnimationClipKind.Skeletal, clip.Kind);
        Assert.Equal("Slide", clip.Name);
        Assert.Equal(1f, clip.Duration, 2);
        Assert.Same(result.Avatar, clip.Avatar.Res);
    }

    // A model with no bones and no animation has no rig to build, so it should not grow one.
    [Fact]
    public void AModelWithNothingToAnimateGetsNoRig()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          "nodes": [ { "name": "Root" } ]
        }
        """));

        ModelImportResult result = ClayBackedImporter.Import(stream, "plain.gltf", new ModelImporterSettings());

        Assert.Null(result.Avatar);
        Assert.Empty(result.Animations);
    }

    // A model can hold a clip whose keys all sit at one time: a single authored pose. It has no length,
    // and a clip has to last something, so it becomes a held pose rather than failing the whole import.
    [Fact]
    public void AClipWithNoLengthImportsAsAHeldPose()
    {
        ModelImportResult result = Import(lastKeyTime: 0f);

        AnimationClip clip = Assert.Single(result.Animations);
        Assert.True(clip.Duration > 0f, "a clip has to last something");

        // The same pose wherever it is sampled, which is what a clip with no length means.
        Skeleton skeleton = result.Avatar!.Skeleton!;
        int bone = skeleton.GetBoneIndex(new StringID("Bone"));
        MotionClip runtime = clip.GetClip(result.Avatar.Runtime)!;

        var start = new Pose(skeleton);
        var end = new Pose(skeleton);
        runtime.GetPose(0f, start);
        runtime.GetPose(1f, end);

        Assert.Equal(1.0, start.GetTransform(bone).position.Y, 2);
        Assert.Equal(start.GetTransform(bone).position.Y, end.GetTransform(bone).position.Y, 3);
    }

    [Fact]
    public void RigTypeNoneSkipsTheRigEntirely()
    {
        ModelImportResult result = Import(ModelRigType.None);

        Assert.Null(result.Avatar);
        Assert.Empty(result.Animations);
    }

    // ---- per clip overrides --------------------------------------------------------------------

    private static ModelImportResult ImportWith(ModelClipSettings clip)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(RiggedGltf()));
        return ClayBackedImporter.Import(stream, "rig.gltf", new ModelImporterSettings
        {
            ClipOverrides = new Dictionary<string, ModelClipSettings> { ["Slide"] = clip },
        });
    }

    [Fact]
    public void AClipCanBeRenamedOnImport()
    {
        ModelImportResult result = ImportWith(new ModelClipSettings { Name = "Walk" });

        AnimationClip clip = Assert.Single(result.Animations);
        Assert.Equal("Walk", clip.Name);

        // The name in the file is what the settings are keyed on, so it has to survive the rename.
        Assert.Equal("Slide", clip.SourceName);
    }

    [Fact]
    public void AClipCanOverrideLooping()
    {
        Assert.False(ImportWith(new ModelClipSettings { Loop = false }).Animations[0].Loop);
        Assert.True(ImportWith(new ModelClipSettings { Loop = true }).Animations[0].Loop);
    }

    // Trimming cuts a slice out of the take, so the clip is shorter and starts part way along it.
    [Fact]
    public void AClipCanBeTrimmed()
    {
        ModelImportResult result = ImportWith(new ModelClipSettings { TrimStart = 0.5f });

        AnimationClip clip = Assert.Single(result.Animations);
        Assert.Equal(0.5, clip.Duration, 2);
        Assert.Equal(0.5, clip.TakeStart, 2);

        Skeleton skeleton = result.Avatar!.Skeleton!;
        var pose = new Pose(skeleton);
        clip.GetClip(result.Avatar.Runtime)!.GetPose(0f, pose);

        // Half way along a slide from 1 to 3.
        Assert.Equal(2.0, pose.GetTransform(skeleton.GetBoneIndex(new StringID("Bone"))).position.Y, 1);
    }

    // Some files key every bone, so a root that stands still has a position track too. The hips moving under it are the body.
    [Fact]
    public void RootMotionComesFromTheHighestBoneThatMoves_NotOneThatIsOnlyKeyed()
    {
        var skeleton = new Skeleton(
            new[] { new StringID("Root"), new StringID("Hips") },
            new[] { Skeleton.InvalidIndex, 0 },
            new[] { Transform3D.Identity, Transform3D.Identity });

        static Prowl.Clay.AnimationBinding Track(int node, Float3 from, Float3 to) => new()
        {
            NodeIndex = node,
            Property = Prowl.Clay.AnimatedProperty.Position,
            Curve = new Prowl.Vector.AnimationCurve(3, new Keyframe(0f, from), new Keyframe(1f, to)),
        };

        var take = new Prowl.Clay.AnimationClip
        {
            Name = "Walk",
            EndTime = 1f,
            Bindings = new[] { Track(0, Float3.Zero, Float3.Zero), Track(1, Float3.Zero, new Float3(0f, 0f, 2f)) },
        };

        Assert.Equal(1, ModelRigBuilder.AnimatedRootBone(take, skeleton));
    }

    [Fact]
    public void ATrimThatWouldEmptyTheClipIsIgnored()
    {
        ModelImportResult result = ImportWith(new ModelClipSettings { TrimStart = 2f, TrimEnd = 1f });

        Assert.Equal(1.0, result.Animations[0].Duration, 2);
    }

    // ---- the hand made humanoid map ------------------------------------------------------------

    // The Avatar editor writes its mapping into the import settings, so it has to survive the import
    // and win over whatever the auto mapper decided.
    [Fact]
    public void AHandMadeBoneMapIsAppliedOverTheAutoMap()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(RiggedGltf()));
        ModelImportResult result = ClayBackedImporter.Import(stream, "rig.gltf", new ModelImporterSettings
        {
            RigType = ModelRigType.Humanoid,
            HumanoidBoneMap = new Dictionary<string, string> { ["Hips"] = "Bone" },
        });

        Avatar avatar = result.Avatar!;
        Skeleton skeleton = avatar.Skeleton!;
        Assert.NotNull(avatar.Description);
        Assert.Equal(skeleton.GetBoneIndex(new StringID("Bone")), avatar.Description!.GetSkeletonBoneIndex(HumanBodyBone.Hips));
    }

    // An empty entry is how the editor says "the auto mapper should not have claimed this one".
    [Fact]
    public void AnEmptyEntryClearsABone()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(RiggedGltf()));
        ModelImportResult result = ClayBackedImporter.Import(stream, "rig.gltf", new ModelImporterSettings
        {
            RigType = ModelRigType.Humanoid,
            HumanoidBoneMap = new Dictionary<string, string> { ["Hips"] = "Bone", ["Spine"] = "" },
        });

        HumanDescription description = result.Avatar!.Description!;
        Assert.Equal(Skeleton.InvalidIndex, description.GetSkeletonBoneIndex(HumanBodyBone.Spine));
    }

    // ---- materials -----------------------------------------------------------------------------

    // One triangle with one named material, which is all an extraction test needs.
    private static string MaterialGltf()
    {
        var bytes = new byte[9 * sizeof(float) + 3 * sizeof(ushort)];
        var span = bytes.AsSpan();
        for (int i = 0; i < 9; i++) BitConverter.TryWriteBytes(span[(i * sizeof(float))..], i % 3 == 0 ? 0f : 1f);
        for (ushort i = 0; i < 3; i++) BitConverter.TryWriteBytes(span[(36 + i * sizeof(ushort))..], i);

        return $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          "nodes": [ { "name": "Tri", "mesh": 0 } ],
          "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1, "material": 0 } ] } ],
          "materials": [ { "name": "Red" } ],
          "buffers": [ { "byteLength": {{bytes.Length}}, "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(bytes)}}" } ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 36, "byteLength": 6 }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [ 0, 0, 0 ], "max": [ 1, 1, 1 ] },
            { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
          ]
        }
        """;
    }

    private sealed class FixedMaterialResolver : IModelMaterialResolver
    {
        private readonly string _name;
        private readonly AssetRef<Material> _result;

        public FixedMaterialResolver(string name, AssetRef<Material> result) { _name = name; _result = result; }

        public AssetRef<Material> Resolve(string materialName) => materialName == _name ? _result : default;
    }

    private static ModelImportResult ImportMaterials(IModelMaterialResolver? resolver)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(MaterialGltf()));
        return ClayBackedImporter.Import(stream, "tri.gltf", new ModelImporterSettings { MaterialResolver = resolver });
    }

    [Fact]
    public void AMaterialIsOwnedByTheImportUnlessSomethingClaimsIt()
    {
        ModelImportResult result = ImportMaterials(null);

        Material material = Assert.Single(result.Materials);
        Assert.Equal("Red", material.Name);
    }

    // An extracted material lives in the project, so the import references it and owns nothing.
    [Fact]
    public void AClaimedMaterialIsReferencedRatherThanBuilt()
    {
        ModelImportResult result = ImportMaterials(new FixedMaterialResolver("Red", new AssetRef<Material>(Guid.NewGuid())));

        Assert.Empty(result.Materials);
    }

    // The heal: a reference that has gone missing is not claimed, so the import rebuilds the material
    // and the model still renders.
    [Fact]
    public void AMissingReferenceFallsBackToAnOwnedMaterial()
    {
        ModelImportResult result = ImportMaterials(new FixedMaterialResolver("Red", default));

        Assert.Single(result.Materials);
    }

    // ---- root motion ----------------------------------------------------------------------------

    /// <summary>
    /// A clip whose body travels across the ground moves the character by it, and plays on the spot:
    /// a looping walk otherwise slides away and snaps back every loop.
    /// </summary>
    [Fact]
    public void TravelAcrossTheGroundBecomesRootMotion()
    {
        ModelImportResult result = Import(endZ: 2f);
        Skeleton skeleton = result.Avatar!.Skeleton!;
        MotionClip clip = result.Animations[0].GetClip(result.Avatar.Runtime)!;

        Assert.True(clip.HasRootMotion);
        Assert.Equal(2.0, clip.RootMotion!.TotalDelta.position.Z, 2);

        // The rise stays in the pose, since height is left alone unless asked for.
        var pose = new Pose(skeleton);
        clip.GetPose(1f, pose);
        int bone = skeleton.GetBoneIndex(new StringID("Bone"));
        Assert.Equal(0.0, pose.GetTransform(bone).position.Z, 2);
        Assert.Equal(3.0, pose.GetTransform(bone).position.Y, 2);
    }

    [Fact]
    public void TravelTurnedOffStaysInThePose()
    {
        var clips = new Dictionary<string, ModelClipSettings> { ["Slide"] = new() { RootTravel = false } };
        ModelImportResult result = Import(endZ: 2f, clips: clips);
        Skeleton skeleton = result.Avatar!.Skeleton!;
        MotionClip clip = result.Animations[0].GetClip(result.Avatar.Runtime)!;

        Assert.Equal(0.0, clip.RootMotion!.TotalDelta.position.Z, 3);

        var pose = new Pose(skeleton);
        clip.GetPose(1f, pose);
        Assert.Equal(2.0, pose.GetTransform(skeleton.GetBoneIndex(new StringID("Bone"))).position.Z, 2);
    }

    // ---- events -------------------------------------------------------------------------------

    /// <summary>
    /// Events are timed on the take, so a trimmed clip shifts them by where it starts, cuts a stretch to
    /// its ends, and drops what falls outside it.
    /// </summary>
    [Fact]
    public void EventsFollowTheTrim()
    {
        var clips = new Dictionary<string, ModelClipSettings>
        {
            ["Slide"] = new()
            {
                TrimStart = 0.25f,
                TrimEnd = 0.75f,
                Events = new List<ClipEvent>
                {
                    new() { Kind = ClipEventKind.Named, Time = 0.1f, Name = "Before" },
                    new() { Kind = ClipEventKind.Named, Time = 0.5f, Name = "Middle" },
                    new() { Kind = ClipEventKind.TransitionWindow, Time = 0.6f, Length = 0.4f },
                },
            },
        };

        AnimationClip clip = Import(clips: clips).Animations[0];

        Assert.Equal(2, clip.Events.Count);
        Assert.Equal("Middle", clip.Events[0].Name);
        Assert.Equal(0.25f, clip.Events[0].Time, 3);
        Assert.Equal(0.35f, clip.Events[1].Time, 3);
        Assert.Equal(0.15f, clip.Events[1].Length, 3);
    }
}
