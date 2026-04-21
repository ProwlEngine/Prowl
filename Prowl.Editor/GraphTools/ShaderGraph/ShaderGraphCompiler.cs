// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;
using System.Text;

using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

namespace Prowl.Editor.GraphTools.ShaderGraphs;

/// <summary>
/// Walks a <see cref="ShaderGraph"/> from the master <see cref="PBROutputNode"/> back
/// through wires, collects every property + every transitively-referenced node, emits
/// a complete Prowl <c>.shader</c> source string, and returns it. The importer hands
/// that string to <c>ShaderParser</c> to produce the <see cref="Resources.Shader"/>
/// sub-asset that materials bind to.
/// </summary>
public static class ShaderGraphCompiler
{
    public sealed class Result
    {
        /// <summary>Generated GLSL <c>.shader</c> source. Always non-null even on failure
        /// (will be a stub fallback shader so editors can still display something).</summary>
        public string ShaderSource = "";
        /// <summary>Errors / warnings produced during compilation. Surfaced as
        /// Node.Messages by the editor's per-frame validation pass.</summary>
        public List<(System.Guid? nodeId, string message, NodeMessageSeverity severity)> Diagnostics = new();
        public bool HasErrors => Diagnostics.Any(d => d.severity == NodeMessageSeverity.Error);
    }

    public static Result Compile(Prowl.Runtime.GraphTools.Graph graph, string shaderName)
    {
        var result = new Result();

        // Locate the master node — the graph is invalid without exactly one.
        PBROutputNode? master = null;
        foreach (var n in graph.Nodes)
            if (n is PBROutputNode m) { master = m; break; }
        if (master == null)
        {
            result.Diagnostics.Add((null, "Graph has no PBR Output node — nothing to compile.", NodeMessageSeverity.Error));
            result.ShaderSource = StubShader(shaderName, "Graph missing master output node.");
            return result;
        }

        // Render settings come from the ShaderGraph asset when available; fall back to
        // opaque defaults for foreign Graph types (shouldn't happen — the importer only
        // calls this for ShaderGraph).
        var settings = (graph as ShaderGraph)?.RenderSettings ?? ShaderGraphRenderSettings.OpaqueDefaults();

        // Single Properties{} block — shared between vertex and fragment passes.
        var propertyBlock = new List<string>();
        var propertyUniforms = new List<string>(); // emitted into both stages' uniform sets
        CollectProperties(graph, propertyBlock, propertyUniforms);

        // Compile the fragment stage — the bulk of work happens here.
        var fragCtx = new ShaderGenContext(graph, ShaderStage.Fragment);
        foreach (var u in propertyUniforms) fragCtx.Uniforms.Add(u);
        // Always need the standard surface helper for the StandardSurface() call below.
        fragCtx.Includes.Add("Fragment");
        fragCtx.Includes.Add("StandardSurface");
        fragCtx.Varyings.Add(("texCoord0", "vec2"));
        fragCtx.Varyings.Add(("worldPos",  "vec3"));
        fragCtx.Varyings.Add(("vColor",    "vec4"));
        fragCtx.Varyings.Add(("vNormal",   "vec3"));
        fragCtx.Varyings.Add(("vTangent",  "vec3"));
        fragCtx.Varyings.Add(("vBitangent","vec3"));

        var surfaceCall = BuildSurfaceCall(master, fragCtx, settings);
        result.Diagnostics.AddRange(fragCtx.Diagnostics);

        // Vertex stage — pulls Vertex Position / Vertex Normal off the master node.
        // Connected wires emit their expression into the vertex main() before transform;
        // unconnected ports fall back to the pristine vertexPosition / vertexNormal
        // attributes (compiler emits no extra code for them).
        var vertCtx = new ShaderGenContext(graph, ShaderStage.Vertex);
        foreach (var u in propertyUniforms) vertCtx.Uniforms.Add(u);
        vertCtx.Includes.Add("Fragment");
        vertCtx.Includes.Add("VertexAttributes");
        vertCtx.Varyings.Add(("texCoord0", "vec2"));
        vertCtx.Varyings.Add(("worldPos",  "vec3"));
        vertCtx.Varyings.Add(("vColor",    "vec4"));
        vertCtx.Varyings.Add(("vNormal",   "vec3"));
        vertCtx.Varyings.Add(("vTangent",  "vec3"));
        vertCtx.Varyings.Add(("vBitangent","vec3"));

        var vertexBody = BuildVertexBody(master, vertCtx);
        result.Diagnostics.AddRange(vertCtx.Diagnostics);

        // Mirror any fragment-only varyings back to the vertex stage so `out`/`in`
        // pairs line up. A node may add a varying (e.g. texCoord1) during fragment
        // evaluation; without mirroring, the vertex stage wouldn't declare the matching
        // `out` and the shader would fail to link.
        foreach (var v in fragCtx.Varyings) vertCtx.Varyings.Add(v);

        // Build helper subtrees for the depth-only passes. The depth pre-pass and
        // shadow caster need the ALPHA CUTOUT path when the graph is cutout, and
        // the VERTEX OFFSET when the master's Vertex Position port is driven —
        // otherwise depth disagrees with the actual colour output and foliage /
        // displaced geometry cast wrong-shaped shadows.
        DepthPassHelper? depthHelper = BuildDepthPassHelper(graph, master, settings, propertyUniforms);
        if (depthHelper != null)
            result.Diagnostics.AddRange(depthHelper.Diagnostics);

        result.ShaderSource = EmitShader(shaderName, propertyBlock,
            vertCtx, fragCtx, surfaceCall, vertexBody, settings, depthHelper);
        return result;
    }

