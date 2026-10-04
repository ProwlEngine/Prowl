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


    private GraphicsDevice? _device;
    private GraphicsBackend _backend;
    private IShaderCompiler? _compiler;

    private VariantSpace[] _axes = [];
    private Keyword[][] _combos = [];
    private Dictionary<string, int> _axisByName = new();
    private Dictionary<string, int>[] _valueIndices = [];
    private int[] _strides = [];
    private int[] _selection = [];
    private Variant?[] _variants = [];
    private int _activeIndex;
    private Variant? _fallback;

    private Dictionary<ProgramKey, GraphicsProgram> _programCache = new();
    private Dictionary<ProgramKey, GraphicsProgram> _fallbackProgramCache = new();
    private bool _created;


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
        _programCache = new();
        _fallbackProgramCache = new();

        _axisByName = new();
        _valueIndices = new Dictionary<string, int>[axes.Length];
        _strides = new int[axes.Length];
        _selection = new int[axes.Length];

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

        _activeIndex = 0;

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
    /// Current variant, compiled on demand if a compiler is attached. Only valid after create.
    /// </summary>
    public Variant ActiveVariant
    {
        get
        {
            EnsureCreated();
            return Resolve(_activeIndex);
        }
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
    /// Restores every axis to its first value (combo 0) and makes that the active variant. No reselect needed.
    /// </summary>
    public void ResetKeywords()
    {
        EnsureCreated();
        Array.Clear(_selection);
        _activeIndex = 0;
    }


    /// <summary>
    /// Sets every keyword whose name is an axis here, silently skipping unknown names, then re-resolves once. Throws if a known axis gets an unknown value. Returns how many applied.
    /// </summary>
    public int ApplyKeywords(ReadOnlySpan<Keyword> keywords)
    {
        EnsureCreated();
        for (int i = 0; i < keywords.Length; i++)
        {
            if (_axisByName.TryGetValue(keywords[i].Name, out int slot) && !_valueIndices[slot].ContainsKey(keywords[i].Value))
                throw UnknownKeyword(keywords[i]);
        }

        int applied = 0;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (TrySelect(keywords[i]))
                applied++;
        }

        if (applied > 0)
            Reselect();

        return applied;
    }


    /// <summary>
    /// Sets a keyword and re-resolves the active variant. Throws if the name isn't a variant axis here or the value isn't one of its values.
    /// </summary>
    public void SetKeyword(Keyword keyword)
    {
        EnsureCreated();
        if (!TrySelect(keyword))
            throw UnknownKeyword(keyword);

        Reselect();
    }


    /// <summary>
    /// Sets several keywords atomically and re-resolves the active variant. Validates all first, throws if any name or value is unknown here.
    /// </summary>
    public void SetKeywords(params Keyword[] keywords)
    {
        EnsureCreated();
        for (int i = 0; i < keywords.Length; i++)
        {
            if (!IsKnown(keywords[i]))
                throw UnknownKeyword(keywords[i]);
        }

        for (int i = 0; i < keywords.Length; i++)
            TrySelect(keywords[i]);

        Reselect();
    }


    /// <summary>
    /// Sets a keyword if its name and value are known, re-resolves active variant. Returns false and does nothing if unknown.
    /// </summary>
    public bool TrySetKeyword(Keyword keyword)
    {
        EnsureCreated();
        if (!TrySelect(keyword))
            return false;

        Reselect();
        return true;
    }


    /// <summary>
    /// Sets several keywords if all names and values are known, re-resolves active variant. Returns false and does nothing if any is unknown.
    /// </summary>
    public bool TrySetKeywords(params Keyword[] keywords)
    {
        EnsureCreated();
        for (int i = 0; i < keywords.Length; i++)
        {
            if (!IsKnown(keywords[i]))
                return false;
        }

        for (int i = 0; i < keywords.Length; i++)
            TrySelect(keywords[i]);

        Reselect();
        return true;
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


    internal GraphicsProgram ResolveProgram(BlendStateDescription baseBlend, DepthStencilStateDescription baseDepth, RasterizerStateDescription baseRaster)
        => ResolveProgram(State, baseBlend, baseDepth, baseRaster);


    internal GraphicsProgram ResolveProgram(PassState state, BlendStateDescription baseBlend, DepthStencilStateDescription baseDepth, RasterizerStateDescription baseRaster)
    {
        EnsureCreated();

        BlendStateDescription blend = state.ToBlendState(baseBlend);
        DepthStencilStateDescription depth = state.ToDepthStencilState(baseDepth);
        RasterizerStateDescription raster = state.ToRasterizerState(baseRaster);
        ProgramKey key = new(_activeIndex, blend, depth, raster);

        if (_programCache.TryGetValue(key, out GraphicsProgram? cached))
            return cached;

        Variant variant = Resolve(_activeIndex);
        bool isFallback = ReferenceEquals(variant, _fallback);

        // While degraded to the fallback, keep re-resolving (Resolve retries the real compile every
        // call) but reuse the fallback GraphicsProgram instead of rebuilding it every request.
        if (isFallback && _fallbackProgramCache.TryGetValue(key, out GraphicsProgram? cachedFallback))
            return cachedFallback;

        if (!variant.TryGetDescription(_backend, out ShaderDescription description))
            throw new InvalidOperationException($"The active variant of pass '{Name}' is not compiled for backend {_backend} and no compiler is attached.");

        description.BlendState = blend;
        description.DepthStencilState = depth;
        description.RasterizerState = raster;

        GraphicsProgram program = _device!.ResourceFactory.CreateGraphicsProgram(description);
        (isFallback ? _fallbackProgramCache : _programCache)[key] = program;
        return program;
    }


    private void Reselect()
    {
        int index = 0;
        for (int i = 0; i < _selection.Length; i++)
            index += _selection[i] * _strides[i];

        _activeIndex = index;
    }


    private bool IsKnown(Keyword keyword)
        => _axisByName.TryGetValue(keyword.Name, out int slot) && _valueIndices[slot].ContainsKey(keyword.Value);


    private bool TrySelect(Keyword keyword)
    {
        if (!_axisByName.TryGetValue(keyword.Name, out int slot) || !_valueIndices[slot].TryGetValue(keyword.Value, out int value))
            return false;

        _selection[slot] = value;
        return true;
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
            try
            {
                return Compile(index);
            }
            catch
            {
                // Fall through to the fallback below - a failed compile is not fatal on its own.
            }
        }

        if (existing != null)
            return existing;

        if (_fallback != null && _fallback.IsCompiledFor(_backend))
            return _fallback;

        throw new InvalidOperationException($"The active variant of pass '{Name}' is not compiled for backend {_backend}, no compiler is attached (or the compile failed), and no fallback variant is available.");
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


    private readonly record struct ProgramKey(int VariantIndex, BlendStateDescription Blend, DepthStencilStateDescription Depth, RasterizerStateDescription Raster);
}
