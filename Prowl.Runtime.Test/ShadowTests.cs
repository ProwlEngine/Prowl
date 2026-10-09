// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests (no GPU) for the light shadow setup: directional cascade placement, coverage and texel snapping, the
/// caster culling volume, spot projections, the shadow flags lights report, the atlas allocator and the shadow
/// cache that decides what is redrawn and at what size.
/// </summary>
public class ShadowTests : RuntimeTestBase
{
    private const int Resolution = 2048;
    private const float CascadeRadius = 35f;
    private const float TexelSize = (CascadeRadius * 2f) / Resolution;

    /// <summary>Creates an angled directional light, so the light-space axes are nothing like the
    /// world axes and a mistake in the basis math can't accidentally cancel out.</summary>
    private DirectionalLight CreateAngledLight()
    {
        GameObject go = CreateGameObject("Directional Light");
        go.Transform.LocalEulerAngles = new Float3(50f, 210f, 0f);
        return go.AddComponent<DirectionalLight>();
    }

    /// <summary>The orthonormal light-space basis GetShadowMatrix builds internally.</summary>
    private static (Float3 right, Float3 up, Float3 forward) LightBasis(DirectionalLight light)
    {
        Float3 forward = light.Transform.Forward;
        Float3 up = Float3.Normalize(light.Transform.Up);
        Float3 right = Float3.Normalize(Float3.Cross(up, forward));
        up = Float3.Normalize(Float3.Cross(forward, right));
        return (right, up, forward);
    }

    private static bool InsideClip(Float3 point, Float4x4 viewProjection)
    {
        Float4 clip = viewProjection * new Float4(point, 1f);
        Float3 ndc = new Float3(clip.X, clip.Y, clip.Z) / clip.W;
        return MathF.Abs(ndc.X) <= 1f && MathF.Abs(ndc.Y) <= 1f && ndc.Z >= 0f && ndc.Z <= 1f;
    }

    [Fact]
    public void DirectionalLight_GetShadowMatrix_CentersOrthoOnFocusWithinTexel()
    {
        DirectionalLight light = CreateAngledLight();
        Float3 focus = new(37.4f, 2.6f, -18.9f);

        light.GetShadowMatrix(focus, Resolution, CascadeRadius, out Float4x4 view, out _);

        // In light view space the focus point should sit at the origin, off only by the texel
        // snapping applied to X and Y (at most half a texel each). If placement drifted further the
        // focal point would no longer be in the middle of the cascade it was built for.
        Float3 focusInLightSpace = Float4x4.TransformPoint(focus, view);
        float tolerance = (TexelSize * 0.5f) + 1e-4f;

        Assert.True(MathF.Abs(focusInLightSpace.X) <= tolerance,
            $"Focus X in light space was {focusInLightSpace.X}, expected within {tolerance}.");
        Assert.True(MathF.Abs(focusInLightSpace.Y) <= tolerance,
            $"Focus Y in light space was {focusInLightSpace.Y}, expected within {tolerance}.");
    }

    [Fact]
    public void DirectionalLight_GetShadowMatrix_SubTexelMovement_ProducesIdenticalView()
    {
        DirectionalLight light = CreateAngledLight();
        (Float3 right, Float3 up, Float3 forward) = LightBasis(light);

        // Start exactly on the texel grid so a fifth-of-a-texel step can't straddle a rounding
        // boundary and legitimately land on the next grid point.
        Float3 focus = (right * (14f * TexelSize)) + (up * (-9f * TexelSize)) + (forward * 6.5f);
        Float3 nudged = focus + (right * (TexelSize * 0.2f));

        light.GetShadowMatrix(focus, Resolution, CascadeRadius, out Float4x4 view, out _);
        light.GetShadowMatrix(nudged, Resolution, CascadeRadius, out Float4x4 nudgedView, out _);

        // Identical, not merely close: a shadow map that slides by a fraction of a texel each frame
        // is what makes shadow edges crawl, and this is exactly the case a moving player hits.
        Assert.Equal(view.ToArray(), nudgedView.ToArray());
    }

