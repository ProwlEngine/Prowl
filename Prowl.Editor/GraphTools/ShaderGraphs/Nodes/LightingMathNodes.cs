// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Nodes that wrap Prowl's built-in lighting / BRDF helpers (Lighting.glsl, PBR.glsl,
// Fragment.glsl). These give graph authors direct access to the same primitives the
// Standard surface shader uses so custom-lighting graphs stay consistent with the
// rest of the rendering pipeline instead of reinventing each term.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

// ─────────────────────────────────────────────────────────────────────────────
// Shared accent for every lit-math / BRDF helper node. Same yellow as Lighting
// access (LightingAccents.Lighting) so the category reads cohesively in the menu.
// ─────────────────────────────────────────────────────────────────────────────

// ═════════════════════════════════════════════════════════════════════════════
// PBR Lighting CalculateForwardLighting
// Full per-fragment PBR sum. Returns a lit vec3 add emission / ambient on top
// in the graph if you want. This is what the built-in PBR path computes.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Full isotropic PBR forward lighting sum for the current fragment runs
/// every active light through the same GGX BRDF the built-in PBR path uses
/// (including specular AA and shadows), and returns the totalled lit colour.
/// </summary>
public sealed class PBRLightingNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "PBR Lighting";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;

    protected override void DefineNode()
    {
        AddInput<Color>("Albedo",    new Color(1, 1, 1, 1), required: true);
        AddInput<Float3>("Normal",   new Float3(0, 0, 1),   required: true,
            tooltip: "Tangent-space normal (the same 0..1 representation a normal map outputs).");
        AddInput<float>("Metallic",  0f, required: true);
        AddInput<float>("Roughness", 0.5f, required: true);
        AddInput<float>("Occlusion", 1f, required: true);
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Forward lighting reads the per-fragment world-space view dir and the
        // tangent-space TBN basis both fragment-only. Bail with a zero fallback
        // (and a node-attached warning) if a vertex-stage subtree wires through.
        if (ctx.RequireFragmentStage(Id, Title))
            return "vec3(0.0)";

        ctx.Includes.Add("Lighting");
        ctx.Includes.Add("ShaderVariables");
        ctx.Varyings.Add(("worldPos", "vec3"));

        var albedo    = ctx.EvaluateInputAs(GetInput("Albedo")!,    ShaderType.Vec4);
        var normalTS  = ctx.EvaluateInputAs(GetInput("Normal")!,    ShaderType.Vec3);
        var metallic  = ctx.EvaluateInputAs(GetInput("Metallic")!,  ShaderType.Float);
        var roughness = ctx.EvaluateInputAs(GetInput("Roughness")!, ShaderType.Float);
        var ao        = ctx.EvaluateInputAs(GetInput("Occlusion")!, ShaderType.Float);

        // Build the world-space normal once (TBN * tangentNormal) via the shared
        // TBN helper matches what the PBR master does in the surface body.
        var tbn = ShaderEmit.EmitTBN(ctx);
        var local = $"_pbrLit{Id:N}";
        ctx.EmitOnce("pbrlit:" + local, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local}_N = normalize({tbn} * {normalTS});");
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local} = CalculateForwardLighting(worldPos, {local}_N, GetWorldViewDir(worldPos), gammaToLinearSpace(({albedo}).rgb), {metallic}, {roughness}, {ao});");
        });
        return local;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// ═════════════════════════════════════════════════════════════════════════════
