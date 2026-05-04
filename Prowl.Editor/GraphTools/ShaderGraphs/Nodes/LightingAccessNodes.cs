// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Lighting-access nodes expose the directional light's uniform state and the ambient / fog
// globals as graph inputs. Local lights (point + spot) live in two scene BVHs and have no
// stable index a graph could reference; use the PBR Lighting / Anisotropic Lighting nodes if
// you want every BVH light folded into the surface, or write a Custom Code block that walks
// LBVH_Iter from LightBVH.glsl directly if you need per-light access in the graph.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

internal static class LightingAccents
{
    public static readonly System.Drawing.Color Lighting =
        System.Drawing.Color.FromArgb(255, 240, 200, 80); // warm yellow
}

// ─── Ambient ──────────────────────────────────────────────────────────────────────
// Three exposed terms: flat _AmbientColor + hemisphere sky / ground. _AmbientStrength
// scales the whole lot. Matches the runtime path (Lighting.glsl::CalculateAmbient).

public sealed class AmbientLightNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Ambient Light";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode()
    {
        AddOutput<Color>("RGBA"); AddOutput<Float3>("RGB");
        AddOutput<float>("R"); AddOutput<float>("G"); AddOutput<float>("B");
    }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        return p.Name switch
        {
            "R" => "_AmbientColor.r", "G" => "_AmbientColor.g", "B" => "_AmbientColor.b",
            "RGB" => "_AmbientColor.rgb",
            _ => "_AmbientColor",
        };
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGBA" => ShaderType.Color, "RGB" => ShaderType.Vec3,
        _ => ShaderType.Float,
    };
}

/// <summary>Sky-direction ambient (only meaningful when RenderSettings.AmbientMode is
/// Hemisphere; otherwise Prowl writes the flat ambient color into this slot too).</summary>
public sealed class AmbientSkyColorNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Ambient Sky";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode()
    {
        AddOutput<Color>("RGBA"); AddOutput<Float3>("RGB");
        AddOutput<float>("R"); AddOutput<float>("G"); AddOutput<float>("B");
    }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        return p.Name switch
        {
            "R" => "_AmbientSkyColor.r", "G" => "_AmbientSkyColor.g", "B" => "_AmbientSkyColor.b",
            "RGB" => "_AmbientSkyColor.rgb",
            _ => "_AmbientSkyColor",
        };
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGBA" => ShaderType.Color, "RGB" => ShaderType.Vec3,
        _ => ShaderType.Float,
    };
}

/// <summary>Ground-direction ambient (hemisphere mode only).</summary>
public sealed class AmbientGroundColorNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Ambient Ground";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode()
    {
        AddOutput<Color>("RGBA"); AddOutput<Float3>("RGB");
        AddOutput<float>("R"); AddOutput<float>("G"); AddOutput<float>("B");
    }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        return p.Name switch
        {
            "R" => "_AmbientGroundColor.r", "G" => "_AmbientGroundColor.g", "B" => "_AmbientGroundColor.b",
            "RGB" => "_AmbientGroundColor.rgb",
            _ => "_AmbientGroundColor",
        };
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGBA" => ShaderType.Color, "RGB" => ShaderType.Vec3,
        _ => ShaderType.Float,
    };
}

/// <summary>Global scalar multiplied into every ambient contribution by the built-in
/// lighting path; hand-authored unlit / custom-lighting graphs use this to stay in sync
/// with the PBR path's ambient intensity.</summary>
public sealed class AmbientStrengthNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Ambient Strength";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<float>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    { ctx.Includes.Add("Lighting"); return "_AmbientStrength"; }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ─── Directional light accessors ──────────────────────────────────────────────────
// The scene's single directional light has stable per-frame uniforms, so the graph can
// reference its parameters directly. Every node here is "Directional" in name to make it
// unambiguous which light the graph is reading.

/// <summary>1 if a directional light is active in the scene this frame, 0 otherwise.
/// Useful for branching inside a Custom Code node that wants to skip directional math
/// when the scene has no sun.</summary>
public sealed class DirectionalLightEnabledNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Directional Light Enabled";
    public override string Category => "Lighting/Directional";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<int>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    { ctx.Includes.Add("Lighting"); return "_DirectionalLightEnabled"; }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Int;
}

/// <summary>Intensity of the scene's directional light. Multiply with the colour to get
/// the radiance coming from the sun direction.</summary>
public sealed class DirectionalLightIntensityNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Directional Light Intensity";
    public override string Category => "Lighting/Directional";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<float>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    { ctx.Includes.Add("Lighting"); return "_DirectionalLightIntensity"; }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>RGB colour of the scene's directional light. Toggle <c>IncludeIntensity</c>
