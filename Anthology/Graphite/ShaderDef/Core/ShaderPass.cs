using System;
using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Render state, id metadata, source shaders, plus the variant set once its ShaderDefinition is created for a device.
/// </summary>
public sealed class ShaderPass
{
    /// <summary>
    /// Pass name, blank if none.
    /// </summary>
    public string Name = "";

    /// <summary>
    /// Tag key-value pairs, from source like <code>{ "Key" = "Value" "Key2" = "Value2" }</code>
    /// </summary>
    public Dictionary<string, string>? Tags = null;

    /// <summary>
    /// Rasterizer, blend, depth, stencil, and other pass state.
    /// </summary>
    public required PassState State;

    /// <summary>
    /// Raw Slang source between SLANGPROGRAM and ENDSLANG. Slang finds its own entrypoints, no stages declared here.
    /// </summary>
    public required string InlineSlang;


    /// <summary>
    /// Raised once per variant when its compile fails. Not raised again until the source or compiler session changes.
    /// </summary>
    public event Action<ShaderPass, Keyword[], Exception>? CompileFailed;


    private GraphicsDevice? _device;
    private GraphicsBackend _backend;
    private IShaderCompiler? _compiler;

    private VariantSpace[] _axes = [];
    private Keyword[][] _combos = [];
    private Dictionary<string, int> _axisByName = new();
    private Dictionary<string, int>[] _valueIndices = [];
    private int[] _strides = [];
    private Variant?[] _variants = [];
    private Variant? _fallback;
    private CompileFailure?[] _failures = [];

    private Dictionary<ProgramKey, GraphicsProgram> _programCache = new();
    private Dictionary<ProgramKey, GraphicsProgram> _fallbackProgramCache = new();
    private bool _created;

    private int _lastKey = -1;
    private PassState? _lastPassState;
    private PassState? _lastOverride;
    private GraphicsProgram? _lastProgram;


    /// <summary>
    /// Binds this pass to a device. fallback is used for unresolved requests, required if compiler is set.
    /// </summary>
    internal void Bind(GraphicsDevice device, VariantSpace[] axes, Variant[] known, IShaderCompiler? compiler, CompileMode mode, Variant? fallback = null)
    {
        _device = device;
        _backend = device.BackendType;
        _compiler = compiler;
        _fallback = fallback;
        _axes = axes;
        _combos = VariantCombos.Generate(axes);
        _variants = new Variant?[_combos.Length];
        _failures = new CompileFailure?[_combos.Length];
        _programCache = new();
        _fallbackProgramCache = new();
        _lastKey = -1;
        _lastProgram = null;

        _axisByName = new();
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

        foreach (Variant variant in known)
        {
            if (TryGetIndex(variant.Keywords, out int index))
                _variants[index] = variant;
        }

        _created = true;

        if (mode == CompileMode.All)
            CompileAll();
    }


    /// <summary>
    /// Variant for a key from GetKey, compiled on demand if a compiler is attached. Key 0 is every axis at its first value.
    /// </summary>
    public Variant GetVariant(int key)
    {
        EnsureCreated();
        if ((uint)key >= (uint)_combos.Length)
            throw new ArgumentOutOfRangeException(nameof(key));

        return Resolve(key);
    }


    /// <summary>
    /// Total variant combos in this pass's axis space.
    /// </summary>
    public int Count { get { EnsureCreated(); return _combos.Length; } }