    /// <summary>
    /// Captures the slice of the graph needed by the DepthNormals + Shadow passes —
    /// the alpha cutout subtree (when the graph is AlphaTest) and the vertex offset
    /// subtree (when Vertex Position is wired on the master). Depth and shadow passes
    /// emit from this same blueprint so they stay in sync.
    /// </summary>
    private sealed class DepthPassHelper
    {
        /// <summary>Fragment-stage context holding the uniforms / samplers / varyings
        /// the alpha subtree pulled in. Null when the graph has no alpha cutout.</summary>
        public ShaderGenContext? AlphaCtx;
        /// <summary>GLSL expression for the evaluated Alpha input ("1.0" when unwired).</summary>
        public string AlphaExpr = "1.0";
        /// <summary>GLSL expression for the evaluated Alpha Cutoff input.</summary>
        public string CutoffExpr = "0.0";
        /// <summary>True when the fragment body should <c>discard</c> on alpha below cutoff.</summary>
        public bool NeedsAlphaDiscard;

        /// <summary>Vertex-stage context holding whatever the Vertex Position subtree
        /// needed. Null when no vertex offset is wired.</summary>
        public ShaderGenContext? VertexCtx;
        /// <summary>GLSL expression for the Vertex Position offset ("vec3(0.0)" when unwired).</summary>
        public string VertexPosOffsetExpr = "vec3(0.0)";
        /// <summary>True when the vertex body should apply an authored position offset.</summary>
        public bool NeedsVertexOffset;

        public readonly List<(System.Guid? nodeId, string message, NodeMessageSeverity severity)> Diagnostics = new();
    }