    [Fact]
    public void DirectionalLight_GetShadowMatrix_CoversWholeRadius()
    {
        DirectionalLight light = CreateAngledLight();
        (Float3 right, Float3 up, Float3 forward) = LightBasis(light);
        Float3 focus = new(4f, 1f, -3f);

        light.GetShadowMatrix(focus, Resolution, CascadeRadius, out Float4x4 view, out Float4x4 projection);
        Float4x4 viewProjection = projection * view;

        // The cascade is selected for every receiver within its radius of the focus, so every such
        // receiver has to land inside the map, in all directions including along the light.
        float reach = CascadeRadius * 0.99f;
        Assert.True(InsideClip(focus + right * reach, viewProjection), "Receiver along light right fell outside the cascade.");
        Assert.True(InsideClip(focus - up * reach, viewProjection), "Receiver along light down fell outside the cascade.");
        Assert.True(InsideClip(focus + forward * reach, viewProjection), "Receiver away from the light fell outside the cascade.");
        Assert.True(InsideClip(focus - forward * reach, viewProjection), "Receiver toward the light fell outside the cascade.");
    }

    [Fact]
    public void DirectionalLight_CasterFrustum_KeepsCastersFarTowardLight()
    {
        DirectionalLight light = CreateAngledLight();
        (_, _, Float3 forward) = LightBasis(light);
        Float3 focus = new(4f, 1f, -3f);

        light.GetShadowMatrix(focus, Resolution, CascadeRadius, out Float4x4 view, out Float4x4 projection);
        Frustum casters = DirectionalLight.GetCasterFrustum(view, projection);

        // A tall caster far up the light direction still shadows the cascade, the depth clamp flattens
        // it onto the near plane, so culling must not drop it.
        Float3 farTowardLight = focus - forward * 500f;
        Assert.True(casters.Intersects(new AABB(farTowardLight - Float3.One, farTowardLight + Float3.One)));

        // The sides still cull.
        Float3 beside = focus + Float3.Normalize(Float3.Cross(forward, Float3.UnitY)) * (CascadeRadius * 3f);
        Assert.False(casters.Intersects(new AABB(beside - Float3.One, beside + Float3.One)));
    }

    [Fact]
    public void SpotLight_WideAngle_BuildsShadowProjection()
    {
        GameObject go = CreateGameObject("Spot Light");
        SpotLight light = go.AddComponent<SpotLight>();
        light.SpotAngle = 100f;

        light.GetShadowMatrix(out Float4x4 view, out Float4x4 projection);

        Float3 ahead = go.Transform.Position + go.Transform.Forward * (light.Range * 0.5f);
        Assert.True(InsideClip(ahead, projection * view));
    }

    [Fact]
    public void PointLight_CastShadows_ReportsShadowedBeforeFirstShadowRender()
    {
        PointLight light = CreateGameObject("Point Light").AddComponent<PointLight>();
        light.CastShadows = true;

        // The light system reads this before the shadow pass of the frame a light first wins a slot,
        // so it must not depend on a shadow map from an earlier frame.
        Assert.True(light.GetForwardLightData().ShadowEnabled);

        light.CastShadows = false;
        Assert.False(light.GetForwardLightData().ShadowEnabled);
    }

    [Fact]
    public void SpotLight_CastShadows_ReportsShadowedBeforeFirstShadowRender()
    {
        SpotLight light = CreateGameObject("Spot Light").AddComponent<SpotLight>();
        light.CastShadows = true;

        Assert.True(light.GetForwardLightData().ShadowEnabled);

        light.CastShadows = false;
        Assert.False(light.GetForwardLightData().ShadowEnabled);
    }

    /// <summary>World space corners of a view slice, from the frustum tangents of a camera at origin.</summary>
    private static Float3[] SliceCorners(Float3 origin, Quaternion rotation, float tanLeft, float tanRight, float tanDown, float tanUp, float near, float far)
    {
        var corners = new Float3[8];
        int i = 0;
        foreach (float depth in new[] { near, far })
            foreach (float x in new[] { tanLeft, tanRight })
                foreach (float y in new[] { tanDown, tanUp })
                    corners[i++] = origin + rotation * new Float3(x * depth, y * depth, depth);
        return corners;
    }

    private static void AssertInside(Float3[] points, Float3 center, float radius)
    {
        foreach (Float3 p in points)
        {
            float d = Float3.Length(p - center);
            Assert.True(d <= radius + 1e-3f, $"Point {p} is {d} from the sphere center, radius {radius}.");
        }
    }

