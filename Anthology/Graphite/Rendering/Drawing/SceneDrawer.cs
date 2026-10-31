using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>Records a view's built in draws for the core features: its table, its draw lists and its sky.</summary>
internal sealed class SceneDrawer(FramePipeline frame, CameraFrame camera)
{
    private static readonly PropertyID s_anisotropicDfg = "Graphite_AnisotropicDfg", s_backDepth = "Graphite_BackDepth",
        s_cascades = "Graphite_Cascades", s_chunks = "Graphite_Chunks", s_dfg = "Graphite_Dfg", s_dielectricE = "Graphite_DielectricE",
        s_directionals = "Graphite_Directionals", s_globals = "Graphite_Globals", s_materialSlots = "Graphite_MaterialSlots",
        s_objectProbes = "Graphite_ObjectProbes", s_objects = "Graphite_Objects", s_previousTransforms = "Graphite_PreviousTransforms",
        s_sceneColor = "Graphite_SceneColor", s_sceneColorSampler = "Graphite_SceneColorSampler", s_sceneDepth = "Graphite_SceneDepth",
        s_screenIrradiance = "Graphite_ScreenIrradiance", s_screenSampler = "Graphite_ScreenSampler",
        s_screenVisibility = "Graphite_ScreenVisibility", s_shadowFaces = "Graphite_ShadowFaces", s_shadowSampler = "Graphite_ShadowSampler",
        s_skyAmbientSH = "Graphite_SkyAmbientSH", s_spatialNodes = "Graphite_SpatialNodes", s_spatialRecords = "Graphite_SpatialRecords",
        s_spatialRegions = "Graphite_SpatialRegions", s_spatialRoots = "Graphite_SpatialRoots", s_tableSampler = "Graphite_TableSampler",
        s_transforms = "Graphite_Transforms", s_views = "Graphite_Views", s_worldLighting = "Graphite_WorldLighting";

    private readonly PropertySet _properties = new();
    private (DeviceBuffer? Buffer, uint Offset, uint Size) _globals;
    private DescriptorTable _viewTable = null!;

    /// <summary>Fills the view's table for the frame. Recorded first in the view's graph.</summary>
    public void Prepare(uint ring)
    {
        GlobalRegistry registry = frame.Globals;
        (DeviceBuffer Buffer, uint Offset, uint Size) globals = (registry.Buffer, (uint)camera.World.GlobalRegion * registry.RegionWords * 4, registry.RegionWords * 4);
        if (globals != _globals)
        {
            _globals = globals;
            camera.ViewTable.Set(s_globals, 0, new DeviceBufferRange(globals.Buffer, globals.Offset, globals.Size));
        }
        camera.ViewTable.Set(s_views, 0, camera.ViewBlocks.Buffer);
        camera.ViewTable.Set(s_spatialRoots, 0, camera.SpatialRoots.Buffer);
        for (int slot = 0; slot < camera.Spatial.Length; slot++)
        {
            if (camera.Spatial[slot] is not { } view)
                continue;
            camera.ViewTable.Set(s_spatialRecords, (uint)(slot * 2 + 1), view.Records.Buffer);
            camera.ViewTable.Set(s_spatialNodes, (uint)(slot * 3 + 1), view.Nodes.Buffer);
            camera.ViewTable.Set(s_spatialNodes, (uint)(slot * 3 + 2), view.RegionNodes.Buffer);
            camera.ViewTable.Set(s_spatialRegions, (uint)slot, view.RegionEntries.Buffer);
        }
        SetWorldBuffers(camera.ViewTable, camera.World.World);
        camera.ViewTable.Set(s_shadowSampler, 0, frame.ShadowSampler);
        camera.ViewTable.Set(s_shadowFaces, 0, camera.World.Shadows!.Faces.Buffer);
        camera.ViewTable.Set(s_cascades, 0, camera.Cascades.Buffer);
        camera.ViewTable.Set(s_screenVisibility, 0, (object?)camera.ScreenVisibilityView() ?? frame.Resources.NoScreenVisibility);
        camera.ViewTable.Set(s_screenIrradiance, 0, (object?)camera.ScreenIrradianceView() ?? frame.Resources.NoScreenIrradiance);
        camera.ViewTable.Set(s_screenSampler, 0, frame.Resources.LinearClamp);
        camera.ViewTable.Set(s_sceneColor, 0, camera.SceneColor != null ? camera.SceneColorView() : frame.Resources.NoSceneColor);
        camera.ViewTable.Set(s_sceneColorSampler, 0, frame.Resources.LinearClamp);
        camera.ViewTable.Set(s_sceneDepth, 0, camera.SceneDepth != null ? camera.SceneDepthView() : frame.Resources.NoSceneDepth);
        camera.ViewTable.Set(s_dfg, 0, frame.Resources.Dfg);
        camera.ViewTable.Set(s_dielectricE, 0, frame.Resources.DielectricE);
        camera.ViewTable.Set(s_anisotropicDfg, 0, frame.Resources.AnisotropicDfg);
        camera.ViewTable.Set(s_backDepth, 0, (object?)camera.BackDepthView() ?? frame.Resources.NoSceneDepth);
        camera.ViewTable.Set(s_tableSampler, 0, frame.Resources.LinearClamp);
        _viewTable = camera.ViewTable.Get(ring);
        _properties.SetBuffer(GraphiteBindings.InstancesName, camera.Instances.Buffer);
        _properties.SetBuffer(GraphiteBindings.DrawRecordsName, camera.Records.Buffer);
    }

