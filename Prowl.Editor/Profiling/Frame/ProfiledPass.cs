using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;


public sealed class ProfiledPass
{
    public int Index { get; }
    public string Name { get; }

    public IReadOnlyList<ResourceRef> Inputs => _inputs;
    public IReadOnlyList<ResourceRef> Outputs => _outputs;
    public IReadOnlyList<ProfiledCommandBuffer> CommandBuffers => _activeCommandBuffers;

    public int RegisteredObjects { get; internal set; }
    public int CulledObjects { get; internal set; }
    public int TotalObjects { get; internal set; }
    public int DrawCallCount { get; internal set; }

    /// <summary>Rendered = not culled - see ProfiledView.RenderedObjects for why this is derived.</summary>
    public int RenderedObjects => TotalObjects - CulledObjects;

    /// <summary>Sum of this pass's command buffers' GpuMilliseconds - see ProfiledView.GpuMilliseconds
    /// for why this is derived instead of a separately-stored rollup.</summary>
    public double GpuMilliseconds
    {
        get
        {
            double sum = 0.0;
            foreach (ProfiledCommandBuffer cb in _activeCommandBuffers)
                sum += cb.GpuMilliseconds;
            return sum;
        }
    }


    public ulong TrianglesDrawn
    {
        get
        {
            ulong sum = 0;
            foreach (ProfiledCommandBuffer cb in _activeCommandBuffers)
                sum += cb.ClippingPrimitives;
            return sum;
        }
    }


    public int PipelineSwitchCount
    {
        get
        {
            int sum = 0;
            foreach (ProfiledCommandBuffer cb in _activeCommandBuffers)
                sum += cb.PipelineSwitchCount;
            return sum;
        }
    }

    private readonly List<ResourceRef> _inputs = new();
    private readonly List<ResourceRef> _outputs = new();

    // CommandBuffer.Id is constantly-incrementing so it must be treated differently than the other ids.
    private readonly Dictionary<ulong, ProfiledCommandBuffer> _commandBuffers = new();
    private readonly List<ProfiledCommandBuffer> _activeCommandBuffers = new();
    private readonly List<ProfiledCommandBuffer> _commandBufferPool = new();
    private bool _touched;

    internal ProfiledPass(int index, string name)
    {
        Index = index;
        Name = name;
    }

    internal bool MarkTouched()
    {
        if (_touched)
            return false;
        _touched = true;
        return true;
    }

    internal void Reset()
    {
        _touched = false;
        _inputs.Clear();
        _outputs.Clear();
        _commandBufferPool.AddRange(_activeCommandBuffers);
        _commandBuffers.Clear();
        _activeCommandBuffers.Clear();
        RegisteredObjects = 0;
        CulledObjects = 0;
        TotalObjects = 0;
        DrawCallCount = 0;
    }

    public void AddObjectCounts(bool registered, bool culled, int drawCallCount)
    {
        RegisteredObjects += registered ? 1 : 0;
        CulledObjects += culled ? 1 : 0;
        TotalObjects += 1;
        DrawCallCount += drawCallCount;
    }

    /// <summary>Overwrites the object counts wholesale - see ProfiledView.SetObjectCounts, used the
    /// same way by SnapshotSerializer.</summary>
    internal void SetObjectCounts(int registered, int culled, int total, int drawCalls)
    {
        RegisteredObjects = registered;
        CulledObjects = culled;
        TotalObjects = total;
        DrawCallCount = drawCalls;
    }

    internal void AddInputPlaceholder(ResourceRef r) => _inputs.Add(r);
    internal void AddOutputPlaceholder(ResourceRef r) => _outputs.Add(r);
    internal bool HasInput(uint id) => ContainsId(_inputs, id);
    internal bool HasOutput(uint id) => ContainsId(_outputs, id);
    internal ResourceRef UpsertInput(uint id, string name, ResourceRefKind kind, SnapshotResourceID resource) => Upsert(_inputs, id, name, kind, resource);
    internal ResourceRef UpsertOutput(uint id, string name, ResourceRefKind kind, SnapshotResourceID resource) => Upsert(_outputs, id, name, kind, resource);

    private static bool ContainsId(List<ResourceRef> refs, uint id)
    {
        foreach (ResourceRef r in refs)
            if (r.Id == id)
                return true;
        return false;
    }

    private static ResourceRef Upsert(List<ResourceRef> refs, uint id, string name, ResourceRefKind kind, SnapshotResourceID resource)
    {
        for (int i = 0; i < refs.Count; i++)
        {
            if (refs[i].Id == id)
            {
                var updated = new ResourceRef(id, name, kind, resource);
                refs[i] = updated;
                return updated;
            }
        }
        return default;
    }

    /// <summary>Overwrites the resource lists wholesale - used by SnapshotSerializer, which
    /// deserializes already-resolved refs rather than replaying placeholder-then-upsert events.</summary>
    internal void SetResources(IReadOnlyList<ResourceRef> inputs, IReadOnlyList<ResourceRef> outputs)
    {
        _inputs.Clear();
        _inputs.AddRange(inputs);
        _outputs.Clear();
        _outputs.AddRange(outputs);
    }

    public ProfiledCommandBuffer CommandBuffer(ulong id, string name)
    {
        if (!_commandBuffers.TryGetValue(id, out ProfiledCommandBuffer? cb))
        {
            if (_commandBufferPool.Count > 0)
            {
                cb = _commandBufferPool[^1];
                _commandBufferPool.RemoveAt(_commandBufferPool.Count - 1);
                cb.ResetForReuse(id, name);
            }
            else
            {
                cb = new ProfiledCommandBuffer(id, name);
            }
            _commandBuffers[id] = cb;
            _activeCommandBuffers.Add(cb);
        }
        else if (!string.IsNullOrEmpty(name))
        {
            cb.SetName(name);
        }
        return cb;
    }

    internal ProfiledPass Clone()
    {
        var clone = new ProfiledPass(Index, Name)
        {
            RegisteredObjects = RegisteredObjects,
            CulledObjects = CulledObjects,
            TotalObjects = TotalObjects,
            DrawCallCount = DrawCallCount,
        };
        clone._inputs.AddRange(_inputs);
        clone._outputs.AddRange(_outputs);
        foreach (ProfiledCommandBuffer cb in _activeCommandBuffers)
        {
            ProfiledCommandBuffer cbClone = cb.Clone();
            clone._commandBuffers[cbClone.Id] = cbClone;
            clone._activeCommandBuffers.Add(cbClone);
        }
        return clone;
    }
}