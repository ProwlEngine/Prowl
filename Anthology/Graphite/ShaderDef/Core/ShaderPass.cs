using System;
using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// A pass that encapsulates render state, identification metadata, and source shader files, plus its
/// runtime variant set once the owning <see cref="ShaderDefinition"/> has been created for a device.
/// </summary>
public sealed class ShaderPass
{
    /// <summary>
    /// The name of this pass, or blank if no name was defined.
    /// </summary>
    public string Name = "";

    /// <summary>
    /// The tag key-value pairs for this pass, defined in source as a list of: <code>{ "Key" = "Value" "Key2" = "Value2" }</code>
    /// </summary>
    public Dictionary<string, string>? Tags = null;

    /// <summary>
    /// The pass state, encapsulating rasterizer settings, blend, depth, stencil, and more.
    /// </summary>
    public required PassState State;

    /// <summary>
    /// The raw Slang source embedded between SLANGPROGRAM and ENDSLANG. Slang derives its own
    /// entrypoints, so no explicit vertex/fragment stages are declared here.
    /// </summary>
    public required string InlineSlang;


    private GraphicsDevice? _device;
    private GraphicsBackend _backend;
    private IShaderCompiler? _compiler;

    private VariantSpace[] _axes = [];
    private Keyword[][] _combos = [];
    private Dictionary<int, int> _nameIdToSlot = new();
    private KeywordMap? _keywordMap;
    private KeywordState _state;
    private Variant?[] _variants = [];
    private int _activeIndex;
    private Variant? _fallback;

    private Dictionary<int, GraphicsProgram> _programCache = new();
    private Dictionary<int, GraphicsProgram> _fallbackProgramCache = new();
    private bool _created;


    /// <summary>
    /// Binds this pass to a device. <paramref name="fallback"/> is the variant every unresolved request
    /// falls back to (required whenever <paramref name="compiler"/> is non-null - see
    /// <see cref="ShaderDefinition.Create(GraphicsDevice, IShaderCompiler, Variant, CompileMode)"/>).
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

        // The keyword name -> slot mapping is shared by every state; the first combination names every
        // axis exactly once, so it seeds the slots the same way VariantSet does.
        _nameIdToSlot = [];
        Keyword[] baseCombo = _combos[0];
        for (int i = 0; i < baseCombo.Length; i++)
            _nameIdToSlot[baseCombo[i].NameId] = i;

        KeywordState[] states = new KeywordState[_combos.Length];
        for (int i = 0; i < _combos.Length; i++)
            states[i] = new KeywordState(_nameIdToSlot, _combos[i]);

        _keywordMap = new KeywordMap(states);
        _state = new KeywordState(_nameIdToSlot, _combos[0]);
        _activeIndex = _keywordMap.FindNearest(_state);

        foreach (Variant variant in known)
        {
            int index = _keywordMap.Find(new KeywordState(_nameIdToSlot, variant.Keywords));
            if (index >= 0)
                _variants[index] = variant;
        }

        _created = true;

