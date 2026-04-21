// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Lighting-access nodes — expose Prowl's per-light uniform state (Lighting.glsl) and
// the ambient / fog globals as graph inputs. Every node with a LightIndex field
// supports indexing up to _LightCount - 1 (max 8 per the MAX_FORWARD_LIGHTS cap).
//
// Direction + attenuation use the shared GetLightDirection / GetLightAttenuation
// helpers in Lighting.glsl so graph authors get identical fall-off to the built-in
// PBR/Lambert paths. No stubs remain.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

internal static class LightingAccents
{
    public static readonly System.Drawing.Color Lighting =
        System.Drawing.Color.FromArgb(255, 240, 200, 80); // warm yellow
}

// ─── Ambient ──────────────────────────────────────────────────────────────────────
// Prowl exposes three ambient terms: the flat _AmbientColor, and — for the hemisphere
// mode — _AmbientSkyColor / _AmbientGroundColor. _AmbientStrength scales the whole lot.
// We expose separate nodes for each term to provide fine-grained control.

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
/// lighting path — exposed so hand-authored unlit/custom-lighting graphs can stay in
/// sync with the PBR path's ambient intensity.</summary>
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

// ─── Per-light scalar accessors ───────────────────────────────────────────────────
// All take a LightIndex field (default 0 — the first/primary light). All emit
// _LightXxx[index] and include Lighting.glsl. The author is responsible for
// respecting _LightCount; out-of-range reads are GLSL UB.

/// <summary>How many active forward lights this frame. Useful for scaling per-light
/// loops inside Custom Code nodes.</summary>
public sealed class LightCountNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Light Count";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<int>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    { ctx.Includes.Add("Lighting"); return "_LightCount"; }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Int;
}

/// <summary>Type of light at <see cref="LightIndex"/>: 0 = directional, 1 = point,
/// 2 = spot. Useful for branching inside a Custom Code node.</summary>
public sealed class LightTypeNode : Node, IShaderNode, IShaderGraphNode
{
    public int LightIndex = 0;
    public override string Title => $"Light Type [{LightIndex}]";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<int>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    { ctx.Includes.Add("Lighting"); return $"_LightType[{LightIndex}]"; }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Int;
}

public sealed class LightIntensityNode : Node, IShaderNode, IShaderGraphNode
{
    public int LightIndex = 0;
    public override string Title => $"Light Intensity [{LightIndex}]";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<float>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    { ctx.Includes.Add("Lighting"); return $"_LightIntensities[{LightIndex}]"; }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

public sealed class LightRangeNode : Node, IShaderNode, IShaderGraphNode
{
    public int LightIndex = 0;
    public override string Title => $"Light Range [{LightIndex}]";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<float>("Out");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    { ctx.Includes.Add("Lighting"); return $"_LightRanges[{LightIndex}]"; }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>Spot-cone outer / inner angles in degrees. Output: Outer (.x) / Inner (.y).
/// Meaningless for non-spot lights.</summary>
public sealed class LightSpotAnglesNode : Node, IShaderNode, IShaderGraphNode
{
    public int LightIndex = 0;
    public override string Title => $"Light Spot Angles [{LightIndex}]";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode()
    {
        AddOutput<Float2>("XY", tooltip: "(outer, inner) cone angles in degrees");
        AddOutput<float>("Outer"); AddOutput<float>("Inner");
    }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        return p.Name switch
        {
            "Outer" => $"_LightSpotAngles[{LightIndex}]",
            "Inner" => $"_LightInnerSpotAngles[{LightIndex}]",
            _       => $"vec2(_LightSpotAngles[{LightIndex}], _LightInnerSpotAngles[{LightIndex}])",
        };
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => p.Name == "XY" ? ShaderType.Vec2 : ShaderType.Float;
}

// ─── Light Color ──────────────────────────────────────────────────────────────────
// Some pipelines pre-multiply light color with intensity. Prowl stores them separately,
// so the node offers `IncludeIntensity` (default true for physical irradiance).

public sealed class LightColorNode : Node, IShaderNode, IShaderGraphNode
{
    public int LightIndex = 0;
    /// <summary>When true, output = color × intensity (physically correct
    /// irradiance). When false, the raw _LightColors entry is returned.</summary>
    public bool IncludeIntensity = true;

