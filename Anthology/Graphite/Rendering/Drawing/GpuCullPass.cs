using System;
using System.Collections.Generic;

using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Fills the commands the GPU cull owns in every view culled this frame, one dispatch a view that culls, counts each
/// command's instances and writes them where its record starts them, see Graphite/Internal/Cull.glsl. After the upload
/// and before the views. A view that culls what is hidden fills the rest from its own graph once its early depth is
/// drawn, see <see cref="RecordLate"/>.
/// </summary>
internal sealed class GpuCullPass(LazyCompute program, LazyCompute late, GeometryPool geometry) : IDisposable
{
    private static readonly PropertyID s_objects = "Cull_Objects", s_shapes = "Cull_Shapes", s_bounds = "Cull_Bounds",
        s_transforms = "Cull_Transforms", s_chunks = "Cull_Chunks", s_view = "Cull_View", s_maps = "Cull_Maps", s_commands = "Cull_Commands",
        s_records = "Cull_Records", s_instances = "Cull_Instances", s_state = "Cull_State", s_occlusion = "Cull_Occlusion", s_hiZ = "Cull_HiZ";

    private const uint MaxGroups = 65535;

    private readonly PropertySet _properties = new(), _lateProperties = new();
    private readonly HashSet<DeviceBuffer> _declared = [];

    /// <summary>The views whose lists the GPU cull fills this frame.</summary>
    public readonly List<CameraFrame> Cameras = [];

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
                Read(camera.Records.Buffer);
                Write(camera.Commands.Buffer);
                Write(camera.Instances.Buffer);
                if (camera.CullState != null)
                    Write(camera.CullState);
                if (camera.Occlusion != null)
                    Write(camera.Occlusion);
            }
        }, Render);
    }

    private void Render(PassContext context)
    {
        CommandBuffer cmd = context.Command;
        cmd.SetComputeShader(program.Program);
        foreach (CameraFrame camera in Cameras)
            Dispatch(cmd, camera, _properties);
    }

    /// <summary>
    /// Records the late phase of a view that culls what is hidden, against its farthest depth pyramid, see
    /// Graphite/Internal/CullLate.glsl.
    /// </summary>
    public void RecordLate(CommandBuffer cmd, CameraFrame camera, Texture hiZ, Sampler point)
    {
        cmd.SetComputeShader(late.Program);
        _lateProperties.SetTexture(s_hiZ, hiZ, point);
        Dispatch(cmd, camera, _lateProperties);
    }

    private void Dispatch(CommandBuffer cmd, CameraFrame camera, PropertySet properties)
    {
        RenderWorld.RenderSide world = camera.World.World.Render;
        properties.SetBuffer(s_objects, world.CullObjects.Buffer);
        properties.SetBuffer(s_shapes, world.CullShapes.Buffer);
        properties.SetBuffer(s_bounds, world.Bounds.Buffer);
        properties.SetBuffer(s_transforms, world.Transforms.Buffer);
        properties.SetBuffer(s_chunks, geometry.Chunks.Buffer);
        properties.SetBuffer(s_view, camera.CullView.Buffer);
        properties.SetBuffer(s_maps, camera.CullMaps.Buffer);
        properties.SetBuffer(s_commands, camera.Commands.Buffer);
        properties.SetBuffer(s_records, camera.Records.Buffer);
        properties.SetBuffer(s_instances, camera.Instances.Buffer);
        // A view keeping no LOD state or culling nothing hidden never touches those bindings, which still need a buffer they may write
        properties.SetBuffer(s_state, camera.CullState ?? camera.Instances.Buffer);
        properties.SetBuffer(s_occlusion, camera.Occlusion ?? camera.Instances.Buffer);
        cmd.SetProperties(properties);
        cmd.Dispatch(Math.Clamp((camera.CullObjects + 63) / 64, 1, MaxGroups), 1, 1);
    }

    public void Dispose()
    {
        program.Dispose();
        late.Dispose();
    }
}
