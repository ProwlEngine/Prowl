// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Grass shader type GPU-instanced billboards positioned per-blade in terrain-local
// space, optionally aligned to the terrain normal, with distance fade and wind sway.
// Vertex stage is hardcoded; fragment runs the user's surface math.

using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Editor.GraphTools.ShaderGraphs.Editors;
using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools.ShaderGraphs.Editors.Types;

public sealed class GrassShaderType : IShaderType
{
    public const string TypeId = "Grass";

    public string Id => TypeId;
    public string DisplayName => "Grass";
    public Type MasterNodeType => typeof(GrassMasterNode);

    private static readonly IShaderPass[] s_passes = { new GrassPass() };
    public IReadOnlyList<IShaderPass> Passes => s_passes;

    public ShaderGraphRenderSettings DefaultRenderSettings
    {
        get
        {
            // Grass-specific defaults: opaque + cutout (AlphaTest queue) + no-cull so
            // blades are two-sided, doesn't cast shadows (too expensive for density).
            var s = ShaderGraphRenderSettings.OpaqueDefaults();
            s.Cull = ShaderCullMode.Off;
            s.Queue = ShaderRenderQueue.AlphaTest;
            s.CastsShadows = false;
            return s;
        }
    }

    public IReadOnlyList<ShaderTypeMenuEntry> MenuEntries { get; } = new[]
    {
        new ShaderTypeMenuEntry("Default", "Shader Graph/Grass", 90),
    };

    public void SeedGraph(ShaderGraph graph, string variantKey)
    {
        graph.ShaderTypeId = TypeId;
        graph.RenderSettings = DefaultRenderSettings;

        // Seed with a master that defaults to Lambert + terrain alignment on —
        // grass looks convincing straight out of the box, users then wire a texture
        // into Albedo + connect TerrainNormal to Normal for real shading.
        var master = new GrassMasterNode { Position = new Float2(560, 160) };
        graph.AddNode(master);
    }
}

internal sealed class GrassPass : IShaderPass
{
    public string Name => "Grass";
    public ShaderPassRole Role => ShaderPassRole.Forward;

