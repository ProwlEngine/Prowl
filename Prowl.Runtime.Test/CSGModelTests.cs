// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.CSG;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using Operation = Prowl.Vector.Geometry.CSGOperation;

namespace Prowl.Runtime.Test;

/// <summary>
/// End-to-end tests for the CSG component pipeline: a GameObject hierarchy mirrored into a CSG
/// scene, rebuilt when it changes, and written out as meshes.
/// </summary>
public class CSGModelTests : RuntimeTestBase
{
    private Scene _scene = null!;
    private CSGModel _model = null!;

    private CSGModel CreateModel()
    {
        _scene = CreateScene(enable: true);

        GameObject root = CreateGameObject("Model");
        _scene.Add(root);
        _model = root.AddComponent<CSGModel>();
        _model.RebuildAtRuntime = true;
        return _model;
    }

    private CSGBrush AddBrush(Float3 size, Float3 position, Operation op = Operation.Additive,
                              GameObject? parent = null)
    {
        GameObject go = CreateGameObject("Brush");
        go.SetParent(parent ?? _model.GameObject);
        go.Transform.Position = position;

        CSGBrush brush = go.AddComponent<CSGBrush>();
        brush.ResetToBox(size);
        brush.Operation = op;
        return brush;
    }

    private CSGGroup AddGroup(Operation op = Operation.Additive, GameObject? parent = null)
    {
        GameObject go = CreateGameObject("Group");
        go.SetParent(parent ?? _model.GameObject);

        CSGGroup group = go.AddComponent<CSGGroup>();
        group.Operation = op;
        return group;
    }

    /// <summary>Rough solid volume, from the divergence theorem over the output triangles.</summary>
    private static float VolumeOf(Mesh mesh)
    {
        float total = 0f;
        Float3[] v = mesh.Vertices;
        uint[] idx = mesh.Indices;

        for (int i = 0; i < idx.Length; i += 3)
            total += Float3.Dot(v[idx[i]], Float3.Cross(v[idx[i + 1]], v[idx[i + 2]])) / 6f;

        return MathF.Abs(total);
    }

    // ================================================================
    //  The hierarchy drives the CSG scene
    // ================================================================

