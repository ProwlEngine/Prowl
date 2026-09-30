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
}
