using System;
using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;

/// <summary>
/// A single frame's profiling data. Collectors write directly into this tree as events stream in
/// (no separate build/freeze step) - the object a collector mutates during the frame is the same
/// object readers (history, UI) see afterwards. Owned by a ring slot in EditorProfiler: reused
/// frame-to-frame via Reset(), so nothing here should be treated as safe to hold onto past the next
/// RingSize frames unless it was obtained via Clone() (only done for an explicitly armed capture).
///
/// Node reuse: View (keyed by name) and Pass (keyed by render-graph index) persist across frames instead
/// of being torn down and rebuilt every frame - both keys are structurally stable, so a node that recurs
/// just gets its old object reset in place. Each level keeps two collections: a dictionary that lives
/// forever (so a recurring key finds its old node), and a "this frame" list that gets cleared every
/// Reset() and rebuilt as nodes are touched (so a node that doesn't recur this frame correctly disappears
/// from the frame's data instead of lingering as a stale zeroed-out entry). CommandBuffer is the
/// exception: its id is a global monotonic rental counter (see ProfiledPass), never stable across
/// frames, so it's reused via a plain object pool instead of being keyed by identity.
/// </summary>
public sealed class ProfiledFrame
{
    public long FrameIndex { get; internal set; }
    public double FrameMilliseconds { get; set; }
    public double Fps { get; set; }

    /// <summary>Whether a capture was armed for this frame, i.e. whether PipelineSwitch/CallingObject/
    /// DrawCall depth was built below CommandBuffer. Always true for a Snapshot's cloned frame.</summary>
    public bool HasCaptureDepth { get; internal set; }

    public TimeSample? CpuRoot { get; internal set; }
    public TimeSample? GpuRoot { get; internal set; }

    private CounterValue[] _counters = Array.Empty<CounterValue>();
    private readonly List<SubmitRecord> _submits = new();

    private readonly Dictionary<string, ProfiledView> _views = new();
    private readonly List<ProfiledView> _activeViews = new();

    public IReadOnlyList<CounterValue> Counters => _counters;
    public IReadOnlyList<ProfiledView> Views => _activeViews;
    public IReadOnlyList<SubmitRecord> Submits => _submits;

    internal void Reset(long frameIndex, bool hasCaptureDepth)
    {
        FrameIndex = frameIndex;
        FrameMilliseconds = 0;
        Fps = 0;
        HasCaptureDepth = hasCaptureDepth;
        CpuRoot = null;
        GpuRoot = null;
        _submits.Clear();
        _activeViews.Clear();
        foreach (ProfiledView view in _views.Values)
            view.Reset();
    }

    public void SetCounterValues(double[] values)
    {
        IReadOnlyList<CounterDef> registry = CountersCollector.Registry;
        if (_counters.Length != values.Length)
            _counters = new CounterValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            CounterDef def = registry[i];
            _counters[i] = new CounterValue(def.Name, def.Category, def.Unit, values[i]);
        }
    }

    internal void SetCounters(IReadOnlyList<CounterValue> counters)
    {
        _counters = new CounterValue[counters.Count];
        for (int i = 0; i < counters.Count; i++)
            _counters[i] = counters[i];
    }

    public ProfiledView View(string name)
    {
        if (!_views.TryGetValue(name, out ProfiledView? view))
        {
            view = new ProfiledView(name);
            _views[name] = view;
        }
        if (view.MarkTouched())
            _activeViews.Add(view);
        return view;
    }

    public void SetCpuRoot(TimeSample root) => CpuRoot = root;
    public void SetGpuRoot(TimeSample root) => GpuRoot = root;
    public void AddSubmit(SubmitRecord s) => _submits.Add(s);

    /// <summary>Deep, fully independent copy - the only place this frame's data is ever duplicated.
    /// Used exclusively when a capture is armed, since a Snapshot must survive indefinitely (saved to
    /// disk, held by the user) while this frame's ring slot keeps getting reset and reused.</summary>
    internal ProfiledFrame Clone()
    {
        var clone = new ProfiledFrame
        {
            FrameIndex = FrameIndex,
            FrameMilliseconds = FrameMilliseconds,
            Fps = Fps,
            HasCaptureDepth = HasCaptureDepth,
            CpuRoot = CpuRoot,
            GpuRoot = GpuRoot,
        };
        clone.SetCounters(_counters);
        clone._submits.AddRange(_submits);
        foreach (ProfiledView view in _activeViews)
        {
            ProfiledView viewClone = view.Clone();
            clone._views[viewClone.Name] = viewClone;
            clone._activeViews.Add(viewClone);
        }
        return clone;
    }
}

public sealed class ProfiledView
{
    public string Name { get; }
    public double CpuMilliseconds { get; internal set; }
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
        CpuMilliseconds = 0;
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
    public void SetCpuMilliseconds(double ms) => CpuMilliseconds = ms;

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
            CpuMilliseconds = CpuMilliseconds,
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

public sealed class ProfiledPass
{
    public int Index { get; }
    public string Name { get; }
    public double CpuMilliseconds { get; internal set; }
    public IReadOnlyList<TimeSample> CpuSamples { get; internal set; } = Array.Empty<TimeSample>();

    public IReadOnlyList<ResourceRef> Inputs => _inputs;
    public IReadOnlyList<ResourceRef> Outputs => _outputs;
    public IReadOnlyList<ProfiledCommandBuffer> CommandBuffers => _activeCommandBuffers;

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
        CpuMilliseconds = 0;
        CpuSamples = Array.Empty<TimeSample>();
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