// Anisotropic PBR Lighting CalculateForwardLightingAniso
// Full per-fragment anisotropic PBR. Splits roughness along tangent / bitangent
// by the Anisotropy factor ([-1, 1]).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Anisotropic PBR forward lighting like <see cref="PBRLightingNode"/> but the
/// specular lobe is elongated along the tangent direction by the Anisotropy
/// factor (-1 stretches along bitangent, +1 along tangent). Uses the built-in
/// <c>CalculateForwardLightingAniso</c> so results match the Standard shader's
/// anisotropic path.
/// </summary>
public sealed class AnisotropicLightingNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Anisotropic Lighting";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;

    protected override void DefineNode()
    {
        AddInput<Color>("Albedo",      new Color(1, 1, 1, 1), required: true);
        AddInput<Float3>("Normal",     new Float3(0, 0, 1),   required: true);
        AddInput<float>("Metallic",    0f, required: true);
        AddInput<float>("Roughness",   0.5f, required: true);
        AddInput<float>("Anisotropy",  0f, required: true,
            tooltip: "-1 stretches highlight along bitangent, +1 along tangent, 0 = isotropic.");
        AddInput<float>("Occlusion",   1f, required: true);
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title))
            return "vec3(0.0)";

        ctx.Includes.Add("Lighting");
        ctx.Includes.Add("ShaderVariables");
        ctx.Varyings.Add(("worldPos",   "vec3"));
        ctx.Varyings.Add(("vTangent",   "vec3"));
        ctx.Varyings.Add(("vBitangent", "vec3"));

        var albedo    = ctx.EvaluateInputAs(GetInput("Albedo")!,      ShaderType.Vec4);
        var normalTS  = ctx.EvaluateInputAs(GetInput("Normal")!,      ShaderType.Vec3);
        var metallic  = ctx.EvaluateInputAs(GetInput("Metallic")!,    ShaderType.Float);
        var roughness = ctx.EvaluateInputAs(GetInput("Roughness")!,   ShaderType.Float);
        var aniso     = ctx.EvaluateInputAs(GetInput("Anisotropy")!,  ShaderType.Float);
        var ao        = ctx.EvaluateInputAs(GetInput("Occlusion")!,   ShaderType.Float);

        var tbn = ShaderEmit.EmitTBN(ctx);
        var local = $"_anisoLit{Id:N}";
        ctx.EmitOnce("anisolit:" + local, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local}_N = normalize({tbn} * {normalTS});");
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local} = CalculateForwardLightingAniso(worldPos, {local}_N, GetWorldViewDir(worldPos), normalize(vTangent), normalize(vBitangent), gammaToLinearSpace(({albedo}).rgb), {metallic}, {roughness}, {aniso}, {ao});");
        });
        return local;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// ═════════════════════════════════════════════════════════════════════════════
// Translucency CalculateTranslucency
// Light scattering through thin / subsurface materials. Two internal modes:
//   ScatteringPower < 0.001  wrapped diffuse + GGX backscatter (foliage, cloth)
//   ScatteringPower ≥ 0.001  spherical Gaussian (skin, wax, marble)
// The node adds the scatter from ONE light (picked by LightIndex). Add multiple
// instances + sum them if you want translucency from every active light.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Backscatter / subsurface-scattering lobe for thin or translucent surfaces.
/// Runs the same <c>CalculateTranslucency</c> helper the Standard shader uses,
/// configured via ScatteringPower (see the node's remarks for mode semantics).
/// </summary>
/// <remarks>
/// Scattering mode: set <c>ScatteringPower</c> to 0 for wrapped-diffuse foliage,
/// or 1..8 for Gaussian skin/wax. <c>Thickness</c> is the per-pixel translucency
/// (e.g. from a texture's B channel), <c>Distortion</c> bends the effective
/// light direction through the surface, <c>Scale</c> is overall intensity.
/// </remarks>
public sealed class TranslucencyNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>Which forward light to use. 0 = primary / directional.</summary>
    public int LightIndex = 0;

    public override string Title => $"Translucency [{LightIndex}]";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;

    protected override void DefineNode()
    {
        AddInput<Float3>("Normal",           new Float3(0, 0, 1), required: true,
            tooltip: "Tangent-space normal.");
        AddInput<float>("Thickness",         1f, required: true,
            tooltip: "Per-pixel translucency / thickness. 0 = opaque, 1 = full scatter.");
        AddInput<float>("ScatteringPower",   0f,
            tooltip: "0 = wrapped diffuse (foliage/cloth). 1..8 = Gaussian (skin/wax).");
        AddInput<float>("Distortion",        0.5f,
            tooltip: "How much the normal bends the light direction through the surface.");
        AddInput<float>("Scale",             1f,
            tooltip: "Overall scatter intensity multiplier.");
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title))
            return "vec3(0.0)";

        ctx.Includes.Add("Lighting");
        ctx.Includes.Add("PBR");
        ctx.Includes.Add("ShaderVariables");
        ctx.Varyings.Add(("worldPos", "vec3"));

        var normalTS = ctx.EvaluateInputAs(GetInput("Normal")!,         ShaderType.Vec3);
        var thick    = ctx.EvaluateInputAs(GetInput("Thickness")!,      ShaderType.Float);
        var sPow     = ctx.EvaluateInputAs(GetInput("ScatteringPower")!,ShaderType.Float);
        var dist     = ctx.EvaluateInputAs(GetInput("Distortion")!,     ShaderType.Float);
        var scale    = ctx.EvaluateInputAs(GetInput("Scale")!,          ShaderType.Float);

        var tbn = ShaderEmit.EmitTBN(ctx);
        var local = $"_trans{Id:N}";
        ctx.EmitOnce("trans:" + local, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local}_N = normalize({tbn} * {normalTS});");
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local}_L = GetLightDirection({LightIndex}, worldPos);");
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local}_V = GetWorldViewDir(worldPos);");
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local}_Lc = _LightColors[{LightIndex}] * _LightIntensities[{LightIndex}] * GetLightAttenuation({LightIndex}, worldPos);");
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local} = CalculateTranslucency({local}_L, {local}_V, {local}_N, {thick}, {sPow}, {dist}, {scale}, {local}_Lc);");
        });
        return local;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// ═════════════════════════════════════════════════════════════════════════════
