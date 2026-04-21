// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Color operations nodes providing Photoshop-style blend modes and HSV/RGB conversions.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

// ─────────────────────────────────────────────────────────────────────────────
// Shared accent colour for all Color nodes
// ─────────────────────────────────────────────────────────────────────────────

internal static class ColorAccents
{
    public static readonly System.Drawing.Color Color = System.Drawing.Color.FromArgb(255, 220, 120, 200); /* pink */
}

// ═════════════════════════════════════════════════════════════════════════════
// HUE NODE
//
// HLSL formula (Evaluate):
//   saturate(3.0*abs(1.0-2.0*frac(h + float3(0.0, -1.0/3.0, 1.0/3.0))) - 1)
//
// GLSL translation:
//   clamp(3.0*abs(1.0 - 2.0*fract(h + vec3(0.0, -1.0/3.0, 1.0/3.0))) - 1.0, 0.0, 1.0)
//
// HLSL→GLSL mapping used throughout this file:
//   frac(x)      → fract(x)
//   saturate(x)  → clamp(x, 0.0, 1.0)
//   lerp(a,b,t)  → mix(a, b, t)
//   float3(...)  → vec3(...)
//   float4(...)  → vec4(...)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Converts a scalar hue value (0..1) to an RGB colour on the hue wheel.
/// </summary>
/// <remarks>
/// The output is the pure saturated colour for the given hue.  Combine with
/// HsvToRgbNode when you also want to control saturation and value.
/// </remarks>
public sealed class HueNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Hue";
    public override string Category => "Color";
    public override System.Drawing.Color AccentColor => ColorAccents.Color;

    protected override void DefineNode()
    {
        AddInput<float>("In", 0f, required: true);
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage stage, ShaderGenContext ctx)
    {
        string h = ctx.EvaluateInputAs(GetInput("In")!, ShaderType.Float);
        return $"clamp(3.0*abs(1.0 - 2.0*fract({h} + vec3(0.0, -1.0/3.0, 1.0/3.0))) - 1.0, 0.0, 1.0)";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// ═════════════════════════════════════════════════════════════════════════════
// HSV → RGB NODE
//
// HLSL formula (Evaluate):
//   (lerp(float3(1,1,1), saturate(3.0*abs(1.0-2.0*frac(h+float3(0.0,-1.0/3.0,1.0/3.0)))-1), s) * v)
//
// GLSL translation:
//   mix(vec3(1.0), clamp(3.0*abs(1.0-2.0*fract(h+vec3(0.0,-1.0/3.0,1.0/3.0)))-1.0,0.0,1.0), s) * v
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Converts Hue/Saturation/Value components to an RGB colour vector.
/// </summary>
/// <remarks>
/// Hue, Saturation, and Value inputs are all expected in 0..1 range.
/// The output is a vec3 RGB colour.
/// </remarks>
public sealed class HsvToRgbNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "HSV to RGB";
    public override string Category => "Color";
    public override System.Drawing.Color AccentColor => ColorAccents.Color;

    protected override void DefineNode()
    {
        AddInput<float>("Hue", 0f, required: true);
        AddInput<float>("Sat", 1f, required: true);
        AddInput<float>("Val", 1f, required: true);
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage stage, ShaderGenContext ctx)
    {
        string h = ctx.EvaluateInputAs(GetInput("Hue")!, ShaderType.Float);
        string s = ctx.EvaluateInputAs(GetInput("Sat")!, ShaderType.Float);
        string v = ctx.EvaluateInputAs(GetInput("Val")!, ShaderType.Float);
        string hueRgb = $"clamp(3.0*abs(1.0 - 2.0*fract({h} + vec3(0.0, -1.0/3.0, 1.0/3.0))) - 1.0, 0.0, 1.0)";
        return $"(mix(vec3(1.0), {hueRgb}, {s}) * {v})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// ═════════════════════════════════════════════════════════════════════════════
// RGB → HSV NODE
//
// Uses a multi-output node (Hue, Sat, Val) with precomputed temp variables.
// The algorithm (Evan Wallace / IQ) works as:
//   k = vec4(0.0, -1.0/3.0, 2.0/3.0, -1.0)
//   p = mix(vec4(c.zy, k.wz), vec4(c.yz, k.xy), step(c.z, c.y))
//   q = mix(vec4(p.xyw, c.x), vec4(c.x, p.yzx), step(p.x, c.x))
//   d = q.x - min(q.w, q.y)
//   e = 1.0e-10
//   hsv = vec3(abs(q.z + (q.w - q.y) / (6.0*d + e)), d / (q.x + e), q.x)
//
// Three output ports ("Hue", "Sat", "Val") use a BodyPrelude temp vec3 so the
// computation runs once regardless of how many outputs are consumed downstream.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Converts an RGB colour to its Hue, Saturation, and Value (HSV) components.
/// </summary>
/// <remarks>
/// All three outputs are in the 0..1 range.  The algorithm emits a single
/// prelude temp variable so the computation is not duplicated when multiple
/// output ports are consumed.
/// </remarks>
public sealed class RgbToHsvNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "RGB to HSV";
    public override string Category => "Color";
    public override System.Drawing.Color AccentColor => ColorAccents.Color;

    protected override void DefineNode()
    {
        AddInput<Float3>("In", Float3.Zero, required: true);
        AddOutput<float>("Hue");
        AddOutput<float>("Sat");
        AddOutput<float>("Val");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage stage, ShaderGenContext ctx)
    {
        // Emit the full HSV computation into a temp vec3 the first time any
        // output port is evaluated; subsequent ports reference the same local.
        // We use the node Id as a stable suffix so multiple instances don't collide.
        string localHsv = $"_hsv_{Id:N}";

        // Emit the decomposition preamble exactly once per compile — ctx.EmitOnce guards
        // the second call when another output port asks for the same locals.
        ctx.EmitOnce("rgb2hsv:" + localHsv, () =>
        {
            string c = ctx.EvaluateInputAs(GetInput("In")!, ShaderType.Vec3);

            // Precomputed variables (translated to GLSL):
            //   float4 k = float4(0.0, -1.0/3.0,  2.0/3.0, -1.0)
            //   float4 p = lerp(float4(c.zy,k.wz), float4(c.yz,k.xy), step(c.z, c.y))
            //   float4 q = lerp(float4(p.xyw,c.x),  float4(c.x,p.yzx),  step(p.x,  c.x))
            //   float  d = q.x - min(q.w, q.y)
            //   float  e = 1.0e-10
            // Then Evaluate builds vec3(abs(q.z+(q.w-q.y)/(6*d+e)), d/(q.x+e), q.x)

            string lk = $"_k_{Id:N}";
            string lp = $"_p_{Id:N}";
            string lq = $"_q_{Id:N}";
            string ld = $"_d_{Id:N}";
            string le = $"_e_{Id:N}";

            ctx.BodyPrelude.AppendLine($"vec4 {lk} = vec4(0.0, -1.0/3.0, 2.0/3.0, -1.0);");
            ctx.BodyPrelude.AppendLine($"vec4 {lp} = mix(vec4(({c}).zy, {lk}.wz), vec4(({c}).yz, {lk}.xy), step(({c}).z, ({c}).y));");
            ctx.BodyPrelude.AppendLine($"vec4 {lq} = mix(vec4({lp}.xyw, ({c}).x), vec4(({c}).x, {lp}.yzx), step({lp}.x, ({c}).x));");
            ctx.BodyPrelude.AppendLine($"float {ld} = {lq}.x - min({lq}.w, {lq}.y);");
            ctx.BodyPrelude.AppendLine($"float {le} = 1.0e-10;");
            ctx.BodyPrelude.AppendLine($"vec3 {localHsv} = vec3(abs({lq}.z + ({lq}.w - {lq}.y) / (6.0 * {ld} + {le})), {ld} / ({lq}.x + {le}), {lq}.x);");
        });

        // Return the appropriate channel based on which output port is being evaluated.
        return p.Name switch
        {
            "Sat" => $"{localHsv}.y",
            "Val" => $"{localHsv}.z",
            _     => $"{localHsv}.x",  // "Hue" and any unexpected port default to Hue
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// PHOTOSHOP BLEND MODE ENUM
// Values 4 (DarkerColor), 9 (LighterColor), and 11 (SoftLight) are intentionally
// omitted (reserved for compatibility).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Photoshop-style layer blend modes (Darken=0 … Divide=20).
/// </summary>
public enum PhotoshopBlendMode
{
    Darken      = 0,
    Multiply    = 1,
    ColorBurn   = 2,
    LinearBurn  = 3,
    // 4 = DarkerColor — reserved, not implemented
    Lighten     = 5,
    Screen      = 6,
    ColorDodge  = 7,
    LinearDodge = 8,
    // 9 = LighterColor — reserved, not implemented
    Overlay     = 10,
    // 11 = SoftLight — reserved, not implemented
    HardLight   = 12,
    VividLight  = 13,
    LinearLight = 14,
    PinLight    = 15,
    HardMix     = 16,
    Difference  = 17,
    Exclusion   = 18,
    Subtract    = 19,
    Divide      = 20,
}

// ═════════════════════════════════════════════════════════════════════════════
// BLEND NODE
//
// Inputs: Src (base layer), Dst (blend layer), both dynamic vector type.
// Clamp toggle and BlendMode enum control the blend operation.
// Opacity input (float, 0..1) lerps between base and blended result.
//
// GLSL formulas use standard Photoshop blend mode definitions.
// Ternary ?: operator is valid GLSL 1.30+ (used for Overlay/HardLight/etc.).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Blends two colour/vector layers using a Photoshop-style blend mode.
/// </summary>
/// <remarks>
/// <para>
/// <b>Src</b> = base layer (bottom);  <b>Dst</b> = blend layer (top).
/// </para>
/// <para>
/// <b>Opacity</b> lerps the result: 0 = pure Src, 1 = fully blended.
/// Maps to <c>mix(src, blended, opacity)</c>.
/// </para>
/// <para>
/// <b>Clamp</b> wraps the blend output in <c>clamp(x,0,1)</c>.  Defaults to true.
/// </para>
/// <para>
/// Output type is the max-channel type of Src and Dst (dynamic).
/// </para>
/// </remarks>
public sealed class BlendNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>The Photoshop-style blend formula to apply.</summary>
    public PhotoshopBlendMode Mode = PhotoshopBlendMode.Overlay;

    /// <summary>When true, clamps the blended value to [0, 1].</summary>
    public bool Clamp = true;

    public override string Title => "Blend";
    public override string Category => "Color";
    public override System.Drawing.Color AccentColor => ColorAccents.Color;

    protected override void DefineNode()
    {
        AddInput<Float3>("Src",     Float3.Zero, required: true);   // base layer
        AddInput<Float3>("Dst",     Float3.One,  required: true);   // blend layer
        AddInput<float>("Opacity",  1f);                             // 0=Src, 1=blended
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage stage, ShaderGenContext ctx)
    {
        // Resolve the unified vector type across Src and Dst (dynamic output).
        var t = ShaderEmit.TypeFromInputs(this, "Src", "Dst", ctx);

        string a = ctx.EvaluateInputAs(GetInput("Src")!, t);
        string b = ctx.EvaluateInputAs(GetInput("Dst")!, t);

        // Emit both operands into temp vars so modes that reference them multiple
        // times (Overlay, HardLight, VividLight, LinearLight, PinLight) don't
        // re-evaluate potentially expensive upstream expressions.
        string la = ctx.FreshLocal("_bsrc");
        string lb = ctx.FreshLocal("_bdst");
        string glslT = ShaderTypeUtil.ToGlsl(t);
        ctx.BodyPrelude.AppendLine($"{glslT} {la} = {a};");
        ctx.BodyPrelude.AppendLine($"{glslT} {lb} = {b};");

        string blended = BlendGlsl(la, lb, Mode);

        if (Clamp)
            blended = $"clamp({blended}, 0.0, 1.0)";

        // Apply opacity: mix(src, blended, opacity).
        string op = ctx.EvaluateInputAs(GetInput("Opacity")!, ShaderType.Float);
        return $"mix({la}, {blended}, {op})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInputs(this, "Src", "Dst", ctx);

    // ─── GLSL blend formulas ─────────────────────────────────────────────────
    // Standard Photoshop blend mode definitions in GLSL.
    // a = Src (base), b = Dst (blend layer).

    private static string BlendGlsl(string a, string b, PhotoshopBlendMode mode) => mode switch
    {
        // Darken / Multiply family ──────────────────────────────────────────
        PhotoshopBlendMode.Darken      => $"min({a}, {b})",
        PhotoshopBlendMode.Multiply    => $"({a} * {b})",
        PhotoshopBlendMode.ColorBurn   => $"(1.0 - ((1.0 - {b}) / {a}))",
        PhotoshopBlendMode.LinearBurn  => $"({a} + {b} - 1.0)",

        // Lighten / Dodge family ────────────────────────────────────────────
        PhotoshopBlendMode.Lighten     => $"max({a}, {b})",
        PhotoshopBlendMode.Screen      => $"(1.0 - (1.0 - {a}) * (1.0 - {b}))",
        PhotoshopBlendMode.ColorDodge  => $"({b} / (1.0 - {a}))",
        PhotoshopBlendMode.LinearDodge => $"({a} + {b})",

        // Overlay / Light family ────────────────────────────────────────────
        PhotoshopBlendMode.Overlay =>
            $"(({b} > 0.5) ? (1.0 - (1.0 - 2.0*({b} - 0.5)) * (1.0 - {a})) : (2.0 * {b} * {a}))",
        PhotoshopBlendMode.HardLight =>
            $"(({a} > 0.5) ? (1.0 - (1.0 - 2.0*({a} - 0.5)) * (1.0 - {b})) : (2.0 * {a} * {b}))",
        PhotoshopBlendMode.VividLight =>
            $"(({a} > 0.5) ? ({b} / ((1.0 - {a}) * 2.0)) : (1.0 - (((1.0 - {b}) * 0.5) / {a})))",
        PhotoshopBlendMode.LinearLight =>
            $"(({a} > 0.5) ? ({b} + 2.0*{a} - 1.0) : ({b} + 2.0*({a} - 0.5)))",
        PhotoshopBlendMode.PinLight =>
            $"(({a} > 0.5) ? max({b}, 2.0*({a} - 0.5)) : min({b}, 2.0*{a}))",
        PhotoshopBlendMode.HardMix =>
            $"round(0.5 * ({a} + {b}))",

        // Difference / Exclusion family ─────────────────────────────────────
        PhotoshopBlendMode.Difference  => $"abs({a} - {b})",
        PhotoshopBlendMode.Exclusion   => $"(0.5 - 2.0*({a} - 0.5)*({b} - 0.5))",
        PhotoshopBlendMode.Subtract    => $"({b} - {a})",
        PhotoshopBlendMode.Divide      => $"({b} / {a})",

        // Fallback — return Dst unchanged (safe no-op for unknown future modes)
        _ => b,
    };
}

// ═════════════════════════════════════════════════════════════════════════════
// BLEND OVER NODE
//
// Inputs: Top (foreground, vec4), Bottom (background, vec4).
// Output: Out (composited result, vec4).
//
// Evaluate (no gamma):
//   a   = src.a + dst.a * (1.0 - src.a)
//   rgb = mix(dst.rgb * dst.a, src.rgb, src.a)      [pre-multiplied alpha blend]
//   out = vec4(rgb, a)
//
// Evaluate (gamma correct):
//   a   = src.a + dst.a * (1.0 - src.a)
//   rgb = pow((pow(src.rgb, 2.2)*src.a + pow(dst.rgb, 2.2)*(1.0-src.a)), 1/2.2)
//   out = vec4(rgb, a)
//
// GammaCorrect is a per-node bool toggle for gamma-corrected blending.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Composites a foreground layer (Top) over a background layer (Bottom) using
/// the standard Porter-Duff "over" operation with pre-multiplied alpha.
/// </summary>
/// <remarks>
/// <para>Both inputs must supply an alpha channel (vec4).  The result is also vec4.</para>
/// <para>
/// When <see cref="GammaCorrect"/> is true the RGB components are converted to
/// linear light (gamma 2.2) before blending and back to gamma afterwards.
/// </para>
/// </remarks>
public sealed class BlendOverNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>
    /// When true, blends in linear light (gamma-correct).
    /// </summary>
    public bool GammaCorrect = false;

    public override string Title => "Blend Over";
    public override string Category => "Color";
    public override System.Drawing.Color AccentColor => ColorAccents.Color;

    protected override void DefineNode()
    {
        AddInput<Float4>("Top",    new Float4(1f, 1f, 1f, 1f), required: true);
        AddInput<Float4>("Bottom", new Float4(0f, 0f, 0f, 0f), required: true);
        AddOutput<Float4>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage stage, ShaderGenContext ctx)
    {
        string src = ctx.EvaluateInputAs(GetInput("Top")!,    ShaderType.Vec4);
        string dst = ctx.EvaluateInputAs(GetInput("Bottom")!, ShaderType.Vec4);

        // Emit inputs into temp vars — they are each referenced multiple times.
        string ls = ctx.FreshLocal("_bovSrc");
        string ld = ctx.FreshLocal("_bovDst");
        ctx.BodyPrelude.AppendLine($"vec4 {ls} = {src};");
        ctx.BodyPrelude.AppendLine($"vec4 {ld} = {dst};");

        // Alpha: standard Porter-Duff alpha-over
        //   a = src.a + dst.a * (1.0 - src.a)
        string alpha = $"({ls}.a + {ld}.a * (1.0 - {ls}.a))";

        string rgb;
        if (!GammaCorrect)
        {
            // Standard pre-multiplied alpha blend: mix(dst.rgb * dst.a, src.rgb, src.a)
            rgb = $"mix({ld}.rgb * {ld}.a, {ls}.rgb, {ls}.a)";
        }
        else
        {
            // Gamma-correct path (gamma = 2.2):
            //   pow( pow(src.rgb, 2.2)*src.a + pow(dst.rgb, 2.2)*(1.0-src.a), 1.0/2.2 )
            const float gamma    = 2.2f;
            const float gammaInv = 1f / gamma;
            string g    = ShaderGenContext.Fmt(gamma);
            string ginv = ShaderGenContext.Fmt(gammaInv);
            rgb = $"pow(pow({ls}.rgb, vec3({g})) * {ls}.a + pow({ld}.rgb, vec3({g})) * (1.0 - {ls}.a), vec3({ginv}))";
        }

        return $"vec4({rgb}, {alpha})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec4;
}
