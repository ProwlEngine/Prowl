using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

internal sealed partial class FramePipeline
{
    // Snaps each world's voxel box and readies its voxel view: three orthographic layers, one looking down each axis,
    // every one the box's largest face at two pixels per voxel, its renderables' Voxelize passes listed
    private void PrepareVoxels()
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            WorldVoxels voxels = world.World.Voxels;
            voxels.Update(RenderView.ExposureOf(world.World.Lighting.Environment.CaptureEV100));
            if (voxels.Data is null || voxels.Desc is not { } desc)
            {
                world.ReleaseVoxelView(_store);
                continue;
            }

            Float3 extent = voxels.Extent;
            float largest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
            uint size = (uint)Math.Max(desc.Resolution.X, Math.Max(desc.Resolution.Y, desc.Resolution.Z)) * 2;
            CameraFrame camera = world.VoxelCamera ??= VoxelCamera();
            camera.VoxelTarget = voxels.Data;
            world.EnsureVoxelTarget(_device, _store, size);

            ViewDesc view = new()
            {
                World = world.World,
                Name = "Voxelization",
                Kind = ViewKind.Voxelization,
                ViewId = camera.View.ViewId,
                LayerCount = 3,
                Sky = false,
                CullingMask = desc.CullingMask,
                Near = 0.05f,
                EV100 = world.World.Lighting.Environment.CaptureEV100,
                FarDistance = largest,
            };
            view.Layers[0] = VoxelLayer(voxels.Center, new Float3(1, 0, 0), new Float3(0, 1, 0), largest, extent.X);
            view.Layers[1] = VoxelLayer(voxels.Center, new Float3(0, 1, 0), new Float3(0, 0, 1), largest, extent.Y);
            view.Layers[2] = VoxelLayer(voxels.Center, new Float3(0, 0, 1), new Float3(0, 1, 0), largest, extent.Z);

