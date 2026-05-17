// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Particle shader type GPU-instanced camera-aligned billboards with per-instance
// rotation, scale, UV animation, and lifetime. Single forward pass, no shadows
// (particles don't cast shadows in Prowl they're alpha-blended additively). The
// vertex stage is custom it extracts position/rotation/scale from the instance
// matrix and builds a camera-aligned quad instead of using the standard model
// transform.
//
// Matches the hand-written Default/Particle.shader behaviour so users swapping
// between a graph-authored particle material and the built-in one see the same
// spawn / rotation / UV-animation layout.

using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools.ShaderGraphs.Editors.Types;

public sealed class ParticleShaderType : IShaderType
{
    public const string TypeId = "Particle";

    public string Id => TypeId;
    public string DisplayName => "Particle";
    public Type MasterNodeType => typeof(ParticleMasterNode);

    private static readonly IShaderPass[] s_passes = { new ParticlePass() };
    public IReadOnlyList<IShaderPass> Passes => s_passes;

    public ShaderGraphRenderSettings DefaultRenderSettings => ShaderGraphRenderSettings.AdditiveDefaults();

    public IReadOnlyList<ShaderTypeMenuEntry> MenuEntries { get; } = new[]
    {
        new ShaderTypeMenuEntry("Default", "Shader Graph/Particle", 70),
    };

    public void SeedGraph(ShaderGraph graph, string variantKey)
    {
        graph.ShaderTypeId = TypeId;
        graph.RenderSettings = DefaultRenderSettings;

        // Seed a minimal but visible graph: VertexColor (which the vertex stage
        // initialises to instanceColor × vertexColor) wired straight into Color.
        // Renders particles as solid, instance-coloured quads users add a
        // texture sample between them to get a sprite-based look.
        var master = new ParticleMasterNode { Position = new Float2(520, 120) };
        graph.AddNode(master);

        var vColor = new VertexColorNode { Position = new Float2(180, 120) };
        graph.AddNode(vColor);

        graph.Edges.Add(new Edge
        {
            SourceNodeId   = vColor.Id,
            SourcePortName = vColor.GetOutput("RGBA")?.Name ?? "RGBA",
            TargetNodeId   = master.Id,
            TargetPortName = master.GetInput("Color")!.Name,
        });
    }
}

/// <summary>
/// Single forward pass. Vertex stage extracts position+rotation+scale from the
/// instance matrix and builds a camera-aligned quad; fragment runs the user's
/// Color / Alpha subtrees.
/// </summary>
internal sealed class ParticlePass : IShaderPass
{
    public string Name => "Particle";
    public ShaderPassRole Role => ShaderPassRole.Forward;

