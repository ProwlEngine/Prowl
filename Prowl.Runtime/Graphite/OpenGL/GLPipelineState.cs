// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

namespace Prowl.Runtime.Graphite.OpenGL;

/// <summary>
/// OpenGL implementation of a graphics pipeline state.
/// </summary>
public class GLPipelineState : PipelineState
{
    private readonly GLGraphiteDevice _device;
    internal uint ProgramHandle { get; private set; }
    internal uint VAOHandle { get; private set; }

    // Cached state for application during draw
    internal PipelineStateDescriptor Descriptor { get; }

    internal GLPipelineState(GLGraphiteDevice device, in PipelineStateDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;
        Topology = descriptor.Topology;
        DebugName = descriptor.DebugName;

        // Validate required shaders
        if (descriptor.VertexShader == null)
            throw new ArgumentException("Vertex shader is required for graphics pipeline.", nameof(descriptor));
        if (descriptor.FragmentShader == null)
            throw new ArgumentException("Fragment shader is required for graphics pipeline.", nameof(descriptor));

        // Create and link the shader program
        ProgramHandle = _device.GL.CreateProgram();

        if (descriptor.VertexShader is GLShaderModule vertexShader)
            _device.GL.AttachShader(ProgramHandle, vertexShader.Handle);

        if (descriptor.FragmentShader is GLShaderModule fragmentShader)
            _device.GL.AttachShader(ProgramHandle, fragmentShader.Handle);

        if (descriptor.GeometryShader is GLShaderModule geometryShader)
            _device.GL.AttachShader(ProgramHandle, geometryShader.Handle);

        _device.GL.LinkProgram(ProgramHandle);

        _device.GL.GetProgram(ProgramHandle, ProgramPropertyARB.LinkStatus, out int success);
        if (success == 0)
        {
            string infoLog = _device.GL.GetProgramInfoLog(ProgramHandle);
            _device.GL.DeleteProgram(ProgramHandle);
            ProgramHandle = 0;
            throw new InvalidOperationException($"Shader program linking failed:\n{infoLog}");
        }

        // Detach shaders after linking (they're no longer needed)
        if (descriptor.VertexShader is GLShaderModule vs)
            _device.GL.DetachShader(ProgramHandle, vs.Handle);
        if (descriptor.FragmentShader is GLShaderModule fs)
            _device.GL.DetachShader(ProgramHandle, fs.Handle);
        if (descriptor.GeometryShader is GLShaderModule gs)
            _device.GL.DetachShader(ProgramHandle, gs.Handle);

        // Create VAO for vertex layout
        VAOHandle = _device.GL.GenVertexArray();
        SetupVertexLayout(descriptor.VertexLayout);
    }

    private void SetupVertexLayout(VertexLayoutDescriptor vertexLayout)
    {
        _device.GL.BindVertexArray(VAOHandle);

        if (vertexLayout.Buffers == null)
        {
            _device.GL.BindVertexArray(0);
            return;
        }

        for (int bufferIndex = 0; bufferIndex < vertexLayout.Buffers.Length; bufferIndex++)
        {
            var buffer = vertexLayout.Buffers[bufferIndex];

            foreach (var attr in buffer.Attributes)
            {
                _device.GL.EnableVertexAttribArray(attr.Location);

                var (componentCount, type, normalized, isInteger) = GetVertexFormatInfo(attr.Format);

                // Store binding info - actual buffer binding happens at draw time
                // Use VertexAttribIFormat for integer types (int, uint), VertexAttribFormat for floats
                if (isInteger)
                {
                    _device.GL.VertexAttribIFormat(attr.Location, componentCount, (VertexAttribIType)type, attr.Offset);
                }
                else
                {
                    _device.GL.VertexAttribFormat(attr.Location, componentCount, type, normalized, attr.Offset);
                }
                _device.GL.VertexAttribBinding(attr.Location, (uint)bufferIndex);
            }

            // Set up instancing - divisor is per binding, not per attribute
            if (buffer.StepMode == VertexStepMode.Instance)
            {
                _device.GL.VertexBindingDivisor((uint)bufferIndex, 1);
            }
        }

        _device.GL.BindVertexArray(0);
    }

