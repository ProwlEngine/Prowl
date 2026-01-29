// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

namespace Prowl.Runtime.Graphite.OpenGL;

/// <summary>
/// OpenGL implementation of a shader module.
/// </summary>
public class GLShaderModule : ShaderModule
{
    private readonly GLGraphiteDevice _device;
    internal uint Handle { get; private set; }

    internal GLShaderModule(GLGraphiteDevice device, in ShaderModuleDescriptor descriptor)
    {
        _device = device;
        Stage = descriptor.Stage;
        EntryPoint = descriptor.EntryPoint;
        DebugName = descriptor.DebugName;

        var shaderType = GetShaderType(descriptor.Stage);
        Handle = _device.GL.CreateShader(shaderType);

        if (descriptor.Source.Type == ShaderSourceType.GLSL)
        {
            CompileGLSL(descriptor.Source.AsGLSL());
        }
        else
        {
            throw new NotSupportedException("SPIR-V shaders are not supported in the OpenGL backend. Use GLSL.");
        }
    }

    private void CompileGLSL(string source)
    {
        _device.GL.ShaderSource(Handle, source);
        _device.GL.CompileShader(Handle);

        _device.GL.GetShader(Handle, ShaderParameterName.CompileStatus, out int success);
        if (success == 0)
        {
            string infoLog = _device.GL.GetShaderInfoLog(Handle);
            _device.GL.DeleteShader(Handle);
            Handle = 0;
            throw new InvalidOperationException($"Shader compilation failed for stage {Stage}:\n{infoLog}");
        }
    }

    private static ShaderType GetShaderType(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => ShaderType.VertexShader,
        ShaderStage.Fragment => ShaderType.FragmentShader,
        ShaderStage.Geometry => ShaderType.GeometryShader,
        ShaderStage.TessellationControl => ShaderType.TessControlShader,
        ShaderStage.TessellationEvaluation => ShaderType.TessEvaluationShader,
        ShaderStage.Compute => ShaderType.ComputeShader,
        _ => throw new ArgumentException($"Invalid shader stage: {stage}", nameof(stage)),
    };

    protected override void DisposeResources()
    {
        if (Handle != 0)
        {
            _device.GL.DeleteShader(Handle);
            Handle = 0;
        }
    }
}
