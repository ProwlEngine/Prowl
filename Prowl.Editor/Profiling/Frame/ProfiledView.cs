using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;


public sealed class ProfiledView
{
    public string Name { get; }
    public int RegisteredObjects { get; internal set; }
    public int CulledObjects { get; internal set; }
    public int TotalObjects { get; internal set; }
    public int DrawCallCount { get; internal set; }

    /// <summary>Rendered = not culled. Derived rather than stored separately - it was always exactly
    /// TotalObjects - CulledObjects, just tracked as its own independently-incremented counter before.</summary>
    public int RenderedObjects => TotalObjects - CulledObjects;

    /// <summary>Sum of this view's passes' GpuMilliseconds. Derived rather than cached: the numbers
    /// already live on the passes (which sum their own command buffers, see ProfiledPass), so storing a
    /// rollup here too would just be the same data twice.</summary>
    public double GpuMilliseconds
    {
        get
        {
            double sum = 0.0;
            foreach (ProfiledPass pass in _activePasses)
                sum += pass.GpuMilliseconds;
            return sum;
        }
    }

    /// <summary>Sum of this view's passes' TrianglesDrawn - see GpuMilliseconds above for why this is
    /// derived rather than cached. GPU-reported (VK_QUERY_TYPE_PIPELINE_STATISTICS ClippingPrimitives),
    /// not a CPU estimate - see ProfiledPass.TrianglesDrawn.</summary>
    public ulong TrianglesDrawn
    {
        get
        {
            ulong sum = 0;
            foreach (ProfiledPass pass in _activePasses)
                sum += pass.TrianglesDrawn;
            return sum;
        }
    }

    private readonly Dictionary<int, ProfiledPass> _passes = new();
    private readonly List<ProfiledPass> _activePasses = new();
    private readonly List<PassEdge> _edges = new();
    private bool _touched;

    public IReadOnlyList<ProfiledPass> Passes => _activePasses;
    public IReadOnlyList<PassEdge> Edges => _edges;

    internal ProfiledView(string name)
    {
        Name = name;
    }

    /// <summary>Marks this view as touched for the current frame; returns true the first time this is
    /// called since the last Reset(), so the caller knows whether to add it to an "active this frame"
    /// list.</summary>
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
        _activePasses.Clear();
        _edges.Clear();
        RegisteredObjects = 0;
        CulledObjects = 0;
        TotalObjects = 0;
        DrawCallCount = 0;
        foreach (ProfiledPass pass in _passes.Values)
            pass.Reset();
    }

    public ProfiledPass Pass(int index, string name)
    {
        if (!_passes.TryGetValue(index, out ProfiledPass? pass))
        {
            pass = new ProfiledPass(index, name);
            _passes[index] = pass;
        }
        if (pass.MarkTouched())
            _activePasses.Add(pass);
        return pass;
    }

    public void AddEdge(PassEdge edge) => _edges.Add(edge);

    public void AddObjectCounts(bool registered, bool culled, int drawCallCount)
    {
        RegisteredObjects += registered ? 1 : 0;
        CulledObjects += culled ? 1 : 0;
        TotalObjects += 1;
        DrawCallCount += drawCallCount;
    }

    /// <summary>Overwrites the object counts wholesale - used by SnapshotSerializer, which
    /// deserializes already-final rollups rather than replaying individual Renderable events.</summary>
    internal void SetObjectCounts(int registered, int culled, int total, int drawCalls)
    {
        RegisteredObjects = registered;
        CulledObjects = culled;
        TotalObjects = total;
        DrawCallCount = drawCalls;
    }

    internal ProfiledView Clone()
    {
        var clone = new ProfiledView(Name)
        {
            RegisteredObjects = RegisteredObjects,
            CulledObjects = CulledObjects,
            TotalObjects = TotalObjects,
            DrawCallCount = DrawCallCount,
        };
        clone._edges.AddRange(_edges);
        foreach (ProfiledPass pass in _activePasses)
        {
            ProfiledPass passClone = pass.Clone();
            clone._passes[passClone.Index] = passClone;
            clone._activePasses.Add(passClone);
        }
        return clone;
    }
}