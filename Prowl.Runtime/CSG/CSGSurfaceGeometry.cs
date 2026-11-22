// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>Where a texture sits within a surface, for the nine way justify.</summary>
public enum SurfaceAlign
{
    Min,
    Center,
    Max,
}

/// <summary>Texture space arithmetic for one surface, where <c>uv = a / scale + offset</c> and <c>a</c> is measured along the rotated basis axis.</summary>
public static class CSGSurfaceGeometry
{
    /// <summary>The surface's span along its own basis axes, before scale and offset. Corners must be in model space.</summary>
    public static bool TryExtent(Float3 normal, float rotationDegrees, IReadOnlyList<Float3> corners,
                                 out Float2 min, out Float2 max)
    {
        min = Float2.Zero;
        max = Float2.Zero;
        if (corners.Count == 0) return false;

        Basis(normal, rotationDegrees, out Float3 u, out Float3 v);

        min = new Float2(float.MaxValue, float.MaxValue);
        max = new Float2(float.MinValue, float.MinValue);

        foreach (Float3 p in corners)
        {
            float a = Float3.Dot(p, u), b = Float3.Dot(p, v);
            min = new Float2(MathF.Min(min.X, a), MathF.Min(min.Y, b));
            max = new Float2(MathF.Max(max.X, a), MathF.Max(max.Y, b));
        }

        return true;
    }

    /// <summary>The rotated projection basis, matching <see cref="SurfaceTexSpace.FromFace"/>.</summary>
    public static void Basis(Float3 normal, float rotationDegrees, out Float3 u, out Float3 v)
    {
        SurfaceTexSpace.BasisFor(normal, out u, out v);
        if (rotationDegrees == 0f) return;

        float r = rotationDegrees * (MathF.PI / 180f);
        float c = MathF.Cos(r), s = MathF.Sin(r);
        (u, v) = (u * c + v * s, v * c - u * s);
    }

    /// <summary>Rebases a texture space so it accepts points transformed by <c>M</c>, such as brush space into model space.</summary>
    public static SurfaceTexSpace Rebase(SurfaceTexSpace space, Float4x4 toSpace)
    {
        return new SurfaceTexSpace(RebaseAxis(space.UAxis, toSpace), RebaseAxis(space.VAxis, toSpace));
    }

    private static Float4 RebaseAxis(Float4 axis, Float4x4 m)
    {
        Float3 n = new(axis.X, axis.Y, axis.Z);

        // Transposed 3x3: each component is the axis dotted with a column.
        Float3 rotated = new(
            Float3.Dot(m.c0.XYZ, n),
            Float3.Dot(m.c1.XYZ, n),
            Float3.Dot(m.c2.XYZ, n));

        return new Float4(rotated.X, rotated.Y, rotated.Z, axis.W + Float3.Dot(m.c3.XYZ, n));
    }

    /// <summary>Scales the texture so it spans the surface exactly once on the chosen axes.</summary>
    public static SurfaceData Fit(SurfaceData data, Float2 min, Float2 max, bool u, bool v, float texelScale)
    {
        Float2 effective = Effective(data, texelScale);

        float scaleU = effective.X, scaleV = effective.Y, offsetU = data.Offset.X, offsetV = data.Offset.Y;
        if (u) Fit1D(min.X, max.X, ref scaleU, ref offsetU);
        if (v) Fit1D(min.Y, max.Y, ref scaleV, ref offsetV);

        data.Offset = new Float2(offsetU, offsetV);
        data.Scale = FromEffective(new Float2(scaleU, scaleV), texelScale);
        return data;
    }

    private static void Fit1D(float min, float max, ref float scale, ref float offset)
    {
        float span = max - min;
        if (MathF.Abs(span) < 1e-6f) return;   // an edge-on surface has nothing to fit to

        scale = span;
        offset = -min / span;
    }