    private static DepthPassHelper? BuildDepthPassHelper(Prowl.Runtime.GraphTools.Graph graph,
        PBROutputNode master, ShaderGraphRenderSettings settings, List<string> propertyUniforms)
    {
        bool hasAlphaWire       = HasIncomingEdgeNamed(graph, master, "Alpha");
        bool hasCutoffWire      = HasIncomingEdgeNamed(graph, master, "Alpha Cutoff");
        bool cutoutQueue        = settings.Queue == ShaderRenderQueue.AlphaTest;
        bool needsAlphaDiscard  = cutoutQueue || hasAlphaWire || hasCutoffWire;

        bool needsVertexOffset  = HasIncomingEdgeNamed(graph, master, "Vertex Position");

        if (!needsAlphaDiscard && !needsVertexOffset) return null;

        var helper = new DepthPassHelper
        {
            NeedsAlphaDiscard = needsAlphaDiscard,
            NeedsVertexOffset = needsVertexOffset,
        };

        if (needsAlphaDiscard)
        {
            var ctx = new ShaderGenContext(graph, ShaderStage.Fragment);
            foreach (var u in propertyUniforms) ctx.Uniforms.Add(u);
            ctx.Includes.Add("Fragment");
            // texCoord0 is what the sampler-path UV fallback uses; pre-populate so the
            // depth pass declares the matching varying on the vertex side too.
            ctx.Varyings.Add(("texCoord0", "vec2"));

            helper.AlphaExpr  = EvalMaster(master, "Alpha",        ctx, "1.0");
            helper.CutoffExpr = EvalMaster(master, "Alpha Cutoff", ctx, "0.0");
            helper.AlphaCtx   = ctx;
            helper.Diagnostics.AddRange(ctx.Diagnostics);
        }

        if (needsVertexOffset)
        {
            var ctx = new ShaderGenContext(graph, ShaderStage.Vertex);
            foreach (var u in propertyUniforms) ctx.Uniforms.Add(u);
            ctx.Includes.Add("Fragment");
            ctx.Includes.Add("VertexAttributes");

            helper.VertexPosOffsetExpr = EvalMaster(master, "Vertex Position", ctx, "vec3(0.0)");
            helper.VertexCtx = ctx;
            helper.Diagnostics.AddRange(ctx.Diagnostics);
        }

        return helper;
    }

    /// <summary>Find by-name whether a master input port has an incoming edge. Lighter
    /// than <see cref="HasIncomingEdge"/> because we don't need the ctx (this runs
    /// before per-pass contexts are built).</summary>
    private static bool HasIncomingEdgeNamed(Prowl.Runtime.GraphTools.Graph graph, Node node, string portName)
    {
        var port = node.GetInput(portName);
        if (port == null || port.IsHidden) return false;
        foreach (var e in graph.Edges)
            if (e.TargetNodeId == node.Id && e.TargetPortName == portName) return true;
        return false;
    }

    /// <summary>Unlit fragment body — flat-shaded passthrough of Albedo + Emission,
    /// no lighting, no normal calc, no fog (matches typical UI / particle / billboard
    /// expectations). Uses the same AlphaCutoff handling as the PBR path.</summary>
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

