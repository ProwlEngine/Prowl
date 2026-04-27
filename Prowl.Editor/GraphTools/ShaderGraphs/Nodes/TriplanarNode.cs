// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

// ═════════════════════════════════════════════════════════════════════════════
// TRIPLANAR SAMPLE
//
// Projects a 2D texture onto arbitrary geometry by sampling along the three
// world-space axis planes (YZ, XZ, XY) and weighting each sample by the
// surface normal. Eliminates UV stretching on rocks, terrain, and any mesh
// without sensible UVs costs three texture fetches per pixel.
//
// Algorithm (standard triplanar):
//   wp        = world position * tile + offset (offset adds a uniform shift)
//   weights   = pow(abs(world normal), sharpness) higher = harder seam
//   weights  /= sum(weights)                       normalise so they sum to 1
//   sample    = tex(wp.zy)*w.x + tex(wp.xz)*w.y + tex(wp.xy)*w.z
//
// Fragment-stage only the world position + normal varyings aren't valid in
// the vertex stage. Falls back to vec4(0) with a node-attached warning.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// World-space triplanar texture projection. Samples a single Texture2D three
/// times one per axis plane and blends the results by the surface normal so
/// the texture wraps around arbitrary geometry without UVs.
/// </summary>
/// <remarks>
/// <para>Wire any <c>Texture2D</c>-typed property (e.g. <c>Texture2DPropertyNode</c>)
/// into <c>Sampler</c>. <c>WorldPos</c> and <c>WorldNormal</c> default to the
/// <c>worldPos</c> / <c>vNormal</c> varyings. <c>Tile</c> scales sampling
/// frequency, <c>Offset</c> shifts every plane uniformly, and <c>Sharpness</c>
/// controls how aggressively the dominant axis dominates over the others
/// (4.0 is a sane starting point; >8 produces hard seams, &lt;1 produces blurry
/// blends).</para>
///
/// <para>Outputs match <c>Tex2DSampleNode</c>: full <c>RGBA</c> plus <c>RGB</c>
/// and individual channels, so a single triplanar can drive both an Albedo and
/// a separate Roughness wire without sampling twice.</para>
/// </remarks>
public sealed class TriplanarSampleNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title    => "Triplanar Sample";
    public override string Category => "Texture";
    public override System.Drawing.Color AccentColor =>
        System.Drawing.Color.FromArgb(255, 60, 170, 130);

    protected override void DefineNode()
    {
        AddInput<Resources.Texture2D>("Sampler", required: true,
            tooltip: "Texture sampled three times one per world axis plane.");
        AddInput<Float3>("WorldPos", Float3.Zero,
            tooltip: "Sample position in world space. Defaults to the worldPos varying.");
        AddInput<Float3>("WorldNormal", new Float3(0, 1, 0),
            tooltip: "Surface normal in world space. Defaults to the vNormal varying.");
        AddInput<float>("Tile", 1f,
            tooltip: "Scales the world-space sampling frequency higher = smaller features.");
        AddInput<Float2>("Offset", Float2.Zero,
            tooltip: "Uniform UV offset applied to every plane.");
        AddInput<float>("Sharpness", 4f,
            tooltip: "Blend hardness exponent. 1 = soft, 4 = balanced, 16 = near-hard seams.");

        AddOutput<Color>("RGBA");
        AddOutput<Float3>("RGB");
        AddOutput<float>("R");
        AddOutput<float>("G");
        AddOutput<float>("B");
        AddOutput<float>("A");
    }

    /// <summary>Stable temp name shared across all output ports of one node, so
    /// the three texture() fetches happen exactly once per fragment regardless
    /// of how many output channels the user wires up.</summary>
    private string SampleTempName() => "_tri_" + Id.ToString("N").Substring(0, 8);

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        // Triplanar leans on the worldPos and vNormal varyings (or wired
        // overrides). Both are populated by the fragment stage; vertex use
        // produces undefined references.
        if (ctx.RequireFragmentStage(Id, Title))
        {
            return outputPort.Name switch
            {
                "R" or "G" or "B" or "A" => "0.0",
                "RGB"                     => "vec3(0.0)",
                _                          => "vec4(0.0)",
            };
        }

        var tmp = SampleTempName();
        ctx.EmitOnce("triplanar:" + tmp, () =>
        {
            var samplerExpr = ctx.EvaluateInput(GetInput("Sampler")!);

            string worldPos;
            if (ctx.IsConnected(GetInput("WorldPos")!))
            {
                worldPos = ctx.EvaluateInputAs(GetInput("WorldPos")!, ShaderType.Vec3);
            }
            else
            {
                ctx.Varyings.Add(("worldPos", "vec3"));
                worldPos = "worldPos";
            }

            string worldNormal;
            if (ctx.IsConnected(GetInput("WorldNormal")!))
            {
                worldNormal = ctx.EvaluateInputAs(GetInput("WorldNormal")!, ShaderType.Vec3);
            }
            else
            {
                ctx.Varyings.Add(("vNormal", "vec3"));
                worldNormal = "normalize(vNormal)";
            }

            var tile      = ctx.EvaluateInputAs(GetInput("Tile")!,      ShaderType.Float);
            var offset    = ctx.EvaluateInputAs(GetInput("Offset")!,    ShaderType.Vec2);
            var sharpness = ctx.EvaluateInputAs(GetInput("Sharpness")!, ShaderType.Float);

            // Per-instance locals so multiple Triplanar nodes coexist cleanly.
            string wp  = $"{tmp}_wp";
            string wn  = $"{tmp}_wn";
            string sx  = $"{tmp}_sx";
            string sy  = $"{tmp}_sy";
            string sz  = $"{tmp}_sz";

            ctx.BodyPrelude.AppendLine($"    vec3 {wp} = ({worldPos}) * ({tile});");
            ctx.BodyPrelude.AppendLine($"    vec3 {wn} = pow(abs({worldNormal}), vec3({sharpness}));");
            // Guard against a fully-orthogonal normal (shouldn't happen on real
            // geometry but pow + tiny floats can underflow). 1e-4 keeps the
            // weighted sum well-defined without visibly biasing the result.
            ctx.BodyPrelude.AppendLine($"    {wn} /= max({wn}.x + {wn}.y + {wn}.z, 0.0001);");
            ctx.BodyPrelude.AppendLine($"    vec4 {sx} = texture({samplerExpr}, {wp}.zy + ({offset}));");
            ctx.BodyPrelude.AppendLine($"    vec4 {sy} = texture({samplerExpr}, {wp}.xz + ({offset}));");
            ctx.BodyPrelude.AppendLine($"    vec4 {sz} = texture({samplerExpr}, {wp}.xy + ({offset}));");
            ctx.BodyPrelude.AppendLine($"    vec4 {tmp} = {sx} * {wn}.x + {sy} * {wn}.y + {sz} * {wn}.z;");
        });

        return outputPort.Name switch
        {
            "RGB" => $"{tmp}.rgb",
            "R"   => $"{tmp}.r",
            "G"   => $"{tmp}.g",
            "B"   => $"{tmp}.b",
            "A"   => $"{tmp}.a",
            _     => tmp, // RGBA
        };
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => outputPort.Name switch
    {
        "RGB"                     => ShaderType.Vec3,
        "R" or "G" or "B" or "A" => ShaderType.Float,
        _                          => ShaderType.Color,
    };
}