            // The whole box's view holds the light trees the radiance cache reads, each region's draws its renderables
            PrepareVoxelView(camera, view, world, size, voxelize: false);
            world.VoxelDrawCamerasUsed = 0;
            Double3 corner = voxels.Center - new Double3(extent.X, extent.Y, extent.Z) * 0.5;
            float voxel = voxels.Header.VoxelSize;
            foreach (VoxelRegion region in voxels.Regions)
            {
                if (world.VoxelDrawCamerasUsed == world.VoxelDrawCameras.Count)
                    world.VoxelDrawCameras.Add(VoxelDrawCamera());
                CameraFrame draw = world.VoxelDrawCameras[world.VoxelDrawCamerasUsed++];
                draw.VoxelTarget = voxels.Data;
                Float3 regionSize = new Float3(region.Max.X - region.Min.X, region.Max.Y - region.Min.Y, region.Max.Z - region.Min.Z) * voxel;
                Double3 regionCenter = corner + new Double3(region.Min.X, region.Min.Y, region.Min.Z) * voxel + new Double3(regionSize.X, regionSize.Y, regionSize.Z) * 0.5;
                ViewLayer bounds = ViewCamera.Orthographic(regionCenter, new Float3(0, 0, 1), new Float3(0, 1, 0), regionSize.Y, -regionSize.Z * 0.5f, regionSize.Z * 0.5f, regionSize.X / regionSize.Y).Layers[0];
                draw.ShadowCull = new CullFrustum(bounds.Projection * bounds.View, regionCenter);
                draw.ShadowFilter = CasterFilter.All;
                PrepareVoxelView(draw, view with { ViewId = draw.View.ViewId }, world, size, voxelize: true);
            }
            PrepareVoxelLighting(world, voxels);
        }
    }

    private void PrepareVoxelView(CameraFrame camera, in ViewDesc view, WorldFrame world, uint size, bool voxelize)
    {
        camera.LastFrame = _frame;
        camera.Desc = view;
        camera.World = world;
        camera.ResolveKeywords(null);
        camera.Info = new FeatureView(view, size, size);
        camera.ResetPassLists();
        if (voxelize)
            camera.ListFor(PassType.Voxelize);
        Prepare(camera, view, world, default, size, size);
        camera.View.PixelWidth = size;
        camera.View.PixelHeight = size;
    }

    private CameraFrame VoxelDrawCamera()
    {
        CameraFrame camera = new(_device, this, WorldFrame.NextCaptureViewId()) { IsShadowView = true };
        _textures.TrackView(camera.ViewTable);
        return camera;
    }

    internal const int VoxelShadowResolution = 2048;

    // The sun's shadow over the whole box, an orthographic map around its half diagonal snapped to texels so it holds
    // still as the box moves, and the record the hit lighting reads
    private void PrepareVoxelLighting(WorldFrame world, WorldVoxels voxels)
    {
        world.EnsureVoxelShadow(_device, _store, VoxelShadowResolution, ShadowFormat);
        world.VoxelShadowDraws.Clear();
        EnvironmentDesc environment = world.World.Lighting.Environment;
        float exposure = voxels.Header.EmissionExposure;
        WorldReflections reflections = world.World.Reflections;
        Float4 sky = reflections.Sky is { } target
            ? new Float4(0, 0, 0, Math.Max((int)target.Mips - 3, 0))
            : new Float4(environment.SkyColor.X * exposure, environment.SkyColor.Y * exposure, environment.SkyColor.Z * exposure, -1);
        VoxelLightingRecord record = new() { Sky = sky };

        if (world.World.Lighting.Sun is { } sun)
        {
            Float3 extent = voxels.Extent;
            float radius = 0.5f * MathF.Sqrt(extent.X * extent.X + extent.Y * extent.Y + extent.Z * extent.Z);
            (Float3 right, Float3 up, Float3 forward) = ShadowMath.Basis(sun.Direction, sun.Up);
            Float4x4 rotation = ShadowMath.Rows(right, up, forward);
            Double3 center = ShadowMath.Snap(voxels.Center, right, up, forward, 2.0 * radius / VoxelShadowResolution);
            float reach = _device.Features.DepthClipDisable ? radius : radius * 3;
            Float4x4 projection = ShadowMath.Orthographic(radius, reach);
            Float4x4 shape = projection * rotation;

            if (sun.Shadow.Enabled)
            {
                CameraFrame camera = world.VoxelShadowCamera ??= ShadowCamera();
                ViewLayer layer = new() { View = rotation, Projection = projection, Position = center };
                DrawShadowView(camera, world, VoxelShadowResolution, layer, new CullFrustum(shape, center, nearPlane: false), -reach, radius, CasterFilter.All);
                world.VoxelShadowDraws.Add(new ShadowDraw(camera, new ShadowTile(0, 0, VoxelShadowResolution), Clear: true));
            }

            Double3 corner = voxels.Center - new Double3(voxels.Extent.X, voxels.Extent.Y, voxels.Extent.Z) * 0.5 - center;
            float size = voxels.Header.VoxelSize;
            Float4x4 fromVoxels = Float4x4.CreateTranslation(new Float3((float)corner.X, (float)corner.Y, (float)corner.Z)) * Float4x4.CreateScale(size);
            record.SunShadow = shape * fromVoxels;
            record.SunDirection = new Float4(-sun.Direction.X, -sun.Direction.Y, -sun.Direction.Z, sun.Shadow.Enabled ? 1 : 0);
            Float3 color = sun.Color * (sun.Intensity * exposure);
            record.SunColor = new Float4(color.X, color.Y, color.Z, 1);
        }
        voxels.Lighting.Write<VoxelLightingRecord>(_uploader, [record]);
    }

    // A layer looking down an axis: the largest face of the box across, the box's depth along the axis
    private static ViewLayer VoxelLayer(Double3 center, Float3 forward, Float3 up, float across, float depth)
        => ViewCamera.Orthographic(center, forward, up, across, -depth * 0.5f, depth * 0.5f, aspect: 1).Layers[0];

    private CameraFrame VoxelCamera()
    {
        CameraFrame camera = new(_device, this, WorldFrame.NextCaptureViewId());
        _textures.TrackView(camera.ViewTable);
        return camera;
    }
}

/// <summary>
/// Fills a world's voxel volume: clears it and writes its header, draws the voxel view's Voxelize passes into it, then
/// builds the occupancy pyramid a level at a time.
/// </summary>
internal sealed class VoxelPass(InternalCompute clear, InternalCompute pyramid, InternalCompute cacheUpdate) : IDisposable
{
    private const uint GroupsPerRow = 1024;
    private static readonly PropertyID s_voxels = "Graphite_Voxels", s_cache = "Graphite_VoxelCache", s_lighting = "Graphite_VoxelLighting",
        s_sunShadow = "Graphite_VoxelSunShadow", s_sky = "Graphite_VoxelSky", s_stride = "_Stride", s_phase = "_Phase", s_frame = "_Frame", s_clear = "_Clear", s_scrolled = "_Scrolled";
    private readonly PropertySet _properties = new();

