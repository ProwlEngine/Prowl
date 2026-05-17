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
// Surface Depth
// View-space (linear) depth of the current fragment. Two modes:
//   EyeSpace  view-space Z in world units. Distance from camera plane to
//               the fragment along the view axis. NOT the same as the existing
//               DepthNode which returns Euclidean distance length(cam-pos).
//   Linear01  same value scaled by 1/far so 0 = camera, 1 = far plane.
// Both modes work in vertex (via prowl_MatV) and fragment (via the linearised
// gl_FragCoord.z fast path).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Selects between the eye-space and 0..1 linear-depth conventions.</summary>
public enum SurfaceDepthMode
{
    /// <summary>View-space depth in world units. 0 at the camera plane, growing
    /// linearly with distance along the view axis.</summary>
    EyeSpace = 0,
    /// <summary>Eye-space depth scaled by <c>1 / farPlane</c>. 0 at camera plane,
    /// 1 at the far plane. Useful as a UV / fade alpha without per-camera tuning.</summary>
    Linear01 = 1,
}

/// <summary>
/// Linear (view-space) depth of the current fragment. <see cref="Mode"/> picks
/// between raw eye-space units and the normalised 0..1 form. This is distinct
/// from <c>DepthNode</c>, which computes Euclidean distance from the camera
/// that distance grows faster off-axis. Use this node whenever you'd compare
/// against <c>_CameraDepthTexture</c> or want a perspective-correct fade.
/// </summary>
public sealed class SurfaceDepthNode : Node, IShaderNode, IShaderGraphNode
{
    public SurfaceDepthMode Mode = SurfaceDepthMode.EyeSpace;

    public override string Title    => Mode == SurfaceDepthMode.Linear01
        ? "Surface Depth · 0-1"
        : "Surface Depth · Eye";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => PostEffectAccents.PostEffect;

    protected override void DefineNode()
    {
        AddOutput<float>("Depth",
            tooltip: "Linear view-space depth. EyeSpace = world units; Linear01 = scaled by 1/far.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");

        string eye;
        if (stage == ShaderStage.Fragment)
        {
            // Fast path the depth buffer already encodes this; linearise the
            // perspective Z back to view-space units. Keeps the math identical
            // to whatever the depth pre-pass writes, so depth comparisons line
            // up exactly.
            ctx.Includes.Add("ProwlCG");
            eye = "linearizeDepthFromProjection(gl_FragCoord.z)";
        }
        else
        {
            // Vertex / tessellation pull the world position through the view
            // matrix and negate the Z to recover view-space depth.
            ctx.Includes.Add("VertexAttributes");
            eye = "-(prowl_MatV * vec4(TransformPosition(vertexPosition), 1.0)).z";
        }

        // _ProjectionParams.w is 1/far (set in RenderPipeline.cs:251), so the
        // Linear01 scaling is one mul rather than a divide.
        return Mode == SurfaceDepthMode.Linear01
            ? $"({eye} * _ProjectionParams.w)"
            : eye;
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// Camera Depth Fade
// 0 at the near plane → 1 at (near + Length). Useful for fading objects out as
// they approach the camera (e.g. FPS weapon held too close, first-person body).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Gradient based on the fragment's distance from the camera near plane:
/// 0 at <c>near + Offset</c>, 1 at <c>near + Offset + Length</c>. <c>Saturate</c>
/// (default true) clamps the output to [0, 1] so the node reads as a drop-in
/// fade alpha; flip it off when you want the unclamped slope so values past
/// the fade range keep growing above 1.
/// </summary>
/// <remarks>
/// <para>The default WorldPos comes from the <c>worldPos</c> varying in
/// fragment, or from the vertex-stage <c>TransformPosition(vertexPosition)</c>
/// when used in vertex / tessellation. Wiring something into <c>WorldPos</c>
/// lets you fade against a custom origin (e.g. a billboard's pivot rather than
/// each pixel).</para>
/// </remarks>
public sealed class CameraDepthFadeNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>Clamp the result to [0, 1]. Default true preserves the
    /// historic behaviour; turn off for the raw signed/unbounded slope.</summary>
    public bool Saturate = true;

    public override string Title => "Camera Depth Fade";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => PostEffectAccents.PostEffect;

    protected override void DefineNode()
    {
        AddInput<float>("Length", 10f, required: true,
            tooltip: "World-space distance from the near plane over which the fade ramps from 0 to 1.");
        AddInput<float>("Offset", 0f,
            tooltip: "Extra bias added to the near plane before the fade starts (world-space units).");
        AddInput<Float3>("WorldPos", Float3.Zero,
            tooltip: "Optional override world-space position. Defaults to this fragment's worldPos.");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        ctx.Includes.Add("Lighting");

        // Position resolution: explicit wire wins; fragment falls back to the
        // worldPos varying; vertex / tessellation compute the world position
        // inline since the varying isn't populated until the fragment stage.
        string posExpr;
        if (ctx.IsConnected(GetInput("WorldPos")!))
        {
            posExpr = ctx.EvaluateInputAs(GetInput("WorldPos")!, ShaderType.Vec3);
        }
        else if (s == ShaderStage.Vertex)
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
        // _ProjectionParams.y = near plane. length(...) is the world-space
        // distance from the camera origin (Euclidean, not view-space Z), so
        // the fade is symmetric around the camera regardless of view direction.
        var raw = $"((length(_WorldSpaceCameraPos.xyz - {posExpr}) - (_ProjectionParams.y + {off})) / max({len}, 1e-5))";
        return Saturate ? $"clamp({raw}, 0.0, 1.0)" : raw;
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
        ctx.Includes.Add("ProwlCG"); // Reproject
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
                ctx.Includes.Add("ProwlCG");
                expr = $"hash1({coord})";
                break;
        }

        if (System.Math.Abs(Scale - 1f) > 1e-6f)
            expr = $"({expr} * {ShaderGenContext.Fmt(Scale)})";
        return expr;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}