// ═════════════════════════════════════════════════════════════════════════════
// TRIPLANAR NORMAL
//
// Variant of TriplanarSampleNode for normal maps. Plain triplanar sampling of
// a normal map is wrong because each plane's tangent space differs you'd get
// the same normal map "facing the wrong way" on the X/Y/Z planes. The fix
// (Reoriented Normal Mapping / RNM-style swizzle) reads each plane's normal
// as if it were authored in that plane's local tangent basis, then rotates
// each into world space before weighted blending.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Triplanar projection of a tangent-space normal map. Equivalent to
/// <see cref="TriplanarSampleNode"/> for albedo, but does the per-plane swizzle
/// dance required so each axis's normal map ends up in the same world-space
/// basis before blending.
/// </summary>
public sealed class TriplanarNormalNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title    => "Triplanar Normal";
    public override string Category => "Texture";
    public override System.Drawing.Color AccentColor =>
        System.Drawing.Color.FromArgb(255, 60, 170, 130);

    protected override void DefineNode()
    {
        AddInput<Resources.Texture2D>("Sampler", required: true,
            tooltip: "Tangent-space normal map (RGB encoded as 0..1 representing -1..+1).");
        AddInput<Float3>("WorldPos", Float3.Zero,
            tooltip: "Defaults to the worldPos varying.");
        AddInput<Float3>("WorldNormal", new Float3(0, 1, 0),
            tooltip: "Defaults to the vNormal varying.");
        AddInput<float>("Tile", 1f);
        AddInput<Float2>("Offset", Float2.Zero);
        AddInput<float>("Sharpness", 4f);
        AddInput<float>("Strength", 1f,
            tooltip: "Scales the XY components of the unpacked normal before re-normalising.");

        AddOutput<Float3>("World Normal",
            tooltip: "Final world-space normal. Feed into PBR Lighting's Normal input.");
    }

    private string TempName() => "_triN_" + Id.ToString("N").Substring(0, 8);

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title))
            return "vec3(0.0, 0.0, 1.0)";

        var tmp = TempName();
        ctx.EmitOnce("triNorm:" + tmp, () =>
        {
            var samplerExpr = ctx.EvaluateInput(GetInput("Sampler")!);

            string worldPos;
            if (ctx.IsConnected(GetInput("WorldPos")!))
                worldPos = ctx.EvaluateInputAs(GetInput("WorldPos")!, ShaderType.Vec3);
            else
            {
                ctx.Varyings.Add(("worldPos", "vec3"));
                worldPos = "worldPos";
            }

            string worldNormal;
            if (ctx.IsConnected(GetInput("WorldNormal")!))
                worldNormal = ctx.EvaluateInputAs(GetInput("WorldNormal")!, ShaderType.Vec3);
            else
            {
                ctx.Varyings.Add(("vNormal", "vec3"));
                worldNormal = "normalize(vNormal)";
            }

            var tile      = ctx.EvaluateInputAs(GetInput("Tile")!,      ShaderType.Float);
            var offset    = ctx.EvaluateInputAs(GetInput("Offset")!,    ShaderType.Vec2);
            var sharpness = ctx.EvaluateInputAs(GetInput("Sharpness")!, ShaderType.Float);
            var strength  = ctx.EvaluateInputAs(GetInput("Strength")!,  ShaderType.Float);

            string wp = $"{tmp}_wp";
            string wn = $"{tmp}_wn";
            string nx = $"{tmp}_nx";
            string ny = $"{tmp}_ny";
            string nz = $"{tmp}_nz";

            ctx.BodyPrelude.AppendLine($"    vec3 {wp} = ({worldPos}) * ({tile});");
            ctx.BodyPrelude.AppendLine($"    vec3 {wn} = pow(abs({worldNormal}), vec3({sharpness}));");
            ctx.BodyPrelude.AppendLine($"    {wn} /= max({wn}.x + {wn}.y + {wn}.z, 0.0001);");

            // Sample-and-unpack each plane (tangent-space [-1, 1] normal). The
            // Strength input scales the XY tilt before re-normalising, matching
            // how a tangent-space normal map is conventionally "softened".
            ctx.BodyPrelude.AppendLine($"    vec3 {nx} = texture({samplerExpr}, {wp}.zy + ({offset})).xyz * 2.0 - 1.0;");
            ctx.BodyPrelude.AppendLine($"    vec3 {ny} = texture({samplerExpr}, {wp}.xz + ({offset})).xyz * 2.0 - 1.0;");
            ctx.BodyPrelude.AppendLine($"    vec3 {nz} = texture({samplerExpr}, {wp}.xy + ({offset})).xyz * 2.0 - 1.0;");
            ctx.BodyPrelude.AppendLine($"    {nx}.xy *= ({strength}); {ny}.xy *= ({strength}); {nz}.xy *= ({strength});");

            // Each plane's normal is authored as if Z is up in that plane's
            // local frame. Rotate into world space by swizzling the components
            // (the sign of the world-normal axis preserves orientation on
            // back-facing triangles), then blend by the same weights.
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {tmp} = normalize(" +
                $"vec3({nx}.xy + sign({worldNormal}.x) * {worldNormal}.zy, abs({worldNormal}.x)) * {wn}.x + " +
                $"vec3({ny}.xy + sign({worldNormal}.y) * {worldNormal}.xz, abs({worldNormal}.y)) * {wn}.y + " +
                $"vec3({nz}.xy + sign({worldNormal}.z) * {worldNormal}.xy, abs({worldNormal}.z)) * {wn}.z" +
                $");");
        });

        return tmp;
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Vec3;
}
