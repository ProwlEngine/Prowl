// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Post-effect / screen-space utility nodes depth fades, dithering, world-position
// reconstruction, and motion reprojection. Thin wrappers over the helpers already
// present in Prowl's .glsl includes (Shadow.glsl, Fragment.glsl, Lighting.glsl).

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

internal static class PostEffectAccents
{
    /// <summary>Cool blue-grey used by depth / screen-space / reconstruction nodes.</summary>
    public static readonly System.Drawing.Color PostEffect =
        System.Drawing.Color.FromArgb(255, 120, 170, 210);
}

// ═════════════════════════════════════════════════════════════════════════════
// Camera Depth Fade
// 0 at the near plane → 1 at (near + Length). Useful for fading objects out as
// they approach the camera (e.g. FPS weapon held too close, first-person body).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Returns a 0..1 gradient that grows with the surface's distance from the
/// camera near plane. 0 at the near plane, 1 at <c>near + Length</c>.
/// Use as an alpha modulator to dissolve out close-to-camera geometry.
/// </summary>
public sealed class CameraDepthFadeNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Camera Depth Fade";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => PostEffectAccents.PostEffect;

    protected override void DefineNode()
    {
        AddInput<float>("Length", 10f, required: true,
            tooltip: "World-space distance from the near plane over which the fade ramps from 0 to 1.");
        AddInput<float>("Offset", 0f,
            tooltip: "Extra bias added to the near plane before the fade starts (world-space units).");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        ctx.Includes.Add("Lighting");
        // Works in both stages compute worldPos inline in vertex.
        string posExpr;
        if (s == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            posExpr = "TransformPosition(vertexPosition)";
        }
        else
        {
            ctx.Varyings.Add(("worldPos", "vec3"));
            posExpr = "worldPos";
        }

        var len = ctx.EvaluateInputAs(GetInput("Length")!, ShaderType.Float);
        var off = ctx.EvaluateInputAs(GetInput("Offset")!, ShaderType.Float);
        // _ProjectionParams.y = near plane. distance(camera, surface) is the eye-space depth.
        return $"clamp((length(_WorldSpaceCameraPos.xyz - {posExpr}) - (_ProjectionParams.y + {off})) / {len}, 0.0, 1.0)";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// Depth Fade (soft-particle)
// 0 when the surface coincides with the scene geometry behind, 1 when it is
// `FadeDistance` world-units in front. Feed into alpha to soften particle /
// billboard intersections with solid geometry.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Soft-particle style depth fade returns a 0..1 gradient based on how far
/// this fragment is in front of the geometry written into the depth pre-pass.
/// Requires the camera to have a depth texture enabled (DepthTextureMode != None),
/// otherwise reads zeros and the fade is uniformly 0.
/// </summary>
public sealed class DepthFadeNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Depth Fade";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => PostEffectAccents.PostEffect;

    protected override void DefineNode()
    {
        AddInput<float>("Fade Distance", 1f, required: true,
            tooltip: "World-space distance over which the fade ramps from 0 at the scene surface to 1.");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "0.0";
        ctx.Includes.Add("Fragment"); // for linearizeDepthFromProjection
        ctx.Includes.Add("ShaderVariables");
        ctx.Uniforms.Add("uniform sampler2D _CameraDepthTexture;");

        var dist = ctx.EvaluateInputAs(GetInput("Fade Distance")!, ShaderType.Float);

        // Compute once per node the same value is typically wired into Alpha,
        // Emission, and maybe a dissolve so re-evaluating would be wasteful.
        var local = $"_depFade{Id:N}";
        ctx.EmitOnce("depfade:" + local, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    vec2 {local}_suv = gl_FragCoord.xy / _ScreenParams.xy;");
            ctx.BodyPrelude.AppendLine(
                $"    float {local}_sd  = linearizeDepthFromProjection(texture(_CameraDepthTexture, {local}_suv).r);");
            ctx.BodyPrelude.AppendLine(
                $"    float {local}_pd  = linearizeDepthFromProjection(gl_FragCoord.z);");
            ctx.BodyPrelude.AppendLine(
                $"    float {local} = clamp(({local}_sd - {local}_pd) / max({dist}, 1e-5), 0.0, 1.0);");
        });
        return local;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// Reconstruct World Position wraps Shadow.glsl's WorldPosFromDepth
// Given a screen UV and a depth value, returns the world-space position of
// whatever geometry drew there. Useful for screen-space post effects that
// need to reason about scene geometry (SSAO, screen-space fog, etc.).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Reconstruct the world-space position of the fragment under a screen UV
/// using the scene depth buffer. Inverts the view-projection to recover xyz.
/// </summary>
/// <remarks>
/// Uses Prowl's <c>WorldPosFromDepth</c> helper from Shadow.glsl. When no UV
/// is connected, the fragment's own screen UV (<c>gl_FragCoord.xy / _ScreenParams.xy</c>)
/// is used; when no Depth is connected, the depth texture is sampled at that
/// UV. Requires the camera to have a depth pre-pass.
/// </remarks>
public sealed class ReconstructWorldPositionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Reconstruct World Position";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => PostEffectAccents.PostEffect;

    protected override void DefineNode()
    {
        AddInput<Float2>("UV",    Float2.Zero,
            tooltip: "Screen-space UV (0..1). Defaults to this fragment's own screen position.");
        AddInput<float>("Depth",  0f,
            tooltip: "Raw depth in [0, 1]. Defaults to _CameraDepthTexture sampled at UV.");
        AddOutput<Float3>("XYZ");
        AddOutput<float>("X");
        AddOutput<float>("Y");
        AddOutput<float>("Z");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage s, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title))
            return outputPort.Name == "XYZ" ? "vec3(0.0)" : "0.0";
        ctx.Includes.Add("Shadow"); // WorldPosFromDepth
        ctx.Includes.Add("ShaderVariables");
        ctx.Uniforms.Add("uniform sampler2D _CameraDepthTexture;");

        var local = $"_wpFromD{Id:N}";
        ctx.EmitOnce("wpfromd:" + local, () =>
        {
            string uv = ctx.IsConnected(GetInput("UV")!)
                ? ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2)
                : "(gl_FragCoord.xy / _ScreenParams.xy)";
            string depth = ctx.IsConnected(GetInput("Depth")!)
                ? ctx.EvaluateInputAs(GetInput("Depth")!, ShaderType.Float)
                : $"texture(_CameraDepthTexture, {uv}).r";
            ctx.BodyPrelude.AppendLine($"    vec3 {local} = WorldPosFromDepth({depth}, {uv});");
        });
        return outputPort.Name switch
        {
            "X" => $"{local}.x", "Y" => $"{local}.y", "Z" => $"{local}.z",
            _   => local,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => outputPort.Name == "XYZ" ? ShaderType.Vec3 : ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// Reproject UV wraps Fragment.glsl's Reproject helper
// Given a screen UV + depth + a previous-frame view-projection, returns the UV
// where the same world point appeared in the previous frame. Core building
// block for motion blur and TAA-style effects.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Returns the previous-frame screen UV for the world point currently under
/// <c>UV</c>+<c>Depth</c>. Uses Prowl's <c>prowl_PrevViewProj</c> matrix
/// (uploaded every frame from the global UBO) unless the user supplies a
/// different matrix through the Custom Code node.
/// </summary>
public sealed class ReprojectUVNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Reproject UV";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => PostEffectAccents.PostEffect;

    protected override void DefineNode()
    {
        AddInput<Float2>("UV",    Float2.Zero,
            tooltip: "Current-frame screen UV. Defaults to this fragment's screen position.");
        AddInput<float>("Depth",  0f,
            tooltip: "Current-frame raw depth. Defaults to _CameraDepthTexture sample at UV.");
        AddOutput<Float2>("Prev UV");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "vec2(0.0)";
        ctx.Includes.Add("Fragment"); // Reproject
        ctx.Includes.Add("ShaderVariables"); // prowl_PrevViewProj
        ctx.Uniforms.Add("uniform sampler2D _CameraDepthTexture;");

        string uv = ctx.IsConnected(GetInput("UV")!)
            ? ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2)
            : "(gl_FragCoord.xy / _ScreenParams.xy)";
        string depth = ctx.IsConnected(GetInput("Depth")!)
            ? ctx.EvaluateInputAs(GetInput("Depth")!, ShaderType.Float)
            : $"texture(_CameraDepthTexture, {uv}).r";

        // Reproject returns vec3 (xy: screen UV, z: reprojected depth). We drop .z here
        // and just return the xy for UV-space use; the Z is available via Custom Code
        // if someone needs to do their own validity test.
        return $"Reproject({uv}, {depth}, prowl_PrevViewProj).xy";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec2;
}