    /// <summary>Lambert diffuse: albedo × max(N·L, 0) summed per light, plus optional
    /// ambient + emission. Cheapest real-lighting mode.</summary>
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
        // Lambert runs the same forward-lighting helper as PBR but with metallic=0 and
        // roughness=1 → the BRDF degenerates to pure Lambert diffuse.
        sb.AppendLine("    vec3 _sgLighting = CalculateForwardLighting(worldPos, _sgWorldN, normalize(_WorldSpaceCameraPos.xyz - worldPos), _sgBaseColor, 0.0, 1.0, _sgAO);");
        if (settings.ReceivesAmbient)
            sb.AppendLine("    vec3 _sgAmbient = CalculateAmbient(_sgWorldN) * _sgBaseColor * _sgAO * _AmbientStrength;");
        else
            sb.AppendLine("    vec3 _sgAmbient = vec3(0.0);");
        sb.AppendLine("    vec3 _sgColor = ApplyFog(_sgAmbient + _sgLighting + _sgEmission, worldPos);");
        sb.AppendLine("    fragColor = vec4(_sgColor, _sgAlpha);");
        return sb.ToString();
    }

    /// <summary>Blinn-Phong: Lambert diffuse plus half-vector specular lobe. Uses
    /// Roughness input as a gloss exponent — lower roughness = tighter, shinier specular.
    /// Implementation reuses CalculateForwardLighting with metal=0 and clamps roughness
    /// so the lighting helper's GGX specular still produces a recognisable Phong-like
    /// highlight; true Blinn-Phong would need its own lighting helper.</summary>
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

    /// <summary>Build the vertex shader's main() body. Adds object-space Position /
    /// Normal offsets from the master node before computing the world-space outputs.
    /// Both inputs are optional — when unwired we just use the raw vertex attributes.</summary>
    private static string BuildVertexBody(PBROutputNode master, ShaderGenContext ctx)
    {
        var sb = new StringBuilder();

        bool hasPosOffset = HasIncomingEdge(master, "Vertex Position", ctx);
        bool hasNormalOverride = HasIncomingEdge(master, "Vertex Normal", ctx);

        sb.AppendLine("    vec3 _vertPos = vertexPosition;");
        sb.AppendLine("    vec3 _vertNormal = vertexNormal;");
        if (hasPosOffset)
        {
            var expr = EvalMaster(master, "Vertex Position", ctx, "vec3(0.0)");
            sb.AppendLine($"    _vertPos += {expr};");
        }
        if (hasNormalOverride)
        {
            var expr = EvalMaster(master, "Vertex Normal", ctx, "vertexNormal");
            sb.AppendLine($"    _vertNormal = {expr};");
        }
        sb.AppendLine("    gl_Position = TransformClip(_vertPos);");
        sb.AppendLine("    texCoord0 = vertexTexCoord0;");
        // Optional varyings — assigned only when a fragment node asked for them. Keeps
        // unused slots out of the generated shader.
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

    /// <summary>Evaluate a master-node input by name. When the port is missing (renamed,
    /// template drift, typo), surface a diagnostic and return <paramref name="fallback"/>
    /// so the compile still produces something instead of null-deref crashing the importer.</summary>
    private static string EvalMaster(PBROutputNode master, string portName, ShaderGenContext ctx, string fallback)
    {
        var port = master.GetInput(portName);
        if (port == null)
        {
            ctx.Diagnostics.Add((master.Id, $"Master output is missing expected input port '{portName}'.", NodeMessageSeverity.Error));
            return fallback;
        }
        return ctx.EvaluateInput(port);
    }

    // ─── Property collection ─────────────────────────────────────────────────────────

    private static void CollectProperties(Prowl.Runtime.GraphTools.Graph graph,
        List<string> propertyBlock, List<string> uniformDecls)
    {
        // De-dupe by emitted property name — multiple property nodes with the same Name
        // would be a user error (validator can flag), but here we just take the first.
        var seen = new HashSet<string>();
        foreach (var n in graph.Nodes)
        {
            if (n is not IShaderProperty p) continue;
            var name = p.PropertyName;
            if (!seen.Add(name)) continue;

            var keyword = ShaderTypeUtil.ToPropertyKeyword(p.PropertyType);
            if (keyword == null) continue; // not a material-property-able type

            // Range(min, max) — override the plain "Float" keyword when the node asks
            // for a ranged slider. The parser lowers Range back to ShaderPropertyType.Float
            // at import time; this is just a way to carry the min/max hint through the
            // generated .shader source.
            if (n is IShaderPropertyRange rp && p.PropertyType == ShaderType.Float)
                keyword = $"Range({ShaderGenContext.Fmt(rp.RangeMin)}, {ShaderGenContext.Fmt(rp.RangeMax)})";

            // Matrix properties have no parseable default syntax — emit without `= ...`.
            // Empty or whitespace default literal is treated the same way.
            var def = p.DefaultLiteral;
            if (string.IsNullOrWhiteSpace(def))
                propertyBlock.Add($"    {name} (\"{p.DisplayName}\", {keyword})");
            else
                propertyBlock.Add($"    {name} (\"{p.DisplayName}\", {keyword}) = {def}");
            uniformDecls.Add($"uniform {ShaderTypeUtil.ToGlsl(p.PropertyType)} {name};");
        }
    }

    // ─── Master node lowering ────────────────────────────────────────────────────────

    /// <summary>Build the fragment shader main() body for the master output node.
    /// Branches on <see cref="ShaderLightingMode"/>: Unlit emits a flat passthrough;
    /// PBR plugs the graph-driven surface terms into Prowl's lighting pipeline.</summary>
    private static string BuildSurfaceCall(PBROutputNode master, ShaderGenContext ctx, ShaderGraphRenderSettings settings)
    {
        // Always evaluate Albedo / Alpha / Emission / Cutoff — they're shared between
        // both lighting modes. PBR-only inputs are pulled below in the PBR branch so
        // unconnected ports in Unlit mode don't generate dead code.
        string albedo      = EvalMaster(master, "Albedo",       ctx, "vec4(1.0)");
        string alpha       = EvalMaster(master, "Alpha",        ctx, "1.0");
        string emission    = EvalMaster(master, "Emission",     ctx, "vec3(0.0)");
        string alphaCutoff = EvalMaster(master, "Alpha Cutoff", ctx, "0.0");

        if (master.Lighting == ShaderLightingMode.Unlit)
            return BuildUnlitBody(albedo, alpha, emission, alphaCutoff);

        string normalTS    = EvalMaster(master, "Normal",    ctx, "vec3(0.0, 0.0, 1.0)");
        string metallic    = EvalMaster(master, "Metallic",  ctx, "0.0");
        string roughness   = EvalMaster(master, "Roughness", ctx, "0.5");
        string occlusion   = EvalMaster(master, "Occlusion", ctx, "1.0");

        if (master.Lighting == ShaderLightingMode.Lambert)
            return BuildLambertBody(albedo, alpha, normalTS, occlusion, emission, alphaCutoff, ctx, settings);
        if (master.Lighting == ShaderLightingMode.BlinnPhong)
            return BuildBlinnPhongBody(albedo, alpha, normalTS, roughness, occlusion, emission, alphaCutoff, ctx, settings);

        // We emit a bespoke surface inline rather than calling StandardSurface() — that
        // helper takes texture *samplers* as arguments (built around the standard slots).
        // The graph instead computes the surface terms directly, then plugs them into
        // the lighting helpers from Lighting.glsl. This makes graph nodes the source
        // of truth for albedo/normal/etc. rather than fixed sampler bindings.
        ctx.Includes.Add("Lighting");
        // SG_NO_SHADOWS tells Lighting.glsl to skip shadow-map sampling entirely —
        // wrapped in #ifdef/#endif so zero runtime cost when the surface opts out.
        if (!settings.ReceivesShadows) ctx.Defines.Add("SG_NO_SHADOWS");

        // Local prefix: single leading underscore + letter (`_sg` for "shader graph") —
        // GLSL forbids identifiers containing two consecutive underscores ANYWHERE,
        // and reserves names beginning with `gl_`. `_sgX` is safe.
        var sb = new StringBuilder();
        sb.AppendLine("    // ── Graph-driven surface evaluation ──");
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
        sb.AppendLine("    // Tangent-space → world-space normal");
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

    // ─── Shader source assembly ──────────────────────────────────────────────────────

    private static string EmitShader(string shaderName, List<string> propertyBlock,
        ShaderGenContext vertCtx, ShaderGenContext fragCtx, string surfaceMain, string vertexBody,
        ShaderGraphRenderSettings settings, DepthPassHelper? depth)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Shader \"Generated/{shaderName}\"");
        sb.AppendLine();
        sb.AppendLine("Properties");
        sb.AppendLine("{");
        foreach (var p in propertyBlock) sb.AppendLine(p);
        sb.AppendLine("}");
        sb.AppendLine();

        EmitStandardPass(sb, vertCtx, fragCtx, surfaceMain, vertexBody, settings);
        // Transparent geometry must NOT participate in the depth pre-pass — it'd
        // corrupt the opaque depth buffer that soft particles, SSAO, and scene-color
        // sampling read from, and its own blend mode would never land behind it.
        // Only opaque shaders contribute to _CameraDepthTexture.
        if (settings.Blend == ShaderBlendMode.Opaque)
            EmitDepthNormalsPass(sb, settings, depth);

        // Shadow casting: CastsShadows is the authoritative toggle, but transparent
        // surfaces are clamped off because a solid silhouette doesn't match the
        // translucent render. Users who need cutout / dithered-alpha shadow casters
        // can author an opaque graph with alpha testing in the fragment body.
        if (settings.CastsShadows && settings.Blend == ShaderBlendMode.Opaque)
            EmitShadowPass(sb, settings, depth);

        return sb.ToString();
    }

    private static string QueueTag(ShaderRenderQueue q) => q switch
    {
        ShaderRenderQueue.Background  => "Background",
        ShaderRenderQueue.AlphaTest   => "AlphaTest",
        ShaderRenderQueue.Transparent => "Transparent",
        ShaderRenderQueue.Overlay     => "Overlay",
        _                              => "Opaque",
    };

    private static string CullKeyword(ShaderCullMode c) => c switch
    {
        ShaderCullMode.Front        => "Front",
        ShaderCullMode.Off          => "Off",
        ShaderCullMode.FrontAndBack => "FrontAndBack",
        _                            => "Back",
    };

    // Parser uses Unity-style shortened names: Less / LEqual / Equal / GEqual / Greater /
    // NotEqual / Always / Never / Off. Keep the shader-graph enum close to those for
    // 1:1 user mental model, but emit exactly what the parser expects.
    private static string ZTestKeyword(ShaderZTest z) => z switch
    {
        ShaderZTest.Off          => "Off",
        ShaderZTest.Never        => "Never",
        ShaderZTest.Less         => "Less",
        ShaderZTest.Equal        => "Equal",
        ShaderZTest.LessEqual    => "LEqual",
        ShaderZTest.Greater      => "Greater",
        ShaderZTest.NotEqual     => "NotEqual",
        ShaderZTest.GreaterEqual => "GEqual",
        ShaderZTest.Always       => "Always",
        _                         => "LEqual",
    };

    /// <summary>Render-state block emitted before GLSLPROGRAM. Emits exactly the syntax
    /// the shader parser understands — Blend uses preset names where possible and the
    /// <c>Blend { Src X; Dst Y; Mode Z; }</c> block form for custom pairs. Winding and
    /// the full ZTest enum are emitted when the graph asks for non-default values.</summary>
    private static void AppendRenderState(StringBuilder sb, ShaderGraphRenderSettings s, string indent)
    {
        sb.AppendLine($"{indent}Tags {{ \"RenderOrder\" = \"{QueueTag(s.Queue)}\" }}");
        sb.AppendLine($"{indent}Cull {CullKeyword(s.Cull)}");
        sb.AppendLine($"{indent}ZWrite {(s.ZWrite ? "On" : "Off")}");
        sb.AppendLine($"{indent}ZTest {ZTestKeyword(s.ZTest)}");
        if (s.Winding == ShaderWinding.CCW)
            sb.AppendLine($"{indent}Winding CCW");

        AppendBlendLine(sb, s, indent);
    }

    /// <summary>
    /// Emit the Blend line in parser-correct syntax. Parser accepts four presets
    /// (Off/Additive/Alpha/Override) as a single identifier; anything else needs the
    /// block form with Src/Dst/Mode entries. We use presets when the settings match
    /// exactly so the generated shader stays readable.
    /// </summary>
    private static void AppendBlendLine(StringBuilder sb, ShaderGraphRenderSettings s, string indent)
    {
        // Custom mode uses BlendSrc/BlendDst/BlendOp fields directly.
        if (s.Blend == ShaderBlendMode.Custom)
        {
            sb.AppendLine($"{indent}Blend {{ Src {BlendFactor(s.BlendSrc)}; Dst {BlendFactor(s.BlendDst)}; Mode {BlendOp(s.BlendOp)}; }}");
            return;
        }

        // Built-in presets — emit the parser's preset name directly.
        switch (s.Blend)
        {
            case ShaderBlendMode.Opaque:
                // No Blend line at all — parser treats absence as DoBlend=false.
                return;
            case ShaderBlendMode.Alpha:
                sb.AppendLine($"{indent}Blend Alpha"); return;
            case ShaderBlendMode.Additive:
                sb.AppendLine($"{indent}Blend Additive"); return;
            case ShaderBlendMode.Override:
                sb.AppendLine($"{indent}Blend Override"); return;
            // Multiply / Premultiplied don't have preset names in the parser — fall
            // back to block form. Same for anything else we add later.
            case ShaderBlendMode.Multiply:
                sb.AppendLine($"{indent}Blend {{ Src DstColor; Dst Zero; Mode Add; }}"); return;
            case ShaderBlendMode.Premultiplied:
                sb.AppendLine($"{indent}Blend {{ Src One; Dst OneMinusSrcAlpha; Mode Add; }}"); return;
        }
    }

    private static string BlendFactor(ShaderBlendFactor f) => f.ToString();
    private static string BlendOp(ShaderBlendOp op) => op.ToString();

    private static void EmitStandardPass(StringBuilder sb,
        ShaderGenContext vertCtx, ShaderGenContext fragCtx, string surfaceMain, string vertexBody,
        ShaderGraphRenderSettings settings)
    {
        sb.AppendLine("Pass \"Standard\"");
        sb.AppendLine("{");
        AppendRenderState(sb, settings, "    ");
        // Pass-level directives pushed by nodes (e.g. `GrabTexture "_GrabTexture"` from
        // SceneColorNode). Fragment nodes push; we emit once the settings block is done.
        foreach (var d in fragCtx.PassDirectives) sb.AppendLine($"    {d}");
        sb.AppendLine();
        sb.AppendLine("    GLSLPROGRAM");
        sb.AppendLine();
        // ─ Vertex ─
        sb.AppendLine("    Vertex");
        sb.AppendLine("    {");
        // #define lines come BEFORE #include so the included GLSL sees them via the
        // preprocessor — used for SG_NO_SHADOWS etc. that Lighting.glsl branches on.
        foreach (var d in vertCtx.Defines) sb.AppendLine($"        #define {d}");
        foreach (var inc in vertCtx.Includes) sb.AppendLine($"        #include \"{inc}\"");
        foreach (var (n, t) in vertCtx.Varyings) sb.AppendLine($"        out {t} {n};");
        foreach (var u in vertCtx.Uniforms) sb.AppendLine($"        {u}");
        // File-scope helper functions (e.g. CustomCode wrappers) go BEFORE main().
        if (vertCtx.TopLevelHelpers.Length > 0) { sb.AppendLine(); sb.Append(vertCtx.TopLevelHelpers.ToString()); }
        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        if (vertCtx.BodyPrelude.Length > 0) sb.Append(vertCtx.BodyPrelude.ToString());
        sb.Append(vertexBody);
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        // ─ Fragment ─
        sb.AppendLine("    Fragment");
        sb.AppendLine("    {");
        foreach (var d in fragCtx.Defines) sb.AppendLine($"        #define {d}");
        foreach (var inc in fragCtx.Includes) sb.AppendLine($"        #include \"{inc}\"");
        sb.AppendLine();
        sb.AppendLine("        layout (location = 0) out vec4 fragColor;");
        foreach (var (n, t) in fragCtx.Varyings) sb.AppendLine($"        in {t} {n};");
        foreach (var u in fragCtx.Uniforms) sb.AppendLine($"        {u}");
        if (fragCtx.TopLevelHelpers.Length > 0) { sb.AppendLine(); sb.Append(fragCtx.TopLevelHelpers.ToString()); }
        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        if (fragCtx.BodyPrelude.Length > 0) sb.Append(fragCtx.BodyPrelude.ToString());
        sb.Append(surfaceMain);
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitDepthNormalsPass(StringBuilder sb, ShaderGraphRenderSettings settings, DepthPassHelper? depth)
    {
        sb.AppendLine("Pass \"DepthNormals\"");
        sb.AppendLine("{");
        sb.AppendLine("    Tags { \"LightMode\" = \"DepthNormals\" }");
        sb.AppendLine($"    Cull {CullKeyword(settings.Cull)}");
        sb.AppendLine();
        sb.AppendLine("    GLSLPROGRAM");

        EmitDepthVertexStage(sb, depth);
        EmitDepthFragmentStage(sb, depth, emitNormalOut: true);

        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitShadowPass(StringBuilder sb, ShaderGraphRenderSettings settings, DepthPassHelper? depth)
    {
        sb.AppendLine("Pass \"Shadow\"");
        sb.AppendLine("{");
        sb.AppendLine("    Tags { \"LightMode\" = \"ShadowCaster\" }");
        sb.AppendLine($"    Cull {CullKeyword(settings.Cull)}");
        sb.AppendLine();
        sb.AppendLine("    GLSLPROGRAM");

        EmitDepthVertexStage(sb, depth);
        EmitDepthFragmentStage(sb, depth, emitNormalOut: false);

        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
    }

    /// <summary>
    /// Vertex stage shared between DepthNormals and Shadow passes. Always transforms
    /// the vertex to clip space; optionally applies the authored Vertex Position
    /// offset + forwards the UV varying when the alpha cutout subtree needs it.
    /// </summary>
    private static void EmitDepthVertexStage(StringBuilder sb, DepthPassHelper? depth)
    {
        bool needsVertOffset = depth?.NeedsVertexOffset == true;
        bool needsNormalOut  = true; // DepthNormals always needs world normal forwarded

        // Collect every varying the alpha cutout subtree referenced so the vertex
        // stage can `out` them and assign from the appropriate attribute. Without
        // this, a cutout graph that samples a texture at UV1, uses VertexColor as
        // a mask, or reads a world normal would fail to link.
        var forwardVaryings = new HashSet<(string name, string type)>();
        if (depth?.NeedsAlphaDiscard == true && depth.AlphaCtx is { } ac)
            foreach (var v in ac.Varyings) forwardVaryings.Add(v);
        // vNormal is always written anyway (DepthNormals pass encodes it); don't
        // re-emit the `out` line.
        forwardVaryings.RemoveWhere(v => v.name == "vNormal");

        sb.AppendLine("    Vertex");
        sb.AppendLine("    {");
        sb.AppendLine("        #include \"Fragment\"");
        sb.AppendLine("        #include \"VertexAttributes\"");

        // Uniforms + includes pulled in by the vertex offset subtree (e.g. a sampler
        // the user wired into a wind-sway custom-code node).
        if (needsVertOffset && depth?.VertexCtx is { } vctx)
        {
            foreach (var inc in vctx.Includes)
                if (inc != "Fragment" && inc != "VertexAttributes")
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
        // Assign each forwarded varying from its source attribute. The mapping
        // mirrors what the main Standard pass's vertex body does.
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
                // Unknown varying — leave uninitialised rather than guess. The
                // fragment side will read garbage, which is no worse than before.
            }
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    /// <summary>
    /// Fragment stage shared between DepthNormals and Shadow passes. When the graph
    /// is cutout, evaluates the Alpha + AlphaCutoff subtree and discards below the
    /// threshold. <paramref name="emitNormalOut"/> controls whether the encoded view
    /// normal is written (DepthNormals) or whether the pass is purely depth (Shadow).
    /// </summary>
    private static void EmitDepthFragmentStage(StringBuilder sb, DepthPassHelper? depth, bool emitNormalOut)
    {
        bool doDiscard = depth?.NeedsAlphaDiscard == true && depth.AlphaCtx != null;

        sb.AppendLine("    Fragment");
        sb.AppendLine("    {");
        sb.AppendLine("        #include \"Fragment\"");

        // Pull in every include, uniform, and varying the alpha subtree needed so
        // `texture(_MainTex, uv)` and friends resolve. The cutout expressions were
        // authored against these. Declaring ALL of the subtree's varyings here
        // mirrors what the vertex stage forwards — see EmitDepthVertexStage.
        if (doDiscard && depth?.AlphaCtx is { } actx)
        {
            foreach (var inc in actx.Includes)
                if (inc != "Fragment") sb.AppendLine($"        #include \"{inc}\"");
            foreach (var u in actx.Uniforms) sb.AppendLine($"        {u}");
            foreach (var (n, t) in actx.Varyings)
            {
                // vNormal is always declared when emitNormalOut is true — avoid a
                // duplicate declaration.
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
            // Standard cutout: discard when alpha falls below the threshold. A zero
            // cutoff is treated as "cutoff disabled" so graphs that were switched to
            // AlphaTest but haven't wired Alpha Cutoff yet don't accidentally kill
            // every fragment.
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

    private static string StubShader(string shaderName, string reason)
    {
        // Magenta "shader's broken" placeholder — keeps materials renderable.
        var sb = new StringBuilder();
        sb.AppendLine($"Shader \"Generated/{shaderName}\"");
        sb.AppendLine("// " + reason);
        sb.AppendLine("Properties { }");
        sb.AppendLine("Pass \"Standard\"");
        sb.AppendLine("{");
        sb.AppendLine("    Tags { \"RenderOrder\" = \"Opaque\" }");
        sb.AppendLine("    GLSLPROGRAM");
        sb.AppendLine("    Vertex { #include \"Fragment\" #include \"VertexAttributes\" void main() { gl_Position = TransformClip(vertexPosition); } }");
        sb.AppendLine("    Fragment { layout(location = 0) out vec4 fragColor; void main() { fragColor = vec4(1.0, 0.0, 1.0, 1.0); } }");
        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