    [Fact]
    public void BrushesUnderTheModelBecomeAMesh()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);

        Update(_scene);

        Assert.NotNull(_model.GeneratedMesh);
        Assert.Equal(1, _model.BrushCount);
        Assert.Equal(8f, VolumeOf(_model.GeneratedMesh!), 2);
    }

    [Fact]
    public void SubtractiveBrushesCarveTheSolid()
    {
        CreateModel();
        AddBrush(Float3.One * 4f, Float3.Zero);
        AddBrush(Float3.One * 2f, new Float3(2, 0, 0), Operation.Subtractive);

        Update(_scene);

        // A 2-cube centred on a face of a 4-cube removes half its volume: 64 - 4 = 60.
        Assert.Equal(60f, VolumeOf(_model.GeneratedMesh!), 1);
    }

    /// <summary>
    /// The reason groups exist. The group resolves to (A minus B) and only that is subtracted from
    /// the world, so B's volume stays solid. Flattening the same brushes into the root would
    /// subtract B as well and give 212 - which is what this test exists to catch.
    /// </summary>
    [Fact]
    public void AGroupResolvesBeforeItIsSubtracted()
    {
        CreateModel();
        AddBrush(Float3.One * 6f, Float3.Zero);

        CSGGroup group = AddGroup(Operation.Subtractive);
        AddBrush(Float3.One * 2f, new Float3(3, 0, 0), Operation.Additive, group.GameObject);
        AddBrush(new Float3(2, 1, 2), new Float3(3, 0.5f, 0), Operation.Subtractive, group.GameObject);

        Update(_scene);

        Assert.Equal(214f, VolumeOf(_model.GeneratedMesh!), 1);
    }

    /// <summary>The same, one level deeper: a group's own children may be groups.</summary>
    [Fact]
    public void NestedGroupsResolveInnermostFirst()
    {
        CreateModel();
        AddBrush(Float3.One * 6f, Float3.Zero);

        CSGGroup outer = AddGroup(Operation.Subtractive);
        AddBrush(Float3.One * 2f, new Float3(3, 0, 0), Operation.Additive, outer.GameObject);

        CSGGroup inner = AddGroup(Operation.Subtractive, outer.GameObject);
        AddBrush(new Float3(2, 1, 2), new Float3(3, 0.5f, 0), Operation.Additive, inner.GameObject);

        Update(_scene);

        Assert.Equal(214f, VolumeOf(_model.GeneratedMesh!), 1);
    }

    [Fact]
    public void PassThroughGroupsAreNotCSGBoundaries()
    {
        CreateModel();
        AddBrush(Float3.One * 4f, Float3.Zero);

        CSGGroup group = AddGroup(Operation.Subtractive);
        group.PassThrough = true;
        AddBrush(Float3.One * 2f, new Float3(2, 0, 0), Operation.Additive, group.GameObject);

        Update(_scene);

        // The child folds into the root as the additive brush it is, adding the 4 units sticking
        // out of the face. Honouring the group's own operation would instead carve 4 away, for 60.
        Assert.Equal(68f, VolumeOf(_model.GeneratedMesh!), 1);
    }

    // ================================================================
    //  Rebuilding on change
    // ================================================================

    /// <summary>Nothing notifies the model that a brush moved, so it has to notice by itself.</summary>
    [Fact]
    public void MovingABrushRebuildsTheMesh()
    {
        CreateModel();
        AddBrush(Float3.One * 4f, Float3.Zero);
        CSGBrush cutter = AddBrush(Float3.One * 2f, new Float3(2, 0, 0), Operation.Subtractive);

        Update(_scene);
        float carved = VolumeOf(_model.GeneratedMesh!);

        cutter.Transform.Position = new Float3(20, 0, 0);
        Update(_scene);

        Assert.Equal(64f, VolumeOf(_model.GeneratedMesh!), 1);
        Assert.True(carved < 64f);
    }

    [Fact]
    public void ChangingAnOperationRebuildsTheMesh()
    {
        CreateModel();
        AddBrush(Float3.One * 4f, Float3.Zero);
        CSGBrush second = AddBrush(Float3.One * 2f, new Float3(2, 0, 0));

        Update(_scene);
        float added = VolumeOf(_model.GeneratedMesh!);

        second.Operation = Operation.Subtractive;
        Update(_scene);

        Assert.True(VolumeOf(_model.GeneratedMesh!) < added);
    }

    [Fact]
    public void ChangingAPlaneRebuildsTheShape()
    {
        CreateModel();
        CSGBrush brush = AddBrush(Float3.One * 2f, Float3.Zero);
        Update(_scene);

        brush.ResetToBox(new Float3(4, 2, 2));
        Update(_scene);

        Assert.Equal(16f, VolumeOf(_model.GeneratedMesh!), 1);
    }

    [Fact]
    public void DisablingABrushRemovesItFromTheOutput()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);
        CSGBrush second = AddBrush(Float3.One * 2f, new Float3(10, 0, 0));

        Update(_scene);
        Assert.Equal(2, _model.BrushCount);

        second.GameObject.Enabled = false;
        Update(_scene);

        Assert.Equal(1, _model.BrushCount);
        Assert.Equal(8f, VolumeOf(_model.GeneratedMesh!), 2);
    }

    [Fact]
    public void AddingABrushLaterIsPickedUp()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);
        Update(_scene);

        AddBrush(Float3.One * 2f, new Float3(10, 0, 0));
        Update(_scene);

        Assert.Equal(2, _model.BrushCount);
        Assert.Equal(16f, VolumeOf(_model.GeneratedMesh!), 2);
    }

    [Fact]
    public void AnUnchangedModelDoesNotProduceNewMeshes()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);

        Update(_scene);
        Mesh? first = _model.GeneratedMesh;

        Update(_scene, 3);

        Assert.Same(first, _model.GeneratedMesh);
    }

    // ================================================================
    //  Output wiring
    // ================================================================

    [Fact]
    public void TheGeneratedMeshReachesTheRendererAndCollider()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);

        Update(_scene);

        MeshRenderer renderer = _model.GameObject.GetComponent<MeshRenderer>();
        MeshCollider collider = _model.GameObject.GetComponent<MeshCollider>();

        Assert.True(renderer.IsValid());
        Assert.Same(_model.GeneratedMesh, renderer.Mesh);
        Assert.Single(renderer.Materials);

        Assert.True(collider.IsValid());
        Assert.Same(_model.GeneratedCollisionMesh, collider.Mesh);
    }

    /// <summary>Per-surface materials have to survive all the way into submeshes.</summary>
    [Fact]
    public void PerSurfaceMaterialsBecomeSubmeshes()
    {
        CreateModel();
        CSGBrush brush = AddBrush(Float3.One * 2f, Float3.Zero);

        var material = new Material(Shader.LoadDefault(DefaultShader.Invalid));
        SurfaceData surface = brush.Surfaces[0];
        surface.Material = material;
        brush.SetSurface(0, surface);

        Update(_scene);

        Assert.Equal(2, _model.GeneratedMesh!.SubMeshCount);
        Assert.Equal(2, _model.GeneratedMaterials.Count);
    }

    [Fact]
    public void SurfaceFlagsSplitVisibleFromCollisionGeometry()
    {
        CreateModel();
        CSGBrush brush = AddBrush(Float3.One * 2f, Float3.Zero);

        SurfaceData surface = brush.Surfaces[0];
        surface.Flags = CSGSurfaceFlags.NoRender;
        brush.SetSurface(0, surface);

        Update(_scene);

        Assert.Equal(30, _model.GeneratedMesh!.Indices.Length);
        Assert.Equal(36, _model.GeneratedCollisionMesh!.Indices.Length);
    }

    [Fact]
    public void ModelFlagsCanDropCollisionEntirely()
    {
        CreateModel();
        _model.Flags = CSGModelFlags.NoCollider;
        AddBrush(Float3.One * 2f, Float3.Zero);

        Update(_scene);

        Assert.NotNull(_model.GeneratedMesh);
        Assert.Null(_model.GeneratedCollisionMesh);
    }

    // ================================================================
    //  Surfaces
    // ================================================================

    /// <summary>
    /// A brush's shape is derived from its planes, and redundant planes contribute no face - so the
    /// face-to-plane map is what keeps surface data attached to the right face.
    /// </summary>
    [Fact]
    public void RedundantPlanesDoNotShiftSurfaceAssignment()
    {
        CreateModel();
        CSGBrush brush = AddBrush(Float3.One * 2f, Float3.Zero);

        // A plane far outside the box cuts nothing, so it produces no face.
        var planes = new List<Plane>(brush.Planes) { new(Float3.UnitX, 50f) };
        brush.Planes = planes.ToArray();

        Assert.Equal(7, brush.Planes.Length);
        Assert.Equal(7, brush.Surfaces.Length);
        Assert.Equal(6, brush.FaceToPlane.Length);

        for (int face = 0; face < 6; face++)
            Assert.Equal(face, brush.FaceToPlane[face]);
    }

    [Fact]
    public void ABrushWhosePlanesEncloseNothingIsSkipped()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);
        CSGBrush broken = AddBrush(Float3.One * 2f, new Float3(10, 0, 0));

        // Two opposed planes that never meet cannot bound a solid.
        broken.Planes = [new Plane(Float3.UnitX, 1f), new Plane(-Float3.UnitX, 1f)];

        Update(_scene);

        Assert.False(broken.IsShapeValid);
        Assert.Equal(1, _model.BrushCount);
        Assert.Equal(8f, VolumeOf(_model.GeneratedMesh!), 2);
    }

    [Fact]
    public void ComponentsTheModelIgnoresDoNotCauseEndlessRebuilds()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);
        AddGroup().PassThrough = true;
        AddBrush(Float3.One, new Float3(5, 0, 0)).Planes = [new Plane(Float3.UnitX, 1f), new Plane(-Float3.UnitX, 1f)];
        AddBrush(Float3.One, new Float3(9, 0, 0)).Enabled = false;

        Update(_scene);
        int builds = _model.BuildCount;
        Update(_scene, 3);

        Assert.Equal(builds, _model.BuildCount);
    }

    [Fact]
    public void MovingTheWholeModelDoesNotRebuild()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);

        Update(_scene);
        int builds = _model.BuildCount;
        _model.Transform.Position = new Float3(3, 0, 0);
        Update(_scene, 2);

        Assert.Equal(builds, _model.BuildCount);
    }

    [Fact]
    public void AModelNeverDestroysAMeshAnotherModelUses()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);
        Update(_scene);
        Mesh shared = _model.GeneratedMesh!;
        CSGModel first = _model;

        GameObject copy = CreateGameObject("Copy");
        _scene.Add(copy);
        copy.AddComponent<MeshRenderer>().Mesh = shared;
        _model = copy.AddComponent<CSGModel>();
        _model.RebuildAtRuntime = true;
        AddBrush(Float3.One, Float3.Zero);

        Update(_scene);
        AddBrush(Float3.One * 3f, Float3.Zero, parent: first.GameObject);
        Update(_scene);

        Assert.True(shared.IsValid());
        Assert.NotSame(shared, _model.GeneratedMesh);
    }

    [Fact]
    public void LightmapUVsSurviveARebuildOfTheSameGeometry()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);
        _model.GenerateLightmapUVs = true;
        Update(_scene);

        _model.RebuildLightmapUVs();
        Mesh unwrapped = _model.GeneratedMesh!;
        _model.SetDirty();
        Update(_scene);

        Assert.Same(unwrapped, _model.GeneratedMesh);
        Assert.False(_model.LightmapUVsStale);
    }

    [Fact]
    public void ModelsDoNotRebuildInAPlayerUnlessAsked()
    {
        CreateModel();
        CSGBrush brush = AddBrush(Float3.One * 2f, Float3.Zero);
        Update(_scene);
        Mesh built = _model.GeneratedMesh!;

        _model.RebuildAtRuntime = false;
        brush.Transform.Position = new Float3(4, 0, 0);
        Update(_scene);

        Assert.Same(built, _model.GeneratedMesh);
    }

    [Fact]
    public void ChangingOnlyATextureOffsetReachesTheMesh()
    {
        CreateModel();
        CSGBrush brush = AddBrush(Float3.One * 2f, Float3.Zero);
        Update(_scene);
        Float2[] before = _model.GeneratedMesh!.UV;

        SurfaceData surface = brush.Surfaces[0];
        surface.Offset = new Float2(0.25f, 0.5f);
        brush.SetSurface(0, surface);
        Update(_scene);

        Assert.NotEqual(before, _model.GeneratedMesh!.UV);
    }

    [Fact]
    public void ReorderingSiblingsRebuildsTheModel()
    {
        CreateModel();
        CSGBrush solid = AddBrush(Float3.One * 2f, Float3.Zero);
        CSGBrush hole = AddBrush(Float3.One, Float3.Zero, Operation.Subtractive);
        Update(_scene);
        float carved = VolumeOf(_model.GeneratedMesh!);

        hole.GameObject.SiblingIndex = 0;
        Update(_scene);

        Assert.True(carved < 7.5f);
        Assert.Equal(8f, VolumeOf(_model.GeneratedMesh!), 2);
    }

    [Fact]
    public void HiddenBrushesAreOnlyLeftOutOfWhatIsDrawn()
    {
        CreateModel();
        AddBrush(Float3.One * 2f, Float3.Zero);
        CSGBrush hidden = AddBrush(Float3.One * 2f, new Float3(5, 0, 0));
        Update(_scene);

        try
        {
            CSGModel.EditorHidden = b => ReferenceEquals(b, hidden);
            _model.SetDirty();
            Update(_scene);

            MeshRenderer renderer = _model.GetComponent<MeshRenderer>();
            Assert.Equal(8f, VolumeOf(renderer.DrawOverride!), 2);
            Assert.Same(_model.GeneratedMesh, renderer.Mesh);
            Assert.Equal(16f, VolumeOf(renderer.Mesh!), 2);
        }
        finally
        {
            CSGModel.EditorHidden = null;
        }
    }

    [Fact]
    public void MovingABrushAndBackLeavesLightmapUVsStale()
    {
        CreateModel();
        CSGBrush brush = AddBrush(Float3.One * 2f, Float3.Zero);
        _model.GenerateLightmapUVs = true;
        Update(_scene);
        _model.RebuildLightmapUVs();

        brush.Transform.Position = new Float3(1, 0, 0);
        Update(_scene);
        brush.Transform.Position = Float3.Zero;
        Update(_scene);

        Assert.True(_model.LightmapUVsStale);
    }
}