        if (mode == CompileMode.All)
            CompileAll();
    }


    /// <summary>
    /// The variant selected by the current keyword state, compiling it on demand if a compiler is
    /// attached. Valid only after the owning shader has been created.
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
    /// The total number of variant combinations in this pass's axis space.
    /// </summary>
    public int Count { get { EnsureCreated(); return _combos.Length; } }

    /// <summary>
    /// The number of variants currently compiled for the device backend.
    /// </summary>
    public int CompiledCount
    {
        get
        {
            EnsureCreated();
            int count = 0;
            for (int i = 0; i < _variants.Length; i++)
            {
                if (_variants[i] != null && _variants[i]!.IsCompiledFor(_backend))
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// The number of variant slots currently populated with a variant, regardless of which backends
    /// it was compiled for.
    /// </summary>
    public int AvailableCount
    {
        get
        {
            EnsureCreated();
            int count = 0;
            for (int i = 0; i < _variants.Length; i++)
            {
                if (_variants[i] != null)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// True if a compiler is attached and can compile missing variants.
    /// </summary>
    public bool HasCompiler => _compiler != null;

    /// <summary>
    /// True if every variant slot is populated, regardless of which backend it was compiled for.
    /// </summary>
    public bool AllAvailable
    {
        get
        {
            EnsureCreated();
            for (int i = 0; i < _variants.Length; i++)
            {
                if (_variants[i] == null)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// True if every variant is compiled for the device backend and ready to bind with no compiler.
    /// </summary>
    public bool AllCompiled
    {
        get
        {
            EnsureCreated();
            for (int i = 0; i < _variants.Length; i++)
            {
                if (_variants[i] == null || !_variants[i]!.IsCompiledFor(_backend))
                    return false;
            }
            return true;
        }
    }


    /// <summary>
    /// Sets a keyword on the current selection and re-resolves the active variant.
    /// Throws if the keyword's name is not a variant axis of this pass.
    /// </summary>
    public void SetKeyword(Keyword keyword)
    {
        EnsureCreated();
        if (!_state.SetKeyword(keyword))
            throw UnknownKeyword(keyword);

        Reselect();
    }


    /// <summary>
    /// Sets several keywords on the current selection and re-resolves the active variant.
    /// Validates every keyword first, then applies them atomically. Throws if any keyword's name is
    /// not a variant axis of this pass.
    /// </summary>
    public void SetKeywords(params Keyword[] keywords)
    {
        EnsureCreated();
        for (int i = 0; i < keywords.Length; i++)
        {
            if (!_nameIdToSlot.ContainsKey(keywords[i].NameId))
                throw UnknownKeyword(keywords[i]);
        }

        for (int i = 0; i < keywords.Length; i++)
            _state.SetKeyword(keywords[i]);

        Reselect();
    }


    /// <summary>
    /// Sets a keyword if its name is a variant axis of this pass, re-resolving the active variant.
    /// Returns <c>false</c> without changing anything if the keyword's name is unknown.
    /// </summary>
    public bool TrySetKeyword(Keyword keyword)
    {
        EnsureCreated();
        if (!_state.SetKeyword(keyword))
            return false;

        Reselect();
        return true;
    }


    /// <summary>
    /// Sets several keywords if all their names are variant axes of this pass, re-resolving the active
    /// variant. Returns <c>false</c> without changing anything if any keyword's name is unknown.
    /// </summary>
    public bool TrySetKeywords(params Keyword[] keywords)
    {
        EnsureCreated();
        for (int i = 0; i < keywords.Length; i++)
        {
            if (!_nameIdToSlot.ContainsKey(keywords[i].NameId))
                return false;
        }

        for (int i = 0; i < keywords.Length; i++)
            _state.SetKeyword(keywords[i]);

        Reselect();
        return true;
    }


    /// <summary>
    /// Compiles every variant for the device backend. Requires an attached compiler.
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
    {
        EnsureCreated();

        if (_programCache.TryGetValue(_activeIndex, out GraphicsProgram? cached))
            return cached;

        Variant variant = Resolve(_activeIndex);
        bool isFallback = ReferenceEquals(variant, _fallback);

        // While degraded to the fallback, keep re-resolving (Resolve retries the real compile every
        // call) but reuse the fallback GraphicsProgram instead of rebuilding it every request.
        if (isFallback && _fallbackProgramCache.TryGetValue(_activeIndex, out GraphicsProgram? cachedFallback))
            return cachedFallback;

        if (!variant.TryGetDescription(_backend, out ShaderDescription description))
            throw new InvalidOperationException($"The active variant of pass '{Name}' is not compiled for backend {_backend} and no compiler is attached.");

        description.BlendState = State.ToBlendState(baseBlend);
        description.DepthStencilState = State.ToDepthStencilState(baseDepth);
        description.RasterizerState = State.ToRasterizerState(baseRaster);

        GraphicsProgram program = _device!.ResourceFactory.CreateGraphicsProgram(description);
        (isFallback ? _fallbackProgramCache : _programCache)[_activeIndex] = program;
        return program;
    }


    private void Reselect()
    {
        _activeIndex = _keywordMap!.FindNearest(_state);
    }


    /// <summary>
    /// Resolves the variant at <paramref name="index"/>: an already-compiled variant if one exists,
    /// otherwise a fresh compile through the attached compiler. If neither is available - no compiler,
    /// or the compile itself throws - falls back to <see cref="_fallback"/> instead of propagating the
    /// failure, as long as the fallback has a compiled description for the active backend. Once a real
    /// compile succeeds, later calls stop needing the fallback (the result is cached on
    /// <see cref="_variants"/> by <see cref="Compile"/>).
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
        => new($"Keyword '{keyword.Name}={keyword.Value}' is not present in any variant axis of pass '{Name}'.");
}
