// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Surface shader type the main "lit or unlit 3D object" genre. Covers Lit PBR,
// Lit Basic (Lambert / Blinn-Phong), and Unlit via the Lighting dropdown on the
// master node. Emits three passes: Standard (forward lit), DepthNormals (depth +
// normals prepass for post-effects), Shadow (shadow caster).
//
// This file is the concrete home of all the vertex/fragment emission logic that
// used to live in ShaderGraphCompiler now it lives on the shader type that
// produces these passes.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Prowl.Editor.GraphTools.ShaderGraphs.Editors;
using Prowl.Runtime;
using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools.ShaderGraphs.Editors.Types;

/// <summary>
/// Surface shader type. The default Create menu entry seeds a full Standard-shader
/// equivalent graph (Albedo texture + tint, Surface map split into AO/Roughness/Metallic,
/// Emission with intensity, Alpha Cutoff) so a fresh material renders identically to
/// the hand-written Default/Standard shader. A secondary "Surface (Blank)" entry
/// seeds just the master for users who want to build from scratch.
/// </summary>
public sealed class SurfaceShaderType : IShaderType
{
    public const string TypeId = "Surface";

    public string Id => TypeId;
    public string DisplayName => "Surface";
    public Type MasterNodeType => typeof(SurfaceMasterNode);

    // Cached pass instances passes are stateless so a single instance serves every
    // compile. The Standard pass runs first and stashes a DepthHelper in the shared
    // scratch bag that Depth and Shadow reuse.
    private static readonly IShaderPass[] s_passes =
    {
        new SurfaceStandardPass(),
        new SurfaceDepthNormalsPass(),
        new SurfaceShadowPass(),
    };

    public IReadOnlyList<IShaderPass> Passes => s_passes;

    public ShaderGraphRenderSettings DefaultRenderSettings => ShaderGraphRenderSettings.OpaqueDefaults();

    public IReadOnlyList<ShaderTypeMenuEntry> MenuEntries { get; } = new[]
    {
        new ShaderTypeMenuEntry("Default", "Shader Graph/Surface",         50),
        new ShaderTypeMenuEntry("Blank",   "Shader Graph/Surface (Blank)", 51),
    };

    public void SeedGraph(ShaderGraph graph, string variantKey)
    {
        graph.ShaderTypeId = TypeId;
        graph.RenderSettings = DefaultRenderSettings;

        if (variantKey == "Blank") SeedBlank(graph);
        else                        SeedStandard(graph);
    }

    /// <summary>Master only clean slate.</summary>
    private static void SeedBlank(ShaderGraph graph)
    {
        graph.AddNode(new SurfaceMasterNode
        {
            Position = new Float2(500, 80),
            Lighting = ShaderLightingMode.PBR,
        });
    }

