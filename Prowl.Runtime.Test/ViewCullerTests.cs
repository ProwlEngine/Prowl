// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ShaderPass = Prowl.Graphite.ShaderDef.ShaderPass;

namespace Prowl.Runtime.Test;

public class ViewCullerTests : RuntimeTestBase
{
    private sealed class FakeRenderable(Material? material, Float3 position, int layer = 0, bool renderable = true, float size = 1f) : IRenderable
    {
        public Material GetMaterial() => material!;
        public int GetLayer() => layer;
        public Float3 GetPosition() => position;

        public void GetRenderingData(ViewerData viewer, out PropertySet properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
        {
            properties = new PropertySet();
            mesh = null!;
            model = Float4x4.CreateTranslation(position);
            instanceData = null;
        }

        public void GetCullingData(out bool isRenderable, out AABB bounds)
        {
            isRenderable = renderable;
            bounds = AABB.FromCenterAndSize(position, new Float3(size));
        }
    }

    private sealed class FakeLight(LightType type, int layer = 0) : IRenderableLight
    {
        public int GetLightID() => GetHashCode();
        public int GetLayer() => layer;
        public LightType GetLightType() => type;
        public Float3 GetLightPosition() => Float3.Zero;
        public Float3 GetLightDirection() => Float3.UnitZ;
        public bool DoCastShadows() => false;
        public ForwardLightData GetForwardLightData() => default;
    }

    private static Shader MakeShader(string name, string? renderOrder)
    {
        var pass = new ShaderPass { Name = "Main", State = new PassState(), InlineSlang = "", Tags = new() };
        if (renderOrder != null)
            pass.Tags[PassTags.RenderOrder] = renderOrder;

        var definition = new ShaderDefinition { Name = name, Passes = [pass] };
        return new Shader(name, [], definition, new ShaderSnapshot());
    }

    private static Material MakeMaterial(string? renderOrder) => new(MakeShader("Test/" + (renderOrder ?? "Untagged"), renderOrder));

    private Camera CreateCamera()
    {
        GameObject go = CreateGameObject("Camera");
        Camera camera = go.AddComponent<Camera>();
        camera.ProjectionMatrix = Float4x4.CreatePerspectiveFov(Maths.ToRadians(60f), 1f, 0.1f, 100f);
        camera.ViewMatrix = Float4x4.CreateLookTo(Float3.Zero, Float3.UnitZ, Float3.UnitY);
        return camera;
    }

    private static Frustum CameraFrustum(Camera camera) => Frustum.FromMatrix(camera.ProjectionMatrix * camera.ViewMatrix);

    [Fact]
    public void Intersects_AcceptsBoxesInsideAndRejectsBoxesOutside()
    {
        Frustum frustum = CameraFrustum(CreateCamera());

        Assert.True(ViewCuller.Intersects(in frustum, AABB.FromCenterAndSize(new Float3(0f, 0f, 10f), Float3.One)));
        Assert.True(ViewCuller.Intersects(in frustum, AABB.FromCenterAndSize(new Float3(0f, 0f, 0.05f), Float3.One)));
        Assert.True(ViewCuller.Intersects(in frustum, AABB.FromCenterAndSize(new Float3(0f, 0f, 50f), new Float3(500f))));

        Assert.False(ViewCuller.Intersects(in frustum, AABB.FromCenterAndSize(new Float3(0f, 0f, -10f), Float3.One)));
        Assert.False(ViewCuller.Intersects(in frustum, AABB.FromCenterAndSize(new Float3(0f, 0f, 200f), Float3.One)));
        Assert.False(ViewCuller.Intersects(in frustum, AABB.FromCenterAndSize(new Float3(50f, 0f, 10f), Float3.One)));
        Assert.False(ViewCuller.Intersects(in frustum, AABB.FromCenterAndSize(new Float3(0f, -50f, 10f), Float3.One)));
    }