    internal void SetCpuTiming(double ms, IReadOnlyList<TimeSample> samples)
    {
        CpuMilliseconds = ms;
        CpuSamples = samples;
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
            CpuMilliseconds = CpuMilliseconds,
            CpuSamples = CpuSamples,
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

public sealed class ProfiledCommandBuffer
{
    public ulong Id { get; private set; }
    public string Name { get; private set; }
    public double GpuMilliseconds { get; private set; }
    public IReadOnlyList<ProfiledPipelineSwitch> Switches => _switches;

    private readonly List<ProfiledPipelineSwitch> _switches = new();

    internal ProfiledCommandBuffer(ulong id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Repurposes a pooled instance (see ProfiledPass._commandBufferPool) for a new rental -
    /// ids are never reused across frames, so this instance's identity is reassigned wholesale rather
    /// than looked up.</summary>
    internal void ResetForReuse(ulong id, string name)
    {
        Id = id;
        Name = name;
        _switches.Clear();
        GpuMilliseconds = 0;
    }

    internal void SetName(string name) => Name = name;
    public void SetGpuMs(double ms) => GpuMilliseconds = ms;

    /// <summary>Appends an already-built switch - used by SnapshotSerializer.</summary>
    internal void AddSwitchInstance(ProfiledPipelineSwitch sw) => _switches.Add(sw);

    public ProfiledPipelineSwitch AddSwitch(
        string shaderName, bool isCompute, ShaderStages stages,
        string passName, string variant, IReadOnlyDictionary<string, string>? tags, string materialName,
        ProfiledPipelineState? state)
    {
        var sw = new ProfiledPipelineSwitch(shaderName, isCompute, stages, passName, variant, tags, materialName, state);
        _switches.Add(sw);
        return sw;
    }

    internal ProfiledCommandBuffer Clone()
    {
        var clone = new ProfiledCommandBuffer(Id, Name);
        clone.SetGpuMs(GpuMilliseconds);
        foreach (ProfiledPipelineSwitch sw in _switches)
            clone._switches.Add(sw.Clone());
        return clone;
    }
}

/// <summary>
/// Only built below CommandBuffer while a capture is armed - order-based, not identity-based.
/// </summary>
public sealed class ProfiledPipelineSwitch
{
    public string ShaderName { get; }
    public bool IsCompute { get; }
    public ShaderStages Stages { get; }
    public string PassName { get; }
    public string Variant { get; }
    public IReadOnlyDictionary<string, string>? Tags { get; }
    public string MaterialName { get; }
    public ProfiledPipelineState? State { get; }
    public IReadOnlyList<ProfiledCallingObject> Objects => _objects;

    /// <summary>Draws issued under this switch that never correlated to a Renderable event - a
    /// fullscreen blit, a post-process pass, a user-invoked immediate draw, anything outside the
    /// normal culled-object pipeline. See DrawHierarchyCollector.FlushLooseDraws for how these get
    /// separated from Objects' draws.</summary>
    public IReadOnlyList<ProfiledDrawCall> Draws => _draws;

    private readonly List<ProfiledCallingObject> _objects = new();
    private readonly List<ProfiledDrawCall> _draws = new();

    internal ProfiledPipelineSwitch(
        string shaderName, bool isCompute, ShaderStages stages,
        string passName, string variant, IReadOnlyDictionary<string, string>? tags, string materialName,
        ProfiledPipelineState? state)
    {
        ShaderName = shaderName;
        IsCompute = isCompute;
        Stages = stages;
        PassName = passName;
        Variant = variant;
        Tags = tags;
        MaterialName = materialName;
        State = state;
    }

    public ProfiledCallingObject AddObject(
        string label, string materialName, string meshName, int layer, Vector.Float3 position,
        bool registered, bool culled)
    {
        var obj = new ProfiledCallingObject(label, materialName, meshName, layer, position, registered, culled);
        _objects.Add(obj);
        return obj;
    }

    /// <summary>Appends an already-built calling object - used by SnapshotSerializer.</summary>
    internal void AddObjectInstance(ProfiledCallingObject obj) => _objects.Add(obj);

    /// <summary>Records a draw that isn't tied to any calling object - see <see cref="Draws"/>.</summary>
    public void AddDraw(ProfiledDrawCall draw) => _draws.Add(draw);

    internal ProfiledPipelineSwitch Clone()
    {
        var clone = new ProfiledPipelineSwitch(ShaderName, IsCompute, Stages, PassName, Variant, Tags, MaterialName, State);
        foreach (ProfiledCallingObject obj in _objects)
            clone._objects.Add(obj.Clone());
        clone._draws.AddRange(_draws);
        return clone;
    }
}

public sealed class ProfiledCallingObject
{
    public string Label { get; }
    public string MaterialName { get; }
    public string MeshName { get; }
    public int Layer { get; }
    public Vector.Float3 Position { get; }
    public bool Registered { get; }
    public bool Culled { get; }
    public IReadOnlyList<ProfiledDrawCall> Draws => _draws;

    private readonly List<ProfiledDrawCall> _draws = new();

    internal ProfiledCallingObject(
        string label, string materialName, string meshName, int layer, Vector.Float3 position,
        bool registered, bool culled)
    {
        Label = label;
        MaterialName = materialName;
        MeshName = meshName;
        Layer = layer;
        Position = position;
        Registered = registered;
        Culled = culled;
    }

    public void AddDraw(ProfiledDrawCall draw) => _draws.Add(draw);

    internal ProfiledCallingObject Clone()
    {
        var clone = new ProfiledCallingObject(Label, MaterialName, MeshName, Layer, Position, Registered, Culled);
        clone._draws.AddRange(_draws);
        return clone;
    }
}