// ═════════════════════════════════════════════════════════════════════════════
// Dither
// Stable per-pixel 0..1 value used for stochastic transparency / stippling /
// colour quantization. Four modes, all in the [0, 1) range so users can
// compare directly against an alpha threshold.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Which hash function drives the <see cref="DitherNode"/>'s output.</summary>
public enum DitherPattern
{
    /// <summary>4×4 ordered Bayer matrix 16 distinct levels, classic "staircase" pattern.
    /// Cheap but has obvious tiling at magnification.</summary>
    Bayer4x4 = 0,
    /// <summary>8×8 ordered Bayer matrix 64 distinct levels, smoother than 4×4.</summary>
    Bayer8x8 = 1,
    /// <summary>Interleaved Gradient Noise Jorge Jimenez's golden-ratio hash. Cheapest
    /// uniform-distribution option; great for temporal jitter.</summary>
    InterleavedGradient = 2,
    /// <summary>Per-pixel hash via Fragment.glsl's hash1. Uniform but non-repeating,
    /// so the pattern won't visibly tile.</summary>
    Hash = 3,
}

/// <summary>
/// Stable per-pixel dither value in [0, 1). Feed into an alpha-compare
/// (<c>discard if alpha &lt; dither</c>) for dithered transparency, or use as
/// a noise source for stippling / quantization effects.
/// </summary>
/// <remarks>
/// Coordinate defaults to <c>gl_FragCoord.xy</c> when unconnected. If you want
/// the pattern to follow the object rather than the screen, feed a vec2 derived
/// from UV or world-position instead.
/// </remarks>
public sealed class DitherNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>Which hash function drives the output.</summary>
    public DitherPattern Pattern = DitherPattern.Bayer4x4;

    /// <summary>Multiplier applied to the dither output useful for softening the
    /// pattern (e.g. 0.5 compresses 0..1 down to 0..0.5).</summary>
    public float Scale = 1f;

    public override string Title => $"Dither · {Pattern}";
    public override string Category => "Arithmetic";
    public override System.Drawing.Color AccentColor => PostEffectAccents.PostEffect;

    protected override void DefineNode()
    {
        AddInput<Float2>("Coord", Float2.Zero,
            tooltip: "Screen-space or object-space 2D coord. Defaults to gl_FragCoord.xy.");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "0.0";
        string coord = ctx.IsConnected(GetInput("Coord")!)
            ? ctx.EvaluateInputAs(GetInput("Coord")!, ShaderType.Vec2)
            : "gl_FragCoord.xy";

        string expr;
        switch (Pattern)
        {
            case DitherPattern.Bayer4x4:
                // 4×4 ordered dither emit the 16-entry matrix once as a top-level const
                // array so multiple Dither nodes share it.
                ctx.EmitOnce("bayer4x4", () =>
                {
                    ctx.TopLevelHelpers.AppendLine(
                        "        const float _bayer4x4[16] = float[16](" +
                        " 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0," +
                        "12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0," +
                        " 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0," +
                        "15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0);");
                });
                expr = $"_bayer4x4[int(mod({coord}.y, 4.0)) * 4 + int(mod({coord}.x, 4.0))]";
                break;

            case DitherPattern.Bayer8x8:
                ctx.EmitOnce("bayer8x8", () =>
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("        const float _bayer8x8[64] = float[64](");
                    // Standard 8×8 Bayer matrix normalised to [0, 1).
                    int[] m = {
                         0, 32,  8, 40,  2, 34, 10, 42,
                        48, 16, 56, 24, 50, 18, 58, 26,
                        12, 44,  4, 36, 14, 46,  6, 38,
                        60, 28, 52, 20, 62, 30, 54, 22,
                         3, 35, 11, 43,  1, 33,  9, 41,
                        51, 19, 59, 27, 49, 17, 57, 25,
                        15, 47,  7, 39, 13, 45,  5, 37,
                        63, 31, 55, 23, 61, 29, 53, 21,
                    };
                    for (int i = 0; i < 64; i++)
                    {
                        sb.Append("            ").Append(m[i]).Append(".0/64.0");
                        if (i < 63) sb.Append(',');
                        sb.AppendLine();
                    }
                    sb.AppendLine("        );");
                    ctx.TopLevelHelpers.Append(sb);
                });
                expr = $"_bayer8x8[int(mod({coord}.y, 8.0)) * 8 + int(mod({coord}.x, 8.0))]";
                break;

            case DitherPattern.InterleavedGradient:
                ctx.Includes.Add("Shadow"); // InterleavedGradientNoise lives there
                expr = $"InterleavedGradientNoise({coord})";
                break;

            case DitherPattern.Hash:
            default:
                ctx.Includes.Add("Fragment");
                expr = $"hash1({coord})";
                break;
        }

        if (System.Math.Abs(Scale - 1f) > 1e-6f)
            expr = $"({expr} * {ShaderGenContext.Fmt(Scale)})";
        return expr;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}