    /// <summary>Slides the texture so the surface sits at a given place within one tile.</summary>
    public static SurfaceData Justify(SurfaceData data, Float2 min, Float2 max,
                                      SurfaceAlign horizontal, SurfaceAlign vertical, float texelScale)
    {
        Float2 effective = Effective(data, texelScale);

        data.Offset = new Float2(Justify1D(min.X, max.X, effective.X, horizontal),
                                 Justify1D(min.Y, max.Y, effective.Y, vertical));

        return data;
    }

    private static float Justify1D(float min, float max, float scale, SurfaceAlign align)
    {
        if (MathF.Abs(scale) < 1e-6f) return 0f;

        return align switch
        {
            SurfaceAlign.Min => -min / scale,
            SurfaceAlign.Max => 1f - max / scale,
            _ => 0.5f - (min + max) * 0.5f / scale,
        };
    }

    /// <summary>Mirrors the texture on the chosen axes, keeping it over the same part of the surface.</summary>
    public static SurfaceData Flip(SurfaceData data, Float2 min, Float2 max, bool u, bool v, float texelScale)
    {
        Float2 effective = Effective(data, texelScale);

        float scaleU = effective.X, scaleV = effective.Y, offsetU = data.Offset.X, offsetV = data.Offset.Y;
        if (u) Flip1D(min.X, max.X, ref scaleU, ref offsetU);
        if (v) Flip1D(min.Y, max.Y, ref scaleV, ref offsetV);

        data.Offset = new Float2(offsetU, offsetV);
        data.Scale = FromEffective(new Float2(scaleU, scaleV), texelScale);
        return data;
    }

    private static void Flip1D(float min, float max, ref float scale, ref float offset)
    {
        if (MathF.Abs(scale) < 1e-6f) return;

        offset += (min + max) / scale;
        scale = -scale;
    }

    /// <summary>Multiplies the texture scale, anchored so the surface's near edge stays put.</summary>
    public static SurfaceData ScaleBy(SurfaceData data, Float2 min, Float2 factor, float texelScale)
    {
        Float2 effective = Effective(data, texelScale);

        float scaleU = effective.X, scaleV = effective.Y, offsetU = data.Offset.X, offsetV = data.Offset.Y;
        ScaleBy1D(min.X, factor.X, ref scaleU, ref offsetU);
        ScaleBy1D(min.Y, factor.Y, ref scaleV, ref offsetV);

        data.Offset = new Float2(offsetU, offsetV);
        data.Scale = FromEffective(new Float2(scaleU, scaleV), texelScale);
        return data;
    }

    private static void ScaleBy1D(float min, float factor, ref float scale, ref float offset)
    {
        if (MathF.Abs(scale) < 1e-6f || MathF.Abs(factor) < 1e-6f) return;

        float scaled = scale * factor;
        offset += min / scale - min / scaled;
        scale = scaled;
    }

    public static SurfaceData Reset(SurfaceData data, bool u, bool v)
    {
        if (u) { data.Scale = new Float2(1f, data.Scale.Y); data.Offset = new Float2(0f, data.Offset.Y); }
        if (v) { data.Scale = new Float2(data.Scale.X, 1f); data.Offset = new Float2(data.Offset.X, 0f); }
        if (u && v) data.Rotation = 0f;
        return data;
    }

    /// <summary>The scale the projection uses, the surface's tile scale combined with the model's texel scale.</summary>
    private static Float2 Effective(in SurfaceData data, float texelScale)
    {
        float texel = texelScale > 0f ? texelScale : 1f;
        return new Float2(NonZero(data.Scale.X) / texel, NonZero(data.Scale.Y) / texel);
    }

    private static Float2 FromEffective(Float2 effective, float texelScale)
    {
        float texel = texelScale > 0f ? texelScale : 1f;
        return new Float2(effective.X * texel, effective.Y * texel);
    }

    private static float NonZero(float value) => value == 0f ? 1f : value;
}