    /// <summary>Mimics Default/Standard.shader as closely as a graph can. Property
    /// names + defaults match the hand-written shader so materials built on this
    /// type expose the same inspector fields in the same order.</summary>
    /// <remarks>
    /// <para><b>Occlusion inversion:</b> Prowl's <c>StandardSurface.glsl</c> computes
    /// <c>ao = 1.0 - surface.r</c> the surface map's R channel stores "how much
    /// light is blocked" (0 = lit, 1 = dark), while <see cref="SurfaceMasterNode"/>'s
    /// <c>Occlusion</c> input expects a multiplier (0 = dark, 1 = lit). We insert a
    /// <see cref="OneMinusNode"/> between the R output and the master so the seed
    /// matches Standard.shader's behaviour exactly.</para>
    ///
    /// <para><b>Layout:</b> four-column vertical layout with ~180 px row spacing to
    /// keep wires between nodes instead of across them. Properties on the left,
    /// samples in the middle, math next, master on the right.</para>
    /// </remarks>
    private static void SeedStandard(ShaderGraph graph)
    {
        // ── Layout ─────────────────────────────────────────────────────────────
        const float colProps  = 60f;
        const float colSamp   = 460f;
        const float colMath   = 820f;
        const float colMaster = 1180f;

        // ── Properties (left column) ───────────────────────────────────────────
        var propMainTex = new Texture2DPropertyNode
        {
            Name = "_MainTex", Label = "Albedo", Default = "grid",
            Position = new Float2(colProps, 40),
        };
        var propMainColor = new ColorPropertyNode
        {
            Name = "_MainColor", Label = "Tint", Value = new Color(1f, 1f, 1f, 1f),
            Position = new Float2(colProps, 200),
        };
        var propSurfaceTex = new Texture2DPropertyNode
        {
            Name = "_SurfaceTex", Label = "Surface (AO, Roughness, Metallic)", Default = "surface",
            Position = new Float2(colProps, 400),
        };
        var propEmissionTex = new Texture2DPropertyNode
        {
            Name = "_EmissionTex", Label = "Emission", Default = "emission",
            Position = new Float2(colProps, 580),
        };
        var propEmissionIntensity = new FloatPropertyNode
        {
            Name = "_EmissionIntensity", Label = "Emission Intensity", Value = 1f,
            Position = new Float2(colProps, 740),
        };
        var propAlphaCutoff = new FloatPropertyNode
        {
            Name = "_AlphaCutoff", Label = "Alpha Cutoff", Value = 0.5f,
            Position = new Float2(colProps, 900),
        };

        foreach (var n in new Node[] { propMainTex, propMainColor, propSurfaceTex, propEmissionTex, propEmissionIntensity, propAlphaCutoff })
            graph.AddNode(n);

        // ── Texture samples (middle-left column) ───────────────────────────────
        var sampleAlbedo   = new Tex2DSampleNode { Position = new Float2(colSamp, 80)  };
        var sampleSurface  = new Tex2DSampleNode { Position = new Float2(colSamp, 400) };
        var sampleEmission = new Tex2DSampleNode { Position = new Float2(colSamp, 620) };
        graph.AddNode(sampleAlbedo);
        graph.AddNode(sampleSurface);
        graph.AddNode(sampleEmission);

        // ── Math (middle-right column) ─────────────────────────────────────────
        // Albedo × Tint both sides vec4, output vec4 wires into master.Albedo.
        var albedoTimesTint = new MultiplyNode { Position = new Float2(colMath, 120) };
        graph.AddNode(albedoTimesTint);

        // Occlusion inversion surface.r is "light blocked" (R=0 lit, R=1 dark);
        // master.Occlusion expects a multiplier (0 dark, 1 lit). OneMinus bridges
        // the two conventions so the seed mirrors StandardSurface.glsl exactly.
        var occlusionInvert = new OneMinusNode { Position = new Float2(colMath, 360) };
        graph.AddNode(occlusionInvert);

        // Emission × Intensity vec3 × float = vec3 into master.Emission.
        var emissionTimesIntensity = new MultiplyNode { Position = new Float2(colMath, 640) };
        graph.AddNode(emissionTimesIntensity);

        // ── Master (right column) ──────────────────────────────────────────────
        var master = new SurfaceMasterNode
        {
            Position = new Float2(colMaster, 260),
            Lighting = ShaderLightingMode.PBR,
        };
        graph.AddNode(master);

        // ── Wiring ─────────────────────────────────────────────────────────────
        // Albedo path: MainTex → sample → × Tint → master.Albedo
        Wire(graph, propMainTex,           "Sampler", sampleAlbedo,           "Sampler");
        Wire(graph, sampleAlbedo,          "RGBA",    albedoTimesTint,        "A");
        Wire(graph, propMainColor,         "RGBA",    albedoTimesTint,        "B");
        Wire(graph, albedoTimesTint,       "Out",     master,                 "Albedo");

        // Surface path: SurfaceTex → sample → R→OneMinus→Occlusion, G→Roughness, B→Metallic
        Wire(graph, propSurfaceTex,        "Sampler", sampleSurface,          "Sampler");
        Wire(graph, sampleSurface,         "R",       occlusionInvert,        "In");
        Wire(graph, occlusionInvert,       "Out",     master,                 "Occlusion");
        Wire(graph, sampleSurface,         "G",       master,                 "Roughness");
        Wire(graph, sampleSurface,         "B",       master,                 "Metallic");

        // Emission path: EmissionTex → sample → × Intensity → master.Emission
        Wire(graph, propEmissionTex,       "Sampler", sampleEmission,         "Sampler");
        Wire(graph, sampleEmission,        "RGB",     emissionTimesIntensity, "A");
        Wire(graph, propEmissionIntensity, "Out",     emissionTimesIntensity, "B");
        Wire(graph, emissionTimesIntensity,"Out",     master,                 "Emission");

        // Alpha cutoff path: AlphaCutoff → master.AlphaCutoff
        Wire(graph, propAlphaCutoff,       "Out",     master,                 "Alpha Cutoff");
    }

    /// <summary>Wire convenience resolves port names and appends an Edge.</summary>
    private static void Wire(ShaderGraph g, Node src, string srcPort, Node dst, string dstPort)
    {
        g.Edges.Add(new Edge
        {
            SourceNodeId   = src.Id,
            SourcePortName = src.GetOutput(srcPort)?.Name ?? srcPort,
            TargetNodeId   = dst.Id,
            TargetPortName = dst.GetInput(dstPort)?.Name  ?? dstPort,
        });
    }
}

/// <summary>
/// Slice of the graph needed by the DepthNormals + Shadow passes the alpha cutout
/// subtree (when the graph is AlphaTest) and the vertex offset subtree (when the
/// master's Vertex Position port is driven). Stashed in
/// <see cref="PassEmitSharedState.Scratch"/> by the Standard pass so Depth and
/// Shadow don't re-evaluate the same subtrees.
/// </summary>
internal sealed class SurfaceDepthHelper
{
    public ShaderGenContext? AlphaCtx;
    public string AlphaExpr = "1.0";
    public string CutoffExpr = "0.0";
    public bool NeedsAlphaDiscard;

