// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.OrigamiUI.Graphite;

/// <summary>The ShaderDef wrapper language around the GLSL: blocks, render state commands and their values, from ShaderSpec.md.</summary>
internal static class ShaderSyntax
{
    public static readonly Dictionary<string, string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Shader"] = "Shader \"Name\" { ... } declares a shader and its quoted name.",
        ["Properties"] = "Material properties shown in the inspector. A property is used in GLSL by declaring a uniform with the same name.",
        ["Surface"] = "Surface \"Lit\" or \"Unlit\" runs a surface function inside Graphite's standard passes.",
        ["Pass"] = "One rendering pass: its name, tags, render state and a GLSLPROGRAM block.",
        ["Fallback"] = "Fallback \"Name\" names a shader for tools to use when this one cannot run.",
        ["Name"] = "Name \"Pass\" names the pass. Names are unique within a shader.",
        ["Tags"] = "Tags { \"Key\" = \"Value\" } read by the renderer, such as LightMode and RenderQueue.",
        ["Stencil"] = "Stencil { ... } configures the stencil test of the pass.",
    };

    public static readonly Dictionary<string, string> Commands = new(StringComparer.Ordinal)
    {
        ["Cull"] = "Cull Back | Front | Off. Which faces are culled. Default Back.",
        ["ZTest"] = "ZTest <comparison> | Disabled. How fragment depth compares to the depth buffer. Default LessEqual.",
        ["ZWrite"] = "ZWrite On | Off. Whether depth is written. Default On.",
        ["ZClip"] = "ZClip On | Off. Off clamps depth instead of clipping. Default On.",
        ["Blend"] = "Blend <src> <dst>. Blend factors for colour and alpha.",
        ["BlendRGB"] = "BlendRGB <src> <dst>. Blend factors for colour only.",
        ["BlendAlpha"] = "BlendAlpha <src> <dst>. Blend factors for alpha only.",
        ["BlendOp"] = "BlendOp <operation>. How source and destination combine. Default Add.",
        ["ColorMask"] = "ColorMask RGBA. The channels written, any of R G B A.",
        ["Offset"] = "Offset <factor>, <units>. Polygon depth offset.",
        ["AlphaToMask"] = "AlphaToMask On | Off. Alpha to coverage. Default Off.",
    };

    public static readonly Dictionary<string, string> StencilCommands = new(StringComparer.Ordinal)
    {
        ["Ref"] = "Ref <int>. Reference value compared against the stencil buffer.",
        ["ReadMask"] = "ReadMask <int>. Bits of the reference and stored value compared.",
        ["WriteMask"] = "WriteMask <int>. Bits of the stencil buffer written.",
        ["Comp"] = "Comp <comparison>. Stencil comparison for both faces.",
        ["CompFront"] = "CompFront <comparison>. Stencil comparison for front faces.",
        ["CompBack"] = "CompBack <comparison>. Stencil comparison for back faces.",
        ["Pass"] = "Pass <operation>. What happens when stencil and depth pass, both faces.",
        ["PassFront"] = "PassFront <operation>. Pass operation for front faces.",
        ["PassBack"] = "PassBack <operation>. Pass operation for back faces.",
        ["Fail"] = "Fail <operation>. What happens when the stencil test fails, both faces.",
        ["FailFront"] = "FailFront <operation>. Fail operation for front faces.",
        ["FailBack"] = "FailBack <operation>. Fail operation for back faces.",
        ["ZFail"] = "ZFail <operation>. What happens when stencil passes but depth fails, both faces.",
        ["ZFailFront"] = "ZFailFront <operation>. ZFail operation for front faces.",
        ["ZFailBack"] = "ZFailBack <operation>. ZFail operation for back faces.",
    };

    public static readonly string[] OnOff = { "On", "Off" };
    public static readonly string[] CullModes = { "Back", "Front", "Off" };
    public static readonly string[] Comparisons = { "Never", "Less", "Equal", "LessEqual", "Greater", "NotEqual", "GreaterEqual", "Always" };
    public static readonly string[] BlendFactors =
    {
        "Zero", "One", "SourceColor", "InverseSourceColor", "SourceAlpha", "InverseSourceAlpha", "DestinationAlpha",
        "InverseDestinationAlpha", "DestinationColor", "InverseDestinationColor", "BlendFactor", "InverseBlendFactor",
    };
    public static readonly string[] BlendOps = { "Add", "Subtract", "ReverseSubtract", "Minimum", "Maximum" };
    public static readonly string[] StencilOps = { "Keep", "Zero", "Replace", "IncrementAndClamp", "DecrementAndClamp", "Invert", "IncrementAndWrap", "DecrementAndWrap" };

    public static readonly Dictionary<string, string> PropertyTypes = new(StringComparer.Ordinal)
    {
        ["Float"] = "Float property, default = 0.5.",
        ["Integer"] = "Integer property, stored as a float, default = 1.",
        ["Color"] = "Colour property, default = (r, g, b, a).",
        ["Vector"] = "Four component vector, default = (x, y, z, w).",
        ["Vector2"] = "Two component vector.",
        ["Vector3"] = "Three component vector.",
        ["Matrix"] = "4x4 matrix, default = ((row0)(row1)(row2)(row3)).",
        ["Texture2D"] = "2D texture, default = \"name\" {} where name is a built in texture such as white.",
        ["Texture2DArray"] = "Array of 2D textures.",
        ["Texture3D"] = "3D volume texture.",
        ["TextureCubemap"] = "Cubemap texture.",
        ["TextureCubemapArray"] = "Array of cubemap textures.",
    };

    public static readonly Dictionary<string, string> Attributes = new(StringComparer.Ordinal)
    {
        ["Gamma"] = "Gamma. The colour is entered in gamma space and stored linear.",
        ["Range"] = "Range(min, max). Shown as a slider between the bounds.",
        ["Keyword"] = "Keyword(AXIS). The material toggle AXIS is on while a texture is set.",
        ["Toggle"] = "Toggle(AXIS). The material toggle AXIS is on while the value is not zero.",
        ["KeywordEnum"] = "KeywordEnum(AXIS). The material axis AXIS takes the value at the integer's index.",
        ["Enum"] = "Enum(A, B, C). Shown as a dropdown of the names, stored as their index.",
        ["Header"] = "Header(\"Text\"). A heading drawn above the property.",
        ["Space"] = "Space. A gap drawn above the property.",
        ["ShowIf"] = "ShowIf(AXIS) or ShowIf(AXIS, VALUE). Shown only while the axis is on, or has the value.",
        ["HideInInspector"] = "HideInInspector. Not shown in the inspector.",
        ["NoScaleOffset"] = "NoScaleOffset. No scale and offset shown for the texture.",
        ["Normal"] = "Normal. The texture is a tangent space normal map.",
        ["HDR"] = "HDR. The colour may exceed 1.",
    };

    public static readonly Dictionary<string, string[]> TagValues = new(StringComparer.Ordinal)
    {
        ["LightMode"] = new[] { "Prepass", "Forward", "ShadowCaster", "ObjectId", "Sky", "Debug" },
        ["RenderQueue"] = new[] { "2000", "2450", "2500", "3000" },
        ["SceneColor"] = new[] { "Read" },
    };

    public static readonly Dictionary<string, string> TagDocs = new(StringComparer.Ordinal)
    {
        ["LightMode"] = "The pass type the renderer draws the pass in: Prepass, Forward, ShadowCaster, ObjectId, Sky, or a feature's own name.",
        ["RenderQueue"] = "Default render queue of the shader's materials. 2500 and up are transparent.",
        ["SceneColor"] = "Read: the pass samples the view's opaque colour through Graphite/SceneColor.glsl.",
    };

    public static readonly Dictionary<string, string> Sections = new(StringComparer.Ordinal)
    {
        ["Shared"] = "Code prepended to every stage of the pass.",
        ["Vertex"] = "The vertex stage. Defines GRAPHITE_VERTEX.",
        ["Fragment"] = "The fragment stage. Defines GRAPHITE_FRAGMENT. A pass without one writes depth only.",
        ["Compute"] = "A compute stage. Defines GRAPHITE_COMPUTE. The only section besides Shared.",
    };

    public static readonly Dictionary<string, string> Pragmas = new(StringComparer.Ordinal)
    {
        ["variant"] = "#pragma variant [global] NAME [V1 V2 ...]. A toggle, or an enum that defines NAME_V for the chosen value.",
        ["global"] = "#pragma global <type> name... Declares values set globally by the renderer rather than per material.",
        ["resource_set"] = "#pragma resource_set N. The descriptor set user resources bind to.",
        ["once"] = "#pragma once. Include this file a single time.",
        ["surface"] = "#pragma surface. Where a Surface block's code is spliced into a standard model.",
        ["kernel"] = "#pragma kernel Name X Y Z. A compute kernel and its thread group size, in .compute files.",
    };

    public static readonly Dictionary<string, string> Directives = new(StringComparer.Ordinal)
    {
        ["include"] = "#include \"File\". Library files start with Graphite/ or Prowl/, other names are relative to the file.",
        ["define"] = "#define NAME value. A preprocessor macro.",
        ["undef"] = "#undef NAME. Removes a macro.",
        ["if"] = "#if expression.",
        ["ifdef"] = "#ifdef NAME.",
        ["ifndef"] = "#ifndef NAME.",
        ["elif"] = "#elif expression.",
        ["else"] = "#else.",
        ["endif"] = "#endif.",
        ["pragma"] = "#pragma directive. Graphite reads variant, global, resource_set, once, surface and kernel.",
        ["extension"] = "#extension NAME : enable.",
        ["error"] = "#error message.",
        ["line"] = "#line number.",
    };

    public static readonly string[] LayoutQualifiers =
    {
        "location", "binding", "set", "component", "index", "offset", "std140", "std430", "push_constant",
        "local_size_x", "local_size_y", "local_size_z", "rgba8", "rgba16f", "rgba32f", "r32f", "r32ui", "r32i", "rg16f",
        "row_major", "column_major", "constant_id", "input_attachment_index",
    };

    /// <summary>Defines the compiler sets for every stage.</summary>
    public static readonly Dictionary<string, string> StageDefines = new(StringComparer.Ordinal)
    {
        ["GRAPHITE_VERTEX"] = "Defined while compiling the vertex stage.",
        ["GRAPHITE_FRAGMENT"] = "Defined while compiling the fragment stage.",
        ["GRAPHITE_COMPUTE"] = "Defined while compiling a compute stage.",
    };

    public static string[]? ValuesFor(string command, bool inStencil)
    {
        if (inStencil)
        {
            if (command.StartsWith("Comp", StringComparison.Ordinal)) return Comparisons;
            if (command.StartsWith("Pass", StringComparison.Ordinal) || command.StartsWith("Fail", StringComparison.Ordinal)
                || command.StartsWith("ZFail", StringComparison.Ordinal)) return StencilOps;
            return null;
        }
        return command switch
        {
            "Cull" => CullModes,
            "ZTest" => Concat(Comparisons, "Disabled"),
            "ZWrite" or "ZClip" or "AlphaToMask" => OnOff,
            "Blend" or "BlendRGB" or "BlendAlpha" => BlendFactors,
            "BlendOp" => BlendOps,
            "ColorMask" => new[] { "RGBA", "RGB", "A" },
            _ => null,
        };
    }

    private static string[] Concat(string[] a, string b)
    {
        var r = new string[a.Length + 1];
        a.CopyTo(r, 0);
        r[^1] = b;
        return r;
    }

    private static readonly HashSet<string> s_values = BuildValues();

    private static HashSet<string> BuildValues()
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { "Disabled", "RGBA", "RGB", "A" };
        foreach (var list in new[] { OnOff, CullModes, Comparisons, BlendFactors, BlendOps, StencilOps })
            foreach (var v in list) set.Add(v);
        return set;
    }

    public static bool IsValue(string word) => s_values.Contains(word);
}
