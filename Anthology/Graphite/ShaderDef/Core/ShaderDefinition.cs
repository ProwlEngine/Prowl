using System;
using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// The parsed representation of a shaderdef shader description, plus the runtime binding to a device
/// and (optionally) a compiler once one of the <c>Create</c> overloads has been called.
/// </summary>
public sealed class ShaderDefinition
{
    /// <summary>
    /// The identifier name of this shader. Required and never null.
    /// </summary>
    public string? Name;

    /// <summary>
    /// Metadata for a fallback shader name that a renderer can use. Empty if none is defined.
    /// </summary>
    public string? Fallback;

    /// <summary>
    /// A list of default properties requested by the markup for shader resources or uniforms.
    /// </summary>
    public ShaderProperty[]? Properties;

    /// <summary>
    /// A list of executable render passes present in the shader.
    /// </summary>
    public ShaderPass[]? Passes;


    private bool _created;

    /// <summary>
    /// True once this shader has been created for a device.
    /// </summary>
    public bool IsCreated => _created;


    /// <summary>
    /// Binds this shader to a device with no compiler and no known variants. Passes can only be
    /// resolved once variants are supplied some other way (e.g. loading a snapshot separately) -
    /// mainly useful for structural inspection before any rendering happens.
    /// </summary>
    public void Create(GraphicsDevice device, CompileMode mode = CompileMode.OnDemand)
        => BindAll(device, null, mode, null);


    /// <summary>
    /// Binds this shader to a device, discovering each pass's variant axes through
    /// <paramref name="compiler"/>. With <see cref="CompileMode.OnDemand"/> variants compile as they
    /// are requested; with <see cref="CompileMode.All"/> every variant compiles up front. Whenever a
    /// requested variant isn't compiled - because it hasn't been requested yet, or because compiling
    /// it failed - every pass falls back to <paramref name="fallback"/> instead of throwing, until a
    /// real compile of that variant succeeds. If <paramref name="fallback"/> itself has no compiled
    /// description for the active backend, resolving still throws: a fallback is required, not
    /// optional, whenever a compiler is attached.
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
    /// Binds this shader to a device from a previously captured <see cref="ShaderSnapshot"/>, without a
    /// compiler. Only variants already present in the snapshot can ever be resolved; requesting any
    /// other variant throws.
    /// </summary>
    public void Create(GraphicsDevice device, ShaderSnapshot snapshot)
        => BindFromSnapshot(device, snapshot, null, null);


    /// <summary>
    /// Binds this shader to a device from a previously captured <see cref="ShaderSnapshot"/>, allowing
    /// missing or wrong-backend variants to be compiled dynamically through <paramref name="compiler"/>.
    /// As with the compiler-attached <see cref="Create(GraphicsDevice, IShaderCompiler, Variant, CompileMode)"/>
    /// overload, <paramref name="fallback"/> is required and used whenever a requested variant isn't
    /// compiled yet or fails to compile.
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
    /// Captures the current axes and compiled variants of every pass into a serialization-friendly
    /// snapshot. Only populated variants are captured.
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


    /// <summary>True if <paramref name="pass"/> carries <paramref name="tag"/>, optionally matching a
    /// specific value.</summary>
    public static bool PassHasTag(ShaderPass pass, string tag, string? tagValue = null)
    {
        if (pass.Tags != null && pass.Tags.TryGetValue(tag, out string value))
            return tagValue == null || value == tagValue;

        return false;
    }

    /// <summary>
    /// Looks up a pass's index by its name, or -1 if no pass has that name.
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
    /// Looks up a pass by its name.
    /// </summary>
    public ShaderPass GetPass(string passName)
        => Passes![GetPassIndex(passName)];

    /// <summary>
    /// Returns the index of the first pass carrying <paramref name="tag"/>, optionally matching a
    /// specific value, or null if none do.
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
    /// Returns the indices of every pass carrying <paramref name="tag"/>, optionally matching a
    /// specific value.
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
