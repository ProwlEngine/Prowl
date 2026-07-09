// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.OpenGL;

namespace Prowl.Runtime;

/// <summary>
/// Translates a <see cref="RasterizerState"/> struct into GL state changes.
///
/// Today's implementation does a full apply every time; the executor's <c>_rasterDirty</c>
/// flag prevents calling this redundantly when the state hasn't been changed via a
/// <c>SetRasterState</c> opcode. A finer-grained per-field diff lives one level up in
/// the executor if it ever shows up in profiling.
/// </summary>
internal static class RasterStateApply
{
    public static void Apply(in RasterizerState state)
    {
        var gl = Graphics.GL;

        // Depth
        if (state.DepthTest) gl.Enable(EnableCap.DepthTest);
        else gl.Disable(EnableCap.DepthTest);
        gl.DepthMask(state.DepthWrite);
        gl.DepthFunc(DepthFuncOf(state.Depth));

        // Blend
        if (state.DoBlend) gl.Enable(EnableCap.Blend);
        else gl.Disable(EnableCap.Blend);
        gl.BlendFunc(BlendOf(state.BlendSrc), BlendOf(state.BlendDst));
        gl.BlendEquation(BlendModeOf(state.Blend));

        // Cull
        if (state.CullFace != RasterizerState.PolyFace.None)
        {
            gl.Enable(EnableCap.CullFace);
            gl.CullFace(CullOf(state.CullFace));
        }
        else
        {
            gl.Disable(EnableCap.CullFace);
        }

        // Winding
        gl.FrontFace(WindingOf(state.Winding));

        // Stencil
        if (state.StencilEnabled)
        {
            gl.Enable(EnableCap.StencilTest);
            gl.StencilFunc(StencilFuncOf(state.StencilFunc), state.StencilRef, (uint)state.StencilReadMask);
            gl.StencilOp(StencilOpOf(state.StencilFailOp), StencilOpOf(state.StencilZFailOp), StencilOpOf(state.StencilPassOp));
            gl.StencilMask((uint)state.StencilWriteMask);
        }
        else
        {
            gl.Disable(EnableCap.StencilTest);
        }
    }

    private static DepthFunction DepthFuncOf(RasterizerState.DepthMode m) => m switch
    {
        RasterizerState.DepthMode.Never => DepthFunction.Never,
        RasterizerState.DepthMode.Less => DepthFunction.Less,
        RasterizerState.DepthMode.Equal => DepthFunction.Equal,
        RasterizerState.DepthMode.Lequal => DepthFunction.Lequal,
        RasterizerState.DepthMode.Greater => DepthFunction.Greater,
        RasterizerState.DepthMode.Notequal => DepthFunction.Notequal,
        RasterizerState.DepthMode.Gequal => DepthFunction.Gequal,
        RasterizerState.DepthMode.Always => DepthFunction.Always,
        _ => DepthFunction.Lequal,
    };

    private static BlendingFactor BlendOf(RasterizerState.Blending b) => b switch
    {
        RasterizerState.Blending.Zero => BlendingFactor.Zero,
        RasterizerState.Blending.One => BlendingFactor.One,
        RasterizerState.Blending.SrcColor => BlendingFactor.SrcColor,
        RasterizerState.Blending.OneMinusSrcColor => BlendingFactor.OneMinusSrcColor,
        RasterizerState.Blending.DstColor => BlendingFactor.DstColor,
        RasterizerState.Blending.OneMinusDstColor => BlendingFactor.OneMinusDstColor,
        RasterizerState.Blending.SrcAlpha => BlendingFactor.SrcAlpha,
        RasterizerState.Blending.OneMinusSrcAlpha => BlendingFactor.OneMinusSrcAlpha,
        RasterizerState.Blending.DstAlpha => BlendingFactor.DstAlpha,
        RasterizerState.Blending.OneMinusDstAlpha => BlendingFactor.OneMinusDstAlpha,
        RasterizerState.Blending.ConstantColor => BlendingFactor.ConstantColor,
        RasterizerState.Blending.OneMinusConstantColor => BlendingFactor.OneMinusConstantColor,
        RasterizerState.Blending.ConstantAlpha => BlendingFactor.ConstantAlpha,
        RasterizerState.Blending.OneMinusConstantAlpha => BlendingFactor.OneMinusConstantAlpha,
        RasterizerState.Blending.SrcAlphaSaturate => BlendingFactor.SrcAlphaSaturate,
        _ => BlendingFactor.Zero,
    };

    private static BlendEquationModeEXT BlendModeOf(RasterizerState.BlendMode m) => m switch
    {
        RasterizerState.BlendMode.Add => BlendEquationModeEXT.FuncAdd,
        RasterizerState.BlendMode.Subtract => BlendEquationModeEXT.FuncSubtract,
        RasterizerState.BlendMode.ReverseSubtract => BlendEquationModeEXT.FuncReverseSubtract,
        RasterizerState.BlendMode.Min => BlendEquationModeEXT.Min,
        RasterizerState.BlendMode.Max => BlendEquationModeEXT.Max,
        _ => BlendEquationModeEXT.FuncAdd,
    };

    private static TriangleFace CullOf(RasterizerState.PolyFace f) => f switch
    {
        RasterizerState.PolyFace.Front => TriangleFace.Front,
        RasterizerState.PolyFace.Back => TriangleFace.Back,
        RasterizerState.PolyFace.FrontAndBack => TriangleFace.FrontAndBack,
        _ => TriangleFace.Back,
    };

    private static FrontFaceDirection WindingOf(RasterizerState.WindingOrder w) => w switch
    {
        RasterizerState.WindingOrder.CW => FrontFaceDirection.CW,
        RasterizerState.WindingOrder.CCW => FrontFaceDirection.Ccw,
        _ => FrontFaceDirection.CW,
    };

    private static StencilFunction StencilFuncOf(RasterizerState.StencilFunction f) => f switch
    {
        RasterizerState.StencilFunction.Never => StencilFunction.Never,
        RasterizerState.StencilFunction.Less => StencilFunction.Less,
        RasterizerState.StencilFunction.Equal => StencilFunction.Equal,
        RasterizerState.StencilFunction.Lequal => StencilFunction.Lequal,
        RasterizerState.StencilFunction.Greater => StencilFunction.Greater,
        RasterizerState.StencilFunction.Notequal => StencilFunction.Notequal,
        RasterizerState.StencilFunction.Gequal => StencilFunction.Gequal,
        RasterizerState.StencilFunction.Always => StencilFunction.Always,
        _ => StencilFunction.Always,
    };

    private static StencilOp StencilOpOf(RasterizerState.StencilOp o) => o switch
    {
        RasterizerState.StencilOp.Keep => StencilOp.Keep,
        RasterizerState.StencilOp.Zero => StencilOp.Zero,
        RasterizerState.StencilOp.Replace => StencilOp.Replace,
        RasterizerState.StencilOp.Incr => StencilOp.Incr,
        RasterizerState.StencilOp.IncrWrap => StencilOp.IncrWrap,
        RasterizerState.StencilOp.Decr => StencilOp.Decr,
        RasterizerState.StencilOp.DecrWrap => StencilOp.DecrWrap,
        RasterizerState.StencilOp.Invert => StencilOp.Invert,
        _ => StencilOp.Keep,
    };
}
