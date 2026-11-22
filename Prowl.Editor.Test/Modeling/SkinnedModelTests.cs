// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Modeling;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

public class SkinnedModelTests : EditorTestHarness
{
    /// <summary>A two bone strip: the lower quad follows the first bone, the upper the second, with one blend shape.</summary>
    private static Mesh SkinnedStrip()
    {
        var mesh = new Mesh
        {
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(1, 2, 0), new(0, 2, 0)],
            Indices = [0, 2, 1, 0, 3, 2, 3, 4, 2, 3, 5, 4],
            BoneIndices = [new(0, 0, 0, 0), new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0)],
            BoneWeights = [new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0)],
            BindPoses = [Float4x4.Identity, Float4x4.CreateTranslation(new Float3(0, -1, 0))],
            BoneNames = ["Hip", "Spine"],
        };
        mesh.BlendShapes =
        [
            new BlendShape
            {
                Name = "Bulge",
                Frames = [new BlendShapeFrame { Weight = 100f, DeltaVertices = [Float3.Zero, Float3.Zero, new(0.5f, 0, 0), Float3.Zero, Float3.Zero, Float3.Zero] }],
            },
        ];
        return mesh;
    }

    private static PMeshObject SkinnedItem()
    {
        var item = new PMeshObject { Name = "Body", Mesh = MeshGeometry.ToGeometryData(SkinnedStrip()) };
        item.Bones.Add(new PMeshBone { Name = "Hip", Parent = -1, Position = Float3.Zero, BindPose = Float4x4.Identity });
        item.Bones.Add(new PMeshBone { Name = "Spine", Parent = 0, Position = new Float3(0, 1, 0), BindPose = Float4x4.CreateTranslation(new Float3(0, -1, 0)) });
        item.Shapes.Add(new PMeshShape { Name = "Bulge", Weight = 25f, FrameWeights = [100f] });
        return item;
    }

    [Fact]
    public void ASkeletonAndBlendShapesReadBackFromTheDocument()
    {
        var document = new PMeshDocument { Objects = { SkinnedItem() } };

        PMeshObject read = PMeshDocument.Read(document.Write()).Objects[0];

        Assert.Equal(["Hip", "Spine"], read.Bones.Select(b => b.Name));
        Assert.Equal(0, read.Bones[1].Parent);
        Assert.Equal(new Float3(0, 1, 0), read.Bones[1].Position);
        Assert.Equal(-1f, read.Bones[1].BindPose.c3.Y, 5);
        PMeshShape shape = Assert.Single(read.Shapes);
        Assert.Equal("Bulge", shape.Name);
        Assert.Equal(25f, shape.Weight);
        Assert.Equal(new[] { 100f }, shape.FrameWeights);
    }

    [Fact]
    public void ImportingASkinnedObjectMakesBonesAndASkinnedRenderer()
    {
        File.WriteAllBytes(AssetAbsolutePath("Strip.pmesh"), new PMeshDocument { Objects = { SkinnedItem() } }.Write());
        Assets.Refresh();

        GameObject root = GameObject.InstantiateDetached(Load<PrefabAsset>(Assets.GetEntry("Strip.pmesh")!.Guid)!);

        GameObject body = root.Transform.Find("Body")!.GameObject;
        Assert.Null(body.GetComponent<MeshRenderer>());
        SkinnedMeshRenderer skinned = body.GetComponent<SkinnedMeshRenderer>()!;
        Assert.NotNull(skinned);
        Mesh mesh = skinned.SharedMesh!;
        Assert.Equal(new[] { "Hip", "Spine" }, mesh.BoneNames!);
        Assert.Equal(2, mesh.BindPoses!.Length);
        Assert.True(mesh.HasBoneIndices && mesh.HasBoneWeights);
        Assert.Equal("Bulge", mesh.GetBlendShapeName(0));
        Assert.Equal(25f, skinned.GetBlendShapeWeight(0));

        Transform?[] bones = skinned.Bones!;
        Assert.Equal(2, bones.Length);
        Assert.Equal("Spine", bones[1]!.GameObject.Name);
        Assert.Same(bones[0], bones[1]!.Parent);
        Assert.Equal(new Float3(0, 1, 0), bones[1]!.LocalPosition);
    }

    [Fact]
    public void EditingASkinnedObjectAsAModelBringsItsSkeletonAndShapes()
    {
        var go = new GameObject("Character");
        var hip = new GameObject("Hip");
        hip.SetParent(go, false);
        var spine = new GameObject("Spine");
        spine.SetParent(hip, false);
        spine.Transform.LocalPosition = new Float3(0, 1, 0);
        SkinnedMeshRenderer skinned = go.AddComponent<SkinnedMeshRenderer>();
        skinned.SharedMesh = SkinnedStrip();
        skinned.SetBones([hip.Transform, spine.Transform], hip.Transform);
        skinned.SetBlendShapeWeight(0, 40f);

        PMeshDocument document = PMeshDocument.FromObjects([go], go);

        PMeshObject item = Assert.Single(document.Objects);
        Assert.Equal(["Hip", "Spine"], item.Bones.Select(b => b.Name));
        Assert.Equal(-1, item.Bones[0].Parent);
        Assert.Equal(0, item.Bones[1].Parent);
        Assert.True(Float3.Distance(item.Bones[1].Position, new Float3(0, 1, 0)) < 1e-5f);
        Assert.Equal(-1f, item.Bones[1].BindPose.c3.Y, 5);
        Assert.Equal(40f, Assert.Single(item.Shapes).Weight);
        Assert.True(item.Mesh.HasLoopAttribute(MeshGeometry.BoneIndices));
    }

    /// <summary>The bone weighing most at a corner, and the corner's weights added up.</summary>
    private static (int Bone, float Weight) Skin(GeometryData.Loop corner)
    {
        if (!corner.TryGetAttribute(MeshGeometry.BoneIndices, out GeometryData.AttributeValue? i) || i is not GeometryData.IntAttributeValue ids) return (-1, 0f);
        if (!corner.TryGetAttribute(MeshGeometry.BoneWeights, out GeometryData.AttributeValue? w) || w is not GeometryData.FloatAttributeValue ws) return (-1, 0f);
        int heaviest = Array.IndexOf(ws.Data, ws.Data.Max());
        return (ids.Data[heaviest], ws.Data.Sum());
    }

    [Fact]
    public void FacesMadeFromNothingTakeTheSkinAndShapesOfThePointsTheyGrewFrom()
    {
        GeometryData mesh = MeshGeometry.ToGeometryData(SkinnedStrip());
        var before = mesh.Vertices.ToDictionary(v => v, v => v.Point);
        var facesBefore = new HashSet<GeometryData.Face>(mesh.Faces);
        GeometryData.Vertex side = mesh.Vertices.First(v => v.Point == new Float3(1, 1, 0));
        GeometryData.Vertex corner = mesh.Vertices.First(v => v.Point == new Float3(1, 2, 0));
        GeometryData.Vertex grown = mesh.AddVertex(1.5f, 1f, 0f);
        GeometryData.Face made = mesh.AddFace(side, grown, corner)!;

        Operators.FinishEdit(mesh, before, facesBefore);

        foreach (GeometryData.Loop at in UVs.Corners(made))
        {
            (int bone, float weight) = Skin(at);
            Assert.True(MathF.Abs(weight - 1f) < 1e-4f, $"weight {weight} at {at.Vert.Point}");
            Assert.Equal(1, bone);
        }
        GeometryData.Loop fromSide = UVs.CornerAt(made, grown)!;
        Assert.True(fromSide.TryGetAttribute(MeshGeometry.BlendShapePosition(0, 0), out GeometryData.AttributeValue? delta)
                    && delta is GeometryData.FloatAttributeValue d && MathF.Abs(d.Data[0] - 0.5f) < 1e-5f);
    }

    [Fact]
    public void JoiningMergesSkeletonsAndShapesByName()
    {
        PMeshObject target = SkinnedItem();
        PMeshObject other = SkinnedItem();
        other.Bones[1].Name = "Head";
        other.Shapes[0].Name = "Smile";
        var plain = new PMeshObject { Name = "Hat", Mesh = Primitives.Make(PrimitiveKind.Cube) };
        Float4x4 move = Float4x4.CreateTranslation(new Float3(5, 0, 0));

        Operators.MergeSkins(target.Mesh, target.Bones, target.Shapes, other.Mesh, other.Bones, other.Shapes, move);
        Operators.Join(target.Mesh, target.Materials, other.Mesh, other.Materials, move);
        Operators.MergeSkins(target.Mesh, target.Bones, target.Shapes, plain.Mesh, plain.Bones, plain.Shapes, Float4x4.Identity);
        Operators.Join(target.Mesh, target.Materials, plain.Mesh, plain.Materials, Float4x4.Identity);

        Assert.Equal(new[] { "Hip", "Spine", "Head" }, target.Bones.Select(b => b.Name));
        Assert.Equal(0, target.Bones[2].Parent);
        Assert.Equal(-5f, target.Bones[2].BindPose.c3.X, 4);
        Assert.Equal(new[] { "Bulge", "Smile" }, target.Shapes.Select(s => s.Name));

        foreach (GeometryData.Face face in target.Mesh.Faces)
            foreach (GeometryData.Loop corner in UVs.Corners(face))
            {
                (int bone, float weight) = Skin(corner);
                Assert.True(MathF.Abs(weight - 1f) < 1e-4f, $"weight {weight} at {corner.Vert.Point}");
                Float3 p = corner.Vert.Point;
                int expected = p.X > 4f ? (p.Y >= 1f ? 2 : 0) : MathF.Abs(p.Z) > 0.4f ? 0 : p.Y >= 1f ? 1 : 0;
                Assert.True(bone == expected, $"bone {bone} at {p}");
            }

        GeometryData.Vertex moved = target.Mesh.Vertices.First(v => Float3.Distance(v.Point, new Float3(6, 1, 0)) < 1e-4f);
        GeometryData.Loop at = moved.NeighborFaces().Select(f => UVs.CornerAt(f, moved)!).First();
        Assert.True(at.TryGetAttribute(MeshGeometry.BlendShapePosition(1, 0), out GeometryData.AttributeValue? smile) && smile is GeometryData.FloatAttributeValue d && MathF.Abs(d.Data[0] - 0.5f) < 1e-5f);
        Assert.True(!at.TryGetAttribute(MeshGeometry.BlendShapePosition(0, 0), out GeometryData.AttributeValue? bulge) || bulge is GeometryData.FloatAttributeValue z && z.Data.All(x => x == 0f));
    }
}
