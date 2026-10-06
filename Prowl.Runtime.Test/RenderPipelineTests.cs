// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class RenderPipelineTests
{
    private static MeshRenderable Renderable(Mesh mesh, Material material, int objectId, float z)
    {
        var props = new PropertyState();
        props.SetInt("_ObjectID", objectId);
        return new MeshRenderable(mesh, material, Float4x4.CreateTranslation(new Float3(0, 0, z)), 0, props);
    }

    // The _ObjectID of every per object property snapshot, in the order the buffer will draw them.
    private static List<int> EncodedObjectIds(CommandBuffer cmd)
    {
        var ids = new List<int>();
        foreach (object? o in cmd._objects)
            if (o is PropertyState ps && ps.HasInt("_ObjectID"))
                ids.Add(ps.GetInt("_ObjectID"));
        return ids;
    }

    [Fact]
    public void PreserveOrder_KeepsSortedTransparentsInterleaved()
    {
        Mesh quad = Mesh.GetFullscreenQuad();
        var glass = new Material(Shader.LoadDefault(DefaultShader.StandardTransparent));
        var smoke = new Material(Shader.LoadDefault(DefaultShader.StandardTransparent));
        smoke.SetFloat("_Metallic", 0.25f);

        // Already sorted back to front: glass, smoke, glass.
        var sorted = new List<IRenderable>
        {
            Renderable(quad, glass, 1, -30),
            Renderable(quad, smoke, 2, -20),
            Renderable(quad, glass, 3, -10),
        };

        var pipeline = new DefaultRenderPipeline();
        using var cmd = Graphics.GetCommandBuffer("Test");
        pipeline.DrawRenderables(cmd, sorted, "RenderOrder", "Transparent", default, null, false, null, preserveOrder: true);

        Assert.Equal([1, 2, 3], EncodedObjectIds(cmd));
    }

    // A big scene encodes a few objects per draw into one buffer, well past what 16 bit indices held.
    [Fact]
    public void CommandBuffer_EncodesMoreThan65kObjects()
    {
        using var cmd = Graphics.GetCommandBuffer("Test");

        for (int i = 0; i < 70_000; i++)
            cmd.SetRenderTarget(null);

        Assert.Equal(70_000, cmd._objects.Count);
    }

    // A submitted buffer that has run must stay out of the pool until its owner disposes it, or the
    // owner's late Dispose would recycle it out from under whoever rented it next.
    [Fact]
    public void CommandBuffer_IsNotReusedUntilOwnerDisposes()
    {
        var first = Graphics.GetCommandBuffer("First");
        Graphics.Submit(first);
        Assert.False(first._inPool);

        first.Dispose();
        Assert.True(first._inPool);
    }

    [Fact]
    public void CommandBuffer_EncodingAfterSubmitThrows()
    {
        using var cmd = Graphics.GetCommandBuffer("Test");
        Graphics.Submit(cmd);

        Assert.Throws<System.InvalidOperationException>(() => cmd.SetRenderTarget(null));
    }

    // Effects rent width / 2, which is 0 on a 1 pixel viewport; release has to find it again.
    [Fact]
    public void TemporaryRT_ZeroSizedRentIsReused()
    {
        TextureImageFormat[] format = [TextureImageFormat.Float2];
        var first = RenderTexture.GetTemporaryRT(0, 7, false, format);
        RenderTexture.ReleaseTemporaryRT(first);

        var second = RenderTexture.GetTemporaryRT(0, 7, false, format);
        RenderTexture.ReleaseTemporaryRT(second);

        Assert.Same(first, second);
    }

    private sealed class MotionPipeline : RenderPipeline
    {
        public Float4x4 Frame(Camera camera, int objectId, int subMesh, Float4x4 model)
        {
            BeginMotionTracking(camera);
            Float4x4 prev = TrackModelMatrix(objectId, subMesh, in model);
            Render(camera, default);
            return prev;
        }

        public (Float4x4 first, Float4x4 second) FrameWithTwoSubMeshes(Camera camera, int objectId, Float4x4 model)
        {
            BeginMotionTracking(camera);
            Float4x4 first = TrackModelMatrix(objectId, 0, in model);
            Float4x4 second = TrackModelMatrix(objectId, 1, in model);
            Render(camera, default);
            return (first, second);
        }
    }

    private static Camera CreateCamera() => new GameObject("Camera").AddComponent<Camera>();

    [Fact]
    public void MotionHistory_EverySubMeshSeesLastFramesMatrix()
    {
        var pipeline = new MotionPipeline();
        Camera camera = CreateCamera();
        Float4x4 before = Float4x4.CreateTranslation(new Float3(1, 0, 0));
        Float4x4 after = Float4x4.CreateTranslation(new Float3(2, 0, 0));

        pipeline.FrameWithTwoSubMeshes(camera, 7, before);
        var (first, second) = pipeline.FrameWithTwoSubMeshes(camera, 7, after);

        Assert.Equal(before, first);
        Assert.Equal(before, second);
    }

    [Fact]
    public void MotionHistory_IsKeptPerCamera()
    {
        var pipeline = new MotionPipeline();
        Camera gameCamera = CreateCamera();
        Camera sceneCamera = CreateCamera();
        Float4x4 before = Float4x4.CreateTranslation(new Float3(1, 0, 0));
        Float4x4 after = Float4x4.CreateTranslation(new Float3(2, 0, 0));

        pipeline.Frame(gameCamera, 7, -1, before);
        pipeline.Frame(sceneCamera, 7, -1, before);
        pipeline.Frame(sceneCamera, 7, -1, after);

        Assert.Equal(before, pipeline.Frame(gameCamera, 7, -1, after));
    }

    private static Camera CreateSizedCamera()
    {
        Camera camera = CreateCamera();
        camera.UpdateRenderData(new RenderTexture(64, 64, true, [TextureImageFormat.Color4b]));
        return camera;
    }

    // An oblique clip plane for water, say, is set by hand and must survive TAA's per frame jitter.
    [Fact]
    public void TAA_KeepsHandSetProjection()
    {
        Camera camera = CreateSizedCamera();
        Float4x4 custom = Float4x4.CreatePerspectiveFov(1f, 1f, 0.5f, 50f);
        camera.ProjectionMatrix = custom;

        var taa = new TAAEffect();
        taa.OnPreCull(camera);
        taa.OnPostRender(camera);

        Assert.True(camera.HasCustomProjectionMatrix);
        Assert.Equal(custom, camera.ProjectionMatrix);
    }

    [Fact]
    public void TAA_LeavesComputedProjectionComputed()
    {
        Camera camera = CreateSizedCamera();

        var taa = new TAAEffect();
        taa.OnPreCull(camera);
        taa.OnPostRender(camera);

        Assert.False(camera.HasCustomProjectionMatrix);
        Assert.False(camera.HasCustomNonJitteredProjectionMatrix);
    }

    private static Float3 ClipToNdc(Float4x4 clip, Float3 viewPos)
    {
        Float4 c = clip * new Float4(viewPos, 1f);
        return new Float3(c.X, c.Y, c.Z) / c.W;
    }

    [Fact]
    public void ToGLClipDepth_SpansWholeDepthRange()
    {
        const float near = 0.3f, far = 500f;
        Float4x4[] projections =
        [
            Float4x4.CreatePerspectiveFov(1.2f, 1.6f, near, far),
            Float4x4.CreateOrtho(20f, 12f, near, far),
        ];

        foreach (Float4x4 projection in projections)
        {
            Float4x4 gl = RenderPipeline.ToGLClipDepth(projection);

            // OpenGL clips and maps depth over -1 to 1, so the near plane has to land on -1, not halfway
            Assert.Equal(-1f, ClipToNdc(gl, new Float3(1f, 0.5f, near)).Z, 4);
            Assert.Equal(1f, ClipToNdc(gl, new Float3(1f, 0.5f, far)).Z, 3);

            Float3 mid = new(2f, -1f, 40f);
            Assert.Equal(ClipToNdc(projection, mid).X, ClipToNdc(gl, mid).X, 5);
            Assert.Equal(ClipToNdc(projection, mid).Y, ClipToNdc(gl, mid).Y, 5);
        }
    }

    [Fact]
    public void CullRenderables_ReusedMask_DoesNotKeepLastResult()
    {
        Mesh quad = Mesh.GetFullscreenQuad();
        var material = new Material(Shader.LoadDefault(DefaultShader.StandardTransparent));
        var renderables = new List<IRenderable> { Renderable(quad, material, 1, 5), Renderable(quad, material, 2, 6) };
        var pipeline = new DefaultRenderPipeline();

        Float4x4 view = Float4x4.CreateLookTo(Float3.Zero, Float3.UnitZ, Float3.UnitY);
        Frustum ahead = Frustum.FromMatrix(Float4x4.CreatePerspectiveFov(1f, 1f, 0.1f, 100f) * view);
        Frustum behind = Frustum.FromMatrix(Float4x4.CreatePerspectiveFov(1f, 1f, 0.1f, 100f)
            * Float4x4.CreateLookTo(Float3.Zero, -Float3.UnitZ, Float3.UnitY));

        // The masks are pooled, so a mask that culled everything comes back for the next view and has to be rewritten
        bool[] first = pipeline.CullRenderables(renderables, behind, LayerMask.Everything);
        Assert.True(first[0] && first[1]);
        pipeline.ReturnCullResult(first);

        bool[] second = pipeline.CullRenderables(renderables, ahead, LayerMask.Everything);
        Assert.False(second[0] || second[1]);
        pipeline.ReturnCullResult(second);
    }
}