    [Fact]
    public void ShadowFitView_Perspective_SphereHoldsWholeSlice()
    {
        Quaternion rotation = Quaternion.FromEuler(new Float3(20f, 135f, 0f));
        Float3 origin = new(3f, 2f, -5f);
        Float4x4 projection = Float4x4.CreatePerspectiveFov(70f * Maths.Deg2Rad, 16f / 9f, 0.1f, 1000f);
        ShadowFitView view = ShadowFitView.FromProjection(origin, rotation, projection, 0.1f);

        float tanY = MathF.Tan(35f * Maths.Deg2Rad), tanX = tanY * 16f / 9f;
        foreach ((float near, float far) in new[] { (0.1f, 4f), (4f, 15f), (15f, 50f) })
        {
            view.GetSliceSphere(near, far, out Float3 center, out float radius);
            AssertInside(SliceCorners(origin, rotation, -tanX, tanX, -tanY, tanY, near, far), center, radius);
        }
    }

    [Fact]
    public void ShadowFitView_Orthographic_SphereHoldsWholeSlice()
    {
        Quaternion rotation = Quaternion.FromEuler(new Float3(-30f, 40f, 0f));
        Float3 origin = new(-1f, 8f, 2f);
        ShadowFitView view = ShadowFitView.FromProjection(origin, rotation, Float4x4.CreateOrtho(24f, 12f, 0.1f, 100f), 0.1f);

        view.GetSliceSphere(5f, 30f, out Float3 center, out float radius);
        var corners = new Float3[8];
        int i = 0;
        foreach (float depth in new[] { 5f, 30f })
            foreach (float x in new[] { -12f, 12f })
                foreach (float y in new[] { -6f, 6f })
                    corners[i++] = origin + rotation * new Float3(x, y, depth);
        AssertInside(corners, center, radius);
    }

    [Fact]
    public void ShadowFitView_SphereRadius_DoesNotChangeAsTheCameraTurns()
    {
        Float4x4 projection = Float4x4.CreatePerspectiveFov(60f * Maths.Deg2Rad, 1.5f, 0.1f, 1000f);
        ShadowFitView a = ShadowFitView.FromProjection(Float3.Zero, Quaternion.Identity, projection, 0.1f);
        ShadowFitView b = ShadowFitView.FromProjection(new Float3(40f, 3f, 9f), Quaternion.FromEuler(new Float3(15f, 77f, 5f)), projection, 0.1f);

        // A radius that follows the view direction would resize the cascade every frame and make its edges crawl
        a.GetSliceSphere(3f, 12f, out _, out float radiusA);
        b.GetSliceSphere(3f, 12f, out _, out float radiusB);
        Assert.Equal(radiusA, radiusB);
    }

    [Fact]
    public void ShadowFitView_Stereo_SphereHoldsBothEyes()
    {
        Float3 head = new(1f, 1.7f, 4f);
        Quaternion headRotation = Quaternion.FromEuler(new Float3(0f, 30f, 0f));
        Float4x4 headToWorld = Float4x4.CreateTRS(head, headRotation, Float3.One);

        // Canted, asymmetric eyes like a wide field of view headset
        XRView left = new() { Position = new Float3(-0.032f, 0f, 0f), Rotation = Quaternion.FromEuler(new Float3(0f, -10f, 0f)), TanLeft = -1.4f, TanRight = 1.0f, TanDown = -1.2f, TanUp = 1.1f };
        XRView right = new() { Position = new Float3(0.032f, 0f, 0f), Rotation = Quaternion.FromEuler(new Float3(0f, 10f, 0f)), TanLeft = -1.0f, TanRight = 1.4f, TanDown = -1.2f, TanUp = 1.1f };

        ShadowFitView view = ShadowFitView.FromEyes(head, headRotation, 0.05f, [left, right], headToWorld);

        view.GetSliceSphere(2f, 10f, out Float3 center, out float radius);
        Float3 headForward = headRotation * Float3.UnitZ;
        foreach (XRView eye in new[] { left, right })
        {
            Float3 eyeOrigin = Float4x4.TransformPoint(eye.Position, headToWorld);
            Quaternion eyeRotation = headRotation * eye.Rotation;
            foreach (float x in new[] { eye.TanLeft, eye.TanRight })
                foreach (float y in new[] { eye.TanDown, eye.TanUp })
                {
                    // Each corner ray of the eye, cut where it crosses the slice's near and far depth along the head
                    Float3 dir = eyeRotation * new Float3(x, y, 1f);
                    foreach (float depth in new[] { 2f, 10f })
                    {
                        float t = (depth - Float3.Dot(eyeOrigin - head, headForward)) / Float3.Dot(dir, headForward);
                        AssertInside([eyeOrigin + dir * t], center, radius);
                    }
                }
        }
    }

