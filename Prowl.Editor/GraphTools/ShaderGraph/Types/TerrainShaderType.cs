// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Terrain shader type — quadtree-chunk meshes GPU-instanced at the terrain's
// world transform. Vertex stage samples _Heightmap to displace vertices and
// compute a central-differences world normal; fragment runs lit PBR on the
// graph-evaluated surface terms.

using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools.ShaderGraphs.Types;

public sealed class TerrainShaderType : IShaderType
{
    public const string TypeId = "Terrain";

    public string Id => TypeId;
    public string DisplayName => "Terrain";
    public Type MasterNodeType => typeof(TerrainMasterNode);

    private static readonly IShaderPass[] s_passes = { new TerrainPass() };
    public IReadOnlyList<IShaderPass> Passes => s_passes;

    public ShaderGraphRenderSettings DefaultRenderSettings => ShaderGraphRenderSettings.OpaqueDefaults();

    public IReadOnlyList<ShaderTypeMenuEntry> MenuEntries { get; } = new[]
    {
        new ShaderTypeMenuEntry("Default", "Shader Graph/Terrain", 80),
    };

    public void SeedGraph(ShaderGraph graph, string variantKey)
    {
        graph.ShaderTypeId = TypeId;
        graph.RenderSettings = DefaultRenderSettings;

        // Minimal functional seed: plain white terrain lit by the terrain's natural
        // normal. Users add a splatmap-driven layer blend by swapping the Albedo
        // wire for SplatmapWeights + textures + TerrainUV.
        var master = new TerrainMasterNode { Position = new Float2(560, 160) };
        graph.AddNode(master);
    }
}

internal sealed class TerrainPass : IShaderPass
{
    public string Name => "Terrain";
    public ShaderPassRole Role => ShaderPassRole.Forward;