    // Returns (componentCount, attribType, normalized, isInteger)
    // isInteger indicates whether to use VertexAttribIFormat (for int/uint shader inputs)
    private static (int count, VertexAttribType type, bool normalized, bool isInteger) GetVertexFormatInfo(VertexFormat format) => format switch
    {
        VertexFormat.Float => (1, VertexAttribType.Float, false, false),
        VertexFormat.Float2 => (2, VertexAttribType.Float, false, false),
        VertexFormat.Float3 => (3, VertexAttribType.Float, false, false),
        VertexFormat.Float4 => (4, VertexAttribType.Float, false, false),
        // Integer types need VertexAttribIFormat to remain as integers in shader
        VertexFormat.Int => (1, VertexAttribType.Int, false, true),
        VertexFormat.Int2 => (2, VertexAttribType.Int, false, true),
        VertexFormat.Int3 => (3, VertexAttribType.Int, false, true),
        VertexFormat.Int4 => (4, VertexAttribType.Int, false, true),
        VertexFormat.Uint => (1, VertexAttribType.UnsignedInt, false, true),
        VertexFormat.Uint2 => (2, VertexAttribType.UnsignedInt, false, true),
        VertexFormat.Uint3 => (3, VertexAttribType.UnsignedInt, false, true),
        VertexFormat.Uint4 => (4, VertexAttribType.UnsignedInt, false, true),
        // Non-normalized short/byte are typically converted to float, not used as true integers
        // If you need true int16/int8 shader inputs, use Int/Uint formats with appropriate data
        VertexFormat.Short2 => (2, VertexAttribType.Short, false, false),
        VertexFormat.Short4 => (4, VertexAttribType.Short, false, false),
        VertexFormat.Short2Norm => (2, VertexAttribType.Short, true, false),
        VertexFormat.Short4Norm => (4, VertexAttribType.Short, true, false),
        VertexFormat.Byte4 => (4, VertexAttribType.Byte, false, false),
        VertexFormat.Byte4Norm => (4, VertexAttribType.Byte, true, false),
        VertexFormat.UByte4 => (4, VertexAttribType.UnsignedByte, false, false),
        VertexFormat.UByte4Norm => (4, VertexAttribType.UnsignedByte, true, false),
        _ => throw new NotSupportedException($"Unsupported vertex format: {format}"),
    };

    internal void Apply()
    {
        _device.GL.UseProgram(ProgramHandle);
        _device.GL.BindVertexArray(VAOHandle);

        ApplyRasterizerState(Descriptor.RasterizerState);
        ApplyDepthStencilState(Descriptor.DepthStencilState);
        ApplyBlendState(Descriptor.BlendState);
    }

    private void ApplyRasterizerState(RasterizerStateDescriptor state)
    {
        // Cull mode
        if (state.CullMode == CullMode.None)
        {
            _device.GL.Disable(EnableCap.CullFace);
        }
        else
        {
            _device.GL.Enable(EnableCap.CullFace);
            _device.GL.CullFace(state.CullMode == CullMode.Front ? TriangleFace.Front : TriangleFace.Back);
        }

        // Front face
        _device.GL.FrontFace(state.FrontFace == FrontFace.CounterClockwise
            ? Silk.NET.OpenGL.FrontFaceDirection.Ccw
            : Silk.NET.OpenGL.FrontFaceDirection.CW);

        // Polygon mode
        _device.GL.PolygonMode(TriangleFace.FrontAndBack, state.PolygonMode switch
        {
            PolygonMode.Fill => Silk.NET.OpenGL.PolygonMode.Fill,
            PolygonMode.Line => Silk.NET.OpenGL.PolygonMode.Line,
            PolygonMode.Point => Silk.NET.OpenGL.PolygonMode.Point,
            _ => Silk.NET.OpenGL.PolygonMode.Fill,
        });

        // Depth bias
        if (state.DepthBiasEnable)
        {
            _device.GL.Enable(EnableCap.PolygonOffsetFill);
            _device.GL.PolygonOffset(state.DepthBiasSlope, state.DepthBiasConstant);
        }
        else
        {
            _device.GL.Disable(EnableCap.PolygonOffsetFill);
        }

        // Depth clamp
        if (state.DepthClampEnable)
            _device.GL.Enable(EnableCap.DepthClamp);
        else
            _device.GL.Disable(EnableCap.DepthClamp);
    }

    private void ApplyDepthStencilState(DepthStencilStateDescriptor state)
    {
        // Depth test
        if (state.DepthTestEnable)
        {
            _device.GL.Enable(EnableCap.DepthTest);
            _device.GL.DepthFunc(GetDepthFunc(state.DepthCompare));
        }
        else
        {
            _device.GL.Disable(EnableCap.DepthTest);
        }

        // Depth write
        _device.GL.DepthMask(state.DepthWriteEnable);

        // Stencil test
        if (state.StencilTestEnable)
        {
            _device.GL.Enable(EnableCap.StencilTest);

            // Front face
            _device.GL.StencilFuncSeparate(
                TriangleFace.Front,
                GetStencilFunc(state.StencilFront.Compare),
                0, // Reference set dynamically
                state.StencilReadMask);

            _device.GL.StencilOpSeparate(
                TriangleFace.Front,
                GetStencilOp(state.StencilFront.FailOp),
                GetStencilOp(state.StencilFront.DepthFailOp),
                GetStencilOp(state.StencilFront.PassOp));

            // Back face
            _device.GL.StencilFuncSeparate(
                TriangleFace.Back,
                GetStencilFunc(state.StencilBack.Compare),
                0,
                state.StencilReadMask);

            _device.GL.StencilOpSeparate(
                TriangleFace.Back,
                GetStencilOp(state.StencilBack.FailOp),
                GetStencilOp(state.StencilBack.DepthFailOp),
                GetStencilOp(state.StencilBack.PassOp));

            _device.GL.StencilMask(state.StencilWriteMask);
        }
        else
        {
            _device.GL.Disable(EnableCap.StencilTest);
        }
    }

