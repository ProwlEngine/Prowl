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

    /// <summary>Never: only variants seeded from a snapshot exist.</summary>
    BakedOnly,
}


public enum VariantState
{
    Ready,
    Pending,
    Failed,
}


/// <summary>
/// A pass's compiled variants for one backend, shared by everything drawing with the pass. Baked variants are seeded in,
/// the rest compile on request under a policy, and failures stay until the pass source or compiler session changes,
/// which drops every variant. Thread safe.
/// </summary>
public sealed class PassVariants
{
    private readonly ShaderPass _pass;
    private readonly VariantSpace[] _axes;
    private readonly Keyword[][] _combos;
    private readonly Dictionary<string, int> _axisByName = new();
    private readonly Dictionary<string, int>[] _valueIndices;
    private readonly int[] _strides;
    private readonly Variant?[] _variants;
    private readonly Task<ShaderDescription>?[] _pending;
    private readonly Exception?[] _failures;
    private readonly ComputeDescription?[] _compute;
    private string _source;
    private int _session;
    private int _revision;

    internal PassVariants(ShaderPass pass, VariantSpace[] axes, IShaderCompiler? compiler, GraphicsBackend backend)
    {
        _pass = pass;
        _axes = axes;
        Compiler = compiler;
        Backend = backend;
        _combos = VariantCombos.Generate(axes);
        _variants = new Variant?[_combos.Length];
        _pending = new Task<ShaderDescription>?[_combos.Length];
        _failures = new Exception?[_combos.Length];
        _compute = new ComputeDescription?[_combos.Length];
        _valueIndices = new Dictionary<string, int>[axes.Length];
        _strides = new int[axes.Length];
        int stride = 1;
        for (int i = axes.Length - 1; i >= 0; i--)
        {
            _axisByName[axes[i].Name] = i;
            _strides[i] = stride;
            stride *= axes[i].Values.Count;
            Dictionary<string, int> values = new();
            for (int v = 0; v < axes[i].Values.Count; v++)
                values[axes[i].Values[v]] = v;
            _valueIndices[i] = values;
        }
        _source = pass.Source;
        _session = compiler?.SessionVersion ?? 0;
        SeedBaked();
    }

    public ShaderPass Pass => _pass;

    public IShaderCompiler? Compiler { get; }
    public GraphicsBackend Backend { get; }
    public IReadOnlyList<VariantSpace> Axes => _axes;

    /// <summary>The globals the pass reads, from the compiler or the baked pass.</summary>
    public IReadOnlyList<string> Globals => Compiler?.GetGlobals(_pass) ?? BakedVariants.Find(_pass)?.Globals ?? [];

    /// <summary>Moves whenever the pass source or compiler session changed and every variant was dropped.</summary>
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
    public int Count => _combos.Length;

    /// <summary>Raised once per variant failure, outside the lock.</summary>
    public event Action<Keyword[], Exception>? CompileFailed;

    public Keyword[] Combo(int key) => _combos[key];

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

    /// <summary>Adds baked variants. Ones not naming every axis of the pass are ignored.</summary>
    public void Seed(IEnumerable<Variant> known)
    {
        lock (this)
        {
            foreach (Variant variant in known)
            {
                if (TryGetFullKey(variant.Keywords, out int key))
                    _variants[key] = variant;
            }
        }
    }

    /// <summary>The variant in its slot, compiled for any backend or none.</summary>
    public Variant? Available(int key)
    {
        lock (this)
            return _variants[key];
    }

    /// <summary>The compute variant if it is compiled, without compiling it.</summary>
    public ComputeDescription? CompiledCompute(int key)
    {
        lock (this)
        {
            Refresh();
            return _compute[key];
        }
    }

    /// <summary>The variant if it is compiled for the backend, without compiling it.</summary>
    public Variant? Compiled(int key)
    {
        lock (this)
        {
            Refresh();
            return _variants[key] is { } variant && variant.IsCompiledFor(Backend) ? variant : null;
        }
    }