    public ShaderGenContext? VertexCtx;
    public string VertexPosOffsetExpr = "vec3(0.0)";
    public bool NeedsVertexOffset;

    public const string ScratchKey = "Surface.DepthHelper";
}

// ═══════════════════════════════════════════════════════════════════════════════
// Shared helpers used across Surface passes.
// ═══════════════════════════════════════════════════════════════════════════════

internal static class SurfacePassHelpers
{
    /// <summary>Evaluate a master-node input by name. When the port is missing
    /// (renamed, template drift, typo), surface a diagnostic and return
    /// <paramref name="fallback"/> so the compile still produces something instead
    /// of null-ref crashing the importer. Works for any MasterNodeBase subclass.</summary>
    public static string EvalMaster(MasterNodeBase master, string portName, ShaderGenContext ctx, string fallback)
    {
        var port = master.GetInput(portName);
        if (port == null)
        {
            ctx.Diagnostics.Add((master.Id, $"Master output is missing expected input port '{portName}'.", NodeMessageSeverity.Error));
            return fallback;
        }
        return ctx.EvaluateInput(port);
    }

    public static bool HasIncomingEdgeNamed(Graph graph, Node node, string portName)
    {
        var port = node.GetInput(portName);
        if (port == null || port.IsHidden) return false;
        foreach (var e in graph.Edges)
            if (e.TargetNodeId == node.Id && e.TargetPortName == portName) return true;
        return false;
    }