// Ambient wraps CalculateAmbient(worldNormal)
// Mode-aware ambient: returns either the flat _AmbientColor or the hemisphere
// blend depending on what the project has configured.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Mode-aware ambient lighting samples either the flat <c>_AmbientColor</c> or
/// the hemisphere sky/ground blend based on the project's <c>_AmbientMode</c>.
/// Prefer this over the raw <see cref="AmbientLightNode"/> when you want the
/// same ambient the built-in PBR path uses.
/// </summary>
public sealed class CalculateAmbientNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Calculate Ambient";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;

    protected override void DefineNode()
    {
        AddInput<Float3>("Normal", new Float3(0, 0, 1), required: true,
            tooltip: "Tangent-space normal.");
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // CalculateAmbient uses EmitTBN which references vTangent / vBitangent /
        // vNormal varyings populated by the fragment stage; vertex use would emit
        // an undefined-symbol error.
        if (ctx.RequireFragmentStage(Id, Title))
            return "vec3(0.0)";

        ctx.Includes.Add("Lighting");
        var normalTS = ctx.EvaluateInputAs(GetInput("Normal")!, ShaderType.Vec3);
        var tbn = ShaderEmit.EmitTBN(ctx);
        var local = $"_amb{Id:N}";
        ctx.EmitOnce("amb:" + local, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local} = CalculateAmbient(normalize({tbn} * {normalTS}));");
        });
        return local;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// ═════════════════════════════════════════════════════════════════════════════
// Apply Fog wraps ApplyFog(color, worldPos)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Applies Prowl's fog (linear / exponential / exp-squared, whichever is
/// active in the project settings) to <paramref name="Color"/>. Matches the
/// built-in surface shader's fog application exactly.
/// </summary>
public sealed class ApplyFogNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Apply Fog";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;

    protected override void DefineNode()
    {
        AddInput<Float3>("Color", Float3.Zero, required: true);
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title))
            return ctx.EvaluateInputAs(GetInput("Color")!, ShaderType.Vec3);

        ctx.Includes.Add("Lighting");
        ctx.Varyings.Add(("worldPos", "vec3"));
        var color = ctx.EvaluateInputAs(GetInput("Color")!, ShaderType.Vec3);
        return $"ApplyFog({color}, worldPos)";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// ═════════════════════════════════════════════════════════════════════════════
// Normal Map wraps ApplyNormalMap from Fragment.glsl
// Samples a normal-map texture and transforms it into world space using the
// vertex varyings (vTangent/vBitangent/vNormal). Much cleaner than authoring
// the TBN dance manually.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Samples a tangent-space normal map and returns the resulting world-space
/// normal. Uses <c>ApplyNormalMap</c> from Fragment.glsl handles the TBN
/// transform and falls back to the interpolated vertex normal when the mesh
/// has no tangents.
/// </summary>
public sealed class NormalMapNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Normal Map";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddInput<Resources.Texture2D>("Normal Tex", required: true);
        AddInput<Float2>("UV", Float2.Zero,
            tooltip: "Defaults to texCoord0.");
        AddOutput<Float3>("World Normal");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title))
            return "vec3(0.0, 0.0, 1.0)";

        ctx.Includes.Add("Fragment");
        ctx.Varyings.Add(("vNormal",    "vec3"));
        ctx.Varyings.Add(("vTangent",   "vec3"));
        ctx.Varyings.Add(("vBitangent", "vec3"));

        var tex = ctx.EvaluateInput(GetInput("Normal Tex")!);
        string uv;
        if (ctx.IsConnected(GetInput("UV")!))
            uv = ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2);
        else
        {
            uv = "texCoord0";
            ctx.Varyings.Add(("texCoord0", "vec2"));
        }
        return $"ApplyNormalMap({tex}, {uv}, vNormal, vTangent, vBitangent)";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// ═════════════════════════════════════════════════════════════════════════════