    public int CompiledCount
    {
        get
        {
            lock (this)
            {
                int count = 0;
                foreach (Variant? variant in _variants)
                {
                    if (variant != null && variant.IsCompiledFor(Backend))
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
                foreach (Variant? variant in _variants)
                {
                    if (variant != null)
                        count++;
                }
                return count;
            }
        }
    }

    /// <summary>The variant's description when ready, compiling it under the policy when it is neither ready nor failed.</summary>
    public VariantState Request(int key, CompilePolicy policy, out ShaderDescription description, out Exception? error)
    {
        description = default;
        error = null;
        Exception? raised = null;
        VariantState state;
        lock (this)
        {
            Refresh();
            state = Poll(key, ref raised);
            if (state == VariantState.Pending && _pending[key] == null)
            {
                if (policy == CompilePolicy.BakedOnly || Compiler == null)
                {
                    _failures[key] = new InvalidOperationException($"The variant of pass '{_pass.Name}' is not compiled for backend {Backend} and cannot be compiled here.");
                    raised = _failures[key];
                    state = VariantState.Failed;
                }
                else if (policy == CompilePolicy.Sync)
                {
                    try
                    {
                        Store(key, Compiler.Compile(_pass, _combos[key], Backend));
                        state = VariantState.Ready;
                    }
                    catch (Exception e)
                    {
                        _failures[key] = raised = e;
                        state = VariantState.Failed;
                    }
                }
                else
                {
                    IShaderCompiler compiler = Compiler;
                    Keyword[] combo = _combos[key];
                    _pending[key] = Task.Run(() => compiler.Compile(_pass, combo, Backend));
                }
            }
            if (state == VariantState.Ready)
                _variants[key]!.TryGetDescription(Backend, out description);
            else if (state == VariantState.Failed)
                error = _failures[key];
        }
        if (raised != null)
            CompileFailed?.Invoke(_combos[key], raised);
        return state;
    }

    /// <summary>The variant's description, compiled now when it is not ready. Throws when it failed.</summary>
    public ShaderDescription Require(int key = 0)
    {
        if (Request(key, CompilePolicy.Sync, out ShaderDescription description, out Exception? error) == VariantState.Ready)
            return description;
        throw new InvalidOperationException($"The variant of pass '{_pass.SourceName}' failed to compile: {error?.Message}", error);
    }

    /// <summary>The variant compiled as a compute program, now when it is not compiled yet. Throws when it failed.</summary>
    public ComputeDescription RequireCompute(int key = 0)
    {
        lock (this)
        {
            Refresh();
            if (_compute[key] is { } compiled)
                return compiled;
            if (_failures[key] is { } failure)
                throw new InvalidOperationException($"The compute variant of pass '{_pass.SourceName}' failed to compile: {failure.Message}", failure);
            if (Compiler == null)
                throw new InvalidOperationException($"The compute variant of pass '{_pass.SourceName}' is not compiled and cannot be compiled here.");
            try
            {
                ComputeDescription description = Compiler.CompileCompute(_pass, _combos[key], Backend);
                _compute[key] = description;
                return description;
            }
            catch (Exception e)
            {
                _failures[key] = e;
                throw;
            }
        }
    }

    /// <summary>Waits for every variant compiling on a worker.</summary>
    public void WaitPending()
    {
        Task?[] pending;
        lock (this)
            pending = (Task?[])_pending.Clone();
        foreach (Task? task in pending)
        {
            try
            {
                task?.Wait();
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
            foreach (Variant? variant in _variants)
            {
                if (variant != null)
                    present.Add(variant);
            }
            return new PassSnapshot { Axes = _axes, Variants = present.ToArray() };
        }
    }

    // A worker's finished compile is taken in, an unfinished one leaves the variant pending
    private VariantState Poll(int key, ref Exception? raised)
    {
        if (_variants[key] is { } variant && variant.IsCompiledFor(Backend))
            return VariantState.Ready;
        if (_failures[key] != null)
            return VariantState.Failed;
        if (_pending[key] is not { } task)
            return VariantState.Pending;
        if (!task.IsCompleted)
            return VariantState.Pending;
        _pending[key] = null;
        if (task.IsCompletedSuccessfully)
        {
            Store(key, task.Result);
            return VariantState.Ready;
        }
        _failures[key] = raised = task.Exception?.InnerException ?? task.Exception;
        return VariantState.Failed;
    }

    private void Store(int key, ShaderDescription description)
    {
        if (_variants[key] is { } existing)
            existing.Store(Backend, description);
        else
            _variants[key] = new Variant(_combos[key], [(Backend, description)]);
    }

    // A changed source or compiler session drops what was compiled from the old one
    private void Refresh()
    {
        int session = Compiler?.SessionVersion ?? 0;
        if (string.Equals(_source, _pass.Source, StringComparison.Ordinal) && session == _session)
            return;
        _source = _pass.Source;
        _session = session;
        Array.Clear(_variants);
        Array.Clear(_pending);
        Array.Clear(_failures);
        Array.Clear(_compute);
        _revision++;
        SeedBaked();
    }

    private void SeedBaked()
    {
        if (BakedVariants.Find(_pass) is not { } baked)
            return;
        foreach (Variant variant in baked.Variants)
        {
            if (TryGetFullKey(variant.Keywords, out int key))
                _variants[key] = variant;
        }
        foreach (((GraphicsBackend backend, int key), ComputeDescription description) in baked.Compute)
        {
            if (backend == Backend && (uint)key < (uint)_compute.Length)
                _compute[key] = description;
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
