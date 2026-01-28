// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Text;

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Shader source code, either GLSL text or SPIR-V binary.
/// </summary>
public readonly struct ShaderSource
{
    /// <summary>The source type.</summary>
    public readonly ShaderSourceType Type;

    /// <summary>The source code bytes.</summary>
    public readonly byte[] Code;

    private ShaderSource(ShaderSourceType type, byte[] code)
    {
        Type = type;
        Code = code;
    }

    /// <summary>
    /// Creates a shader source from GLSL text.
    /// </summary>
    public static ShaderSource FromGLSL(string source) => new(ShaderSourceType.GLSL, Encoding.UTF8.GetBytes(source));

    /// <summary>
    /// Creates a shader source from SPIR-V binary.
    /// </summary>
    public static ShaderSource FromSPIRV(byte[] spirv) => new(ShaderSourceType.SPIRV, spirv);

    /// <summary>
    /// Gets the GLSL source as a string (only valid for GLSL sources).
    /// </summary>
    public string AsGLSL()
    {
        if (Type != ShaderSourceType.GLSL)
            throw new InvalidOperationException("Shader source is not GLSL");
        return Encoding.UTF8.GetString(Code);
    }
}

/// <summary>
/// Describes how to create a shader module.
/// </summary>
public struct ShaderModuleDescriptor
{
    /// <summary>Shader stage (vertex, fragment, etc.).</summary>
    public ShaderStage Stage;

    /// <summary>Shader source code.</summary>
    public ShaderSource Source;

    /// <summary>Entry point function name.</summary>
    public string EntryPoint;

    /// <summary>Optional debug name for graphics debuggers.</summary>
    public string? DebugName;

    public ShaderModuleDescriptor()
    {
        Stage = ShaderStage.Vertex;
        Source = default;
        EntryPoint = "main";
        DebugName = null;
    }

    /// <summary>
    /// Creates a vertex shader module descriptor from GLSL source.
    /// </summary>
    public static ShaderModuleDescriptor VertexGLSL(string source, string entryPoint = "main") => new()
    {
        Stage = ShaderStage.Vertex,
        Source = ShaderSource.FromGLSL(source),
        EntryPoint = entryPoint,
    };

    /// <summary>
    /// Creates a fragment shader module descriptor from GLSL source.
    /// </summary>
    public static ShaderModuleDescriptor FragmentGLSL(string source, string entryPoint = "main") => new()
    {
        Stage = ShaderStage.Fragment,
        Source = ShaderSource.FromGLSL(source),
        EntryPoint = entryPoint,
    };

    /// <summary>
    /// Creates a geometry shader module descriptor from GLSL source.
    /// </summary>
    public static ShaderModuleDescriptor GeometryGLSL(string source, string entryPoint = "main") => new()
    {
        Stage = ShaderStage.Geometry,
        Source = ShaderSource.FromGLSL(source),
        EntryPoint = entryPoint,
    };

    /// <summary>
    /// Creates a compute shader module descriptor from GLSL source.
    /// </summary>
    public static ShaderModuleDescriptor ComputeGLSL(string source, string entryPoint = "main") => new()
    {
        Stage = ShaderStage.Compute,
        Source = ShaderSource.FromGLSL(source),
        EntryPoint = entryPoint,
    };
}
