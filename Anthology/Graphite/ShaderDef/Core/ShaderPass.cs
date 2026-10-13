using System;
using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Render state, id metadata, source shaders, plus the variant set once its ShaderDefinition is created for a device.
/// </summary>
public sealed class ShaderPass : IDisposable
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
    /// Raw program source between GLSLPROGRAM and ENDGLSL, interpreted only by the compiler.
    /// </summary>
    public required string Source;

    /// <summary>
    /// Name compile errors report the source under, the shader name when parsed.
    /// </summary>
    public string SourceName = "";

    /// <summary>
    /// Line of the document the first line of <see cref="Source"/> is on.
    /// </summary>
    public int SourceLine = 1;


    /// <summary>
    /// Raised once per variant when its compile fails. Not raised again until the source or compiler session changes.
    /// </summary>
    public event Action<ShaderPass, Keyword[], Exception>? CompileFailed;


    private GraphicsDevice? _device;
    private string _shaderName = "";
    private int _passIndex;
    private GraphicsBackend _backend;
    private PassVariants? _variants;
    private Variant? _fallback;

    private Dictionary<ProgramKey, GraphicsProgram> _programCache = new();
    private Dictionary<ProgramKey, GraphicsProgram> _fallbackProgramCache = new();
    private bool _created;

    private int _lastKey = -1;
    private PassState? _lastPassState;
    private PassState? _lastOverride;
    private GraphicsProgram? _lastProgram;


    /// <summary>
    /// The pass's compiled variants for a backend, shared by every user of the pass. Made on first use, and again when
    /// asked for with another compiler or backend.
    /// </summary>
    public PassVariants Variants(IShaderCompiler? compiler, GraphicsBackend backend)
    {
        lock (this)
        {
            if (_variants == null || _variants.Compiler != compiler || _variants.Backend != backend)
            {
                VariantSpace[] axes = [.. AxesWith(compiler)];
                Adopt(new PassVariants(this, axes, compiler, backend));
            }
            return _variants!;
        }
    }


    /// <summary>The pass's variant axes, from the compiler or, without one, the baked pass.</summary>
    public IReadOnlyList<VariantSpace> AxesWith(IShaderCompiler? compiler)
        => compiler?.GetAxes(this) ?? BakedVariants.Find(this)?.Axes ?? (IReadOnlyList<VariantSpace>?)_variants?.Axes ?? [];


    /// <summary>
    /// Binds this pass to a device. A variant not ready falls back to the pass's default variant, every axis at its
    /// first value, then to fallback.
    /// </summary>
    internal void Bind(GraphicsDevice device, string shaderName, int passIndex, VariantSpace[] axes, Variant[] known, IShaderCompiler? compiler, CompileMode mode, Variant? fallback = null)
    {
        _device = device;
        _shaderName = shaderName;
        _passIndex = passIndex;
        _backend = device.BackendType;
        _fallback = fallback;
        ReleasePrograms();
        lock (this)
            Adopt(new PassVariants(this, axes, compiler, _backend));
        _variants!.Seed(known);
        _created = true;

        if (mode == CompileMode.All && compiler != null)
            CompileAll();
    }


    private void Adopt(PassVariants variants)
    {
        _variants = variants;
        variants.CompileFailed += (combo, exception) => CompileFailed?.Invoke(this, combo, exception);
    }


    /// <summary>
    /// Variant for a key from GetKey, compiled on demand if a compiler is attached. Key 0 is every axis at its first value.
    /// </summary>
    public Variant GetVariant(int key)
    {
        EnsureCreated();
        if ((uint)key >= (uint)_variants!.Count)
            throw new ArgumentOutOfRangeException(nameof(key));

        return Resolve(key).Variant;
    }


    /// <summary>
    /// Total variant combos in this pass's axis space.
    /// </summary>
    public int Count { get { EnsureCreated(); return _variants!.Count; } }

    /// <summary>
    /// Variants compiled for the device backend.
    /// </summary>
    public int CompiledCount { get { EnsureCreated(); return _variants!.CompiledCount; } }

    /// <summary>
    /// Variant slots populated, any backend.
    /// </summary>
    public int AvailableCount { get { EnsureCreated(); return _variants!.AvailableCount; } }

    /// <summary>
    /// True if a compiler is attached.
    /// </summary>
    public bool HasCompiler => _variants?.Compiler != null;

    /// <summary>
    /// True if every variant slot is populated, any backend.
    /// </summary>
    public bool AllAvailable => AvailableCount == Count;

    /// <summary>
    /// True if every variant is compiled for the device backend, ready to bind with no compiler.
    /// </summary>
    public bool AllCompiled => CompiledCount == Count;


    /// <summary>
    /// Variant axes of this pass in slot order. Empty until created.
    /// </summary>
    public IReadOnlyList<VariantSpace> Axes { get { EnsureCreated(); return _variants!.Axes; } }


    /// <summary>
    /// Mixed-radix variant key for keywords, unlisted axes at their first value. Skips unknown names, throws on an unknown value for a known axis. Does not touch the pass.
    /// </summary>
    public int GetKey(ReadOnlySpan<Keyword> keywords)
    {
        EnsureCreated();
        if (_variants!.TryGetKey(keywords, out int key))
            return key;
        foreach (Keyword keyword in keywords)
        {
            if (!_variants.TryGetKey([keyword], out _))
                throw new ArgumentException($"Keyword '{keyword.Name}={keyword.Value}' is not a known axis or value of pass '{Name}'.");
        }
        return key;
    }


    /// <summary>
    /// Compiles every variant for the device backend. Needs a compiler attached.
    /// </summary>
    public void CompileAll()
    {
        EnsureCreated();
        if (_variants!.Compiler == null)
            throw new InvalidOperationException($"CompileAll on pass '{Name}' requires an attached compiler.");

        for (int i = 0; i < _variants.Count; i++)
            _variants.Request(i, CompilePolicy.Sync, out _, out _);
    }


    /// <summary>
    /// Disposes every program this pass created. The pass can still resolve programs afterwards.
    /// </summary>
    public void Dispose() => ReleasePrograms();


    private void ReleasePrograms()
    {
        foreach (GraphicsProgram program in _programCache.Values)
            program.Dispose();
        foreach (GraphicsProgram program in _fallbackProgramCache.Values)
            program.Dispose();

        _programCache = new();
        _fallbackProgramCache = new();
        _lastKey = -1;
        _lastProgram = null;
    }


    internal PassSnapshot Snapshot()
    {
        EnsureCreated();
        return _variants!.Snapshot();
    }


    internal ShaderDescription GetDescription(int key)
    {
        EnsureCreated();
        if ((uint)key >= (uint)_variants!.Count)
            throw new ArgumentOutOfRangeException(nameof(key));

        if (!Resolve(key).Variant.TryGetDescription(_backend, out ShaderDescription description))
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

        if (_variants!.Compiled(key) != null)
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

        (Variant variant, bool isFallback) = Resolve(key);

        if (isFallback && _fallbackProgramCache.TryGetValue(programKey, out GraphicsProgram? cachedFallback))
            return cachedFallback;

        if (!variant.TryGetDescription(_backend, out ShaderDescription description))
            throw new InvalidOperationException($"The variant of pass '{Name}' is not compiled for backend {_backend} and no compiler is attached.");

        description.BlendState = blend;
        description.DepthStencilState = depth;
        description.RasterizerState = raster;

        GraphicsProgram program = _device!.ResourceFactory.CreateGraphicsProgram(description);
        program.Name = ProgramName(variant.Keywords);
        (isFallback ? _fallbackProgramCache : _programCache)[programKey] = program;
        return program;
    }


    private string ProgramName(Keyword[] keywords)
    {
        string name = $"{_shaderName}/{(string.IsNullOrEmpty(Name) ? _passIndex.ToString() : Name)}";
        if (keywords.Length == 0)
            return name;

        string[] axes = new string[keywords.Length];
        for (int i = 0; i < keywords.Length; i++)
            axes[i] = $"{keywords[i].Name}={keywords[i].Value}";

        return $"{name} [{string.Join(", ", axes)}]";
    }


    /// <summary>
    /// The variant at key, compiled now when it can be, else the pass's default variant, else the fallback variant.
    /// </summary>
    private (Variant Variant, bool Fallback) Resolve(int key)
    {
        PassVariants variants = _variants!;
        if (variants.Request(key, CompilePolicy.Sync, out _, out Exception? error) == VariantState.Ready)
            return (variants.Compiled(key)!, false);
        if (variants.Compiler == null && variants.Available(key) is { } uncompiled)
            return (uncompiled, false);
        if (key != 0 && variants.Request(0, CompilePolicy.Sync, out _, out _) == VariantState.Ready)
            return (variants.Compiled(0)!, true);
        if (_fallback != null && _fallback.IsCompiledFor(_backend))
            return (_fallback, true);

        throw new InvalidOperationException($"The variant of pass '{Name}' is not compiled for backend {_backend} and no fallback variant is available.", error);
    }


    private void EnsureCreated()
    {
        if (!_created)
            throw new InvalidOperationException($"Pass '{Name}' has not been created. Call ShaderDefinition.Create first.");
    }


    private readonly record struct ProgramKey(int VariantIndex, BlendStateDescription Blend, DepthStencilStateDescription Depth, RasterizerStateDescription Raster);
}