    [Fact]
    public void DirectionalLight_CascadeSplits_EndAtShadowDistanceAndFavourTheNearSlice()
    {
        const float near = 0.1f, distance = 50f;
        float previous = near;
        for (int i = 1; i <= 4; i++)
        {
            float split = DirectionalLight.GetCascadeSplit(i, 4, near, distance);
            Assert.True(split > previous);
            previous = split;
        }
        Assert.Equal(distance, DirectionalLight.GetCascadeSplit(4, 4, near, distance), 3);

        // The first slice is much shorter than an even split, it is the one right in front of the camera
        Assert.True(DirectionalLight.GetCascadeSplit(1, 4, near, distance) < distance / 4f * 0.5f);
    }

    // ---------------------------------------------------------------- atlas allocator

    // Marks every 16 texel cell a tile covers, failing on the first one already taken
    private static void Claim(bool[,] cells, ShadowTile tile)
    {
        int c = ShadowTileAllocator.SmallestTile;
        for (int y = tile.Y / c; y < (tile.Y + tile.Size) / c; y++)
            for (int x = tile.X / c; x < (tile.X + tile.Size) / c; x++)
            {
                Assert.False(cells[x, y], $"Tile {tile} overlaps another tile.");
                cells[x, y] = true;
            }
    }

    [Fact]
    public void ShadowTileAllocator_LargestFirst_FillsTheAtlasExactly()
    {
        var allocator = new ShadowTileAllocator(1024);
        var cells = new bool[64, 64];
        // 3 x 512 + 3 x 256 + 3 x 128 + 4 x 64 adds up to exactly 1024 x 1024
        int[] sizes = [512, 512, 512, 256, 256, 256, 128, 128, 128, 64, 64, 64, 64];

        foreach (int size in sizes)
        {
            Assert.True(allocator.TryAllocate(size, out ShadowTile tile));
            Claim(cells, tile);
        }

        Assert.Equal(allocator.CapacityTexels, allocator.UsedTexels);
        Assert.False(allocator.TryAllocate(16, out _));
    }

    [Fact]
    public void ShadowTileAllocator_FreedTiles_MergeBackIntoTheWholeAtlas()
    {
        var allocator = new ShadowTileAllocator(1024);
        var rng = new Random(4);
        var tiles = new List<ShadowTile>();
        while (allocator.TryAllocate(16 << rng.Next(4), out ShadowTile tile))
            tiles.Add(tile);

        foreach (ShadowTile tile in tiles.OrderBy(_ => rng.Next()))
            allocator.Free(tile);

        Assert.Equal(0, allocator.UsedTexels);
        Assert.True(allocator.TryAllocate(1024, out _));
    }

    [Fact]
    public void ShadowTileAllocator_Churn_NeverHandsOutOverlappingTiles()
    {
        var allocator = new ShadowTileAllocator(512);
        var rng = new Random(11);
        var live = new List<ShadowTile>();

        for (int step = 0; step < 3000; step++)
        {
            if (live.Count > 0 && rng.Next(3) == 0)
            {
                int k = rng.Next(live.Count);
                allocator.Free(live[k]);
                live.RemoveAt(k);
            }
            else if (allocator.TryAllocate(16 << rng.Next(4), out ShadowTile tile))
            {
                live.Add(tile);
            }
        }

        var cells = new bool[32, 32];
        long used = 0;
        foreach (ShadowTile tile in live)
        {
            Claim(cells, tile);
            used += (long)tile.Size * tile.Size;
        }
        Assert.Equal(used, allocator.UsedTexels);
    }

    // ---------------------------------------------------------------- shadow cache

