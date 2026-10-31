using System;
using System.Collections.Generic;
using System.Threading.Tasks;


namespace Prowl.Graphite.ShaderDef;


/// <summary>When a variant that is not compiled yet gets compiled.</summary>
public enum CompilePolicy
{
    /// <summary>On the requesting thread, before the request returns.</summary>
    Sync,

    /// <summary>On a worker, the request reporting it pending until done.</summary>
    Async,

    /// <summary>Never: only variants seeded from a snapshot or <see cref="BakedVariants"/> exist.</summary>
    BakedOnly,
}


internal enum VariantState
{
    Ready,
    Pending,
    Failed,
}


/// <summary>
/// A pass's compiled variants for one backend, shared by everything drawing with the pass. Baked variants are seeded in,
/// the rest compile on request under a policy, and failures stay until the pass source or compiler session changes,
/// which drops every variant. Only variants seeded or requested are held. Thread safe.
/// </summary>
internal sealed class PassVariants
{
    private sealed class Slot
    {
        public Variant? Variant;
        public CompileJob? Job;
        public ShaderDescription Compiled;
        public Exception? Failure;
        public ComputeDescription? Compute;
        public CompileJob? ComputeJob;
    }

    private readonly ShaderPass _pass;
    private readonly VariantSpace[] _axes;
    private readonly int _count;
    private readonly Dictionary<string, int> _axisByName = new();
    private readonly Dictionary<string, int>[] _valueIndices;
    private readonly int[] _strides;
    private readonly Dictionary<int, Slot> _slots = new();
    private string _source;
    private MaterialAbi? _material;
    private int _session;
    private int _revision;

    internal PassVariants(ShaderPass pass, VariantSpace[] axes, IShaderCompiler? compiler, GraphicsBackend backend)
    {
        _pass = pass;
        _axes = axes;
        Compiler = compiler;
        Backend = backend;
        _valueIndices = new Dictionary<string, int>[axes.Length];
        _strides = new int[axes.Length];
        long stride = 1;
        for (int i = axes.Length - 1; i >= 0; i--)
        {
            _axisByName[axes[i].Name] = i;
            _strides[i] = (int)stride;
            stride *= axes[i].Values.Count;
            if (stride > int.MaxValue)
                throw new InvalidOperationException($"Pass '{pass.SourceName}' has over {int.MaxValue} variants, too many to key.");
            Dictionary<string, int> values = new();
            for (int v = 0; v < axes[i].Values.Count; v++)
                values[axes[i].Values[v]] = v;
            _valueIndices[i] = values;
        }
        _count = (int)stride;
        _source = pass.Source;
        _material = pass.Material;
        _session = compiler?.SessionVersion ?? 0;
        SeedBaked();
    }

    public ShaderPass Pass => _pass;

    public IShaderCompiler? Compiler { get; }
    public GraphicsBackend Backend { get; }
    public IReadOnlyList<VariantSpace> Axes => _axes;

    /// <summary>The globals the pass reads, from the compiler.</summary>
    public IReadOnlyList<ShaderGlobal> Globals => Compiler?.GetGlobals(_pass) ?? [];

    /// <summary>Moves whenever the pass source, material ABI or compiler session changed and every variant was dropped.</summary>
    public int Revision
    {
        get
        {
            lock (this)
            {
                Refresh();
                return _revision;
            }
        }
    }

    /// <summary>Every combination of the axes, held or not.</summary>
    public int Count => _count;

    /// <summary>The keywords of a key, every axis in slot order.</summary>
    public Keyword[] Combo(int key)
    {
        if ((uint)key >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(key));
        Keyword[] combo = new Keyword[_axes.Length];
        for (int i = 0; i < _axes.Length; i++)
            combo[i] = new Keyword(_axes[i].Name, _axes[i].Values[key / _strides[i] % _axes[i].Values.Count]);
        return combo;
    }

