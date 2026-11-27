// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

using Prowl.Vector;

namespace SoftwareRasterizer.Rasterizer;

public class BlendFunction
{
    private readonly Func<Float4, Float4, float> srcFactor;
    private readonly Func<Float4, Float4, float> dstFactor;

    public BlendFunction(Func<Float4, Float4, float> srcFactor, Func<Float4, Float4, float> dstFactor)
    {
        this.srcFactor = srcFactor;
        this.dstFactor = dstFactor;
    }

    public Float4 Blend(Float4 src, Float4 dst)
    {
        float srcF = srcFactor(src, dst);
        float dstF = dstFactor(src, dst);
        return srcF * src + dstF * dst;
    }

    // Predefined blend factors
    public static readonly Func<Float4, Float4, float> Zero = (src, dst) => 0;
    public static readonly Func<Float4, Float4, float> One = (src, dst) => 1;
    public static readonly Func<Float4, Float4, float> SrcAlpha = (src, dst) => src.A;
    public static readonly Func<Float4, Float4, float> OneMinusSrcAlpha = (src, dst) => 1 - src.A;
    public static readonly Func<Float4, Float4, float> DstAlpha = (src, dst) => dst.A;
    public static readonly Func<Float4, Float4, float> OneMinusDstAlpha = (src, dst) => 1 - dst.A;

    // Predefined blend functions
    public static readonly BlendFunction Normal = new BlendFunction(One, Zero);
    public static readonly BlendFunction AlphaBlend = new BlendFunction(SrcAlpha, OneMinusSrcAlpha);
    public static readonly BlendFunction Additive = new BlendFunction(One, One);
}
