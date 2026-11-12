using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Fills the commands the GPU cull owns in every view culled this frame: counts each command's instances, sums the
/// counts into where each command's instances start, then writes them, see Graphite/Internal/Cull.glsl. After the
/// upload and before the views.
/// </summary>
internal sealed class GpuCullPass(InternalCompute program, GeometryPool geometry) : IDisposable
{
    private static readonly PropertyID s_objects = "Cull_Objects", s_shapes = "Cull_Shapes", s_bounds = "Cull_Bounds",
        s_transforms = "Cull_Transforms", s_chunks = "Cull_Chunks", s_view = "Cull_View", s_maps = "Cull_Maps", s_commands = "Cull_Commands",
        s_records = "Cull_Records", s_instances = "Cull_Instances", s_cursors = "Cull_Cursors", s_state = "Cull_State", s_results = "Cull_Results";

    private const uint MaxGroups = 65535;

    private readonly PropertySet _properties = new();
    private readonly HashSet<DeviceBuffer> _declared = new();

    /// <summary>The views whose lists the GPU cull fills this frame.</summary>
    public readonly List<CameraFrame> Cameras = new();

    public void Add(FrameGraph graph)
    {
        if (Cameras.Count == 0)
            return;
        graph.AddPass("Graphite.GpuCull", builder =>
        {
            _declared.Clear();
            void Read(DeviceBuffer buffer)
            {
                if (_declared.Add(buffer))
                    builder.Read(graph.Import(buffer), BufferAccess.ShaderRead);
            }
            void Write(DeviceBuffer buffer)
            {
                if (_declared.Add(buffer))
                    builder.Write(graph.Import(buffer), BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
            }

            Read(geometry.Chunks.Buffer);
            foreach (CameraFrame camera in Cameras)
            {
                RenderWorld.RenderSide world = camera.World.World.Render;
                Read(world.CullObjects.Buffer);
                Read(world.CullShapes.Buffer);
                Read(world.Bounds.Buffer);
                Read(world.Transforms.Buffer);
                Read(camera.CullView.Buffer);
                Read(camera.CullMaps.Buffer);
                Write(camera.Commands.Buffer);
                Write(camera.Records.Buffer);
                Write(camera.Instances.Buffer);
                Write(camera.CullCursors.Buffer);
                Write(camera.CullResults.Buffer);
                if (camera.CullState != null)
                    Write(camera.CullState);
            }
        }, Render);
    }

    private void Render(PassContext context)
    {
        CommandBuffer cmd = context.Command;
        cmd.SetComputeShader(program.Program);
        foreach (CameraFrame camera in Cameras)
        {
            RenderWorld.RenderSide world = camera.World.World.Render;
            _properties.SetBuffer(s_objects, world.CullObjects.Buffer);
            _properties.SetBuffer(s_shapes, world.CullShapes.Buffer);
            _properties.SetBuffer(s_bounds, world.Bounds.Buffer);
            _properties.SetBuffer(s_transforms, world.Transforms.Buffer);
            _properties.SetBuffer(s_chunks, geometry.Chunks.Buffer);
            _properties.SetBuffer(s_view, camera.CullView.Buffer);
            _properties.SetBuffer(s_maps, camera.CullMaps.Buffer);
            _properties.SetBuffer(s_commands, camera.Commands.Buffer);
            _properties.SetBuffer(s_records, camera.Records.Buffer);
            _properties.SetBuffer(s_instances, camera.Instances.Buffer);
            _properties.SetBuffer(s_cursors, camera.CullCursors.Buffer);
            _properties.SetBuffer(s_results, camera.CullResults.Buffer);
            _properties.SetBuffer(s_state, camera.CullState ?? camera.CullCursors.Buffer);
            cmd.SetProperties(_properties);

            uint groups = Math.Min((camera.CullObjects + 63) / 64, MaxGroups);
            Dispatch(cmd, 0, groups);
            cmd.ComputeBarrier();
            Dispatch(cmd, 1, 1);
            cmd.ComputeBarrier();
            Dispatch(cmd, 2, groups);
        }
    }

    private static void Dispatch(CommandBuffer cmd, uint mode, uint groups)
    {
        cmd.SetPushConstants(mode);
        cmd.Dispatch(Math.Max(groups, 1), 1, 1);
    }

    public void Dispose() => program.Dispose();
}