    private void ApplyBlendState(BlendStateDescriptor state)
    {
        if (state.Attachments == null || state.Attachments.Length == 0)
        {
            _device.GL.Disable(EnableCap.Blend);
            _device.GL.ColorMask(true, true, true, true);
            return;
        }

        // Check if any attachment has blending enabled
        bool anyBlendEnabled = false;
        foreach (var att in state.Attachments)
        {
            if (att.BlendEnable)
            {
                anyBlendEnabled = true;
                break;
            }
        }

        if (anyBlendEnabled)
        {
            _device.GL.Enable(EnableCap.Blend);
        }
        else
        {
            _device.GL.Disable(EnableCap.Blend);
        }

        // Apply per-attachment blend state using indexed GL functions (OpenGL 4.0+)
        for (uint i = 0; i < state.Attachments.Length; i++)
        {
            var attachment = state.Attachments[i];

            if (attachment.BlendEnable)
            {
                _device.GL.Enable(EnableCap.Blend, i);
                _device.GL.BlendFuncSeparate(i,
                    GetBlendFactor(attachment.SrcColorFactor),
                    GetBlendFactor(attachment.DstColorFactor),
                    GetBlendFactor(attachment.SrcAlphaFactor),
                    GetBlendFactor(attachment.DstAlphaFactor));

                _device.GL.BlendEquationSeparate(i,
                    GetBlendEquation(attachment.ColorOp),
                    GetBlendEquation(attachment.AlphaOp));
            }
            else
            {
                _device.GL.Disable(EnableCap.Blend, i);
            }

            // Per-attachment color write mask
            _device.GL.ColorMask(i,
                (attachment.WriteMask & ColorWriteMask.Red) != 0,
                (attachment.WriteMask & ColorWriteMask.Green) != 0,
                (attachment.WriteMask & ColorWriteMask.Blue) != 0,
                (attachment.WriteMask & ColorWriteMask.Alpha) != 0);
        }
    }

    private static DepthFunction GetDepthFunc(CompareFunction func) => func switch
    {
        Graphite.CompareFunction.Never => DepthFunction.Never,
        Graphite.CompareFunction.Less => DepthFunction.Less,
        Graphite.CompareFunction.Equal => DepthFunction.Equal,
        Graphite.CompareFunction.LessEqual => DepthFunction.Lequal,
        Graphite.CompareFunction.Greater => DepthFunction.Greater,
        Graphite.CompareFunction.NotEqual => DepthFunction.Notequal,
        Graphite.CompareFunction.GreaterEqual => DepthFunction.Gequal,
        Graphite.CompareFunction.Always => DepthFunction.Always,
        _ => throw new NotSupportedException($"Unsupported compare function: {func}"),
    };

    private static StencilFunction GetStencilFunc(CompareFunction func) => func switch
    {
        Graphite.CompareFunction.Never => StencilFunction.Never,
        Graphite.CompareFunction.Less => StencilFunction.Less,
        Graphite.CompareFunction.Equal => StencilFunction.Equal,
        Graphite.CompareFunction.LessEqual => StencilFunction.Lequal,
        Graphite.CompareFunction.Greater => StencilFunction.Greater,
        Graphite.CompareFunction.NotEqual => StencilFunction.Notequal,
        Graphite.CompareFunction.GreaterEqual => StencilFunction.Gequal,
        Graphite.CompareFunction.Always => StencilFunction.Always,
        _ => throw new NotSupportedException($"Unsupported compare function: {func}"),
    };

    private static Silk.NET.OpenGL.StencilOp GetStencilOp(StencilOp op) => op switch
    {
        Graphite.StencilOp.Keep => Silk.NET.OpenGL.StencilOp.Keep,
        Graphite.StencilOp.Zero => Silk.NET.OpenGL.StencilOp.Zero,
        Graphite.StencilOp.Replace => Silk.NET.OpenGL.StencilOp.Replace,
        Graphite.StencilOp.IncrementClamp => Silk.NET.OpenGL.StencilOp.Incr,
        Graphite.StencilOp.DecrementClamp => Silk.NET.OpenGL.StencilOp.Decr,
        Graphite.StencilOp.Invert => Silk.NET.OpenGL.StencilOp.Invert,
        Graphite.StencilOp.IncrementWrap => Silk.NET.OpenGL.StencilOp.IncrWrap,
        Graphite.StencilOp.DecrementWrap => Silk.NET.OpenGL.StencilOp.DecrWrap,
        _ => throw new NotSupportedException($"Unsupported stencil operation: {op}"),
    };