    private sealed class DeformingRenderable(MeshRenderable inner) : IRenderable
    {
        public Material GetMaterial() => inner.GetMaterial();
        public int GetLayer() => inner.GetLayer();
        public Float3 GetPosition() => inner.GetPosition();
        public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
            => inner.GetRenderingData(viewer, out properties, out mesh, out model, out instanceData);
        public void GetCullingData(out bool isRenderable, out AABB bounds) => inner.GetCullingData(out isRenderable, out bounds);
        public bool DeformsEveryFrame => true;
    }

    /// <summary>A camera, some cubes and lights, run through the shadow cache one frame per <see cref="Update"/>.</summary>
    private sealed class ShadowScene
    {
        public readonly DefaultRenderPipeline Pipeline = new();
        public readonly ShadowRenderer Renderer = new();
        public readonly List<IRenderable> Renderables = new();
        public readonly List<Light> Lights = new();
        public readonly Mesh Cube = Mesh.CreateCube(Float3.One);
        public readonly Material Material = new(Shader.LoadDefault(DefaultShader.Standard));
        public DirectionalLight? Sun;
        public Float3 CameraPosition = Float3.Zero;
        public Float3 CameraForward = Float3.UnitZ;

        public MeshRenderable AddCube(Float3 position)
        {
            var cube = new MeshRenderable(Cube, Material, Float4x4.CreateTranslation(position), 0);
            Renderables.Add(cube);
            return cube;
        }

        public void Move(MeshRenderable cube, Float3 position) => cube.Set(Cube, Material, Float4x4.CreateTranslation(position), 0);

        public void Update()
        {
            Time.CurrentTime.FrameCount++;
            Float4x4 view = Float4x4.CreateLookTo(CameraPosition, CameraForward, Float3.UnitY);
            Float4x4 projection = Float4x4.CreatePerspectiveFov(60f * Maths.Deg2Rad, 16f / 9f, 0.1f, 500f);
            Quaternion rotation = Quaternion.LookRotation(CameraForward, Float3.UnitY);
            var camera = new ShadowCamera(this, CameraPosition, Frustum.FromMatrix(projection * view), projection, 1080,
                ShadowFitView.FromProjection(CameraPosition, rotation, projection, 0.1f));
            Renderer.Update(Pipeline, camera, Sun, Lights, Renderables);
        }
    }

    private PointLight AddLamp(ShadowScene scene, Float3 position, float range = 6f)
    {
        GameObject go = CreateGameObject("Lamp");
        go.Transform.Position = position;
        PointLight lamp = go.AddComponent<PointLight>();
        lamp.Range = range;
        scene.Lights.Add(lamp);
        return lamp;
    }

    [Fact]
    public void ShadowCache_StaticScene_DrawsOnceThenReuses()
    {
        var scene = new ShadowScene();
        AddLamp(scene, new Float3(0, 0, 10));
        scene.AddCube(new Float3(1, 0, 10));

        scene.Update();
        Assert.Equal(6, scene.Renderer.FacesDrawn);
        Assert.Equal(1, scene.Renderer.LightsShadowed);

        scene.Update();
        Assert.Equal(0, scene.Renderer.FacesDrawn);
        Assert.Equal(1, scene.Renderer.LightsShadowed);
    }

    [Fact]
    public void ShadowCache_CasterMovingInsideTheLight_Redraws()
    {
        var scene = new ShadowScene();
        AddLamp(scene, new Float3(0, 0, 10));
        MeshRenderable cube = scene.AddCube(new Float3(1, 0, 10));
        scene.Update();
        scene.Update();

        scene.Move(cube, new Float3(2, 0, 10));
        scene.Update();
        Assert.Equal(6, scene.Renderer.FacesDrawn);
    }

    [Fact]
    public void ShadowCache_CasterMovingOutsideTheLight_DoesNotRedraw()
    {
        var scene = new ShadowScene();
        AddLamp(scene, new Float3(0, 0, 10));
        scene.AddCube(new Float3(1, 0, 10));
        MeshRenderable far = scene.AddCube(new Float3(40, 0, 10));
        scene.Update();
        scene.Update();

        scene.Move(far, new Float3(42, 0, 10));
        scene.Update();
        Assert.Equal(0, scene.Renderer.FacesDrawn);
    }

