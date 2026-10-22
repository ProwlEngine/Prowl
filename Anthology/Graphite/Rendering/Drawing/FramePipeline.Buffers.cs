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
    private void CollectBuffers(IEnumerable<WorldFrame> worlds)
    {
        _captures.Clear();
        foreach (WorldFrame world in worlds)
            _captures.AddRange(world.Captures());
        BufferSet shared = Buffers.Shared;
        shared.Clear();
        shared.Add(Geometry.Chunks.Persistent);
        shared.Add(Geometry.Meshes.Persistent);
        foreach (GeometryPage page in Geometry.Pages)
        {
            shared.Add(page.VertexBuffer);
            shared.Add(page.IndexBuffer);
        }
        foreach (MaterialTable table in Materials.Tables.Values)
            shared.Add(table.Records.Persistent);
        shared.Add(Globals.Persistent);
        _deform.Worlds.Clear();
        foreach (WorldFrame world in worlds)
        {
            RenderWorld.RenderSide render = world.World.Render;
            BufferSet buffers = world.Buffers;
            buffers.Clear();
            _deform.Worlds.Add(render);
            WorldProbes probes = world.World.Probes;
            buffers.Add(probes.ProbeRecords.Persistent);
            buffers.Add(probes.TetRecords.Persistent);
            buffers.Add(probes.Requests.Persistent);
            buffers.Add(probes.ObjectShBuffer);
            foreach (SpatialIndex set in world.World.SpatialSets)
            {
                if (set.Slot < SpatialIndex.ProbesSet)
                    continue;
                buffers.Add(set.StaticRecords.Persistent);
                buffers.Add(set.StaticNodes.Persistent);
            }
            buffers.Add(world.World.Reflections.SkyAmbient);
            foreach (GpuTable table in render.Tables)
                buffers.Add(table.Persistent);
            WorldLighting lighting = world.World.Lighting;
            foreach (GpuTable table in lighting.Tables)
                buffers.Add(table.Persistent);
            if (world.Shadows != null)
                buffers.Add(world.Shadows.Faces.Persistent);
        }
        foreach (CameraFrame camera in Active())
            camera.CollectBuffers();
    }
}