    /// <summary>
    /// Variants compiled for the device backend.
    /// </summary>
    public int CompiledCount
    {
        get
        {
            EnsureCreated();
            int count = 0;
            foreach (Variant? variant in _variants)
            {
                if (variant is { } v && v.IsCompiledFor(_backend))
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Variant slots populated, any backend.
    /// </summary>
    public int AvailableCount
    {
        get
        {
            EnsureCreated();
            int count = 0;
            foreach (Variant? variant in _variants)
            {
                if (variant is not null)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// True if a compiler is attached.
    /// </summary>
    public bool HasCompiler => _compiler != null;

    /// <summary>
    /// True if every variant slot is populated, any backend.
    /// </summary>
    public bool AllAvailable
    {
        get
        {
            EnsureCreated();
            foreach (Variant? variant in _variants)
            {
                if (variant is null)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// True if every variant is compiled for the device backend, ready to bind with no compiler.
    /// </summary>
    public bool AllCompiled
    {
        get
        {
            EnsureCreated();
            foreach (Variant? variant in _variants)
            {
                if (variant is not { } v || !v.IsCompiledFor(_backend))
                    return false;
            }
            return true;
        }
    }


    /// <summary>
    /// Variant axes of this pass in slot order. Empty until created.
    /// </summary>
    public IReadOnlyList<VariantSpace> Axes { get { EnsureCreated(); return _axes; } }


    /// <summary>
    /// Mixed-radix variant key for keywords, unlisted axes at their first value. Skips unknown names, throws on an unknown value for a known axis. Does not touch the pass.
    /// </summary>
    public int GetKey(ReadOnlySpan<Keyword> keywords)
    {
        EnsureCreated();
        if (keywords.IsEmpty)
            return 0;

        Span<int> selection = _axes.Length <= 32 ? stackalloc int[_axes.Length] : new int[_axes.Length];
        for (int i = 0; i < keywords.Length; i++)
        {
            if (!_axisByName.TryGetValue(keywords[i].Name, out int slot))
                continue;

            if (!_valueIndices[slot].TryGetValue(keywords[i].Value, out int value))
                throw UnknownKeyword(keywords[i]);

            selection[slot] = value;
        }

        int key = 0;
        for (int i = 0; i < selection.Length; i++)
            key += selection[i] * _strides[i];

        return key;
    }


    /// <summary>
    /// Compiles every variant for the device backend. Needs a compiler attached.
    /// </summary>
    public void CompileAll()
    {
        EnsureCreated();
        if (_compiler == null)
            throw new InvalidOperationException($"CompileAll on pass '{Name}' requires an attached compiler.");

        for (int i = 0; i < _combos.Length; i++)
            Compile(i);
    }


    internal PassSnapshot Snapshot()
    {
        EnsureCreated();
        List<Variant> present = new();
        for (int i = 0; i < _variants.Length; i++)
        {
            if (_variants[i] != null)
                present.Add(_variants[i]!);
        }

        return new PassSnapshot { Axes = _axes, Variants = present.ToArray() };
    }


    internal ShaderDescription GetDescription(int key)
    {
        EnsureCreated();
        if ((uint)key >= (uint)_combos.Length)
            throw new ArgumentOutOfRangeException(nameof(key));

        if (!Resolve(key).TryGetDescription(_backend, out ShaderDescription description))
            throw new InvalidOperationException($"The variant of pass '{Name}' is not compiled for backend {_backend} and no compiler is attached.");

        return description;
    }


    internal GraphicsProgram ResolveProgram(int key, BlendStateDescription baseBlend, DepthStencilStateDescription baseDepth, RasterizerStateDescription baseRaster)
        => ResolveProgram(key, State, baseBlend, baseDepth, baseRaster);


    internal GraphicsProgram ResolveProgram(int key, PassState state, BlendStateDescription baseBlend, DepthStencilStateDescription baseDepth, RasterizerStateDescription baseRaster)
    {
        EnsureCreated();

        return GetOrCreateProgram(key, state.ToBlendState(baseBlend), state.ToDepthStencilState(baseDepth), state.ToRasterizerState(baseRaster));
    }


    internal GraphicsProgram ResolveDefaultProgram(int key, PassState? overrideState = null)
    {
        EnsureCreated();

        if (_lastProgram != null && key == _lastKey && State.Equals(_lastPassState) && Equals(overrideState, _lastOverride))
            return _lastProgram;

        PassState effective = overrideState is null ? State : overrideState.Apply(State);
        GraphicsProgram program = GetOrCreateProgram(
            key,
            effective.ToBlendState(CommandBufferExtensions.DefaultBlend),
            effective.ToDepthStencilState(CommandBufferExtensions.DefaultDepth),
            effective.ToRasterizerState(CommandBufferExtensions.DefaultRaster));

        if (_variants[key] is { } resolved && resolved.IsCompiledFor(_backend))
        {
            _lastKey = key;
            _lastPassState = State.Clone();
            _lastOverride = overrideState?.Clone();
            _lastProgram = program;
        }

        return program;
    }


    private GraphicsProgram GetOrCreateProgram(int key, BlendStateDescription blend, DepthStencilStateDescription depth, RasterizerStateDescription raster)
    {
        ProgramKey programKey = new(key, blend, depth, raster);

        if (_programCache.TryGetValue(programKey, out GraphicsProgram? cached))
            return cached;

        Variant variant = Resolve(key);
        bool isFallback = ReferenceEquals(variant, _fallback);

        if (isFallback && _fallbackProgramCache.TryGetValue(programKey, out GraphicsProgram? cachedFallback))
            return cachedFallback;

        if (!variant.TryGetDescription(_backend, out ShaderDescription description))
            throw new InvalidOperationException($"The variant of pass '{Name}' is not compiled for backend {_backend} and no compiler is attached.");

        description.BlendState = blend;
        description.DepthStencilState = depth;
        description.RasterizerState = raster;

        GraphicsProgram program = _device!.ResourceFactory.CreateGraphicsProgram(description);
        (isFallback ? _fallbackProgramCache : _programCache)[programKey] = program;
        return program;
    }


    private bool TryGetIndex(Keyword[] keywords, out int index)
    {
        index = 0;
        int seen = 0;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (!_axisByName.TryGetValue(keywords[i].Name, out int slot) || !_valueIndices[slot].TryGetValue(keywords[i].Value, out int value))
                return false;

            index += value * _strides[slot];
            seen++;
        }

        return seen == _axes.Length;
    }


    /// <summary>
    /// Resolves variant at index: use existing compiled one, or compile fresh. Falls back to fallback variant on no compiler or compile failure, if fallback has a compiled description for this backend.
    /// </summary>
    private Variant Resolve(int index)
    {
        Variant? existing = _variants[index];
        if (existing != null && existing.IsCompiledFor(_backend))
            return existing;

        if (_compiler != null)
        {
            CompileFailure? failure = _failures[index];
            if (failure == null || failure.SessionVersion != _compiler.SessionVersion || !string.Equals(failure.Source, InlineSlang, StringComparison.Ordinal))
            {
                try
                {
                    Variant compiled = Compile(index);
                    _failures[index] = null;
                    return compiled;
                }
                catch (Exception exception)
                {
                    _failures[index] = failure = new CompileFailure(InlineSlang, _compiler.SessionVersion, exception);
                    CompileFailed?.Invoke(this, _combos[index], exception);
                }
            }

            if (existing == null && !(_fallback != null && _fallback.IsCompiledFor(_backend)))
                throw new InvalidOperationException($"The variant of pass '{Name}' failed to compile and no fallback variant is available.", failure!.Exception);
        }

        if (existing != null)
            return existing;

        if (_fallback != null && _fallback.IsCompiledFor(_backend))
            return _fallback;

        throw new InvalidOperationException($"The variant of pass '{Name}' is not compiled for backend {_backend}, no compiler is attached, and no fallback variant is available.");
    }


    private Variant Compile(int index)
    {
        Variant? existing = _variants[index];
        if (existing != null && existing.IsCompiledFor(_backend))
            return existing;

        ShaderDescription description = _compiler!.Compile(this, _combos[index], _backend);

        if (existing == null)
        {
            existing = new Variant(_combos[index], [(_backend, description)]);
            _variants[index] = existing;
        }
        else
        {
            existing.Store(_backend, description);
        }

        return existing;
    }


    private void EnsureCreated()
    {
        if (!_created)
            throw new InvalidOperationException($"Pass '{Name}' has not been created. Call ShaderDefinition.Create first.");
    }


    private ArgumentException UnknownKeyword(Keyword keyword)
        => new($"Keyword '{keyword.Name}={keyword.Value}' is not a known axis or value of pass '{Name}'.");


    private sealed record CompileFailure(string Source, int SessionVersion, Exception Exception);


    private readonly record struct ProgramKey(int VariantIndex, BlendStateDescription Blend, DepthStencilStateDescription Depth, RasterizerStateDescription Raster);
}