    /// <summary>Build the depth-helper blueprint shared between Depth and Shadow
    /// passes. Called once by the Standard pass and stashed in scratch null when
    /// the graph has neither alpha cutout nor a vertex offset.</summary>
    public static SurfaceDepthHelper? BuildDepthHelper(Graph graph, SurfaceMasterNode master,
        ShaderGraphRenderSettings settings, List<string> propertyUniforms,
        PassEmitSharedState shared)
    {
        bool hasAlphaWire      = HasIncomingEdgeNamed(graph, master, "Alpha");
        bool hasCutoffWire     = HasIncomingEdgeNamed(graph, master, "Alpha Cutoff");
        bool cutoutQueue       = settings.Queue == ShaderRenderQueue.AlphaTest;
        bool needsAlphaDiscard = cutoutQueue || hasAlphaWire || hasCutoffWire;
        bool needsVertexOffset = HasIncomingEdgeNamed(graph, master, "Vertex Position");

        if (!needsAlphaDiscard && !needsVertexOffset) return null;

        var helper = new SurfaceDepthHelper
        {
            NeedsAlphaDiscard = needsAlphaDiscard,
            NeedsVertexOffset = needsVertexOffset,
        };

        if (needsAlphaDiscard)
        {
            var ctx = new ShaderGenContext(graph, ShaderStage.Fragment);
            foreach (var u in propertyUniforms) ctx.Uniforms.Add(u);
            ctx.Includes.Add("ProwlCG");
            ctx.Varyings.Add(("texCoord0", "vec2"));

            helper.AlphaExpr  = EvalMaster(master, "Alpha",        ctx, "1.0");
            helper.CutoffExpr = EvalMaster(master, "Alpha Cutoff", ctx, "0.0");
            helper.AlphaCtx   = ctx;
            shared.Diagnostics.AddRange(ctx.Diagnostics);
        }

        if (needsVertexOffset)
        {
            var ctx = new ShaderGenContext(graph, ShaderStage.Vertex);
            foreach (var u in propertyUniforms) ctx.Uniforms.Add(u);
            ctx.Includes.Add("ProwlCG");
            ctx.Includes.Add("VertexAttributes");

            helper.VertexPosOffsetExpr = EvalMaster(master, "Vertex Position", ctx, "vec3(0.0)");
            helper.VertexCtx = ctx;
            shared.Diagnostics.AddRange(ctx.Diagnostics);
        }

        return helper;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Standard pass forward-lit main pass.
// ═══════════════════════════════════════════════════════════════════════════════

internal sealed class SurfaceStandardPass : IShaderPass
{
    public string Name => "Standard";
    public ShaderPassRole Role => ShaderPassRole.Forward;

    public string EmitPass(MasterNodeBase masterBase, ShaderGraph graph, PassEmitSharedState shared)
    {
        var master = (SurfaceMasterNode)masterBase;
        var settings = graph.RenderSettings;

        // ── Fragment context ──────────────────────────────────────────────────────
        var fragCtx = new ShaderGenContext(graph, ShaderStage.Fragment);
        foreach (var u in shared.PropertyUniforms) fragCtx.Uniforms.Add(u);
        fragCtx.Includes.Add("ProwlCG");
        fragCtx.Includes.Add("StandardSurface");
        fragCtx.Varyings.Add(("texCoord0", "vec2"));
        fragCtx.Varyings.Add(("worldPos",  "vec3"));
        fragCtx.Varyings.Add(("vColor",    "vec4"));
        fragCtx.Varyings.Add(("vNormal",   "vec3"));
        fragCtx.Varyings.Add(("vTangent",  "vec3"));
        fragCtx.Varyings.Add(("vBitangent","vec3"));

        var surfaceBody = BuildSurfaceBody(master, fragCtx, settings);
        shared.Diagnostics.AddRange(fragCtx.Diagnostics);

        // ── Vertex context ────────────────────────────────────────────────────────
        // Mirror fragment-introduced varyings BEFORE building the vertex body the
        // vertex stage needs to know which optional `out` slots to declare + assign.
        var vertCtx = new ShaderGenContext(graph, ShaderStage.Vertex);
        foreach (var u in shared.PropertyUniforms) vertCtx.Uniforms.Add(u);
        vertCtx.Includes.Add("ProwlCG");
        vertCtx.Includes.Add("VertexAttributes");
        vertCtx.Varyings.Add(("texCoord0", "vec2"));
        vertCtx.Varyings.Add(("worldPos",  "vec3"));
        vertCtx.Varyings.Add(("vColor",    "vec4"));
        vertCtx.Varyings.Add(("vNormal",   "vec3"));
        vertCtx.Varyings.Add(("vTangent",  "vec3"));
        vertCtx.Varyings.Add(("vBitangent","vec3"));
        foreach (var v in fragCtx.Varyings) vertCtx.Varyings.Add(v);

        var vertexBody = BuildVertexBody(master, vertCtx);
        shared.Diagnostics.AddRange(vertCtx.Diagnostics);

        // ── Stash the depth-helper blueprint for Depth + Shadow passes ────────────
        var depthHelper = SurfacePassHelpers.BuildDepthHelper(graph, master, settings, shared.PropertyUniforms, shared);
        if (depthHelper != null)
            shared.Scratch[SurfaceDepthHelper.ScratchKey] = depthHelper;

        // ── Emit the pass block ───────────────────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine("Pass \"Standard\"");
        sb.AppendLine("{");
        ShaderGraphEmit.AppendRenderState(sb, settings, "    ");
        foreach (var d in fragCtx.PassDirectives) sb.AppendLine($"    {d}");
        sb.AppendLine();
        sb.AppendLine("    GLSLPROGRAM");
        sb.AppendLine();

        // Vertex
        sb.AppendLine("    Vertex");
        sb.AppendLine("    {");
        foreach (var d in vertCtx.Defines)   sb.AppendLine($"        #define {d}");
        foreach (var inc in vertCtx.Includes) sb.AppendLine($"        #include \"{inc}\"");
        foreach (var (n, t) in vertCtx.Varyings) sb.AppendLine($"        out {t} {n};");
        foreach (var u in vertCtx.Uniforms)  sb.AppendLine($"        {u}");
        if (vertCtx.TopLevelHelpers.Length > 0) { sb.AppendLine(); sb.Append(vertCtx.TopLevelHelpers.ToString()); }
        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        if (vertCtx.BodyPrelude.Length > 0) sb.Append(vertCtx.BodyPrelude.ToString());
        sb.Append(vertexBody);
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Fragment
        sb.AppendLine("    Fragment");
        sb.AppendLine("    {");
        foreach (var d in fragCtx.Defines)    sb.AppendLine($"        #define {d}");
        foreach (var inc in fragCtx.Includes) sb.AppendLine($"        #include \"{inc}\"");
        sb.AppendLine();
        sb.AppendLine("        layout (location = 0) out vec4 fragColor;");
        foreach (var (n, t) in fragCtx.Varyings) sb.AppendLine($"        in {t} {n};");
        foreach (var u in fragCtx.Uniforms)   sb.AppendLine($"        {u}");
        if (fragCtx.TopLevelHelpers.Length > 0) { sb.AppendLine(); sb.Append(fragCtx.TopLevelHelpers.ToString()); }
        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        if (fragCtx.BodyPrelude.Length > 0) sb.Append(fragCtx.BodyPrelude.ToString());
        sb.Append(surfaceBody);
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ─── Fragment body builders (dispatched on Lighting enum) ────────────────────

    private static string BuildSurfaceBody(SurfaceMasterNode master, ShaderGenContext ctx, ShaderGraphRenderSettings settings)
    {
        string albedo      = SurfacePassHelpers.EvalMaster(master, "Albedo",       ctx, "vec4(1.0)");
        string alpha       = SurfacePassHelpers.EvalMaster(master, "Alpha",        ctx, "1.0");
        string emission    = SurfacePassHelpers.EvalMaster(master, "Emission",     ctx, "vec3(0.0)");
        string alphaCutoff = SurfacePassHelpers.EvalMaster(master, "Alpha Cutoff", ctx, "0.0");

        if (master.Lighting == ShaderLightingMode.Unlit)
            return BuildUnlitBody(albedo, alpha, emission, alphaCutoff);

        string normalTS  = SurfacePassHelpers.EvalMaster(master, "Normal",    ctx, "vec3(0.0, 0.0, 1.0)");
        string metallic  = SurfacePassHelpers.EvalMaster(master, "Metallic",  ctx, "0.0");
        string roughness = SurfacePassHelpers.EvalMaster(master, "Roughness", ctx, "0.5");
        string occlusion = SurfacePassHelpers.EvalMaster(master, "Occlusion", ctx, "1.0");

        if (master.Lighting == ShaderLightingMode.Lambert)
            return BuildLambertBody(albedo, alpha, normalTS, occlusion, emission, alphaCutoff, ctx, settings);
        if (master.Lighting == ShaderLightingMode.BlinnPhong)
            return BuildBlinnPhongBody(albedo, alpha, normalTS, roughness, occlusion, emission, alphaCutoff, ctx, settings);

        // Default: PBR.
        ctx.Includes.Add("Lighting");
        if (!settings.ReceivesShadows) ctx.Defines.Add("SG_NO_SHADOWS");

        var sb = new StringBuilder();
        sb.AppendLine("    // ── Graph-driven PBR surface ──");
        sb.AppendLine($"    vec4  _sgAlbedo    = {albedo};");
        sb.AppendLine($"    float _sgAlpha     = {alpha};");
        sb.AppendLine($"    vec3  _sgNormalTS  = {normalTS};");
        sb.AppendLine($"    float _sgMetallic  = clamp({metallic}, 0.0, 1.0);");
        sb.AppendLine($"    float _sgRoughness = clamp({roughness}, 0.04, 1.0);");
        sb.AppendLine($"    float _sgAO        = {occlusion};");
        sb.AppendLine($"    vec3  _sgEmission  = {emission};");
        sb.AppendLine($"    float _sgCutoff    = {alphaCutoff};");
        sb.AppendLine();
        sb.AppendLine("    if (_sgCutoff > 0.0 && _sgAlpha < _sgCutoff) discard;");
        sb.AppendLine();
        sb.AppendLine("    mat3 _sgTBN = mat3(normalize(vTangent), normalize(vBitangent), normalize(vNormal));");
        sb.AppendLine("    vec3 _sgWorldN = normalize(_sgTBN * _sgNormalTS);");
        sb.AppendLine();
        sb.AppendLine("    vec3 _sgViewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);");
        sb.AppendLine("    vec3 _sgBaseColor = gammaToLinearSpace(_sgAlbedo.rgb);");
        sb.AppendLine("    vec3 _sgLighting = CalculateForwardLighting(worldPos, _sgWorldN, _sgViewDir, _sgBaseColor, _sgMetallic, _sgRoughness, _sgAO);");
        sb.AppendLine("    vec3 _sgDiffuse = _sgBaseColor * (1.0 - _sgMetallic);");
        if (settings.ReceivesAmbient)
            sb.AppendLine("    vec3 _sgAmbient = CalculateAmbient(_sgWorldN) * _sgDiffuse * _sgAO * _AmbientStrength;");
        else
            sb.AppendLine("    vec3 _sgAmbient = vec3(0.0);");
        sb.AppendLine("    vec3 _sgColor = ApplyFog(_sgAmbient + _sgLighting + _sgEmission, worldPos);");
        sb.AppendLine();
        sb.AppendLine("    fragColor = vec4(_sgColor, _sgAlpha);");
        return sb.ToString();
    }

    private static string BuildUnlitBody(string albedo, string alpha, string emission, string alphaCutoff)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    // ── Unlit (graph-driven) ──");
        sb.AppendLine($"    vec4  _sgAlbedo  = {albedo};");
        sb.AppendLine($"    float _sgAlpha   = {alpha};");
        sb.AppendLine($"    vec3  _sgEmiss   = {emission};");
        sb.AppendLine($"    float _sgCutoff  = {alphaCutoff};");
        sb.AppendLine();
        sb.AppendLine("    if (_sgCutoff > 0.0 && _sgAlpha < _sgCutoff) discard;");
        sb.AppendLine();
        sb.AppendLine("    fragColor = vec4(_sgAlbedo.rgb + _sgEmiss, _sgAlpha);");
        return sb.ToString();
    }

    private static string BuildLambertBody(string albedo, string alpha, string normalTS,
        string occlusion, string emission, string alphaCutoff, ShaderGenContext ctx,
        ShaderGraphRenderSettings settings)
    {
        ctx.Includes.Add("Lighting");
        if (!settings.ReceivesShadows) ctx.Defines.Add("SG_NO_SHADOWS");
        var sb = new StringBuilder();
        sb.AppendLine("    // ── Lambert lighting (graph-driven) ──");
        sb.AppendLine($"    vec4  _sgAlbedo   = {albedo};");
        sb.AppendLine($"    float _sgAlpha    = {alpha};");
        sb.AppendLine($"    vec3  _sgNormalTS = {normalTS};");
        sb.AppendLine($"    float _sgAO       = {occlusion};");
        sb.AppendLine($"    vec3  _sgEmission = {emission};");
        sb.AppendLine($"    float _sgCutoff   = {alphaCutoff};");
        sb.AppendLine();
        sb.AppendLine("    if (_sgCutoff > 0.0 && _sgAlpha < _sgCutoff) discard;");
        sb.AppendLine();
        sb.AppendLine("    mat3 _sgTBN = mat3(normalize(vTangent), normalize(vBitangent), normalize(vNormal));");
        sb.AppendLine("    vec3 _sgWorldN = normalize(_sgTBN * _sgNormalTS);");
        sb.AppendLine("    vec3 _sgBaseColor = gammaToLinearSpace(_sgAlbedo.rgb);");
        sb.AppendLine("    vec3 _sgLighting = CalculateForwardLighting(worldPos, _sgWorldN, normalize(_WorldSpaceCameraPos.xyz - worldPos), _sgBaseColor, 0.0, 1.0, _sgAO);");
        if (settings.ReceivesAmbient)
            sb.AppendLine("    vec3 _sgAmbient = CalculateAmbient(_sgWorldN) * _sgBaseColor * _sgAO * _AmbientStrength;");
        else
            sb.AppendLine("    vec3 _sgAmbient = vec3(0.0);");
        sb.AppendLine("    vec3 _sgColor = ApplyFog(_sgAmbient + _sgLighting + _sgEmission, worldPos);");
        sb.AppendLine("    fragColor = vec4(_sgColor, _sgAlpha);");
        return sb.ToString();
    }

    private static string BuildBlinnPhongBody(string albedo, string alpha, string normalTS,
        string roughness, string occlusion, string emission, string alphaCutoff, ShaderGenContext ctx,
        ShaderGraphRenderSettings settings)
    {
        ctx.Includes.Add("Lighting");
        if (!settings.ReceivesShadows) ctx.Defines.Add("SG_NO_SHADOWS");
        var sb = new StringBuilder();
        sb.AppendLine("    // ── Blinn-Phong lighting (graph-driven) ──");
        sb.AppendLine($"    vec4  _sgAlbedo    = {albedo};");
        sb.AppendLine($"    float _sgAlpha     = {alpha};");
        sb.AppendLine($"    vec3  _sgNormalTS  = {normalTS};");
        sb.AppendLine($"    float _sgRoughness = clamp({roughness}, 0.04, 1.0);");
        sb.AppendLine($"    float _sgAO        = {occlusion};");
        sb.AppendLine($"    vec3  _sgEmission  = {emission};");
        sb.AppendLine($"    float _sgCutoff    = {alphaCutoff};");
        sb.AppendLine();
        sb.AppendLine("    if (_sgCutoff > 0.0 && _sgAlpha < _sgCutoff) discard;");
        sb.AppendLine();
        sb.AppendLine("    mat3 _sgTBN = mat3(normalize(vTangent), normalize(vBitangent), normalize(vNormal));");
        sb.AppendLine("    vec3 _sgWorldN = normalize(_sgTBN * _sgNormalTS);");
        sb.AppendLine("    vec3 _sgBaseColor = gammaToLinearSpace(_sgAlbedo.rgb);");
        sb.AppendLine("    vec3 _sgLighting = CalculateForwardLighting(worldPos, _sgWorldN, normalize(_WorldSpaceCameraPos.xyz - worldPos), _sgBaseColor, 0.0, _sgRoughness, _sgAO);");
        if (settings.ReceivesAmbient)
            sb.AppendLine("    vec3 _sgAmbient = CalculateAmbient(_sgWorldN) * _sgBaseColor * _sgAO * _AmbientStrength;");
        else
            sb.AppendLine("    vec3 _sgAmbient = vec3(0.0);");
        sb.AppendLine("    vec3 _sgColor = ApplyFog(_sgAmbient + _sgLighting + _sgEmission, worldPos);");
        sb.AppendLine("    fragColor = vec4(_sgColor, _sgAlpha);");
        return sb.ToString();
    }

    // ─── Vertex body ─────────────────────────────────────────────────────────────

    private static string BuildVertexBody(SurfaceMasterNode master, ShaderGenContext ctx)
    {
        var sb = new StringBuilder();

        bool hasPosOffset = HasIncomingEdge(master, "Vertex Position", ctx);
        bool hasNormalOverride = HasIncomingEdge(master, "Vertex Normal", ctx);

        sb.AppendLine("    vec3 _vertPos = vertexPosition;");
        sb.AppendLine("    vec3 _vertNormal = vertexNormal;");
        if (hasPosOffset)
        {
            var expr = SurfacePassHelpers.EvalMaster(master, "Vertex Position", ctx, "vec3(0.0)");
            sb.AppendLine($"    _vertPos += {expr};");
        }
        if (hasNormalOverride)
        {
            var expr = SurfacePassHelpers.EvalMaster(master, "Vertex Normal", ctx, "vertexNormal");
            sb.AppendLine($"    _vertNormal = {expr};");
        }
        sb.AppendLine("    gl_Position = TransformClip(_vertPos);");
        sb.AppendLine("    texCoord0 = vertexTexCoord0;");
        foreach (var (name, _) in ctx.Varyings)
        {
            if (name == "texCoord1")     sb.AppendLine("    texCoord1 = vertexTexCoord1;");
            if (name == "vInstanceData") sb.AppendLine("    vInstanceData = GetInstanceCustomData();");
        }
        sb.AppendLine("    worldPos = TransformPosition(_vertPos);");
        sb.AppendLine("    vColor = GetInstanceColor();");
        sb.AppendLine("    vNormal = TransformDirection(_vertNormal);");
        sb.AppendLine("#ifdef HAS_TANGENTS");
        sb.AppendLine("    vTangent = TransformDirection(vertexTangent.xyz);");
        sb.AppendLine("    vBitangent = cross(vNormal, vTangent);");
        sb.AppendLine("    if (dot(vBitangent, vBitangent) < 0.000001) {");
        sb.AppendLine("        vTangent = abs(vNormal.y) < 0.999 ? normalize(cross(vNormal, vec3(0,1,0))) : normalize(cross(vNormal, vec3(1,0,0)));");
        sb.AppendLine("        vBitangent = cross(vNormal, vTangent);");
        sb.AppendLine("    }");
        sb.AppendLine("#else");
        sb.AppendLine("    vTangent = vec3(1, 0, 0);");
        sb.AppendLine("    vBitangent = vec3(0, 1, 0);");
        sb.AppendLine("#endif");
        return sb.ToString();
    }

    private static bool HasIncomingEdge(Node node, string portName, ShaderGenContext ctx)
    {
        var port = node.GetInput(portName);
        if (port == null || port.IsHidden) return false;
        foreach (var e in ctx.Graph.Edges)
            if (e.TargetNodeId == node.Id && e.TargetPortName == portName) return true;
        return false;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// DepthNormals pass depth-only + view-space normals, skipped for transparent.
// ═══════════════════════════════════════════════════════════════════════════════

internal sealed class SurfaceDepthNormalsPass : IShaderPass
{
    public string Name => "DepthNormals";
    public ShaderPassRole Role => ShaderPassRole.DepthPrepass;

    public string EmitPass(MasterNodeBase masterBase, ShaderGraph graph, PassEmitSharedState shared)
    {
        // Transparent geometry must NOT participate it'd corrupt the opaque depth
        // buffer that soft particles / SSAO / scene-color sampling read from.
        if (graph.RenderSettings.Blend != ShaderBlendMode.Opaque)
            return string.Empty;

        var depth = shared.Scratch.TryGetValue(SurfaceDepthHelper.ScratchKey, out var d) ? d as SurfaceDepthHelper : null;

        var sb = new StringBuilder();
        sb.AppendLine("Pass \"DepthNormals\"");
        sb.AppendLine("{");
        sb.AppendLine("    Tags { \"LightMode\" = \"DepthNormals\" }");
        sb.AppendLine($"    Cull {ShaderGraphEmit.CullKeyword(graph.RenderSettings.Cull)}");
        sb.AppendLine();
        sb.AppendLine("    GLSLPROGRAM");

        SurfaceDepthPassEmit.EmitVertexStage(sb, depth);
        SurfaceDepthPassEmit.EmitFragmentStage(sb, depth, emitNormalOut: true);

        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        return sb.ToString();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Shadow pass ShadowCaster into the shadow atlas.
// ═══════════════════════════════════════════════════════════════════════════════

internal sealed class SurfaceShadowPass : IShaderPass
{
    public string Name => "Shadow";
    public ShaderPassRole Role => ShaderPassRole.ShadowCaster;

    public string EmitPass(MasterNodeBase masterBase, ShaderGraph graph, PassEmitSharedState shared)
    {
        var settings = graph.RenderSettings;
        // CastsShadows is the authoritative toggle; transparent is force-off because a
        // solid silhouette doesn't match a translucent render. Cutout users can author
        // an opaque graph with alpha testing.
        if (!settings.CastsShadows || settings.Blend != ShaderBlendMode.Opaque)
            return string.Empty;

        var depth = shared.Scratch.TryGetValue(SurfaceDepthHelper.ScratchKey, out var d) ? d as SurfaceDepthHelper : null;

        var sb = new StringBuilder();
        sb.AppendLine("Pass \"Shadow\"");
        sb.AppendLine("{");
        sb.AppendLine("    Tags { \"LightMode\" = \"ShadowCaster\" }");
        sb.AppendLine($"    Cull {ShaderGraphEmit.CullKeyword(settings.Cull)}");
        sb.AppendLine();
        sb.AppendLine("    GLSLPROGRAM");

        SurfaceDepthPassEmit.EmitVertexStage(sb, depth);
        SurfaceDepthPassEmit.EmitFragmentStage(sb, depth, emitNormalOut: false);

        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        return sb.ToString();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Shared depth/shadow pass vertex + fragment stage emission. Both passes produce
// the exact same vertex stage (optional position offset + UV forwarding for alpha
// cutout) and very similar fragment stages (DepthNormals writes a normal; Shadow
// is depth-only).
// ═══════════════════════════════════════════════════════════════════════════════

internal static class SurfaceDepthPassEmit
{
    public static void EmitVertexStage(StringBuilder sb, SurfaceDepthHelper? depth)
    {
        bool needsVertOffset = depth?.NeedsVertexOffset == true;
        bool needsNormalOut  = true;

        var forwardVaryings = new HashSet<(string name, string type)>();
        if (depth?.NeedsAlphaDiscard == true && depth.AlphaCtx is { } ac)
            foreach (var v in ac.Varyings) forwardVaryings.Add(v);
        forwardVaryings.RemoveWhere(v => v.name == "vNormal");

        sb.AppendLine("    Vertex");
        sb.AppendLine("    {");
        sb.AppendLine("        #include \"ProwlCG\"");
        sb.AppendLine("        #include \"VertexAttributes\"");

        if (needsVertOffset && depth?.VertexCtx is { } vctx)
        {
            foreach (var inc in vctx.Includes)
                if (inc != "ProwlCG" && inc != "VertexAttributes")
                    sb.AppendLine($"        #include \"{inc}\"");
            foreach (var u in vctx.Uniforms) sb.AppendLine($"        {u}");
        }

        if (needsNormalOut) sb.AppendLine("        out vec3 vNormal;");
        foreach (var (n, t) in forwardVaryings) sb.AppendLine($"        out {t} {n};");

        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        if (needsVertOffset && depth?.VertexCtx is { } vctx2)
        {
            if (vctx2.BodyPrelude.Length > 0) sb.Append(vctx2.BodyPrelude.ToString());
            sb.AppendLine($"            vec3 _vertPos = vertexPosition + {depth!.VertexPosOffsetExpr};");
            sb.AppendLine("            gl_Position = TransformClip(_vertPos);");
        }
        else
        {
            sb.AppendLine("            gl_Position = TransformClip(vertexPosition);");
        }
        if (needsNormalOut) sb.AppendLine("            vNormal = TransformDirection(vertexNormal);");
        foreach (var (n, _) in forwardVaryings)
        {
            switch (n)
            {
                case "texCoord0":     sb.AppendLine("            texCoord0 = vertexTexCoord0;"); break;
                case "texCoord1":     sb.AppendLine("            texCoord1 = vertexTexCoord1;"); break;
                case "worldPos":      sb.AppendLine("            worldPos = TransformPosition(vertexPosition);"); break;
                case "vColor":        sb.AppendLine("            vColor = GetInstanceColor();"); break;
                case "vTangent":      sb.AppendLine("            vTangent = TransformDirection(vertexTangent.xyz);"); break;
                case "vBitangent":    sb.AppendLine("            vBitangent = cross(TransformDirection(vertexNormal), TransformDirection(vertexTangent.xyz));"); break;
                case "vInstanceData": sb.AppendLine("            vInstanceData = GetInstanceCustomData();"); break;
            }
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    public static void EmitFragmentStage(StringBuilder sb, SurfaceDepthHelper? depth, bool emitNormalOut)
    {
        bool doDiscard = depth?.NeedsAlphaDiscard == true && depth.AlphaCtx != null;

        sb.AppendLine("    Fragment");
        sb.AppendLine("    {");
        sb.AppendLine("        #include \"ProwlCG\"");

        if (doDiscard && depth?.AlphaCtx is { } actx)
        {
            foreach (var inc in actx.Includes)
                if (inc != "ProwlCG") sb.AppendLine($"        #include \"{inc}\"");
            foreach (var u in actx.Uniforms) sb.AppendLine($"        {u}");
            foreach (var (n, t) in actx.Varyings)
            {
                if (n == "vNormal" && emitNormalOut) continue;
                sb.AppendLine($"        in {t} {n};");
            }
        }

        if (emitNormalOut)
        {
            sb.AppendLine("        layout (location = 0) out vec4 normalOut;");
            sb.AppendLine("        in vec3 vNormal;");
        }

        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        if (doDiscard && depth?.AlphaCtx is { } actx2)
        {
            if (actx2.BodyPrelude.Length > 0) sb.Append(actx2.BodyPrelude.ToString());
            sb.AppendLine($"            float _sgCutoffAlpha  = {depth!.AlphaExpr};");
            sb.AppendLine($"            float _sgCutoffThresh = {depth.CutoffExpr};");
            sb.AppendLine("            if (_sgCutoffThresh > 0.0 && _sgCutoffAlpha < _sgCutoffThresh) discard;");
        }

        if (emitNormalOut)
            sb.AppendLine("            normalOut = EncodeViewNormal(normalize(vNormal));");
        else
            sb.AppendLine("            gl_FragDepth = gl_FragCoord.z;");

        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }
}
