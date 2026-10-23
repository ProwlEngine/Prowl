using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

internal sealed partial class FramePipeline
{
    // Holds the draw program of the environment's sky material while it has a Sky pass
    private void UpdateSkyProgram(WorldFrame world)
    {
        ushort? program = null;
        if (world.World.Lighting.Environment.Sky is { IsDisposed: false, Table: not null } sky && DrawPrograms.TryGet(sky.Draw, PassType.Sky, out ushort id))
            program = id;
        if (program == world.SkyProgram)
            return;
        if (program is ushort acquired)
            DrawPrograms.Acquire(acquired);
        if (world.SkyProgram is ushort released)
            DrawPrograms.Release(released);
        world.SkyProgram = program;
    }

    private void CaptureSky(WorldFrame world, in FrameTime time)
    {
        WorldReflections reflections = world.World.Reflections;
        world.SkyCamera ??= Capturing();
        WorldReflections.Scratch scratch = reflections.SkyScratch!;
        if (world.SkyProgram is ushort sky && !Programs.Ready(sky, false, world.World.Globals.KeywordSnapshot))
            reflections.SkyIncomplete();

        ViewDesc capture = new()
        {
            World = world.World,
            Name = "Sky capture",
            Kind = ViewKind.SkyCapture,
            ViewId = world.SkyCamera.View.ViewId,
            LayerCount = 6,
            Target = scratch.All,
            CullingMask = 0,
            ClearColor = world.World.Lighting.Environment.SkyColor,
            Near = 0.05f,
        };
        for (int face = 0; face < 6; face++)
            capture.Layers[face] = new ViewLayer { View = WorldReflections.FaceView(face), Projection = WorldReflections.FaceProjection };
        world.SkyCamera.CaptureCube = scratch.AllTexture;
        Draw(world.SkyCamera, capture, [], world, time);
        Fill(world, reflections.Sky!, scratch, null, reflections.SkyAmbient);
    }

    private void Fill(WorldFrame world, WorldReflections.Target target, WorldReflections.Scratch scratch, DeviceBuffer? readback, DeviceBuffer? ambient)
    {
        CaptureFill fill = new() { Source = scratch.AllTexture, Target = target, Samples = world.World.Reflections.PrefilterSamples, Readback = readback, SkyAmbient = ambient };
        _planned.Add(new PlannedInstance(PlannedKind.Fill, world, fill, $"Capture fill {_planned.Count}"));
    }

    private void CaptureProbe(WorldFrame world, ProbeCapture work, in FrameTime time)
    {
        WorldReflections reflections = world.World.Reflections;
        ReflectionProbeDesc probe = reflections.Probe(work.Index);
        WorldReflections.Target target = reflections.ProbeTarget(work.Index);
        WorldReflections.Scratch scratch = work.Scratch;
        Float4x4 projection = WorldReflections.FaceProjectionBetween(probe.NearClip, probe.FarClip);
        if (world.SkyProgram is ushort sky && !Programs.Ready(sky, false, world.World.Globals.KeywordSnapshot) && !Programs.Failed(sky, false, world.World.Globals.KeywordSnapshot))
            reflections.Request(work.Index);
        int faces = work.EndFace - work.FirstFace;
        if (faces == 6)
        {
            CameraFrame camera = Pooled(world.ProbeCameras, ref world.ProbeCamerasUsed);
            ViewDesc capture = new()
            {
                World = world.World,
                Name = $"Reflection probe {work.Index}",
                Kind = ViewKind.ReflectionProbe,
                ViewId = camera.View.ViewId,
                LayerCount = 6,
                Target = scratch.All,
                CullingMask = probe.CullingMask,
                ClearColor = world.World.Lighting.Environment.SkyColor,
                Near = probe.NearClip,
                FarDistance = probe.FarClip,
            };
            for (int face = 0; face < 6; face++)
                capture.Layers[face] = new ViewLayer { View = WorldReflections.FaceView(face), Projection = projection, Position = probe.Position };
            camera.CaptureCube = scratch.AllTexture;
            Draw(camera, capture, [], world, time);
        }
        else if (faces == 1)
        {
            CameraFrame camera = Pooled(world.ProbeFaceCameras, ref world.ProbeFaceCamerasUsed);
            ViewDesc capture = new()
            {
                World = world.World,
                Name = $"Reflection probe {work.Index} face {work.FirstFace}",
                Kind = ViewKind.ReflectionProbe,
                ViewId = camera.View.ViewId,
                Target = scratch.Faces[work.FirstFace],
                CullingMask = probe.CullingMask,
                ClearColor = world.World.Lighting.Environment.SkyColor,
                Near = probe.NearClip,
                FarDistance = probe.FarClip,
            };
            capture.Layers[0] = new ViewLayer { View = WorldReflections.FaceView(work.FirstFace), Projection = projection, Position = probe.Position };
            camera.CaptureCube = scratch.FaceTextures[work.FirstFace];
            Draw(camera, capture, [], world, time);
        }

        if (work.Prefilter)
            Fill(world, target, scratch, work.Readback, null);
    }

    private CameraFrame Capturing()
    {
        CameraFrame camera = new(_device, this, WorldFrame.NextCaptureViewId());
        _textures.TrackView(camera.ViewTable);
        return camera;
    }

    private CameraFrame Pooled(List<CameraFrame> pool, ref int used)
    {
        if (used == pool.Count)
            pool.Add(Capturing());
        return pool[used++];
    }

    // Capture views keep their textures while drawing now and then, and let them go once unused for a while
    private void ReleaseIdleCaptures()
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            foreach (CameraFrame camera in world.Captures())
            {
                if (_frame - camera.LastFrame > StaleViewFrames)
                {
                    camera.ReleaseSceneColor();
                    camera.ReleaseSceneDepth();
                }
            }
        }
    }
}