    /// <summary>Binds the frame's texture tables and the view's table for a program drawn without draw records.</summary>
    public void BindTables(CommandBuffer cmd)
    {
        cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame.Resources.TextureTable);
        cmd.SetDescriptorTable(GraphiteBindings.ViewSet, _viewTable);
        cmd.SetPushConstants(new FramePipeline.Push());
    }

    /// <summary>Records one of the view's draw lists.</summary>
    public void DrawList(CommandBuffer cmd, DrawList list) => Draw(cmd, list, camera.CommandBases[camera.Lists.IndexOf(list)]);

    // The host's buffers a program reads, from the world's globals over the renderer's. A missing one skips the draw.
    private bool BindUserBuffers(GraphicsProgram program, Shader shader)
    {
        IReadOnlyDictionary<PropertyID, DeviceBuffer> buffers = camera.World.World.Globals.BufferSnapshot;
        foreach (PropertyID name in frame.Programs.UserBuffers(program))
        {
            if (!buffers.TryGetValue(name, out DeviceBuffer? buffer) || buffer.IsDisposed)
            {
                frame.Report(new RendererError("global.buffer.missing", $"{shader.Name} reads the buffer {name.Text()}, which no Globals.SetBuffer provides, so it does not draw.", RendererErrorSeverity.Error, shader) { Key = $"global.buffer.{name.Text()}" });
                return false;
            }
            _properties.SetBuffer(name, buffer);
        }
        return true;
    }

    // The world's buffers sit in every view's table, which a frame in flight's table never sees change
    private void SetWorldBuffers(TableRing table, RenderWorld world)
    {
        RenderWorld.RenderSide render = world.Render;
        WorldLighting lighting = world.Lighting;
        table.Set(s_chunks, 0, frame.Geometry.Chunks.Buffer);
        table.Set(s_transforms, 0, render.Transforms.Buffer);
        table.Set(s_previousTransforms, 0, render.PreviousTransforms.Buffer);
        table.Set(s_objects, 0, render.Attributes.Buffer);
        table.Set(s_materialSlots, 0, render.MaterialSlots.Buffer);
        foreach (SpatialIndex set in world.SpatialSets)
        {
            table.Set(s_spatialRecords, (uint)(set.Slot * 2), set.StaticRecords.Buffer);
            table.Set(s_spatialNodes, (uint)(set.Slot * 3), set.StaticNodes.Buffer);
        }
        table.Set(s_worldLighting, 0, lighting.World.Buffer);
        table.Set(s_objectProbes, 0, world.Probes.ObjectSh);
        table.Set(s_skyAmbientSH, 0, world.Reflections.SkyAmbient);
        table.Set(s_directionals, 0, lighting.Directionals.Buffer);
    }

    // A fullscreen triangle at the far plane with the sky material's slot in the push constants
    public void DrawSky(CommandBuffer cmd)
    {
        WorldFrame world = camera.World;
        if (!camera.Desc.Sky || camera.Desc.Overlay || world.SkyProgram is not ushort id || world.World.Lighting.Environment.Sky is not { IsDisposed: false, Table: { } table } sky)
            return;
        GraphicsProgram? program = frame.Programs.Get(id, false, camera.KeywordSet);
        if (program == null || !BindUserBuffers(program, sky.Shader))
            return;

        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame.Resources.TextureTable);
        cmd.SetDescriptorTable(GraphiteBindings.ViewSet, _viewTable);
        _properties.SetBuffer(GraphiteBindings.MaterialsName, table.Records.Buffer);
        cmd.SetProperties(_properties);
        cmd.SetPushConstants(new FramePipeline.Push { InstanceBase = sky.Slot });
        cmd.Draw(3);
    }

    private void Draw(CommandBuffer cmd, DrawList list, int commandBase)
    {
        uint stride = (uint)Marshal.SizeOf<IndirectDrawIndexedArguments>();
        bool multiDraw = frame.Device.Features.MultiDrawIndirect;
        bool shadow = camera.Desc.Kind == ViewKind.Shadow;
        foreach (DrawCall call in list.Calls)
        {
            frame.CountDraw(shadow, call.CommandCount);
            GraphicsProgram? program = frame.Programs.Get(call.Program, call.Mirrored != camera.Mirrored, camera.KeywordSet);
            if (program == null)
                continue;

            Shader shader = frame.DrawPrograms[call.Program].Shader;
            if (!frame.Materials.Tables.TryGetValue(shader, out MaterialTable? table) || !BindUserBuffers(program, shader))
                continue;

            GeometryPage page = frame.Geometry.Pages[call.Page];
            bool group = cmd.WantsDebugMeta;
            if (group)
                cmd.PushDebugGroup(shader.Name, DebugGroups.DrawCall(camera.World.World, shader, program, list, call));
            cmd.SetShader(program);
            cmd.SetVertexSource(frame.VertexSource(call.Page, call.Topology));
            cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame.Resources.TextureTable);
            cmd.SetDescriptorTable(GraphiteBindings.ViewSet, _viewTable);
            _properties.SetBuffer(GraphiteBindings.MaterialsName, table.Records.Buffer);
            _properties.SetBuffer(GraphiteBindings.VerticesName, page.Vertices);
            cmd.SetProperties(_properties);
            cmd.SetPushConstants(new FramePipeline.Push { DrawRecordBase = (uint)(commandBase + call.FirstCommand) });
            uint offset = (uint)(commandBase + call.FirstCommand) * stride;
            if (multiDraw)
                cmd.DrawIndexedIndirect(camera.Commands.Buffer, offset, (uint)call.CommandCount, stride);
            else
            {
                for (int c = 0; c < call.CommandCount; c++)
                    cmd.DrawIndexedIndirect(camera.Commands.Buffer, offset + (uint)c * stride, 1, stride);
            }
            if (group)
                cmd.PopDebugGroup();
        }
    }
}