    public override string Title => $"Light Color [{LightIndex}]";
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
        string rgb = IncludeIntensity
            ? $"(_LightColors[{LightIndex}] * _LightIntensities[{LightIndex}])"
            : $"_LightColors[{LightIndex}]";
        return p.Name switch
        {
            "R"    => $"{rgb}.r",
            "G"    => $"{rgb}.g",
            "B"    => $"{rgb}.b",
            "RGBA" => $"vec4({rgb}, 1.0)",
            _      => rgb, // "RGB"
        };
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGBA" => ShaderType.Color, "RGB" => ShaderType.Vec3,
        _ => ShaderType.Float,
    };
}

// ─── Light Position ───────────────────────────────────────────────────────────────

public sealed class LightPositionNode : Node, IShaderNode, IShaderGraphNode
{
    public int LightIndex = 0;
    public override string Title => $"Light Position [{LightIndex}]";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode()
    {
        AddOutput<Float3>("XYZ");
        AddOutput<float>("X"); AddOutput<float>("Y"); AddOutput<float>("Z");
    }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        var lp = $"_LightPositions[{LightIndex}]";
        return p.Name switch
        {
            "X" => $"{lp}.x", "Y" => $"{lp}.y", "Z" => $"{lp}.z",
            _   => lp,
        };
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => p.Name == "XYZ" ? ShaderType.Vec3 : ShaderType.Float;
}

// ─── Light Direction (to-light "L" vector) ───────────────────────────────────────
// Uses GetLightDirection() from Lighting.glsl — handles all three light types
// (directional ignores worldPos; point/spot compute the surface-relative direction).

public sealed class LightDirectionNode : Node, IShaderNode, IShaderGraphNode
{
    public int LightIndex = 0;
    public override string Title => $"Light Direction [{LightIndex}]";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<Float3>("Out",
        tooltip: "Unit direction FROM surface TO light. Correct for directional, point, and spot.");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        ctx.Varyings.Add(("worldPos", "vec3"));
        return $"GetLightDirection({LightIndex}, worldPos)";
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// ─── Light Attenuation (distance + spot cone) ────────────────────────────────────
// Uses GetLightAttenuation() from Lighting.glsl — matches the built-in PBR path's
// fall-off exactly. Directional lights always return 1.0.

public sealed class LightAttenuationNode : Node, IShaderNode, IShaderGraphNode
{
    public int LightIndex = 0;
    public override string Title => $"Light Attenuation [{LightIndex}]";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<float>("Out",
        tooltip: "Combined distance + spot-cone attenuation, [0,1]. 1.0 for directional lights.");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        ctx.Varyings.Add(("worldPos", "vec3"));
        return $"GetLightAttenuation({LightIndex}, worldPos)";
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ─── Half Direction (Blinn-Phong) ─────────────────────────────────────────────────

public sealed class HalfDirectionNode : Node, IShaderNode, IShaderGraphNode
{
    public int LightIndex = 0;
    public override string Title => $"Half Direction [{LightIndex}]";
    public override string Category => "Lighting";
    public override System.Drawing.Color AccentColor => LightingAccents.Lighting;
    protected override void DefineNode() => AddOutput<Float3>("Out",
        tooltip: "Unit half-vector: normalize(viewDir + lightDir). Type-correct for all light types.");
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        ctx.Includes.Add("ShaderVariables");
        ctx.Varyings.Add(("worldPos", "vec3"));
        var tmp = $"_hd{Id:N}";
        ctx.EmitOnce(tmp, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {tmp} = normalize(GetWorldViewDir(worldPos) + GetLightDirection({LightIndex}, worldPos));");
        });
        return tmp;
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// ─── Fog ──────────────────────────────────────────────────────────────────────────
// Scene Data already exposes FogColor; here we add the params so authors doing
// custom fog math have access to density / linear start-end.

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

/// <summary>Which fog mode is active — x: linear enabled, y: exp enabled, z: exp2 enabled
/// (0/1 flags). Branch on these when authoring a custom fog blend.</summary>
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