// BRDF primitives direct access to the Fresnel/NDF/Geometry terms from PBR.glsl
// for authors building custom lighting models.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Schlick's Fresnel approximation mixes F0 toward white as the view
/// direction approaches grazing. The cheap version used inside direct lighting.</summary>
public sealed class FresnelSchlickNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Fresnel Schlick";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;

    protected override void DefineNode()
    {
        AddInput<float>("CosTheta", 1f, required: true,
            tooltip: "dot(H, V) or dot(N, V), clamped to [0, 1].");
        AddInput<Float3>("F0", new Float3(0.04f, 0.04f, 0.04f), required: true,
            tooltip: "Reflectance at normal incidence. 0.04 for dielectrics, albedo for metals.");
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("PBR");
        var cos = ctx.EvaluateInputAs(GetInput("CosTheta")!, ShaderType.Float);
        var f0  = ctx.EvaluateInputAs(GetInput("F0")!,       ShaderType.Vec3);
        return $"FresnelSchlick({cos}, {f0})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

/// <summary>Roughness-aware Fresnel used for environment reflection / IBL where
/// rough surfaces should lose Fresnel bite.</summary>
public sealed class FresnelSchlickRoughnessNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Fresnel Schlick (Roughness)";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;

    protected override void DefineNode()
    {
        AddInput<float>("CosTheta",  1f,    required: true);
        AddInput<Float3>("F0",       new Float3(0.04f, 0.04f, 0.04f), required: true);
        AddInput<float>("Roughness", 0.5f,  required: true);
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("PBR");
        var cos  = ctx.EvaluateInputAs(GetInput("CosTheta")!,  ShaderType.Float);
        var f0   = ctx.EvaluateInputAs(GetInput("F0")!,        ShaderType.Vec3);
        var r    = ctx.EvaluateInputAs(GetInput("Roughness")!, ShaderType.Float);
        return $"FresnelSchlickRoughness({cos}, {f0}, {r})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

/// <summary>Disney diffuse term softer than pure Lambert, matches the PBR
/// diffuse the built-in lighting uses. Returns a scalar multiplier you apply
/// to your albedo × light colour.</summary>
public sealed class DisneyDiffuseNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Disney Diffuse";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;

    protected override void DefineNode()
    {
        AddInput<float>("NdotV",     0.5f, required: true);
        AddInput<float>("NdotL",     0.5f, required: true);
        AddInput<float>("LdotH",     0.5f, required: true);
        AddInput<float>("Roughness", 0.5f, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("PBR");
        var ndv = ctx.EvaluateInputAs(GetInput("NdotV")!,     ShaderType.Float);
        var ndl = ctx.EvaluateInputAs(GetInput("NdotL")!,     ShaderType.Float);
        var ldh = ctx.EvaluateInputAs(GetInput("LdotH")!,     ShaderType.Float);
        var r   = ctx.EvaluateInputAs(GetInput("Roughness")!, ShaderType.Float);
        return $"DisneyDiffuse({ndv}, {ndl}, {ldh}, {r})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>GGX normal distribution the microfacet NDF at the heart of the PBR
/// specular lobe. Output higher = tighter, brighter highlight.</summary>
public sealed class DistributionGGXNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Distribution GGX";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;

    protected override void DefineNode()
    {
        AddInput<Float3>("N",         new Float3(0, 0, 1), required: true);
        AddInput<Float3>("H",         new Float3(0, 0, 1), required: true);
        AddInput<float>("Roughness",  0.5f, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("PBR");
        var n = ctx.EvaluateInputAs(GetInput("N")!, ShaderType.Vec3);
        var h = ctx.EvaluateInputAs(GetInput("H")!, ShaderType.Vec3);
        var r = ctx.EvaluateInputAs(GetInput("Roughness")!, ShaderType.Float);
        return $"DistributionGGX({n}, {h}, {r})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>Smith geometry-visibility term for GGX. Combines the masking +
/// shadowing factors for the view and light directions.</summary>
public sealed class GeometrySmithNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Geometry Smith";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;

    protected override void DefineNode()
    {
        AddInput<Float3>("N",        new Float3(0, 0, 1), required: true);
        AddInput<Float3>("V",        new Float3(0, 0, 1), required: true);
        AddInput<Float3>("L",        new Float3(0, 0, 1), required: true);
        AddInput<float>("Roughness", 0.5f, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("PBR");
        var n = ctx.EvaluateInputAs(GetInput("N")!, ShaderType.Vec3);
        var v = ctx.EvaluateInputAs(GetInput("V")!, ShaderType.Vec3);
        var l = ctx.EvaluateInputAs(GetInput("L")!, ShaderType.Vec3);
        var r = ctx.EvaluateInputAs(GetInput("Roughness")!, ShaderType.Float);
        return $"GeometrySmith({n}, {v}, {l}, {r})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}