    [Fact]
    public void ShadowCache_CasterMaterialChange_Redraws()
    {
        var scene = new ShadowScene();
        AddLamp(scene, new Float3(0, 0, 10));
        scene.AddCube(new Float3(1, 0, 10));
        scene.Update();
        scene.Update();

        scene.Material.SetFloat("_AlphaCutoff", 0.3f);
        scene.Update();
        Assert.Equal(6, scene.Renderer.FacesDrawn);
    }

    [Fact]
    public void ShadowCache_LightMoving_Redraws()
    {
        var scene = new ShadowScene();
        PointLight lamp = AddLamp(scene, new Float3(0, 0, 10));
        scene.AddCube(new Float3(1, 0, 10));
        scene.Update();
        scene.Update();

        lamp.Transform.Position = new Float3(0.5f, 0, 10);
        scene.Update();
        Assert.Equal(6, scene.Renderer.FacesDrawn);
    }

    [Fact]
    public void ShadowCache_DeformingCaster_RedrawsEveryFrame()
    {
        var scene = new ShadowScene();
        AddLamp(scene, new Float3(0, 0, 10));
        scene.Renderables.Add(new DeformingRenderable(new MeshRenderable(scene.Cube, scene.Material, Float4x4.CreateTranslation(new Float3(1, 0, 10)), 0)));

        for (int i = 0; i < 3; i++)
        {
            scene.Update();
            Assert.Equal(6, scene.Renderer.FacesDrawn);
        }
    }

    [Fact]
    public void ShadowCache_LightLeavingTheView_KeepsItsTilesForWhenItReturns()
    {
        var scene = new ShadowScene();
        PointLight lamp = AddLamp(scene, new Float3(0, 0, 10));
        scene.AddCube(new Float3(1, 0, 10));
        scene.Update();

        scene.CameraForward = -Float3.UnitZ;
        scene.Update();
        Assert.Equal(0, scene.Renderer.LightsShadowed);
        Assert.True(scene.Renderer.GetTileSize(lamp) > 0);

        scene.CameraForward = Float3.UnitZ;
        scene.Update();
        Assert.Equal(1, scene.Renderer.LightsShadowed);
        Assert.Equal(0, scene.Renderer.FacesDrawn);
    }

    [Fact]
    public void ShadowCache_TileSize_FollowsScreenSizeAndIgnoresSmallMoves()
    {
        var scene = new ShadowScene();
        PointLight lamp = AddLamp(scene, new Float3(0, 0, 60), range: 2f);
        scene.AddCube(new Float3(0.5f, 0, 60));

        scene.Update();
        int far = scene.Renderer.GetTileSize(lamp);

        scene.CameraPosition = new Float3(0, 0, 50);
        scene.Update();
        int near = scene.Renderer.GetTileSize(lamp);
        Assert.True(near > far, $"Tile should grow as the light fills more of the screen, was {far} then {near}.");

        scene.CameraPosition = new Float3(0, 0, 50.4f);
        scene.Update();
        Assert.Equal(near, scene.Renderer.GetTileSize(lamp));
        Assert.Equal(0, scene.Renderer.FacesDrawn);
    }

    [Fact]
    public void ShadowCache_ResolutionChanges_ArePacedPerFrame()
    {
        int budget = ShadowAtlas.MaxResolutionChangesPerFrame;
        try
        {
            ShadowAtlas.MaxResolutionChangesPerFrame = 6;
            var scene = new ShadowScene();
            foreach (float x in new[] { -5f, 0f, 5f })
            {
                AddLamp(scene, new Float3(x, 0, 60), range: 2f);
                scene.AddCube(new Float3(x + 0.5f, 0, 60));
            }
            scene.Update();

            // All three want bigger tiles once the camera comes close, but only one light's worth may change a frame
            scene.CameraPosition = new Float3(0, 0, 50);
            for (int frame = 0; frame < 3; frame++)
            {
                scene.Update();
                Assert.Equal(6, scene.Renderer.FacesDrawn);
            }
            scene.Update();
            Assert.Equal(0, scene.Renderer.FacesDrawn);
        }
        finally
        {
            ShadowAtlas.MaxResolutionChangesPerFrame = budget;
        }
    }

