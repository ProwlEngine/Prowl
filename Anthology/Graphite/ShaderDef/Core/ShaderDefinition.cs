using System;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// The parsed representation of a shaderdef shader description, plus the runtime binding to a device
/// and (optionally) a compiler once <see cref="Create(GraphicsDevice, IShaderCompiler, CompileMode)"/>
/// has been called.
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
    /// Binds this shader to a device, discovering each pass's variant axes through the given compiler.
    /// With <see cref="CompileMode.OnDemand"/> variants compile as they are requested; with
    /// <see cref="CompileMode.All"/> every variant compiles up front. A null compiler produces passes
    /// that can hold cached variants but cannot compile new ones.
    /// </summary>
    public void Create(GraphicsDevice device, IShaderCompiler? compiler = null, CompileMode mode = CompileMode.OnDemand)
    {
        if (Passes == null)
            throw new InvalidOperationException("Shader has no passes.");

        foreach (ShaderPass pass in Passes)
        {
            VariantSpace[] axes = compiler != null ? [.. compiler.GetAxes(pass)] : [];
            pass.Bind(device, axes, [], compiler, mode);
        }

        _created = true;
    }


    /// <summary>
    /// Binds this shader to a device from a previously captured <see cref="ShaderSnapshot"/>, without
    /// requiring a compiler. Pass a compiler to allow missing or wrong-backend variants to be
    /// recompiled dynamically.
    /// </summary>
    public void Create(GraphicsDevice device, ShaderSnapshot snapshot, IShaderCompiler? compiler = null)
    {
        if (Passes == null)
            throw new InvalidOperationException("Shader has no passes.");

        if (snapshot.Passes == null || snapshot.Passes.Length != Passes.Length)
            throw new ArgumentException("Snapshot pass count does not match the shader's pass count.", nameof(snapshot));

        for (int i = 0; i < Passes.Length; i++)
        {
            PassSnapshot pass = snapshot.Passes[i];
            Passes[i].Bind(device, pass.Axes ?? [], pass.Variants ?? [], compiler, CompileMode.OnDemand);
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
}