    public string EmitPass(MasterNodeBase masterBase, ShaderGraph graph, PassEmitSharedState shared)
    {
        var master = (GrassMasterNode)masterBase;
        var settings = graph.RenderSettings;

        var fragCtx = new ShaderGenContext(graph, ShaderStage.Fragment);
        foreach (var u in shared.PropertyUniforms) fragCtx.Uniforms.Add(u);
        fragCtx.Includes.Add("ProwlCG");
        if (master.Lighting != ShaderLightingMode.Unlit) fragCtx.Includes.Add("Lighting");

        fragCtx.Varyings.Add(("texCoord0", "vec2"));
        fragCtx.Varyings.Add(("vColor",    "vec4"));
        fragCtx.Varyings.Add(("worldPos",  "vec3"));
        fragCtx.Varyings.Add(("vNormal",   "vec3"));

        string albedo       = SurfacePassHelpers.EvalMaster(master, "Albedo",        fragCtx, "vec4(1.0)");
        string alpha        = SurfacePassHelpers.EvalMaster(master, "Alpha",         fragCtx, "1.0");
        string alphaCutoff  = SurfacePassHelpers.EvalMaster(master, "Alpha Cutoff",  fragCtx, "0.5");
        string emission     = SurfacePassHelpers.EvalMaster(master, "Emission",      fragCtx, "vec3(0.0)");
        string normalTS     = SurfacePassHelpers.EvalMaster(master, "Normal",        fragCtx, "vec3(0,0,1)");
        string roughness    = SurfacePassHelpers.EvalMaster(master, "Roughness",     fragCtx, "0.9");
        string translucency = SurfacePassHelpers.EvalMaster(master, "Translucency",  fragCtx, "25.0");

        // Wind inputs are vertex-stage they drive the sway. Build a small vertex
        // context and evaluate them there so users can modulate via noise / properties.
        var vertCtx = new ShaderGenContext(graph, ShaderStage.Vertex);
        foreach (var u in shared.PropertyUniforms) vertCtx.Uniforms.Add(u);
        vertCtx.Includes.Add("ProwlCG");
        vertCtx.Includes.Add("VertexAttributes");
        // Forward the fragment's varyings vertex writes them, fragment reads.
        foreach (var v in fragCtx.Varyings) vertCtx.Varyings.Add(v);

        string windStrength = SurfacePassHelpers.EvalMaster(master, "Wind Strength", vertCtx, "0.3");
        string windSpeed    = SurfacePassHelpers.EvalMaster(master, "Wind Speed",    vertCtx, "1.5");

        shared.Diagnostics.AddRange(fragCtx.Diagnostics);
        shared.Diagnostics.AddRange(vertCtx.Diagnostics);

        var sb = new StringBuilder();
        sb.AppendLine($"Pass \"{Name}\"");
        sb.AppendLine("{");
        ShaderGraphEmit.AppendRenderState(sb, settings, "    ");
        foreach (var d in fragCtx.PassDirectives) sb.AppendLine($"    {d}");
        sb.AppendLine();
        sb.AppendLine("    GLSLPROGRAM");
        sb.AppendLine();

        EmitVertexStage(sb, master, vertCtx, windStrength, windSpeed);
        sb.AppendLine();
        EmitFragmentStage(sb, master, fragCtx, albedo, alpha, alphaCutoff, emission, normalTS, roughness, translucency, settings);

        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitVertexStage(StringBuilder sb, GrassMasterNode master, ShaderGenContext vertCtx,
        string windStrength, string windSpeed)
    {
        sb.AppendLine("    Vertex");
        sb.AppendLine("    {");
        foreach (var d in vertCtx.Defines)    sb.AppendLine($"        #define {d}");
        foreach (var inc in vertCtx.Includes) sb.AppendLine($"        #include \"{inc}\"");
        sb.AppendLine();
        foreach (var (n, t) in vertCtx.Varyings) sb.AppendLine($"        out {t} {n};");
        foreach (var u in vertCtx.Uniforms)   sb.AppendLine($"        {u}");
        // Terrain uniforms required for alignment and grass-blade world placement.
        sb.AppendLine("        uniform sampler2D _Heightmap;");
        sb.AppendLine("        uniform float _TerrainSize;");
        sb.AppendLine("        uniform float _TerrainHeight;");
        sb.AppendLine("        uniform mat4 _TerrainLocalToWorld;");
        sb.AppendLine("        uniform vec3 _TerrainUp;");
        if (vertCtx.TopLevelHelpers.Length > 0) { sb.AppendLine(); sb.Append(vertCtx.TopLevelHelpers.ToString()); }
        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        if (vertCtx.BodyPrelude.Length > 0) sb.Append(vertCtx.BodyPrelude.ToString());
        sb.AppendLine("#ifdef GPU_INSTANCING");
        sb.AppendLine("            vec3 localPosition = instanceModelRow3.xyz;");
        sb.AppendLine("            vec3 bladePosition = (_TerrainLocalToWorld * vec4(localPosition, 1.0)).xyz;");
        sb.AppendLine("            float scaleX = length(instanceModelRow0.xyz);");
        sb.AppendLine("            float scaleY = length(instanceModelRow1.xyz);");
        sb.AppendLine();

        // Terrain normal at this blade, computed from central-differences on the
        // heightmap. Always computed even when AlignToTerrain is off it's used
        // for lighting.
        sb.AppendLine("            vec2 terrainUV = localPosition.xz / _TerrainSize;");
        sb.AppendLine("            vec2 hmSize2 = vec2(textureSize(_Heightmap, 0));");
        sb.AppendLine("            float vStep = hmSize2.x > 1.0 ? (1.0 / (hmSize2.x - 1.0)) : 0.001;");
        sb.AppendLine("            vec2 baseUV = terrainUV * (hmSize2 - 1.0) / hmSize2 + 0.5 / hmSize2;");
        sb.AppendLine("            vec2 dUV = vec2(vStep * (hmSize2.x - 1.0) / hmSize2.x, 0.0);");
        sb.AppendLine("            float hR = texture(_Heightmap, baseUV + dUV.xy).r * _TerrainHeight;");
        sb.AppendLine("            float hL = texture(_Heightmap, baseUV - dUV.xy).r * _TerrainHeight;");
        sb.AppendLine("            float hU = texture(_Heightmap, baseUV + dUV.yx).r * _TerrainHeight;");
        sb.AppendLine("            float hD = texture(_Heightmap, baseUV - dUV.yx).r * _TerrainHeight;");
        sb.AppendLine("            float wStep = vStep * _TerrainSize;");
        sb.AppendLine("            vec3 localN = normalize(vec3(-(hR - hL) / (wStep * 2.0), 1.0, -(hU - hD) / (wStep * 2.0)));");
        sb.AppendLine("            vec3 terrainNormal = normalize((_TerrainLocalToWorld * vec4(localN, 0.0)).xyz);");
        sb.AppendLine();

        // Up axis for blade orientation.
        if (master.AlignToTerrain)
            sb.AppendLine("            vec3 upDir = terrainNormal;");
        else
            sb.AppendLine("            vec3 upDir = _TerrainUp;");

        sb.AppendLine();
        sb.AppendLine("            vec3 quadRight;");
        sb.AppendLine("            vec3 localOffset;");
        if (master.Billboard)
        {
            sb.AppendLine("            vec3 cameraRight = vec3(PROWL_MATRIX_V[0][0], PROWL_MATRIX_V[1][0], PROWL_MATRIX_V[2][0]);");
            sb.AppendLine("            cameraRight = normalize(cameraRight - upDir * dot(cameraRight, upDir));");
            sb.AppendLine("            quadRight = cameraRight;");
            sb.AppendLine("            localOffset = cameraRight * vertexPosition.x * scaleX + upDir * vertexPosition.y * scaleY;");
        }
        else
        {
            sb.AppendLine("            vec3 rightAxis = normalize((_TerrainLocalToWorld * vec4(normalize(instanceModelRow0.xyz), 0.0)).xyz);");
            sb.AppendLine("            rightAxis = normalize(rightAxis - upDir * dot(rightAxis, upDir));");
            sb.AppendLine("            quadRight = rightAxis;");
            sb.AppendLine("            localOffset = rightAxis * vertexPosition.x * scaleX + upDir * vertexPosition.y * scaleY;");
        }

        sb.AppendLine();
        // Wind sway only affects top vertices (y > 0). Per-blade phase and bend
        // factor live in instance custom data's x,y lanes.
        sb.AppendLine($"            float _sgWindStr = {windStrength};");
        sb.AppendLine($"            float _sgWindSpd = {windSpeed};");
        sb.AppendLine("            float windPhase = instanceCustomData.x;");
        sb.AppendLine("            float bendFactor = instanceCustomData.y;");
        sb.AppendLine("            float windAmount = max(0.0, vertexPosition.y);");
        sb.AppendLine("            float wind = sin(_Time.y * _sgWindSpd + bladePosition.x * 0.7 + bladePosition.z * 0.4 + windPhase) * _sgWindStr * bendFactor;");
        sb.AppendLine("            localOffset.x += wind * windAmount;");
        sb.AppendLine("            localOffset.z += wind * windAmount * 0.3;");
        sb.AppendLine();
        sb.AppendLine("            vec3 wp = bladePosition + localOffset;");
        sb.AppendLine("            wp += upDir * 0.01 * scaleY;");
        sb.AppendLine("            worldPos = wp;");
        sb.AppendLine("            vNormal = normalize(cross(upDir, quadRight));");
        sb.AppendLine("            vColor = instanceColor;");
        sb.AppendLine("            texCoord0 = vertexTexCoord0;");
        sb.AppendLine("            gl_Position = PROWL_MATRIX_VP * vec4(wp, 1.0);");
        sb.AppendLine("#else");
        sb.AppendLine("            gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);");
        sb.AppendLine("            texCoord0 = vertexTexCoord0;");
        sb.AppendLine("            worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;");
        sb.AppendLine("            vColor = vec4(1.0);");
        sb.AppendLine("            vNormal = vec3(0, 1, 0);");
        sb.AppendLine("#endif");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    private static void EmitFragmentStage(StringBuilder sb, GrassMasterNode master, ShaderGenContext fragCtx,
        string albedo, string alpha, string alphaCutoff, string emission, string normalTS, string roughness,
        string translucency, ShaderGraphRenderSettings settings)
    {
        if (!settings.ReceivesShadows) fragCtx.Defines.Add("SG_NO_SHADOWS");

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
        sb.AppendLine($"            vec4  _sgAlbedo  = {albedo} * vColor;");
        sb.AppendLine($"            float _sgAlpha   = {alpha} * vColor.a;");
        sb.AppendLine($"            float _sgCutoff  = {alphaCutoff};");
        sb.AppendLine($"            vec3  _sgEmiss   = {emission};");
        sb.AppendLine();
        sb.AppendLine("            if (_sgCutoff > 0.0 && _sgAlpha < _sgCutoff) discard;");
        sb.AppendLine();
        sb.AppendLine("            vec3 _sgBaseColor = gammaToLinearSpace(_sgAlbedo.rgb);");

        if (master.Lighting == ShaderLightingMode.Unlit)
        {
            sb.AppendLine("            fragColor = vec4(_sgBaseColor + _sgEmiss, _sgAlpha);");
        }
        else
        {
            // Grass doesn't carry tangents build a simple TBN from vNormal so
            // tangent-space normal maps still produce a world-space normal.
            sb.AppendLine($"            vec3 _sgNormalTS = {normalTS};");
            sb.AppendLine("            vec3 _sgN = normalize(vNormal) * (gl_FrontFacing ? 1.0 : -1.0);");
            sb.AppendLine("            vec3 _sgT = normalize(cross(_sgN, abs(_sgN.y) < 0.99 ? vec3(0,1,0) : vec3(1,0,0)));");
            sb.AppendLine("            vec3 _sgB = normalize(cross(_sgN, _sgT));");
            sb.AppendLine("            vec3 _sgWorldN = normalize(mat3(_sgT, _sgB, _sgN) * _sgNormalTS);");
            sb.AppendLine("            vec3 _sgViewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);");
            string met = master.Lighting == ShaderLightingMode.Lambert ? "0.0" : "0.0";  // grass is never metal
            string rg  = master.Lighting == ShaderLightingMode.Lambert ? "1.0" : $"clamp({roughness}, 0.04, 1.0)";
            sb.AppendLine($"            float _sgTrans = {translucency};");
            sb.AppendLine($"            vec3 _sgLighting = CalculateForwardLighting(worldPos, _sgWorldN, _sgViewDir, _sgBaseColor, {met}, {rg}, 1.0, _sgTrans, 0.0, 0.5, 1.0);");
            if (settings.ReceivesAmbient)
                sb.AppendLine("            vec3 _sgAmbient = CalculateAmbient(_sgWorldN) * _sgBaseColor * _AmbientStrength;");
            else
                sb.AppendLine("            vec3 _sgAmbient = vec3(0.0);");
            sb.AppendLine("            vec3 _sgColor = ApplyFog(_sgAmbient + _sgLighting + _sgEmiss, worldPos);");
            sb.AppendLine("            fragColor = vec4(_sgColor, _sgAlpha);");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }
}
