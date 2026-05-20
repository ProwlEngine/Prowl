// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs;

/// <summary>
/// The set of types a shader-graph wire can carry. Maps 1:1 to the GLSL keyword used
/// in the generated source. Mirrors Prowl's existing shader Property types so material
/// uniforms line up cleanly.
/// </summary>
public enum ShaderType
{
    Float,
    Vec2,
    Vec3,
    Vec4,
    Color,    // visually a color picker, generated as vec4
    Int,
    Bool,
    Mat3,
    Mat4,
    Sampler2D,
    SamplerCube,
}

/// <summary>Static helpers translating between <see cref="ShaderType"/>, GLSL keywords,
/// and Prowl C# types kept central so node implementations don't reinvent it.</summary>
public static class ShaderTypeUtil
{
    /// <summary>GLSL keyword used in declarations / uniforms (e.g. <c>vec3</c>).</summary>
    public static string ToGlsl(ShaderType t) => t switch
    {
        ShaderType.Float       => "float",
        ShaderType.Vec2        => "vec2",
        ShaderType.Vec3        => "vec3",
        ShaderType.Vec4        => "vec4",
        ShaderType.Color       => "vec4",
        ShaderType.Int         => "int",
        ShaderType.Bool        => "bool",
        ShaderType.Mat3        => "mat3",
        ShaderType.Mat4        => "mat4",
        ShaderType.Sampler2D   => "sampler2D",
        ShaderType.SamplerCube => "samplerCube",
        _ => "float",
    };

    /// <summary>Number of float channels; samplers report 0.</summary>
    public static int Channels(ShaderType t) => t switch
    {
        ShaderType.Float => 1,
        ShaderType.Vec2  => 2,
        ShaderType.Vec3  => 3,
        ShaderType.Vec4  => 4,
        ShaderType.Color => 4,
        ShaderType.Int   => 1,
        ShaderType.Bool  => 1,
        _ => 0,
    };

    /// <summary>Map back to the Prowl-side runtime C# type used by ports.</summary>
    public static Type ToCsType(ShaderType t) => t switch
    {
        ShaderType.Float       => typeof(float),
        ShaderType.Vec2        => typeof(Float2),
        ShaderType.Vec3        => typeof(Float3),
        ShaderType.Vec4        => typeof(Float4),
        ShaderType.Color       => typeof(Color),
        ShaderType.Int         => typeof(int),
        ShaderType.Bool        => typeof(bool),
        ShaderType.Mat3        => typeof(Float3x3),
        ShaderType.Mat4        => typeof(Float4x4),
        ShaderType.Sampler2D   => typeof(Resources.Texture2D),
        ShaderType.SamplerCube => typeof(Resources.Texture3D), // closest cubemap stand-in
        _ => typeof(float),
    };

    /// <summary>The Prowl property keyword used when declaring this in a Properties{} block.
    /// Only types that can be a material property return non-null.</summary>
    public static string? ToPropertyKeyword(ShaderType t) => t switch
    {
        ShaderType.Float       => "Float",
        ShaderType.Vec2        => "Vector2",
        ShaderType.Vec3        => "Vector3",
        ShaderType.Vec4        => "Vector4",
        ShaderType.Color       => "Color",
        ShaderType.Int         => "Int",
        ShaderType.Mat4        => "Matrix",
        ShaderType.Sampler2D   => "Texture2D",
        ShaderType.SamplerCube => "Texture3D",
        _ => null,
    };

    /// <summary>True when <paramref name="t"/> is one of the scalar-or-vector numeric
    /// types that promotion can interchange: Float/Int/Bool/Vec2/Vec3/Vec4/Color.
    /// Samplers and matrices are intentionally excluded they need explicit typing.</summary>
    public static bool IsNumeric(ShaderType t)
        => t == ShaderType.Float || t == ShaderType.Int || t == ShaderType.Bool
        || t == ShaderType.Vec2 || t == ShaderType.Vec3 || t == ShaderType.Vec4
        || t == ShaderType.Color;

    /// <summary>
    /// Promote <paramref name="expr"/> from <paramref name="from"/> to <paramref name="to"/>.
    /// Covers every numeric-to-numeric pair: scalar casts (Int/Bool <-> Float), scalar
    /// broadcasts (Float -> Vec2/3/4), vector truncation (VecN -> VecM where M&lt;N),
    /// vector extension (VecM -> VecN where N&gt;M; alpha = 1 when target is vec4),
    /// and the trivial Color <-> Vec4 no-op.
    /// </summary>
    /// <remarks>
    /// Non-numeric pairs (Sampler2D -> Float, Mat3 -> Vec3, etc.) don't have a sensible
    /// promotion we return the expression unchanged and leave the generated GLSL to
    /// surface a type error. Caller should diagnose these at a higher level if it wants
    /// a nicer error message than "cannot convert sampler2D to float".
    /// </remarks>
    public static string Promote(string expr, ShaderType from, ShaderType to)
    {
        if (from == to) return expr;

        // Color and Vec4 share the same GLSL type bit-for-bit compatible.
        if ((from == ShaderType.Color && to == ShaderType.Vec4) ||
            (from == ShaderType.Vec4 && to == ShaderType.Color)) return expr;

        // Non-numeric -> non-numeric (e.g. samplers, matrices): can't promote. Let the
        // GLSL compiler reject it no sane textual transformation exists.
        if (!IsNumeric(from) || !IsNumeric(to)) return expr;

        int fc = Channels(from), tc = Channels(to);

        // Canonicalise any bool/int scalar to float first so downstream broadcast code
        // doesn't need to special-case "int(x)" vs "float(x)".
        if (fc == 1 && from != ShaderType.Float)
            expr = $"float({expr})";

        // Scalar broadcast: 1 -> N produces vecN(x).
        if (fc == 1 && tc > 1) return $"{ToGlsl(to)}({expr})";

        // Target scalar: take the red channel, cast back if we need Int/Bool.
        if (tc == 1)
        {
            var red = $"({expr}).x";
            return to switch
            {
                ShaderType.Int  => $"int({red})",
                ShaderType.Bool => $"({red} != 0.0)",
                _               => red,
            };
        }

        // Vector truncation: VecN -> VecM, keep leading channels.
        if (fc > tc) return $"({expr})." + "xyzw".Substring(0, tc);

        // Vector extension fill with 0, alpha = 1 when target is vec4 / Color so an
        // RGB -> RGBA promotion yields an opaque colour by default.
        if (fc == 2 && tc == 3) return $"vec3({expr}, 0.0)";
        if (fc == 2 && tc == 4) return $"vec4({expr}, 0.0, 1.0)";
        if (fc == 3 && tc == 4) return $"vec4({expr}, 1.0)";

        return expr;
    }
}