    private static BlendingFactor GetBlendFactor(BlendFactor factor) => factor switch
    {
        Graphite.BlendFactor.Zero => BlendingFactor.Zero,
        Graphite.BlendFactor.One => BlendingFactor.One,
        Graphite.BlendFactor.SrcColor => BlendingFactor.SrcColor,
        Graphite.BlendFactor.OneMinusSrcColor => BlendingFactor.OneMinusSrcColor,
        Graphite.BlendFactor.DstColor => BlendingFactor.DstColor,
        Graphite.BlendFactor.OneMinusDstColor => BlendingFactor.OneMinusDstColor,
        Graphite.BlendFactor.SrcAlpha => BlendingFactor.SrcAlpha,
        Graphite.BlendFactor.OneMinusSrcAlpha => BlendingFactor.OneMinusSrcAlpha,
        Graphite.BlendFactor.DstAlpha => BlendingFactor.DstAlpha,
        Graphite.BlendFactor.OneMinusDstAlpha => BlendingFactor.OneMinusDstAlpha,
        Graphite.BlendFactor.ConstantColor => BlendingFactor.ConstantColor,
        Graphite.BlendFactor.OneMinusConstantColor => BlendingFactor.OneMinusConstantColor,
        Graphite.BlendFactor.SrcAlphaSaturate => BlendingFactor.SrcAlphaSaturate,
        _ => throw new NotSupportedException($"Unsupported blend factor: {factor}"),
    };

    private static BlendEquationModeEXT GetBlendEquation(BlendOp op) => op switch
    {
        Graphite.BlendOp.Add => BlendEquationModeEXT.FuncAdd,
        Graphite.BlendOp.Subtract => BlendEquationModeEXT.FuncSubtract,
        Graphite.BlendOp.ReverseSubtract => BlendEquationModeEXT.FuncReverseSubtract,
        Graphite.BlendOp.Min => BlendEquationModeEXT.Min,
        Graphite.BlendOp.Max => BlendEquationModeEXT.Max,
        _ => throw new NotSupportedException($"Unsupported blend operation: {op}"),
    };

    internal static PrimitiveType GetPrimitiveType(PrimitiveTopology topology) => topology switch
    {
        PrimitiveTopology.PointList => PrimitiveType.Points,
        PrimitiveTopology.LineList => PrimitiveType.Lines,
        PrimitiveTopology.LineStrip => PrimitiveType.LineStrip,
        PrimitiveTopology.TriangleList => PrimitiveType.Triangles,
        PrimitiveTopology.TriangleStrip => PrimitiveType.TriangleStrip,
        _ => throw new NotSupportedException($"Unsupported primitive topology: {topology}"),
    };

    protected override void DisposeResources()
    {
        if (VAOHandle != 0)
        {
            _device.GL.DeleteVertexArray(VAOHandle);
            VAOHandle = 0;
        }

        if (ProgramHandle != 0)
        {
            _device.GL.DeleteProgram(ProgramHandle);
            ProgramHandle = 0;
        }
    }
}

/// <summary>
/// OpenGL implementation of a compute pipeline state.
/// </summary>
public class GLComputePipelineState : ComputePipelineState
{
    private readonly GLGraphiteDevice _device;
    internal uint ProgramHandle { get; private set; }

    internal GLComputePipelineState(GLGraphiteDevice device, in ComputePipelineStateDescriptor descriptor)
    {
        _device = device;
        DebugName = descriptor.DebugName;

        ProgramHandle = _device.GL.CreateProgram();

        if (descriptor.ComputeShader is not GLShaderModule computeShader)
            throw new ArgumentException("Compute shader is required.", nameof(descriptor));

        _device.GL.AttachShader(ProgramHandle, computeShader.Handle);
        _device.GL.LinkProgram(ProgramHandle);

        _device.GL.GetProgram(ProgramHandle, ProgramPropertyARB.LinkStatus, out int success);
        if (success == 0)
        {
            string infoLog = _device.GL.GetProgramInfoLog(ProgramHandle);
            _device.GL.DeleteProgram(ProgramHandle);
            ProgramHandle = 0;
            throw new InvalidOperationException($"Compute shader program linking failed:\n{infoLog}");
        }

        _device.GL.DetachShader(ProgramHandle, computeShader.Handle);
    }

    internal void Apply()
    {
        _device.GL.UseProgram(ProgramHandle);
    }

    protected override void DisposeResources()
    {
        if (ProgramHandle != 0)
        {
            _device.GL.DeleteProgram(ProgramHandle);
            ProgramHandle = 0;
        }
    }
}