    public void Add(FramePipeline frame, FrameGraph graph, WorldFrame world, int currentFrame)
    {
        WorldVoxels voxels = world.World.Voxels;
        if (voxels.Data is not { } data || world.VoxelCamera is not { } camera || camera.LastFrame != currentFrame || world.VoxelTarget is not { } target)
            return;
        VoxelHeader header = voxels.Header;

        VoxelRegion[] regions = [.. voxels.Regions];
        List<(Int3 Min, Int3 Max)> pieces = PyramidPieces(regions, header);
        graph.AddPass("Graphite.VoxelClear", builder => builder.Write(graph.Import(data), BufferAccess.ShaderWrite), (context, _) =>
        {
            CommandBuffer cmd = context.Command;
            cmd.SetComputeShader(clear.Program);
            _properties.SetBuffer(s_voxels, data);
            cmd.SetProperties(_properties);
            if (regions.Length == 0)
            {
                cmd.SetPushConstants(new ClearPush { Header = header });
                cmd.Dispatch(1, 1, 1);
            }
            foreach (VoxelRegion region in regions)
            {
                cmd.SetPushConstants(new ClearPush { Header = header, Min = new Int4(region.Min.X, region.Min.Y, region.Min.Z, 0), Max = new Int4(region.Max.X, region.Max.Y, region.Max.Z, 0) });
                Dispatch(cmd, (uint)region.Count);
            }
        });

        if (world.VoxelShadow is { } shadow)
        {
            graph.AddPass("Graphite.VoxelSunShadow", builder =>
            {
                frame.Buffers.ReadWorld(graph, builder, world);
                foreach (ShadowDraw draw in world.VoxelShadowDraws)
                    draw.Camera.Buffers.Read(graph, builder, BufferAccess.AllReads);
                return builder.Write(graph.Import(shadow), TextureState.Attachment);
            }, (context, handle) =>
            {
                RenderTexture texture = context.Get(handle);
                if (world.VoxelShadowDraws.Count > 0)
                    ShadowRecorder.Record(context.Command, texture, world.VoxelShadowDraws, frame.TileClearProgram, context.Task.RingSlot);
                else
                    context.Command.SetFramebuffer(texture.Framebuffer, new TargetLoadStoreOps(AttachmentOps.Discard, AttachmentOps.Clear(0f)));
            });
        }

        graph.AddPass("Graphite.Voxelize", builder =>
        {
            frame.Buffers.ReadWorld(graph, builder, world);
            for (int i = 0; i < world.VoxelDrawCamerasUsed; i++)
                world.VoxelDrawCameras[i].Buffers.Read(graph, builder, BufferAccess.AllReads);
            builder.Write(graph.Import(data), BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
            return builder.Write(graph.Import(target), TextureState.Attachment);
        }, (context, handle) =>
        {
            CommandBuffer cmd = context.Command;
            cmd.SetFramebuffer(context.Get(handle).Framebuffer, new TargetLoadStoreOps(AttachmentOps.Discard, AttachmentOps.Discard));
            cmd.SetFullViewport();
            for (int i = 0; i < world.VoxelDrawCamerasUsed; i++)
            {
                CameraFrame draw = world.VoxelDrawCameras[i];
                draw.Drawer.Prepare(context.Task.RingSlot);
                if (draw.FindList(PassType.Voxelize) is { } list)
                    draw.Drawer.DrawList(cmd, list);
            }
        });

        graph.AddPass("Graphite.VoxelPyramid", builder => builder.Write(graph.Import(data), BufferAccess.ShaderRead | BufferAccess.ShaderWrite), (context, _) =>
        {
            CommandBuffer cmd = context.Command;
            cmd.SetComputeShader(pyramid.Program);
            _properties.SetBuffer(s_voxels, data);
            cmd.SetProperties(_properties);
            for (uint level = 1; level <= 6; level++)
            {
                if (level > 1)
                    cmd.ComputeBarrier();
                // Level 6 is the byte per level 1 cell of its coarsest empty level
                int shift = level == 6 ? 1 : (int)level;
                foreach ((Int3 min, Int3 max) in pieces)
                {
                    Int3 cellMin = new(min.X >> shift, min.Y >> shift, min.Z >> shift), cellMax = new(max.X >> shift, max.Y >> shift, max.Z >> shift);
                    Int3 size = cellMax - cellMin;
                    cmd.SetPushConstants(new PyramidPush { Level = new Int4((int)level, 0, 0, 0), Min = new Int4(cellMin, 0), Max = new Int4(cellMax, 0) });
                    Dispatch(cmd, (uint)(level == 6 ? size.X / 4 * size.Y * size.Z : size.X * size.Y * size.Z));
                }
            }
        });

        AddCacheUpdate(frame, graph, world, voxels, data);
    }

    // A part of the radiance cache traced again, after the voxels, their pyramid and the sun's shadow are ready, bound to
    // the voxel view's tables for its light trees and local shadows
    private void AddCacheUpdate(FramePipeline frame, FrameGraph graph, WorldFrame world, WorldVoxels voxels, DeviceBuffer data)
    {
        if (voxels.Cache is not { } cache || world.VoxelShadow is not { } shadow || world.VoxelCamera is not { } camera)
            return;
        bool stale = voxels.CacheStale;
        Int3 scrolled = voxels.CacheScrolled;
        voxels.CacheStale = false;
        voxels.CacheScrolled = Int3.Zero;
        uint stride = Math.Max(voxels.CacheStride, 1);
        uint updates = voxels.CacheFrame++;
        Texture sky = frame.Resources.NoCube;
        WorldReflections.Target? captured = world.World.Reflections.Sky;

        graph.AddPass("Graphite.VoxelCache", builder =>
        {
            frame.Buffers.ReadWorld(graph, builder, world);
            camera.Buffers.Read(graph, builder, BufferAccess.AllReads);
            frame.DeclareShadowReads(graph, builder);
            builder.Read(graph.Import(data), BufferAccess.AllReads);
            builder.Read(graph.Import(voxels.Lighting.Buffer), BufferAccess.AllReads);
            builder.Read(graph.Import(shadow), TextureState.Sampled);
            if (captured is { Persistent.Texture: { } texture })
            {
                builder.Read(graph.Import(texture), TextureState.Sampled);
                sky = captured.Cube;
            }
            return builder.Write(graph.Import(cache), BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
        }, (context, _) =>
        {
            CommandBuffer cmd = context.Command;
            cmd.SetComputeShader(cacheUpdate.Program);
            camera.Drawer.Prepare(context.Task.RingSlot);
            camera.Drawer.BindTables(cmd);
            _properties.SetBuffer(s_cache, cache);
            _properties.SetBuffer(s_lighting, voxels.Lighting.Buffer);
            _properties.SetTexture(s_sunShadow, shadow.DepthTexture!, frame.ShadowSampler);
            _properties.SetTexture(s_sky, sky, frame.Resources.LinearClamp);
            if (stale)
            {
                SetCacheUniforms(1, 0, 0, clear: 1);
                cmd.SetProperties(_properties);
                Dispatch(cmd, WorldVoxels.CacheEntries);
                cmd.ComputeBarrier();
            }
            else if (scrolled != Int3.Zero)
            {
                SetCacheUniforms(1, 0, 0, clear: 2);
                _properties.SetInt4(s_scrolled, new Int4(scrolled.X, scrolled.Y, scrolled.Z, 0));
                cmd.SetProperties(_properties);
                Dispatch(cmd, WorldVoxels.CacheEntries);
                cmd.ComputeBarrier();
            }
            SetCacheUniforms(stride, updates % stride, updates, clear: 0);
            cmd.SetProperties(_properties);
            Dispatch(cmd, (WorldVoxels.CacheEntries + stride - 1) / stride);
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PyramidPush
    {
        public Int4 Level, Min, Max;
    }

    // The stored voxels a box region covers, split where the wrap crosses the edge and grown to whole level 5 cells
    private static List<(Int3 Min, Int3 Max)> PyramidPieces(VoxelRegion[] regions, VoxelHeader header)
    {
        List<(Int3, Int3)> pieces = new();
        Span<int> resolution = [(int)header.ResolutionX, (int)header.ResolutionY, (int)header.ResolutionZ];
        Span<int> wrap = [(int)header.WrapX, (int)header.WrapY, (int)header.WrapZ];
        foreach (VoxelRegion region in regions)
        {
            Span<int> min = [region.Min.X, region.Min.Y, region.Min.Z], max = [region.Max.X, region.Max.Y, region.Max.Z];
            (int Low, int High)[][] ranges = new (int, int)[3][];
            for (int a = 0; a < 3; a++)
            {
                int start = (min[a] + wrap[a]) % resolution[a], end = start + max[a] - min[a];
                ranges[a] = end <= resolution[a] ? [Align(start, end)] : [Align(start, resolution[a]), Align(0, end - resolution[a])];
            }
            foreach ((int Low, int High) x in ranges[0])
                foreach ((int Low, int High) y in ranges[1])
                    foreach ((int Low, int High) z in ranges[2])
                        pieces.Add((new Int3(x.Low, y.Low, z.Low), new Int3(x.High, y.High, z.High)));
        }
        return pieces;
    }

    private static (int, int) Align(int low, int high) => (low & ~31, (high + 31) & ~31);

    private struct ClearPush
    {
        public VoxelHeader Header;
        public Int4 Min, Max;
    }

    private void SetCacheUniforms(uint stride, uint phase, uint frame, int clear)
    {
        _properties.SetInt(s_stride, (int)stride);
        _properties.SetInt(s_phase, (int)phase);
        _properties.SetInt(s_frame, (int)frame);
        _properties.SetInt(s_clear, clear);
    }

    private static void Dispatch(CommandBuffer cmd, uint threads)
    {
        uint groups = (threads + 63) / 64;
        cmd.Dispatch(Math.Min(groups, GroupsPerRow), (groups + GroupsPerRow - 1) / GroupsPerRow, 1);
    }

    public void Dispose()
    {
        clear.Dispose();
        pyramid.Dispose();
        cacheUpdate.Dispose();
    }
}
