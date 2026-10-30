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
            camera.ViewTable.Set("Graphite_Globals", 0, new DeviceBufferRange(globals.Buffer, globals.Offset, globals.Size));
        }
        camera.ViewTable.Set("Graphite_Views", 0, camera.ViewBlocks.Buffer);
        camera.ViewTable.Set("Graphite_SpatialRoots", 0, camera.SpatialRoots.Buffer);
        for (int slot = 0; slot < camera.Spatial.Length; slot++)
        {
            if (camera.Spatial[slot] is not { } view)
                continue;
            camera.ViewTable.Set("Graphite_SpatialRecords", (uint)(slot * 2 + 1), view.Records.Buffer);
            camera.ViewTable.Set("Graphite_SpatialNodes", (uint)(slot * 3 + 1), view.Nodes.Buffer);
            camera.ViewTable.Set("Graphite_SpatialNodes", (uint)(slot * 3 + 2), view.RegionNodes.Buffer);
            camera.ViewTable.Set("Graphite_SpatialRegions", (uint)slot, view.RegionEntries.Buffer);
        }
        SetWorldBuffers(camera.ViewTable, camera.World.World);
        camera.ViewTable.Set("Graphite_ShadowSampler", 0, frame.ShadowSampler);
        camera.ViewTable.Set("Graphite_ShadowFaces", 0, camera.World.Shadows!.Faces.Buffer);
        camera.ViewTable.Set("Graphite_Cascades", 0, camera.Cascades.Buffer);
        camera.ViewTable.Set("Graphite_ScreenVisibility", 0, (object?)camera.ScreenVisibilityView() ?? frame.NoScreenVisibility);
        camera.ViewTable.Set("Graphite_ScreenIrradiance", 0, (object?)camera.ScreenIrradianceView() ?? frame.NoScreenIrradiance);
        camera.ViewTable.Set("Graphite_ScreenSampler", 0, frame.TrilinearClamp);
        camera.ViewTable.Set("Graphite_SceneColor", 0, camera.SceneColor != null ? camera.SceneColorView() : frame.NoSceneColor);
        camera.ViewTable.Set("Graphite_SceneColorSampler", 0, frame.TrilinearClamp);
        camera.ViewTable.Set("Graphite_SceneDepth", 0, camera.SceneDepth != null ? camera.SceneDepthView() : frame.NoSceneDepth);
        camera.ViewTable.Set("Graphite_Dfg", 0, frame.Dfg);
        camera.ViewTable.Set("Graphite_DielectricE", 0, frame.DielectricE);
        camera.ViewTable.Set("Graphite_AnisotropicDfg", 0, frame.AnisotropicDfg);
        camera.ViewTable.Set("Graphite_BackDepth", 0, (object?)camera.BackDepthView() ?? frame.NoSceneDepth);
        camera.ViewTable.Set("Graphite_TableSampler", 0, frame.TrilinearClamp);
        _viewTable = camera.ViewTable.Get(ring);
        _properties.SetBuffer(GraphiteBindings.InstancesName, camera.Instances.Buffer);
        _properties.SetBuffer(GraphiteBindings.DrawRecordsName, camera.Records.Buffer);
    }

    /// <summary>Binds the frame's texture tables and the view's table for a program drawn without draw records.</summary>
    public void BindTables(CommandBuffer cmd)
    {
        cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame.TextureTable);
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
        table.Set("Graphite_Chunks", 0, frame.Geometry.Chunks.Buffer);
        table.Set("Graphite_Transforms", 0, render.Transforms.Buffer);
        table.Set("Graphite_PreviousTransforms", 0, render.PreviousTransforms.Buffer);
        table.Set("Graphite_Objects", 0, render.Attributes.Buffer);
        table.Set("Graphite_MaterialSlots", 0, render.MaterialSlots.Buffer);
        foreach (SpatialIndex set in world.SpatialSets)
        {
            table.Set("Graphite_SpatialRecords", (uint)(set.Slot * 2), set.StaticRecords.Buffer);
            table.Set("Graphite_SpatialNodes", (uint)(set.Slot * 3), set.StaticNodes.Buffer);
        }
        table.Set("Graphite_WorldLighting", 0, lighting.World.Buffer);
        table.Set("Graphite_ObjectProbes", 0, world.Probes.ObjectSh);
        table.Set("Graphite_SkyAmbientSH", 0, world.Reflections.SkyAmbient);
        table.Set("Graphite_Directionals", 0, lighting.Directionals.Buffer);
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
        cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame.TextureTable);
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
            cmd.SetDescriptorTable(GraphiteBindings.FrameSet, frame.TextureTable);
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
