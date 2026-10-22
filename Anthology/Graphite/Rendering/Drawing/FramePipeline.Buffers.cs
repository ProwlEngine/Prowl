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
        _buffers.Clear();
        _importedBuffers.Clear();
        _buffers.Add(Geometry.Chunks.Persistent);
        _buffers.Add(Geometry.Meshes.Persistent);
        foreach (GeometryPage page in Geometry.Pages)
        {
            _buffers.Add(page.VertexBuffer);
            _buffers.Add(page.IndexBuffer);
        }
        foreach (MaterialTable table in Materials.Tables.Values)
            _buffers.Add(table.Records.Persistent);
        _buffers.Add(Globals.Persistent);
        _probes.Worlds.Clear();
        _deform.Worlds.Clear();
        foreach (WorldFrame world in worlds)
        {
            RenderWorld.RenderSide render = world.World.Render;
            _deform.Worlds.Add(render);
            WorldProbes probes = world.World.Probes;
            _probes.Worlds.Add(probes);
            _buffers.Add(probes.ProbeRecords.Persistent);
            _buffers.Add(probes.TetRecords.Persistent);
            _buffers.Add(probes.Requests.Persistent);
            _buffers.Add(probes.ObjectShBuffer);
            foreach (SpatialIndex set in world.World.SpatialSets)
            {
                if (set.Slot < SpatialIndex.ProbesSet)
                    continue;
                _buffers.Add(set.StaticRecords.Persistent);
                _buffers.Add(set.StaticNodes.Persistent);
            }
            _importedBuffers.Add(world.World.Reflections.SkyAmbient);

            foreach (GpuTable table in render.Tables)
                _buffers.Add(table.Persistent);
            WorldLighting lighting = world.World.Lighting;
            foreach (GpuTable table in lighting.Tables)
                _buffers.Add(table.Persistent);
        }
        foreach (WorldFrame world in worlds)
        {
            if (world.Shadows != null)
                _buffers.Add(world.Shadows.Faces.Persistent);
        }
        foreach (CameraFrame camera in Active())
            camera.AddBuffers(_buffers);

        if (Same(Buffers, _buffers, _importedBuffers))
            return;

        Buffers.Persistent = [.. _buffers];
        Buffers.Sizes = new uint[_buffers.Count];
        for (int i = 0; i < _buffers.Count; i++)
            Buffers.Sizes[i] = _buffers[i].Description.SizeInBytes;
        Buffers.Imported = [.. _importedBuffers];
        _graphDirty = true;
    }

    private static bool Same(FrameBuffers current, List<PersistentBuffer> buffers, List<DeviceBuffer> imports)
    {
        if (current.Persistent.Length != buffers.Count || current.Imported.Length != imports.Count)
            return false;
        for (int i = 0; i < buffers.Count; i++)
        {
            if (current.Persistent[i] != buffers[i] || buffers[i].Description.SizeInBytes != current.Sizes[i])
                return false;
        }
        for (int i = 0; i < imports.Count; i++)
        {
            if (current.Imported[i] != imports[i])
                return false;
        }
        return true;
    }
}
