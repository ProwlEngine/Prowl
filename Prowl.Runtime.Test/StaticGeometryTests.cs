// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class StaticGeometryTests : RuntimeTestBase
{
    private readonly Mesh _cube = Mesh.CreateCube(Float3.One);
    private readonly Material _material = new(Shader.LoadDefault(DefaultShader.Standard));

    private MeshRenderer AddRenderer(Scene scene, Float3 position, Material? material = null, Mesh? mesh = null, bool isStatic = true)
    {
        GameObject go = CreateGameObject("Renderer");
        go.IsStatic = isStatic;
        go.Transform.Position = position;
        scene.Add(go);
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = mesh ?? _cube;
        renderer.Material = material ?? _material;
        return renderer;
    }

    // Renderers only drop out of the batch while editing, at runtime it stays as built
    private static void EnterEditing()
    {
        Application.IsEditor = true;
        Application.IsPlaying = false;
    }

    private static List<IRenderable> Collect(Scene scene)
    {
        var renderables = new List<IRenderable>();
        scene.CollectRenderables(null!, renderables, new List<IRenderableLight>());
        return renderables;
    }

    private static uint DrawnIndices(List<IRenderable> renderables)
    {
        var ranges = new List<IndexRange>();
        foreach (IRenderable renderable in renderables)
            if (renderable is IIndexRangeRenderable ranged)
                ranged.AppendRanges(ranges);
        uint total = 0;
        foreach (IndexRange range in ranges) total += range.Count;
        return total;
    }

    [Fact]
    public void IndexRange_Append_MergesTouchingRanges()
    {
        var ranges = new List<IndexRange>();
        IndexRange.Append(ranges, 0, 6);
        IndexRange.Append(ranges, 6, 3);
        IndexRange.Append(ranges, 12, 3);
        Assert.Equal([new IndexRange(0, 9), new IndexRange(12, 3)], ranges);
    }

    [Fact]
    public void Update_MergesStaticRenderersSharingAMaterial()
    {
        Scene scene = CreateScene(enable: true);
        AddRenderer(scene, new Float3(-5, 0, 0));
        AddRenderer(scene, new Float3(5, 0, 0));

        scene.UpdateStaticGeometry();

        Assert.Equal(1, scene.StaticGeometry.GroupCount);
        Assert.Equal(2, scene.StaticGeometry.LiveSourceCount);

        List<IRenderable> renderables = Collect(scene);
        Assert.All(renderables, r => Assert.IsAssignableFrom<IIndexRangeRenderable>(r));
        Assert.Equal((uint)_cube.IndexCount * 2, DrawnIndices(renderables));

        // Merged into world space, so the shared mesh spans both cubes
        renderables[0].GetRenderingData(default, out _, out Mesh merged, out Float4x4 model, out _);
        Assert.Equal(Float4x4.Identity, model);
        Assert.Equal(-5.5f, merged.bounds.Min.X, 4);
        Assert.Equal(5.5f, merged.bounds.Max.X, 4);
    }

    [Fact]
    public void Update_MergesIdenticalMaterials_ButNotDifferentOnes()
    {
        Scene scene = CreateScene(enable: true);
        var twin = new Material(Shader.LoadDefault(DefaultShader.Standard));
        var red = new Material(Shader.LoadDefault(DefaultShader.Standard));
        red.SetColor("_MainColor", new Color(1, 0, 0, 1));

        AddRenderer(scene, new Float3(0, 0, 0));
        AddRenderer(scene, new Float3(2, 0, 0), twin);
        AddRenderer(scene, new Float3(4, 0, 0), red);

        scene.UpdateStaticGeometry();

        Assert.Equal(2, scene.StaticGeometry.GroupCount);
        Assert.Equal(3, scene.StaticGeometry.LiveSourceCount);
    }

    [Fact]
    public void Update_LeavesMovingTransparentAndSkinnedRenderersAlone()
    {
        Scene scene = CreateScene(enable: true);
        var glass = new Material(Shader.LoadDefault(DefaultShader.StandardTransparent));
        Mesh skinned = Mesh.CreateCube(Float3.One);
        skinned.BoneIndices = new Float4[skinned.VertexCount];
        skinned.BoneWeights = Enumerable.Repeat(new Float4(1, 0, 0, 0), skinned.VertexCount).ToArray();

        AddRenderer(scene, Float3.Zero, isStatic: false);
        AddRenderer(scene, Float3.Zero, glass);
        AddRenderer(scene, Float3.Zero, mesh: skinned);

        scene.UpdateStaticGeometry();

        Assert.Equal(0, scene.StaticGeometry.LiveSourceCount);
        Assert.Equal(3, Collect(scene).Count(r => r is MeshRenderable));
    }

    [Fact]
    public void BatchedRenderer_DrawsNothingItself()
    {
        Scene scene = CreateScene(enable: true);
        AddRenderer(scene, Float3.Zero);
        Assert.Single(Collect(scene), r => r is MeshRenderable);

        scene.UpdateStaticGeometry();

        Assert.DoesNotContain(Collect(scene), r => r is MeshRenderable);
    }

    [Fact]
    public void MovingABatchedRenderer_DropsItOutOfTheBatch()
    {
        EnterEditing();
        Scene scene = CreateScene(enable: true);
        MeshRenderer moved = AddRenderer(scene, new Float3(-5, 0, 0));
        AddRenderer(scene, new Float3(5, 0, 0));
        scene.UpdateStaticGeometry();

        moved.Transform.Position = new Float3(-6, 0, 0);
        List<IRenderable> renderables = Collect(scene);

        Assert.Equal(1, scene.StaticGeometry.LiveSourceCount);
        Assert.Single(renderables, r => r is MeshRenderable);
        Assert.Equal((uint)_cube.IndexCount, DrawnIndices(renderables));
    }

    [Fact]
    public void DisablingOrUnmarkingStatic_DropsItOutOfTheBatch()
    {
        EnterEditing();
        Scene scene = CreateScene(enable: true);
        MeshRenderer disabled = AddRenderer(scene, new Float3(0, 0, 0));
        MeshRenderer unmarked = AddRenderer(scene, new Float3(3, 0, 0));
        AddRenderer(scene, new Float3(6, 0, 0));
        scene.UpdateStaticGeometry();

        disabled.Enabled = false;
        unmarked.GameObject.IsStatic = false;
        List<IRenderable> renderables = Collect(scene);

        Assert.Equal(1, scene.StaticGeometry.LiveSourceCount);
        Assert.Single(renderables, r => r is MeshRenderable);
        Assert.Equal((uint)_cube.IndexCount, DrawnIndices(renderables));
    }

    [Fact]
    public void AtRuntime_ChangedRenderersStayInTheBatch_UntilItIsUpdated()
    {
        Scene scene = CreateScene(enable: true);
        MeshRenderer moved = AddRenderer(scene, new Float3(-5, 0, 0));
        MeshRenderer disabled = AddRenderer(scene, new Float3(5, 0, 0));
        scene.UpdateStaticGeometry();

        moved.Transform.Position = new Float3(-6, 0, 0);
        disabled.Enabled = false;
        List<IRenderable> renderables = Collect(scene);

        Assert.Equal(2, scene.StaticGeometry.LiveSourceCount);
        Assert.DoesNotContain(renderables, r => r is MeshRenderable);
        Assert.Equal((uint)_cube.IndexCount * 2, DrawnIndices(renderables));

        scene.UpdateStaticGeometry();
        Assert.Equal(1, scene.StaticGeometry.LiveSourceCount);
    }

    [Fact]
    public void ChangingAMaterialProperty_DropsOutRenderersThatNoLongerMatch()
    {
        EnterEditing();
        Scene scene = CreateScene(enable: true);
        var twin = new Material(Shader.LoadDefault(DefaultShader.Standard));
        AddRenderer(scene, new Float3(0, 0, 0));
        AddRenderer(scene, new Float3(3, 0, 0), twin);
        scene.UpdateStaticGeometry();
        Assert.Equal(1, scene.StaticGeometry.GroupCount);

        twin.SetColor("_MainColor", new Color(0, 1, 0, 1));
        Collect(scene);

        Assert.Equal(1, scene.StaticGeometry.LiveSourceCount);
    }

    [Fact]
    public void NegativeScale_KeepsTrianglesFacingOutward()
    {
        Scene scene = CreateScene(enable: true);
        MeshRenderer mirrored = AddRenderer(scene, Float3.Zero);
        mirrored.Transform.LocalScale = new Float3(-1, 1, 1);
        scene.UpdateStaticGeometry();

        Collect(scene)[0].GetRenderingData(default, out _, out Mesh merged, out _, out _);
        Float3[] v = merged.Vertices;
        Float3[] n = merged.Normals;
        uint[] idx = merged.Indices;
        for (int t = 0; t < idx.Length; t += 3)
        {
            Float3 face = Float3.Cross(v[idx[t + 1]] - v[idx[t]], v[idx[t + 2]] - v[idx[t]]);
            Float3 sourceFace = Float3.Cross(_cube.Vertices[_cube.Indices[t + 1]] - _cube.Vertices[_cube.Indices[t]],
                                             _cube.Vertices[_cube.Indices[t + 2]] - _cube.Vertices[_cube.Indices[t]]);
            // The source winding relative to its normal is kept, mirrored geometry included
            Assert.Equal(MathF.Sign(Float3.Dot(sourceFace, _cube.Normals[_cube.Indices[t]])), MathF.Sign(Float3.Dot(face, n[idx[t]])));
        }
    }

    [Fact]
    public void Lightmapped_BakesItsPlacementIntoUV2()
    {
        Scene scene = CreateScene(enable: true);
        MeshRenderer lit = AddRenderer(scene, Float3.Zero);
        scene.BakedLighting.Lightmaps.Add(null!);
        scene.BakedLighting.Placements[lit.GameObject.Identifier] = new Scene.LightmapPlacement { Index = 0, ScaleOffset = new Float4(0.5f, 0.25f, 0.1f, 0.2f) };
        scene.UpdateStaticGeometry();

        Collect(scene)[0].GetRenderingData(default, out _, out Mesh merged, out _, out _);
        Assert.True(merged.HasUV2);

        // The cube has no UV2, so the lightmap is laid out over its UV0
        Float2 source = _cube.UV[0];
        Float2 baked = merged.UV2[0];
        Assert.Equal(source.X * 0.5f + 0.1f, baked.X, 5);
        Assert.Equal(source.Y * 0.25f + 0.2f, baked.Y, 5);
    }

    [Fact]
    public void Update_OnlyBatchesLightmappedRenderersSharingAPage()
    {
        Scene scene = CreateScene(enable: true);
        MeshRenderer a = AddRenderer(scene, new Float3(0, 0, 0));
        MeshRenderer b = AddRenderer(scene, new Float3(3, 0, 0));
        scene.BakedLighting.Lightmaps.Add(null!);
        scene.BakedLighting.Lightmaps.Add(null!);
        scene.BakedLighting.Placements[a.GameObject.Identifier] = new Scene.LightmapPlacement { Index = 0, ScaleOffset = new Float4(1, 1, 0, 0) };
        scene.BakedLighting.Placements[b.GameObject.Identifier] = new Scene.LightmapPlacement { Index = 1, ScaleOffset = new Float4(1, 1, 0, 0) };

        scene.UpdateStaticGeometry();

        Assert.Equal(2, scene.StaticGeometry.GroupCount);
    }

    [Fact]
    public void SavedScene_DrawsFromTheStoredGeometry_WithoutRebuilding()
    {
        Scene scene = CreateScene(enable: true);
        AddRenderer(scene, new Float3(-5, 0, 0));
        AddRenderer(scene, new Float3(5, 0, 0));
        scene.UpdateStaticGeometry();

        EchoObject saved = Serializer.Serialize(scene);
        Scene loaded = Serializer.Deserialize<Scene>(saved)!;
        loaded.Enable();
        try
        {
            List<IRenderable> renderables = Collect(loaded);

            Assert.Equal(2, loaded.StaticGeometry.LiveSourceCount);
            Assert.DoesNotContain(renderables, r => r is MeshRenderable);
            Assert.Equal((uint)_cube.IndexCount * 2, DrawnIndices(renderables));
            Assert.False(loaded.StaticGeometry.NeedsRebuild(loaded));
        }
        finally
        {
            loaded.Disable();
            loaded.Dispose();
        }
    }

    [Fact]
    public void NeedsRebuild_OnceANewStaticRendererAppearsOrOneDropsOut()
    {
        EnterEditing();
        Scene scene = CreateScene(enable: true);
        MeshRenderer first = AddRenderer(scene, Float3.Zero);
        scene.UpdateStaticGeometry();
        Collect(scene);
        Assert.False(scene.StaticGeometry.NeedsRebuild(scene));

        AddRenderer(scene, new Float3(3, 0, 0));
        Assert.True(scene.StaticGeometry.NeedsRebuild(scene));

        scene.UpdateStaticGeometry();
        first.Transform.Position = new Float3(0, 1, 0);
        Collect(scene);
        Assert.True(scene.StaticGeometry.NeedsRebuild(scene));
    }

    [Fact]
    public void Clusters_SplitLargeGroupsSoTheyCullSeparately()
    {
        Scene scene = CreateScene(enable: true);
        Mesh dense = Mesh.CreateSphere(1, 48, 48);
        for (int i = 0; i < 8; i++)
            AddRenderer(scene, new Float3(i * 20, 0, 0), mesh: dense);

        scene.UpdateStaticGeometry();

        List<IRenderable> renderables = Collect(scene);
        Assert.True(renderables.Count > 1);
        Assert.Equal((uint)dense.IndexCount * 8, DrawnIndices(renderables));
    }
}