    [Fact]
    public void ShadowCache_CrowdedAtlas_ShrinksEveryLightBeforeDroppingAny()
    {
        int size = ShadowAtlas.RequestedSize;
        try
        {
            ShadowAtlas.RequestedSize = 1024;
            var scene = new ShadowScene();
            var lamps = new List<PointLight>();
            for (int i = 0; i < 12; i++)
            {
                float x = (i % 4 - 1.5f) * 3f, y = (i / 4 - 1f) * 3f;
                lamps.Add(AddLamp(scene, new Float3(x, y, 9), range: 3f));
            }

            scene.Update();
            Assert.Equal(12, scene.Renderer.LightsShadowed);
            foreach (PointLight lamp in lamps)
                Assert.InRange(scene.Renderer.GetTileSize(lamp), ShadowAtlas.MinTileSize, 256);
        }
        finally
        {
            ShadowAtlas.RequestedSize = size;
        }
    }

    [Fact]
    public void ShadowCache_SpotLight_DrawsOnceThenReuses()
    {
        var scene = new ShadowScene();
        GameObject go = CreateGameObject("Spot");
        go.Transform.Position = new Float3(0, 5, 10);
        go.Transform.LocalEulerAngles = new Float3(90f, 0f, 0f);
        SpotLight spot = go.AddComponent<SpotLight>();
        spot.Range = 10f;
        scene.Lights.Add(spot);
        scene.AddCube(new Float3(0, 0, 10));

        scene.Update();
        Assert.Equal(1, scene.Renderer.FacesDrawn);
        scene.Update();
        Assert.Equal(0, scene.Renderer.FacesDrawn);
        Assert.Equal(1, scene.Renderer.LightsShadowed);
    }

    private static DirectionalLight AddSun(ShadowScene scene, GameObject go)
    {
        go.Transform.LocalEulerAngles = new Float3(50f, 30f, 0f);
        DirectionalLight sun = go.AddComponent<DirectionalLight>();
        sun.Cascades = DirectionalLight.CascadeCount.Two;
        scene.Sun = sun;
        return sun;
    }

    [Fact]
    public void ShadowCache_Directional_CascadeRefreshesEveryCPlusOneFrames()
    {
        var scene = new ShadowScene();
        DirectionalLight sun = AddSun(scene, CreateGameObject("Sun"));
        sun.Cascades = DirectionalLight.CascadeCount.Four;
        scene.AddCube(new Float3(0, 0, 4));

        scene.Update();
        Assert.Equal(4, scene.Renderer.CascadesDrawn);

        // Nothing moves, they redraw anyway on their schedule: 12 frames give 12 + 6 + 4 + 3
        int drawn = 0;
        for (int i = 0; i < 12; i++)
        {
            scene.Update();
            drawn += scene.Renderer.CascadesDrawn;
        }
        Assert.Equal(25, drawn);
    }

    [Fact]
    public void ShadowCache_Directional_CameraMoving_RedrawsTheNearCascade()
    {
        var scene = new ShadowScene();
        AddSun(scene, CreateGameObject("Sun"));
        scene.AddCube(new Float3(0, 0, 4));
        scene.Update();
        scene.Update();

        scene.CameraPosition = new Float3(3, 0, 0);
        scene.Update();
        Assert.True(scene.Renderer.CascadesDrawn >= 1);
    }

    [Fact]
    public void ShadowCache_MovingCasterBesideStaticOnes_KeepsTheStaticLayer()
    {
        var scene = new ShadowScene();
        AddLamp(scene, new Float3(0, 0, 10));
        MeshRenderable wall = scene.AddCube(new Float3(-1, 0, 10));
        wall.IsStatic = true;
        MeshRenderable crate = scene.AddCube(new Float3(1, 0, 10));

        scene.Update();
        Assert.Equal(6, scene.Renderer.StaticFacesDrawn);
        Assert.Equal(6, scene.Renderer.FacesDrawn);

        scene.Update();
        Assert.Equal(0, scene.Renderer.StaticFacesDrawn);
        Assert.Equal(0, scene.Renderer.FacesDrawn);

        // Only the moving caster changed, so the static layer is copied in rather than drawn again
        scene.Move(crate, new Float3(1.5f, 0, 10));
        scene.Update();
        Assert.Equal(0, scene.Renderer.StaticFacesDrawn);
        Assert.Equal(6, scene.Renderer.FacesDrawn);

        wall.Set(scene.Cube, scene.Material, Float4x4.CreateTranslation(new Float3(-1.5f, 0, 10)), 0);
        scene.Update();
        Assert.Equal(6, scene.Renderer.StaticFacesDrawn);
    }