    public string EmitPass(MasterNodeBase masterBase, ShaderGraph graph, PassEmitSharedState shared)
    {
        var master = (TerrainMasterNode)masterBase;
        var settings = graph.RenderSettings;

        // Fragment context.
        var fragCtx = new ShaderGenContext(graph, ShaderStage.Fragment);
        foreach (var u in shared.PropertyUniforms) fragCtx.Uniforms.Add(u);
        fragCtx.Includes.Add("Fragment");
        fragCtx.Includes.Add("Lighting");
        fragCtx.Varyings.Add(("texCoord0",   "vec2"));  // terrain UV
        fragCtx.Varyings.Add(("worldPos",    "vec3"));
        fragCtx.Varyings.Add(("worldNormal", "vec3"));

        string albedo    = SurfacePassHelpers.EvalMaster((MasterNodeBase)master, "Albedo",    fragCtx, "vec4(1.0)");
        string normalTS  = SurfacePassHelpers.EvalMaster((MasterNodeBase)master, "Normal",    fragCtx, "vec3(0,0,1)");
        string metallic  = SurfacePassHelpers.EvalMaster((MasterNodeBase)master, "Metallic",  fragCtx, "0.0");
        string roughness = SurfacePassHelpers.EvalMaster((MasterNodeBase)master, "Roughness", fragCtx, "0.8");
        string occlusion = SurfacePassHelpers.EvalMaster((MasterNodeBase)master, "Occlusion", fragCtx, "1.0");
        string emission  = SurfacePassHelpers.EvalMaster((MasterNodeBase)master, "Emission",  fragCtx, "vec3(0.0)");
        shared.Diagnostics.AddRange(fragCtx.Diagnostics);

        var sb = new StringBuilder();
        sb.AppendLine($"Pass \"{Name}\"");
        sb.AppendLine("{");
        ShaderGraphEmit.AppendRenderState(sb, settings, "    ");
        foreach (var d in fragCtx.PassDirectives) sb.AppendLine($"    {d}");
        sb.AppendLine();
        sb.AppendLine("    GLSLPROGRAM");
        sb.AppendLine();

        EmitVertexStage(sb);
        sb.AppendLine();
        EmitFragmentStage(sb, master, fragCtx, albedo, normalTS, metallic, roughness, occlusion, emission, settings);

        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitVertexStage(StringBuilder sb)
    {
        sb.AppendLine("    Vertex");
        sb.AppendLine("    {");
        sb.AppendLine("        #include \"Fragment\"");
        sb.AppendLine("        #include \"VertexAttributes\"");
        sb.AppendLine();
        sb.AppendLine("        out vec2 texCoord0;");
        sb.AppendLine("        out vec3 worldPos;");
        sb.AppendLine("        out vec3 worldNormal;");
        sb.AppendLine();
        sb.AppendLine("        uniform sampler2D _Heightmap;");
        sb.AppendLine("        uniform float _TerrainSize;");
        sb.AppendLine("        uniform float _TerrainHeight;");
        sb.AppendLine("        uniform mat4 _TerrainWorldToLocal;");
        sb.AppendLine("        uniform mat4 _TerrainLocalToWorld;");
        sb.AppendLine();
        // Texel-center remap — matches Default/Terrain.shader so grass/trees align.
        sb.AppendLine("        vec2 hmSampleUV(vec2 uv) {");
        sb.AppendLine("            vec2 s = vec2(textureSize(_Heightmap, 0));");
        sb.AppendLine("            return uv * (s - 1.0) / s + 0.5 / s;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        sb.AppendLine("#ifdef GPU_INSTANCING");
        sb.AppendLine("            mat4 instanceModel = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);");
        sb.AppendLine("            vec4 wp4 = instanceModel * vec4(vertexPosition, 1.0);");
        sb.AppendLine("            vec3 terrainLocal = (_TerrainWorldToLocal * wp4).xyz;");
        sb.AppendLine("            vec2 terrainUV = terrainLocal.xz / _TerrainSize;");
        sb.AppendLine("            texCoord0 = terrainUV;");
        sb.AppendLine();
        sb.AppendLine("            float height = texture(_Heightmap, hmSampleUV(terrainUV)).r * _TerrainHeight;");
        sb.AppendLine("            vec3 displacedLocal = vec3(terrainLocal.x, height, terrainLocal.z);");
        sb.AppendLine("            vec3 wp = (_TerrainLocalToWorld * vec4(displacedLocal, 1.0)).xyz;");
        sb.AppendLine();
        // Central-differences normal — same as the standalone Terrain.shader.
        sb.AppendLine("            float hmSize = float(textureSize(_Heightmap, 0).x);");
        sb.AppendLine("            float vStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;");
        sb.AppendLine("            float hR = texture(_Heightmap, hmSampleUV(terrainUV + vec2(vStep, 0.0))).r * _TerrainHeight;");
        sb.AppendLine("            float hL = texture(_Heightmap, hmSampleUV(terrainUV - vec2(vStep, 0.0))).r * _TerrainHeight;");
        sb.AppendLine("            float hU = texture(_Heightmap, hmSampleUV(terrainUV + vec2(0.0, vStep))).r * _TerrainHeight;");
        sb.AppendLine("            float hD = texture(_Heightmap, hmSampleUV(terrainUV - vec2(0.0, vStep))).r * _TerrainHeight;");
        sb.AppendLine("            float wStep = vStep * _TerrainSize;");
        sb.AppendLine("            vec3 localN = normalize(vec3(-(hR - hL) / (wStep * 2.0), 1.0, -(hU - hD) / (wStep * 2.0)));");
        sb.AppendLine("            worldNormal = normalize((_TerrainLocalToWorld * vec4(localN, 0.0)).xyz);");
        sb.AppendLine("            worldPos = wp;");
        sb.AppendLine("            gl_Position = PROWL_MATRIX_VP * vec4(wp, 1.0);");
        sb.AppendLine("#else");
        sb.AppendLine("            gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);");
        sb.AppendLine("            texCoord0 = vertexTexCoord0;");
        sb.AppendLine("            worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;");
        sb.AppendLine("            worldNormal = normalize((PROWL_MATRIX_M * vec4(0.0, 1.0, 0.0, 0.0)).xyz);");
        sb.AppendLine("#endif");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    private static void EmitFragmentStage(StringBuilder sb, TerrainMasterNode master, ShaderGenContext fragCtx,
        string albedo, string normalTS, string metallic, string roughness, string occlusion, string emission,
        ShaderGraphRenderSettings settings)
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
        sb.AppendLine($"            vec4  _sgAlbedo    = {albedo};");
        sb.AppendLine($"            vec3  _sgNormalTS  = {normalTS};");
        sb.AppendLine($"            float _sgMetallic  = clamp({metallic}, 0.0, 1.0);");
        sb.AppendLine($"            float _sgRoughness = clamp({roughness}, 0.04, 1.0);");
        sb.AppendLine($"            float _sgAO        = {occlusion};");
        sb.AppendLine($"            vec3  _sgEmission  = {emission};");
        sb.AppendLine();
        // Terrain doesn't ship with per-vertex tangents — derive a TBN from the
        // heightmap-computed world normal and an arbitrary perpendicular so
        // tangent-space normal maps still work.
        sb.AppendLine("            vec3 _sgN = normalize(worldNormal);");
        sb.AppendLine("            vec3 _sgT = normalize(cross(_sgN, abs(_sgN.y) < 0.99 ? vec3(0,1,0) : vec3(1,0,0)));");
        sb.AppendLine("            vec3 _sgB = normalize(cross(_sgN, _sgT));");
        sb.AppendLine("            mat3 _sgTBN = mat3(_sgT, _sgB, _sgN);");
        sb.AppendLine("            vec3 _sgWorldN = normalize(_sgTBN * _sgNormalTS);");
        sb.AppendLine();
        sb.AppendLine("            vec3 _sgBaseColor = gammaToLinearSpace(_sgAlbedo.rgb);");

        if (master.Lighting == ShaderLightingMode.Unlit)
        {
            sb.AppendLine("            fragColor = vec4(_sgBaseColor + _sgEmission, 1.0);");
        }
        else
        {
            // PBR / Lambert both use CalculateForwardLighting — Lambert just passes
            // metallic=0 and high roughness, matching SurfacePass behaviour.
            string met = master.Lighting == ShaderLightingMode.Lambert ? "0.0" : "_sgMetallic";
            string rg  = master.Lighting == ShaderLightingMode.Lambert ? "1.0" : "_sgRoughness";
            sb.AppendLine("            vec3 _sgViewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);");
            sb.AppendLine($"            vec3 _sgLighting = CalculateForwardLighting(worldPos, _sgWorldN, _sgViewDir, _sgBaseColor, {met}, {rg}, _sgAO);");
            sb.AppendLine($"            vec3 _sgDiffuse = _sgBaseColor * (1.0 - {met});");
            if (settings.ReceivesAmbient)
                sb.AppendLine("            vec3 _sgAmbient = CalculateAmbient(_sgWorldN) * _sgDiffuse * _sgAO * _AmbientStrength;");
            else
                sb.AppendLine("            vec3 _sgAmbient = vec3(0.0);");
            sb.AppendLine("            vec3 _sgColor = ApplyFog(_sgAmbient + _sgLighting + _sgEmission, worldPos);");
            sb.AppendLine("            fragColor = vec4(_sgColor, 1.0);");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }
}