    /// <summary>The key of keywords, unlisted axes at their first value and unknown names skipped. False on an unknown value of a known axis.</summary>
    public bool TryGetKey(ReadOnlySpan<Keyword> keywords, out int key)
    {
        key = 0;
        Span<int> selection = _axes.Length <= 32 ? stackalloc int[_axes.Length] : new int[_axes.Length];
        for (int i = 0; i < keywords.Length; i++)
        {
            if (!_axisByName.TryGetValue(keywords[i].Name, out int slot))
                continue;
            if (!_valueIndices[slot].TryGetValue(keywords[i].Value, out int value))
                return false;
            selection[slot] = value;
        }
        for (int i = 0; i < selection.Length; i++)
            key += selection[i] * _strides[i];
        return true;
    }

    /// <summary>The key of keywords, unlisted axes and unknown values at their axis's first value, unknown names skipped.</summary>
    public int KeyOf(ReadOnlySpan<Keyword> keywords)
    {
        int key = 0;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (_axisByName.TryGetValue(keywords[i].Name, out int slot) && _valueIndices[slot].TryGetValue(keywords[i].Value, out int value))
                key += value * _strides[slot];
        }
        return key;
    }

    /// <summary>Adds baked variants. Ones not naming every axis of the pass are ignored.</summary>
    public void Seed(IEnumerable<Variant> known)
    {
        lock (this)
        {
            foreach (Variant variant in known)
            {
                if (TryGetFullKey(variant.Keywords, out int key))
                    At(key).Variant = variant;
            }
        }
    }

    /// <summary>The variant in its slot, compiled for any backend or none.</summary>
    public Variant? Available(int key)
    {
        lock (this)
            return Find(key)?.Variant;
    }

    /// <summary>The compute variant if it is compiled, without compiling it.</summary>
    public ComputeDescription? CompiledCompute(int key)
    {
        lock (this)
        {
            Refresh();
            return Find(key)?.Compute;
        }
    }

    /// <summary>The variant if it is compiled for the backend, without compiling it.</summary>
    public Variant? Compiled(int key)
    {
        lock (this)
        {
            Refresh();
            return Find(key)?.Variant is { } variant && variant.IsCompiledFor(Backend) ? variant : null;
        }
    }

    public int CompiledCount
    {
        get
        {
            lock (this)
            {
                int count = 0;
                foreach (Slot slot in _slots.Values)
                {
                    if (slot.Variant != null && slot.Variant.IsCompiledFor(Backend))
                        count++;
                }
                return count;
            }
        }
    }

    public int AvailableCount
    {
        get
        {
            lock (this)
            {
                int count = 0;
                foreach (Slot slot in _slots.Values)
                {
                    if (slot.Variant != null)
                        count++;
                }
                return count;
            }
        }
    }

    /// <summary>
    /// The variant's description when ready, compiling it under the policy when it is neither ready nor failed. An Async
    /// request queues the compile at its priority, raising one already queued lower.
    /// A Sync request compiles on the calling thread, or waits for a compile already running. A variant that cannot compile here, BakedOnly or without a compiler, fails this request without
    /// being remembered.
    /// </summary>
    public VariantState Request(int key, CompilePolicy policy, out ShaderDescription description, out Exception? error) => Request(key, policy, CompilePriority.Visible, out description, out error);

    /// <inheritdoc cref="Request(int, CompilePolicy, out ShaderDescription, out Exception?)"/>
    public VariantState Request(int key, CompilePolicy policy, CompilePriority priority, out ShaderDescription description, out Exception? error)
    {
        while (true)
        {
            description = default;
            error = null;
            CompileJob? run = null;
            Task? wait = null;
            VariantState state;
            lock (this)
            {
                Refresh();
                Slot slot = At(key);
                state = Poll(key, slot);
                if (state == VariantState.Pending && slot.Job is { } job)
                {
                    if (policy == CompilePolicy.Sync)
                    {
                        job.Raise(CompilePriority.Fallback);
                        wait = job.Done;
                    }
                    else
                    {
                        job.Raise(priority);
                    }
                }
                else if (state == VariantState.Pending)
                {
                    if (policy == CompilePolicy.BakedOnly || Compiler == null)
                    {
                        error = new InvalidOperationException($"The variant of pass '{_pass.Name}' is not compiled for backend {Backend} and cannot be compiled here.");
                        state = VariantState.Failed;
                    }
                    else
                    {
                        IShaderCompiler compiler = Compiler;
                        Keyword[] combo = Combo(key);
                        void Work() => slot.Compiled = compiler.Compile(_pass, combo, Backend);
                        if (policy == CompilePolicy.Sync)
                            slot.Job = run = new CompileJob(Work, priority);
                        else
                            Queue(key, slot, Work, priority);
                    }
                }
                if (state == VariantState.Ready)
                    slot.Variant!.TryGetDescription(Backend, out description);
                else if (state == VariantState.Failed)
                    error ??= slot.Failure;
            }
            run?.Run();
            wait ??= run?.Done;
            if (wait == null)
                return state;
            try
            {
                wait.Wait();
            }
            catch (AggregateException)
            {
            }
        }
    }

    /// <summary>The variant's description, compiled now when it is not ready. Throws when it failed.</summary>
    public ShaderDescription Require(int key = 0)
    {
        if (Request(key, CompilePolicy.Sync, out ShaderDescription description, out Exception? error) == VariantState.Ready)
            return description;
        throw new InvalidOperationException($"The variant of pass '{_pass.SourceName}' failed to compile: {error?.Message}", error);
    }

    /// <summary>
    /// The variant compiled as a compute program, now when it is not compiled yet, waiting for a compile already queued.
    /// Throws when it failed.
    /// </summary>
    public ComputeDescription RequireCompute(int key = 0)
    {
        while (true)
        {
            IShaderCompiler? compiler = null;
            Task? queued = null;
            int revision;
            lock (this)
            {
                Refresh();
                Slot slot = At(key);
                if (slot.Compute is { } compiled)
                    return compiled;
                if (slot.Failure is { } failure)
                    throw new InvalidOperationException($"The compute variant of pass '{_pass.SourceName}' failed to compile: {failure.Message}", failure);
                if (slot.ComputeJob is { } job)
                {
                    job.Raise(CompilePriority.Fallback);
                    queued = job.Done;
                }
                else
                {
                    compiler = Compiler ?? throw new InvalidOperationException($"The compute variant of pass '{_pass.SourceName}' is not compiled and cannot be compiled here.");
                }
                revision = _revision;
            }
            if (compiler != null)
                return CompileCompute(compiler, key, revision);
            try
            {
                queued!.Wait();
            }
            catch (AggregateException)
            {
            }
        }
    }

    /// <summary>Starts compiling the variant as a compute program on a worker, unless it is compiled, failed, queued or cannot compile here.</summary>
    public void QueueCompute(int key, CompilePriority priority)
    {
        lock (this)
        {
            Refresh();
            Slot slot = At(key);
            if (slot.Compute != null || slot.Failure != null || Compiler is not { } compiler)
                return;
            if (slot.ComputeJob is { } queued)
            {
                queued.Raise(priority);
                return;
            }
            int revision = _revision;
            slot.ComputeJob = CompileQueue.Enqueue(() => CompileCompute(compiler, key, revision), priority);
        }
    }

    // Compiles a compute variant, kept unless the pass changed meanwhile
    private ComputeDescription CompileCompute(IShaderCompiler compiler, int key, int revision)
    {
        ComputeDescription description;
        try
        {
            description = compiler.CompileCompute(_pass, Combo(key), Backend);
        }
        catch (Exception e)
        {
            lock (this)
            {
                if (revision == _revision)
                {
                    At(key).Failure = e;
                    At(key).ComputeJob = null;
                }
            }
            throw;
        }
        lock (this)
        {
            if (revision == _revision)
            {
                At(key).Compute = description;
                At(key).ComputeJob = null;
            }
        }
        return description;
    }

    /// <summary>Waits for every variant compiling on a worker.</summary>
    public void WaitPending()
    {
        List<Task> pending = new();
        lock (this)
        {
            foreach (Slot slot in _slots.Values)
            {
                if (slot.Job != null)
                    pending.Add(slot.Job.Done);
            }
        }
        foreach (Task task in pending)
        {
            try
            {
                task.Wait();
            }
            catch (AggregateException)
            {
            }
        }
    }

    internal PassSnapshot Snapshot()
    {
        lock (this)
        {
            List<Variant> present = new();
            foreach (Slot slot in _slots.Values)
            {
                if (slot.Variant != null)
                    present.Add(slot.Variant);
            }
            return new PassSnapshot { Axes = _axes, Variants = present.ToArray() };
        }
    }

    // A finished compile is taken in
    private VariantState Poll(int key, Slot slot)
    {
        if (slot.Variant is { } variant && variant.IsCompiledFor(Backend))
            return VariantState.Ready;
        if (slot.Failure != null)
            return VariantState.Failed;
        if (slot.Job is not { } job || !job.IsCompleted)
            return VariantState.Pending;
        slot.Job = null;
        if (job.Done.IsCanceled)
            return VariantState.Pending;
        if (job.Done.IsFaulted)
        {
            slot.Failure = job.Done.Exception?.InnerException ?? job.Done.Exception;
            return VariantState.Failed;
        }
        Store(key, slot, slot.Compiled);
        slot.Compiled = default;
        return VariantState.Ready;
    }

    // A queued compile takes its result in as soon as it is done, unasked
    private void Queue(int key, Slot slot, Action work, CompilePriority priority)
    {
        slot.Job = CompileQueue.Enqueue(work, priority);
        slot.Job.Done.ContinueWith(_ =>
        {
            lock (this)
            {
                if (_slots.GetValueOrDefault(key) == slot)
                    Poll(key, slot);
            }
        }, TaskScheduler.Default);
    }

    // A new variant every time, so a baked one seeded in is never changed and seeds again as it shipped
    private void Store(int key, Slot slot, ShaderDescription description)
    {
        List<(GraphicsBackend, ShaderDescription)> compiled = new();
        if (slot.Variant is { } existing)
        {
            foreach ((GraphicsBackend backend, ShaderDescription other) in existing.Compiled)
            {
                if (backend != Backend)
                    compiled.Add((backend, other));
            }
        }
        compiled.Add((Backend, description));
        slot.Variant = new Variant(Combo(key), [.. compiled]);
    }

    private Slot? Find(int key) => _slots.GetValueOrDefault(key);

    private Slot At(int key)
    {
        if (!_slots.TryGetValue(key, out Slot? slot))
        {
            if ((uint)key >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(key));
            _slots[key] = slot = new Slot();
        }
        return slot;
    }

    // A changed source, material ABI or compiler session drops what was compiled from the old one
    private void Refresh()
    {
        int session = Compiler?.SessionVersion ?? 0;
        if (string.Equals(_source, _pass.Source, StringComparison.Ordinal) && _material == _pass.Material && session == _session)
            return;
        _source = _pass.Source;
        _material = _pass.Material;
        _session = session;
        foreach (Slot slot in _slots.Values)
        {
            slot.Job?.Cancel();
            slot.ComputeJob?.Cancel();
        }
        _slots.Clear();
        _revision++;
        SeedBaked();
    }

    private void SeedBaked()
    {
        if ((Compiler as BakedVariants)?.Find(_pass) is not { } baked)
            return;
        foreach (Variant variant in baked.Variants)
        {
            if (TryGetFullKey(variant.Keywords, out int key))
                At(key).Variant = variant;
        }
        foreach (((GraphicsBackend backend, VariantKey key), ComputeDescription description) in baked.Compute)
        {
            if (backend == Backend && (uint)key.Index < (uint)_count)
                At(key.Index).Compute = description;
        }
    }

    private bool TryGetFullKey(Keyword[] keywords, out int key)
    {
        key = 0;
        int seen = 0;
        foreach (Keyword keyword in keywords)
        {
            if (!_axisByName.TryGetValue(keyword.Name, out int slot) || !_valueIndices[slot].TryGetValue(keyword.Value, out int value))
                return false;
            key += value * _strides[slot];
            seen++;
        }
        return seen == _axes.Length;
    }
}
