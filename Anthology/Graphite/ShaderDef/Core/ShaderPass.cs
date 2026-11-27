using System;
using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>A global a pass reads, named by a global pragma with its GLSL type.</summary>
public readonly record struct ShaderGlobal(string Name, string Type);


/// <summary>
/// How a material pass reads its properties and the renderer's globals, given by the renderer. The compiler puts it in
/// place of the pass's loose uniforms: a property becomes its read of the material record, a global its reader macro
/// <c>Graphite_Global_TYPE(offset)</c> over an offset the renderer sets as specialization constant, and any other loose
/// uniform an error where it is used. Stages reading neither get none of it.
/// </summary>
public sealed class MaterialAbi
{
    public MaterialAbi(IReadOnlyList<(string Name, string Text)> declarations, (string Name, string FragmentDeclaration) slot, IReadOnlyDictionary<string, (string Type, string Read, string With)> properties, uint resourceSet)
    {
        Declarations = declarations;
        Slot = slot;
        Properties = properties;
        ResourceSet = resourceSet;
        System.Text.StringBuilder text = new();
        text.Append(resourceSet).Append('\n');
        foreach ((string name, string declaration) in declarations)
            text.Append(name).Append('\n').Append(declaration).Append('\n');
        text.Append(slot.Name).Append('\n').Append(slot.FragmentDeclaration).Append('\n');
        List<string> names = [.. properties.Keys];
        names.Sort(StringComparer.Ordinal);
        foreach (string name in names)
            text.Append(name).Append(' ').Append(properties[name].Type).Append(' ').Append(properties[name].Read).Append('\n').Append(properties[name].With);
        Signature = text.ToString();
    }

    /// <summary>Declarations and macros a stage reading a property or global starts with, each skipped when the stage declares its name.</summary>
    public IReadOnlyList<(string Name, string Text)> Declarations { get; }

    /// <summary>The varying carrying the material slot: vertex stages write it themselves, fragment stages reading properties get its declaration.</summary>
    public (string Name, string FragmentDeclaration) Slot { get; }

    /// <summary>Each property's GLSL type, the expression reading it, and GLSL defined along with it, like a texture's parts.</summary>
    public IReadOnlyDictionary<string, (string Type, string Read, string With)> Properties { get; }

    /// <summary>The set the pass's own resources go to when they name none.</summary>
    public uint ResourceSet { get; }