    [Fact]
    public void ShadowCache_OnlyStaticCasters_KeepsOneLayer()
    {
        var scene = new ShadowScene();
        AddLamp(scene, new Float3(0, 0, 10));
        scene.AddCube(new Float3(-1, 0, 10)).IsStatic = true;
        scene.AddCube(new Float3(1, 0, 10)).IsStatic = true;

        scene.Update();
        Assert.Equal(6, scene.Renderer.FacesDrawn);
        Assert.Equal(0, scene.Renderer.StaticFacesDrawn);
    }

    [Fact]
    public void ShadowCache_VisualVersionChange_Redraws()
    {
        var scene = new ShadowScene();
        AddLamp(scene, new Float3(0, 0, 10));
        MeshRenderable cube = scene.AddCube(new Float3(1, 0, 10));
        scene.Update();
        scene.Update();

        // What MeshRenderer.MarkVisuallyDirty hands its renderable
        cube.VisualVersion++;
        scene.Update();
        Assert.Equal(6, scene.Renderer.FacesDrawn);
    }

    private SpotLight AddDownSpot(ShadowScene scene)
    {
        GameObject go = CreateGameObject("Spot");
        go.Transform.Position = new Float3(0, 6, 10);
        go.Transform.LocalEulerAngles = new Float3(90f, 0f, 0f);
        SpotLight spot = go.AddComponent<SpotLight>();
        spot.Range = 12f;
        spot.SpotAngle = 60f;
        scene.Lights.Add(spot);
        return spot;
    }

    [Fact]
    public void ShadowCache_OpaqueCastersOfOneMesh_DrawAsOneBatch()
    {
        var scene = new ShadowScene();
        AddDownSpot(scene);
        for (int i = 0; i < 10; i++)
        {
            // Every cube has its own material, which only matters for color, not for depth
            var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
            material.SetColor("_MainColor", new Color(i / 10f, 0.5f, 0.5f, 1f));
            scene.Renderables.Add(new MeshRenderable(scene.Cube, material, Float4x4.CreateTranslation(new Float3(i - 4.5f, 0, 10)), 0));
        }
        scene.Renderables.Add(new MeshRenderable(Mesh.CreateSphere(0.5f, 8, 8), scene.Material, Float4x4.CreateTranslation(new Float3(0, 1, 11)), 0));

        scene.Update();
        Assert.Equal(2, scene.Renderer.BatchedDraws);
        Assert.Equal(0, scene.Renderer.UnbatchedDraws);
    }

    [Fact]
    public void ShadowCache_CutoutCasters_DrawWithTheirOwnMaterial()
    {
        var scene = new ShadowScene();
        AddDownSpot(scene);
        var cutout = new Material(Shader.LoadDefault(DefaultShader.StandardCutout));
        for (int i = 0; i < 3; i++)
            scene.Renderables.Add(new MeshRenderable(scene.Cube, cutout, Float4x4.CreateTranslation(new Float3(i - 1, 0, 10)), 0));

        scene.Update();
        Assert.Equal(0, scene.Renderer.BatchedDraws);
        Assert.Equal(3, scene.Renderer.UnbatchedDraws);
    }

    [Fact]
    public void ShadowCache_DepthPrecisionChange_RebuildsEveryShadow()
    {
        ShadowDepthPrecision precision = ShadowAtlas.DepthPrecision;
        try
        {
            var scene = new ShadowScene();
            AddLamp(scene, new Float3(0, 0, 10));
            scene.AddCube(new Float3(1, 0, 10));
            scene.Update();
            scene.Update();
            Assert.Equal(0, scene.Renderer.FacesDrawn);

            ShadowAtlas.DepthPrecision = precision == ShadowDepthPrecision.Bits16 ? ShadowDepthPrecision.Bits24 : ShadowDepthPrecision.Bits16;
            scene.Update();
            Assert.Equal(6, scene.Renderer.FacesDrawn);
        }
        finally
        {
            ShadowAtlas.DepthPrecision = precision;
        }
    }
}