    [Fact]
    public void Cull_ClassifiesByRenderOrderTagAndSortsByDistance()
    {
        Camera camera = CreateCamera();
        Material opaque = MakeMaterial(PassTags.Opaque);
        Material transparent = MakeMaterial(PassTags.Transparent);
        Material ui = MakeMaterial(PassTags.UI);
        Material untagged = MakeMaterial(null);

        var culler = new SceneCuller();
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, 30f)));
        culler.Add(new FakeRenderable(transparent, new Float3(0f, 0f, 10f)));
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, 10f)));
        culler.Add(new FakeRenderable(transparent, new Float3(0f, 0f, 30f)));
        culler.Add(new FakeRenderable(untagged, new Float3(0f, 0f, 20f)));
        culler.Add(new FakeRenderable(transparent, new Float3(0f, 0f, 20f)));
        culler.Add(new FakeRenderable(ui, new Float3(0f, 0f, 5f)));
        culler.Add(new FakeRenderable(null, new Float3(0f, 0f, 1f)));

        var results = new ViewCullResults();
        ViewCuller.Cull(culler, camera, results);

        Assert.Equal([7, 2, 4, 0], results.Opaque);
        Assert.Equal([3, 5, 1], results.Transparent);
        Assert.Equal([6], results.UI);
        Assert.Empty(results.Culled);
        Assert.Equal(new Float3(0f, 0f, 20f), results.WorldBounds[4].Center);
        Assert.True(results.Renderable[4]);
        Assert.Equal(camera.Transform.Position, results.ShadowFocus);
    }

    [Fact]
    public void Cull_RemovesFrustumLayerAndNonRenderableEntries()
    {
        Camera camera = CreateCamera();
        camera.CullingMask = LayerMask.Everything;
        camera.CullingMask.RemoveLayer(3);
        Material opaque = MakeMaterial(PassTags.Opaque);

        var culler = new SceneCuller();
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, 10f)));
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, -10f)));
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, 10f), layer: 3));
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, 10f), renderable: false));
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, 10f), layer: 4));

        var results = new ViewCullResults();
        ViewCuller.Cull(culler, camera, results);

        Assert.Equal([0, 4], results.Opaque);
        Assert.Equal([1, 2, 3], results.Culled);
        Assert.False(results.Renderable[3]);
    }

    [Fact]
    public void Cull_FiltersLightsByLayerAndPutsDirectionalFirst()
    {
        Camera camera = CreateCamera();
        camera.CullingMask.RemoveLayer(5);

        var point = new FakeLight(LightType.Point);
        var hidden = new FakeLight(LightType.Spot, layer: 5);
        var sun = new FakeLight(LightType.Directional);
        var spot = new FakeLight(LightType.Spot);

        var culler = new SceneCuller();
        culler.Add(point);
        culler.Add(hidden);
        culler.Add(sun);
        culler.Add(spot);

        var results = new ViewCullResults();
        ViewCuller.Cull(culler, camera, results);

        Assert.Same(sun, results.Directional);
        Assert.Equal<IRenderableLight>([sun, point, spot], results.Lights);
    }

    [Fact]
    public void Cull_ReusesBuffersAcrossFrames()
    {
        Camera camera = CreateCamera();
        Material opaque = MakeMaterial(PassTags.Opaque);
        var culler = new SceneCuller();
        for (int i = 0; i < 4; i++)
            culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, 10f + i)));

        var results = new ViewCullResults();
        ViewCuller.Cull(culler, camera, results);
        AABB[] bounds = results.WorldBounds;
        List<int> opaqueList = results.Opaque;

        ViewCuller.Cull(culler, camera, results);
        Assert.Same(bounds, results.WorldBounds);
        Assert.Same(opaqueList, results.Opaque);
        Assert.Equal(4, results.Opaque.Count);
    }

    [Fact]
    public void CullForFrustum_ReusesWorldBoundsFromCull()
    {
        Camera camera = CreateCamera();
        Material opaque = MakeMaterial(PassTags.Opaque);

        var culler = new SceneCuller();
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, 10f)));
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, -10f)));
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 0f, 10f), layer: 3, renderable: false));
        culler.Add(new FakeRenderable(opaque, new Float3(0f, 20f, 10f)));

        var results = new ViewCullResults();
        ViewCuller.Cull(culler, camera, results);

        var output = new List<int> { 99 };
        ViewCuller.CullForFrustum(culler, in results.Frustum, results.WorldBounds, results.Renderable, output);
        Assert.Equal([0], output);

        Frustum behind = Frustum.FromMatrix(camera.ProjectionMatrix * Float4x4.CreateLookTo(Float3.Zero, -Float3.UnitZ, Float3.UnitY));
        ViewCuller.CullForFrustum(culler, in behind, results.WorldBounds, results.Renderable, output);
        Assert.Equal([1], output);
    }

    [Fact]
    public void CameraView_GetOrCreate_ReturnsTheSameInstancePerCamera()
    {
        Camera a = CreateCamera();
        Camera b = CreateCamera();

        CameraView viewA = CameraView.GetOrCreate(a);
        Assert.Same(viewA, CameraView.GetOrCreate(a));
        Assert.Same(a, viewA.Camera);
        Assert.NotSame(viewA, CameraView.GetOrCreate(b));
        Assert.NotNull(viewA.Cull);
    }
}