    public string EmitPass(MasterNodeBase masterBase, ShaderGraph graph, PassEmitSharedState shared)
    {
        var master = (ParticleMasterNode)masterBase;
        var settings = graph.RenderSettings;

        // Build fragment first so we know which varyings it requested we'll mirror
        // those to the vertex stage's `out` list.
        var fragCtx = new ShaderGenContext(graph, ShaderStage.Fragment);
        foreach (var u in shared.PropertyUniforms) fragCtx.Uniforms.Add(u);
        fragCtx.Includes.Add("ProwlCG");

        // Particle standard varyings everyone gets these, no matter what the graph does.
        fragCtx.Varyings.Add(("texCoord0", "vec2"));
        fragCtx.Varyings.Add(("vColor",    "vec4"));
        fragCtx.Varyings.Add(("worldPos",  "vec3"));

        var colorPort = master.GetInput("Color");
        var alphaPort = master.GetInput("Alpha");
        string colorExpr = colorPort != null ? fragCtx.EvaluateInput(colorPort) : "vec4(1.0)";
        // When Alpha isn't wired, fall back to Color.a. Writing "_sgAlpha = _sgColor.a"
        // after both are evaluated keeps the semantics explicit.
        bool alphaWired = alphaPort != null && fragCtx.IsConnected(alphaPort);
        string alphaExpr = alphaWired ? fragCtx.EvaluateInputAs(alphaPort!, ShaderType.Float) : "1.0";

        // Soft-particles depth fade. When enabled, declare the depth sampler.
        if (master.SoftParticles)
        {
            fragCtx.Uniforms.Add("uniform sampler2D _CameraDepthTexture;");
            fragCtx.Includes.Add("ProwlCG");  // linearizeDepthFromProjection lives here
        }

        shared.Diagnostics.AddRange(fragCtx.Diagnostics);

        var sb = new StringBuilder();
        sb.AppendLine($"Pass \"{Name}\"");
        sb.AppendLine("{");
        ShaderGraphEmit.AppendRenderState(sb, settings, "    ");
        foreach (var d in fragCtx.PassDirectives) sb.AppendLine($"    {d}");
        sb.AppendLine();
        sb.AppendLine("    GLSLPROGRAM");
        sb.AppendLine();

        // ─ Vertex (hardcoded billboard) ─
        EmitVertexStage(sb, fragCtx.Varyings);

        sb.AppendLine();

        // ─ Fragment ─
        EmitFragmentStage(sb, master, fragCtx, colorExpr, alphaExpr, alphaWired);

        sb.AppendLine("    ENDGLSL");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitVertexStage(StringBuilder sb, IEnumerable<(string name, string type)> fragmentVaryings)
    {
        sb.AppendLine("    Vertex");
        sb.AppendLine("    {");
        sb.AppendLine("        #include \"ProwlCG\"");
        sb.AppendLine("        #include \"VertexAttributes\"");
        sb.AppendLine();
        // Forward exactly the varyings the fragment wants vLifetime only appears
        // if ParticleLifetimeNode was used, so we write it conditionally below.
        foreach (var (n, t) in fragmentVaryings)
            sb.AppendLine($"        out {t} {n};");
        sb.AppendLine();
        sb.AppendLine("        void main()");
        sb.AppendLine("        {");
        sb.AppendLine("#ifdef GPU_INSTANCING");
        // Mirror the default Particle.shader exactly position, scale, rotation
        // extracted from the instance matrix; billboard built around camera axes.
        sb.AppendLine("            vec3 particlePosition = instanceModelRow3.xyz;");
        sb.AppendLine("            float scaleX = length(instanceModelRow0.xyz);");
        sb.AppendLine("            float scaleY = length(instanceModelRow1.xyz);");
        sb.AppendLine();
        sb.AppendLine("            vec3 matrixRight = instanceModelRow0.xyz / scaleX;");
        sb.AppendLine("            float rotationAngle = atan(matrixRight.y, matrixRight.x);");
        sb.AppendLine("            float cosRot = cos(rotationAngle);");
        sb.AppendLine("            float sinRot = sin(rotationAngle);");
        sb.AppendLine();
        sb.AppendLine("            vec3 cameraRight = vec3(PROWL_MATRIX_V[0][0], PROWL_MATRIX_V[1][0], PROWL_MATRIX_V[2][0]);");
        sb.AppendLine("            vec3 cameraUp    = vec3(PROWL_MATRIX_V[0][1], PROWL_MATRIX_V[1][1], PROWL_MATRIX_V[2][1]);");
        sb.AppendLine();
        sb.AppendLine("            vec2 rotated = vec2(");
        sb.AppendLine("                vertexPosition.x * cosRot - vertexPosition.y * sinRot,");
        sb.AppendLine("                vertexPosition.x * sinRot + vertexPosition.y * cosRot);");
        sb.AppendLine();
        sb.AppendLine("            vec3 wp = particlePosition");
        sb.AppendLine("                    + cameraRight * rotated.x * scaleX");
        sb.AppendLine("                    + cameraUp    * rotated.y * scaleY;");
        sb.AppendLine("            gl_Position = PROWL_MATRIX_VP * vec4(wp, 1.0);");
        sb.AppendLine();
        // UV animation custom-data packs (lifetime.x, uvOffset.yz, uvScale.w).
        sb.AppendLine("            texCoord0 = vertexTexCoord0 * instanceCustomData.w + instanceCustomData.yz;");
        sb.AppendLine("            vColor = vertexColor * instanceColor;");
        sb.AppendLine("            worldPos = wp;");
        // Only write vLifetime when the fragment requested it cheap optimisation
        // and keeps unused `out` declarations out of the generated shader.
        sb.AppendLine("#ifdef PARTICLE_LIFETIME");
        sb.AppendLine("            vLifetime = instanceCustomData.x;");
        sb.AppendLine("#endif");
        sb.AppendLine("#else");
        // Non-instanced fallback mostly useful for previews and tests.
        sb.AppendLine("            gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);");
        sb.AppendLine("            texCoord0 = vertexTexCoord0;");
        sb.AppendLine("            vColor = vertexColor;");
        sb.AppendLine("            worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;");
        sb.AppendLine("#ifdef PARTICLE_LIFETIME");
        sb.AppendLine("            vLifetime = 0.0;");
        sb.AppendLine("#endif");
        sb.AppendLine("#endif");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    private static void EmitFragmentStage(StringBuilder sb, ParticleMasterNode master, ShaderGenContext fragCtx,
        string colorExpr, string alphaExpr, bool alphaWired)
    {
        // If ParticleLifetimeNode was used it added vLifetime to fragCtx.Varyings —
        // propagate that via #define so the vertex stage knows to write it.
        bool wantsLifetime = false;
        foreach (var (n, _) in fragCtx.Varyings) if (n == "vLifetime") { wantsLifetime = true; break; }

        sb.AppendLine("    Fragment");
        sb.AppendLine("    {");
        foreach (var d in fragCtx.Defines)    sb.AppendLine($"        #define {d}");
        if (wantsLifetime) sb.AppendLine("        #define PARTICLE_LIFETIME");
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
        sb.AppendLine($"            vec4  _sgColor = {colorExpr};");
        sb.AppendLine($"            float _sgAlpha = {(alphaWired ? alphaExpr : "_sgColor.a")};");

        // Soft particles fade where the particle is within SoftParticleDistance
        // of the scene geometry behind it. Zero when the depth texture isn't bound.
        if (master.SoftParticles)
        {
            sb.AppendLine("            {");
            sb.AppendLine("                vec2 _sfUV = gl_FragCoord.xy / _ScreenParams.xy;");
            sb.AppendLine("                float _sceneDepth = texture(_CameraDepthTexture, _sfUV).r;");
            sb.AppendLine("                float _sceneLin   = linearizeDepthFromProjection(_sceneDepth);");
            sb.AppendLine("                float _fragLin    = linearizeDepthFromProjection(gl_FragCoord.z);");
            sb.AppendLine($"                float _fade = clamp((_sceneLin - _fragLin) / {ShaderGenContext.Fmt(master.SoftParticleDistance)}, 0.0, 1.0);");
            sb.AppendLine("                _sgAlpha *= _fade;");
            sb.AppendLine("            }");
        }

        // Same discard threshold the hand-written Particle.shader uses avoids
        // writing fully-transparent fragments through the blend.
        sb.AppendLine("            if (_sgAlpha < 0.01) discard;");
        sb.AppendLine("            fragColor = vec4(gammaToLinearSpace(_sgColor.rgb), _sgAlpha);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }
}