    /// <summary>Everything the ABI writes, as text, so compiled variants can tell ABIs apart.</summary>
    public string Signature { get; }
}


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
    /// How the pass reads material properties and globals, set by the renderer for passes it draws materials with. Null
    /// leaves loose uniforms in one uniform block.
    /// </summary>
    public MaterialAbi? Material;

    /// <summary>The source with the material ABI, what compiled variants are made from.</summary>
    public string Fingerprint => Material is { } material ? Source + "\n" + material.Signature : Source;


    /// <summary>A pass with the same fields, not created for a device.</summary>
    internal ShaderPass Copy() => new()
    {
        Name = Name,
        Tags = Tags is null ? null : new Dictionary<string, string>(Tags),
        State = State.Clone(),
        Source = Source,
        SourceName = SourceName,
        SourceLine = SourceLine,
        Material = Material,
    };


    private GraphicsDevice? _device;
    private string _shaderName = "";
    private int _passIndex;
    private GraphicsBackend _backend;
    private PassVariants? _variants;

    private Dictionary<ProgramKey, GraphicsProgram> _programCache = new();
    private bool _created;

    private int _lastKey = -1;
    private PassState? _lastPassState;
    private PassState? _lastOverride;
    private GraphicsProgram? _lastProgram;


    /// <summary>
    /// The pass's compiled variants for a backend, shared by every user of the pass. Made on first use, and again when
    /// asked for with another compiler or backend.
    /// </summary>
    internal PassVariants Variants(IShaderCompiler? compiler, GraphicsBackend backend)
    {
        lock (this)
        {
            if (_variants == null || _variants.Compiler != compiler || _variants.Backend != backend)
                _variants = new PassVariants(this, [.. AxesWith(compiler)], compiler, backend);
            return _variants;
        }
    }


    /// <summary>The pass's variant axes from the compiler, or without one those it was created with.</summary>
    public IReadOnlyList<VariantSpace> AxesWith(IShaderCompiler? compiler)
        => compiler?.GetAxes(this) ?? (IReadOnlyList<VariantSpace>?)_variants?.Axes ?? [];


    /// <summary>
    /// Binds this pass to a device. Variants compile on the calling thread when first asked for, and one that failed
    /// throws its error each time it is asked for until the source or compiler session changes.
    /// </summary>
    internal void Bind(GraphicsDevice device, string shaderName, int passIndex, VariantSpace[] axes, Variant[] known, IShaderCompiler? compiler)
    {
        _device = device;
        _shaderName = shaderName;
        _passIndex = passIndex;
        _backend = device.BackendType;
        ReleasePrograms();
        PassVariants variants = new(this, axes, compiler, _backend);
        variants.Seed(known);
        lock (this)
            _variants = variants;
        _created = true;
    }


    /// <summary>
    /// Variant for a key from GetKey, compiled now if it is not yet and a compiler is attached. The default key is every
    /// axis at its first value. Throws when the variant failed to compile.
    /// </summary>
    public Variant GetVariant(VariantKey key = default)
    {
        EnsureCreated();
        if ((uint)key.Index >= (uint)_variants!.Count)
            throw new ArgumentOutOfRangeException(nameof(key));

        return Resolve(key.Index);
    }


    /// <summary>
    /// The variant compiled as a compute program, compiled now when it is not yet. Throws when it failed.
    /// </summary>
    public ComputeDescription CompileCompute(VariantKey key = default)
    {
        EnsureCreated();
        return _variants!.RequireCompute(key.Index);
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
    /// The variant of keywords, unlisted axes at their first value. Skips unknown names, throws on an unknown value for a known axis. Does not touch the pass.
    /// </summary>
    public VariantKey GetKey(ReadOnlySpan<Keyword> keywords)
    {
        EnsureCreated();
        if (_variants!.TryGetKey(keywords, out int key))
            return new VariantKey(key);
        foreach (Keyword keyword in keywords)
        {
            if (!_variants.TryGetKey([keyword], out _))
                throw new ArgumentException($"Keyword '{keyword.Name}={keyword.Value}' is not a known axis or value of pass '{Name}'.");
        }
        return new VariantKey(key);
    }


    /// <summary>
    /// Disposes every program this pass created. The pass can still resolve programs afterwards.
    /// </summary>
    public void Dispose() => ReleasePrograms();


    private void ReleasePrograms()
    {
        foreach (GraphicsProgram program in _programCache.Values)
            program.Dispose();

        _programCache = new();
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

        _lastKey = key;
        _lastPassState = State.Clone();
        _lastOverride = overrideState?.Clone();
        _lastProgram = program;
        return program;
    }


    private GraphicsProgram GetOrCreateProgram(int key, BlendStateDescription blend, DepthStencilStateDescription depth, RasterizerStateDescription raster)
    {
        ProgramKey programKey = new(key, blend, depth, raster);

        if (_programCache.TryGetValue(programKey, out GraphicsProgram? cached))
            return cached;

        Variant variant = Resolve(key);
        if (!variant.TryGetDescription(_backend, out ShaderDescription description))
            throw new InvalidOperationException($"The variant of pass '{Name}' is not compiled for backend {_backend} and no compiler is attached.");

        description.BlendState = blend;
        description.DepthStencilState = depth;
        description.RasterizerState = raster;

        GraphicsProgram program = _device!.ResourceFactory.CreateGraphicsProgram(description);
        program.Name = ProgramName(variant.Keywords);
        _programCache[programKey] = program;
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
    /// The variant at key, compiled now when it can be. Without a compiler, a known variant even when it is not compiled
    /// for the backend. Throws with the compile error when it failed.
    /// </summary>
    private Variant Resolve(int key)
    {
        PassVariants variants = _variants!;
        if (variants.Request(key, CompilePolicy.Sync, out _, out Exception? error) == VariantState.Ready)
            return variants.Compiled(key)!;
        if (variants.Compiler == null && variants.Available(key) is { } uncompiled)
            return uncompiled;

        throw new InvalidOperationException($"The variant of pass '{Name}' is not compiled for backend {_backend}: {error?.Message ?? "no compiler is attached"}", error);
    }


    private void EnsureCreated()
    {
        if (!_created)
            throw new InvalidOperationException($"Pass '{Name}' has not been created. Call ShaderDefinition.Create first.");
    }


    private readonly record struct ProgramKey(int VariantIndex, BlendStateDescription Blend, DepthStencilStateDescription Depth, RasterizerStateDescription Raster);
}