/// to get colour × intensity (physically correct radiance) or the raw colour.</summary>
public sealed class DirectionalLightColorNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>When true, output = color × intensity (irradiance). When false, the raw
    /// colour is returned and the user multiplies by intensity themselves.</summary>
    public bool IncludeIntensity = true;

    public override string Title => "Directional Light Color";
    public override string Category => "Lighting/Directional";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode()
    {
        AddOutput<Color>("RGBA"); AddOutput<Float3>("RGB");
        AddOutput<float>("R"); AddOutput<float>("G"); AddOutput<float>("B");
    }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        string rgb = IncludeIntensity
            ? "(_DirectionalLightColor * _DirectionalLightIntensity)"
            : "_DirectionalLightColor";
        return p.Name switch
        {
            "R"    => $"{rgb}.r",
            "G"    => $"{rgb}.g",
            "B"    => $"{rgb}.b",
            "RGBA" => $"vec4({rgb}, 1.0)",
            _      => rgb,
        };
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGBA" => ShaderType.Color, "RGB" => ShaderType.Vec3,
        _ => ShaderType.Float,
    };
}

/// <summary>Unit direction FROM the surface TO the directional light. Prowl's directional
/// light convention has Transform.Forward already pointing surface-to-sun, so this is just
/// <c>normalize(_DirectionalLightDirection)</c> the standard "L" vector for dot products.</summary>
public sealed class DirectionalLightDirectionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Directional Light Direction";
    public override string Category => "Lighting/Directional";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<Float3>("Out",
        tooltip: "Unit vector FROM the surface TO the sun.");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    { ctx.Includes.Add("Lighting"); return "normalize(_DirectionalLightDirection)"; }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

/// <summary>Half-vector for the directional light: <c>normalize(viewDir + lightDir)</c>.
/// Plug into Blinn-Phong-style specular math.</summary>
public sealed class DirectionalHalfDirectionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Directional Half Direction";
    public override string Category => "Lighting/Directional";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<Float3>("Out",
        tooltip: "Unit half-vector for the sun: normalize(viewDir + sunDir).");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        ctx.Includes.Add("ShaderVariables");
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
        var tmp = $"_hd{Id:N}";
        ctx.EmitOnce(tmp, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {tmp} = normalize(GetWorldViewDir({posExpr}) + normalize(_DirectionalLightDirection));");
        });
        return tmp;
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

/// <summary>Directional shadow attenuation in [0, 1]. 1 = fully lit, 0 = fully shadowed.
/// Wraps Lighting.glsl's <c>SampleDirectionalShadow</c> so graph output matches the
/// built-in PBR path's cascade sampling exactly.</summary>
public sealed class DirectionalShadowFactorNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Directional Shadow Factor";
    public override string Category => "Lighting/Directional";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode()
    {
        AddInput<Float3>("Normal", new Float3(0, 1, 0), required: true,
            tooltip: "World-space normal used for slope / normal bias.");
        AddOutput<float>("Out", tooltip: "1 = fully lit, 0 = fully in shadow.");
    }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title))
            return "1.0";

        ctx.Includes.Add("Lighting");
        ctx.Varyings.Add(("worldPos", "vec3"));
        var n = ctx.EvaluateInputAs(GetInput("Normal")!, ShaderType.Vec3);
        return $"(1.0 - SampleDirectionalShadow(worldPos, normalize({n})))";
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ─── Fog ──────────────────────────────────────────────────────────────────────────
// Scene Data already exposes FogColor; here we add the params so authors doing custom
// fog math have access to density / linear start-end.

public sealed class FogParamsNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Fog Params";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode()
    {
        AddOutput<Float4>("Out",
            tooltip: "(x: density/sqrt(ln(2)) for Exp2, y: density/ln(2) for Exp, z: -1/(end-start) for Linear, w: end/(end-start) for Linear)");
    }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    { ctx.Includes.Add("Lighting"); return "_FogParams"; }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec4;
}

/// <summary>Which fog mode is active. x: linear, y: exp, z: exp2 (0 / 1 flags). Branch on
/// these when authoring a custom fog blend.</summary>
public sealed class FogStatesNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Fog States";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<Float3>("Out",
        tooltip: "(Linear, Exp, Exp2) enable flags");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    { ctx.Includes.Add("Lighting"); return "_FogStates"; }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}
