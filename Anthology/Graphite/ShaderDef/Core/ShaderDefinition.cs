using System;
using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Parsed shaderdef shader, plus runtime binding to a device/compiler once Create is called.
/// </summary>
public sealed class ShaderDefinition
{
    /// <summary>
    /// Shader name. Required, never null.
    /// </summary>
    public string? Name;

    /// <summary>
    /// Fallback shader name for renderers. Empty if none.
    /// </summary>
    public string? Fallback;

    /// <summary>
    /// Default properties requested by markup for resources/uniforms.
    /// </summary>
    public ShaderProperty[]? Properties;

    /// <summary>
    /// Render passes in the shader.
    /// </summary>
    public ShaderPass[]? Passes;


    private bool _created;

    /// <summary>
    /// True once created for a device.
    /// </summary>
    public bool IsCreated => _created;


    /// <summary>
    /// Binds to a device with no compiler and no known variants. Passes only resolve once variants
    /// come from elsewhere (e.g. a snapshot). Mostly for structural inspection pre-render.
    /// </summary>
    public void Create(GraphicsDevice device, CompileMode mode = CompileMode.OnDemand)
        => BindAll(device, null, mode, null);


    /// <summary>
    /// Binds to a device, discovering each pass's variant axes via the compiler. OnDemand compiles
    /// variants as requested; All compiles everything up front. If a requested variant isn't compiled
    /// yet or failed, falls back to the fallback variant instead of throwing. Fallback must itself
    /// have a compiled description for the active backend, or resolving throws anyway.
    /// </summary>
    public void Create(GraphicsDevice device, IShaderCompiler compiler, Variant fallback, CompileMode mode = CompileMode.OnDemand)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(fallback);

        BindAll(device, compiler, mode, fallback);
    }


    private void BindAll(GraphicsDevice device, IShaderCompiler? compiler, CompileMode mode, Variant? fallback)
    {
        if (Passes == null)
            throw new InvalidOperationException("Shader has no passes.");

        foreach (ShaderPass pass in Passes)
        {
            VariantSpace[] axes = compiler != null ? [.. compiler.GetAxes(pass)] : [];
            pass.Bind(device, axes, [], compiler, mode, fallback);
        }

        _created = true;
    }


    /// <summary>
    /// Binds to a device from a captured snapshot, no compiler. Only variants already in the
    /// snapshot resolve; anything else throws.
    /// </summary>
    public void Create(GraphicsDevice device, ShaderSnapshot snapshot)
        => BindFromSnapshot(device, snapshot, null, null);


    /// <summary>
    /// Binds to a device from a captured snapshot, allowing missing or wrong-backend variants to
    /// compile dynamically via the compiler. Fallback is required, same as the compiler-attached
    /// Create overload.
    /// </summary>
    public void Create(GraphicsDevice device, ShaderSnapshot snapshot, IShaderCompiler compiler, Variant fallback)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(fallback);

        BindFromSnapshot(device, snapshot, compiler, fallback);
    }


    private void BindFromSnapshot(GraphicsDevice device, ShaderSnapshot snapshot, IShaderCompiler? compiler, Variant? fallback)
    {
        if (Passes == null)
            throw new InvalidOperationException("Shader has no passes.");

        if (snapshot.Passes == null || snapshot.Passes.Length != Passes.Length)
            throw new ArgumentException("Snapshot pass count does not match the shader's pass count.", nameof(snapshot));

        for (int i = 0; i < Passes.Length; i++)
        {
            PassSnapshot pass = snapshot.Passes[i];
            Passes[i].Bind(device, pass.Axes ?? [], pass.Variants ?? [], compiler, CompileMode.OnDemand, fallback);
        }

        _created = true;
    }


    /// <summary>
    /// Captures every pass's axes and compiled variants into a serializable snapshot. Only
    /// populated variants are captured.
    /// </summary>
    public ShaderSnapshot Snapshot()
    {
        if (Passes == null)
            return new ShaderSnapshot { Passes = [] };

        PassSnapshot[] passes = new PassSnapshot[Passes.Length];
        for (int i = 0; i < Passes.Length; i++)
            passes[i] = Passes[i].Snapshot();

        return new ShaderSnapshot { Passes = passes };
    }


    /// <summary>True if pass carries tag, optionally matching a specific value.</summary>
    public static bool PassHasTag(ShaderPass pass, string tag, string? tagValue = null)
    {
        if (pass.Tags != null && pass.Tags.TryGetValue(tag, out string value))
            return tagValue == null || value == tagValue;

        return false;
    }

    /// <summary>
    /// Pass index by name, or -1 if not found.
    /// </summary>
    public int GetPassIndex(string passName)
    {
        ShaderPass[] passes = Passes ?? [];
        for (int i = 0; i < passes.Length; i++)
        {
            if (passes[i].Name == passName)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Pass by name.
    /// </summary>
    public ShaderPass GetPass(string passName)
        => Passes![GetPassIndex(passName)];

    /// <summary>
    /// Index of first pass carrying tag, optionally matching a specific value. Null if none.
    /// </summary>
    public int? GetPassWithTag(string tag, string? tagValue = null)
    {
        ShaderPass[] passes = Passes ?? [];
        for (int i = 0; i < passes.Length; i++)
        {
            if (PassHasTag(passes[i], tag, tagValue))
                return i;
        }

        return null;
    }

    /// <summary>
    /// Indices of every pass carrying tag, optionally matching a specific value.
    /// </summary>
    public List<int> GetPassesWithTag(string tag, string? tagValue = null)
    {
        ShaderPass[] passes = Passes ?? [];
        List<int> result = [];

        for (int i = 0; i < passes.Length; i++)
        {
            if (PassHasTag(passes[i], tag, tagValue))
                result.Add(i);
        }

        return result;
    }
}
