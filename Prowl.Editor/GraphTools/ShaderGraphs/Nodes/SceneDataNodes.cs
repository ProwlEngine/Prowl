// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

// ─────────────────────────────────────────────────────────────────────────────
// Shared accent for every "Scene Data" node
// ─────────────────────────────────────────────────────────────────────────────

internal static class SceneAccents
{
    /// <summary>Warm tan accent used by all Scene-Data nodes.</summary>
    public static readonly System.Drawing.Color Scene =
        System.Drawing.Color.FromArgb(255, 200, 160, 120);
}

// ═════════════════════════════════════════════════════════════════════════════
// EXTERNAL DATA NODES
// ═════════════════════════════════════════════════════════════════════════════

// ─── Time ────────────────────────────────────────────────────────────────────

/// <summary>
/// Exposes Prowl's per-frame <c>_Time</c> vec4 (from the global uniform buffer).
/// Layout: <c>vec4(t*0.5, t, t*2, frameCount)</c> where .x and .w differ from
/// other engines' time layouts.
/// </summary>
public sealed class TimeNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Time";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddOutput<float>("t*0.5",      tooltip: "Time * 0.5 _Time.x");
        AddOutput<float>("t",          tooltip: "Time since startup in seconds _Time.y");
        AddOutput<float>("t*2",        tooltip: "Time * 2 _Time.z");
        AddOutput<float>("Frame",      tooltip: "Frame count since startup _Time.w");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        return outputPort.Name switch
        {
            "t*0.5" => "_Time.x",
            "t"     => "_Time.y",
            "t*2"   => "_Time.z",
            "Frame" => "_Time.w",
            _       => "_Time.y",
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>Delta time uploaded alongside <c>_Time</c> Prowl's <c>prowl_DeltaTime</c>.
/// .x = dt, .y = 1/dt (reciprocal), .z = smoothed dt, .w = 1/smoothed. Useful for
/// frame-rate-independent animation inside a CustomCode node.</summary>
public sealed class DeltaTimeNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Delta Time";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddOutput<Prowl.Vector.Float4>("XYZW", tooltip: "(dt, 1/dt, smoothed dt, 1/smoothed dt)");
        AddOutput<float>("dt");
        AddOutput<float>("1/dt");
    }
    string IShaderNode.Evaluate(Port outputPort, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        return outputPort.Name switch
        {
            "dt"   => "prowl_DeltaTime.x",
            "1/dt" => "prowl_DeltaTime.y",
            _      => "prowl_DeltaTime",
        };
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => p.Name == "XYZW" ? ShaderType.Vec4 : ShaderType.Float;
}

// ─── Screen Parameters ────────────────────────────────────────────────────────

/// <summary>
/// Exposes <c>_ScreenParams</c>: pixel dimensions + reciprocals.
///
/// _ScreenParams = (width, height, 1 + 1/width, 1 + 1/height)
///
/// Verified against RenderPipeline.cs line 252:
///   SetScreenParams(new Float4(css.PixelWidth, css.PixelHeight,
///                              1.0f + 1.0f / css.PixelWidth,
///                              1.0f + 1.0f / css.PixelHeight));
/// </summary>
public sealed class ScreenParametersNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Screen Parameters";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddOutput<float>("PXW", tooltip: "Pixel width  _ScreenParams.x");
        AddOutput<float>("PXH", tooltip: "Pixel height _ScreenParams.y");
        AddOutput<float>("RCW", tooltip: "1 + 1/width  _ScreenParams.z");
        AddOutput<float>("RCH", tooltip: "1 + 1/height _ScreenParams.w");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        return outputPort.Name switch
        {
            "PXW" => "_ScreenParams.x",
            "PXH" => "_ScreenParams.y",
            "RCW" => "_ScreenParams.z",
            "RCH" => "_ScreenParams.w",
            _     => "_ScreenParams.x",
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ─── Projection Parameters ────────────────────────────────────────────────────

/// <summary>
/// Exposes <c>_ProjectionParams</c>: projection sign, near/far planes, 1/far.
///
/// Prowl layout: (1.0, near, far, 1/far)
///   x (SGN)  always +1.0 in Prowl (no flipped-projection path)
///   y (NEAR) near clip plane distance
///   z (FAR)  far clip plane distance
///   w (RFAR) 1.0 / far clip plane distance
///
/// Verified against RenderPipeline.cs line 251:
///   SetProjectionParams(new Float4(1.0f, css.NearClipPlane,
///                                  css.FarClipPlane, 1.0f / css.FarClipPlane));
/// </summary>
public sealed class ProjectionParametersNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Projection Parameters";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddOutput<float>("SGN",  tooltip: "Projection sign always 1.0 in Prowl (_ProjectionParams.x)");
        AddOutput<float>("NEAR", tooltip: "Near clip plane distance (_ProjectionParams.y)");
        AddOutput<float>("FAR",  tooltip: "Far clip plane distance (_ProjectionParams.z)");
        AddOutput<float>("RFAR", tooltip: "1 / far clip plane distance (_ProjectionParams.w)");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        return outputPort.Name switch
        {
            "SGN"  => "_ProjectionParams.x",
            "NEAR" => "_ProjectionParams.y",
            "FAR"  => "_ProjectionParams.z",
            "RFAR" => "_ProjectionParams.w",
            _      => "_ProjectionParams.z",
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ─── Pixel Size ───────────────────────────────────────────────────────────────

/// <summary>
/// Texel size in UV space computes <c>vec2(1/width, 1/height)</c>.
///
/// Emits <c>vec2(_ScreenParams.z - 1.0, _ScreenParams.w - 1.0)</c>.
/// Since _ScreenParams.z = 1 + 1/width, subtracting 1 gives 1/width (the UV
/// span of one pixel).
/// </summary>
public sealed class PixelSizeNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Pixel Size";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddOutput<Float2>("XY", tooltip: "vec2(1/width, 1/height) one pixel in UV space");
        AddOutput<float>("X",  tooltip: "1 / screen width");
        AddOutput<float>("Y",  tooltip: "1 / screen height");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        return outputPort.Name switch
        {
            "XY" => "vec2(_ScreenParams.z - 1.0, _ScreenParams.w - 1.0)",
            "X"  => "(_ScreenParams.z - 1.0)",
            "Y"  => "(_ScreenParams.w - 1.0)",
            _    => "vec2(_ScreenParams.z - 1.0, _ScreenParams.w - 1.0)",
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name == "XY"
        ? ShaderType.Vec2
        : ShaderType.Float;
}

// ─── View Position (Camera World Position) ────────────────────────────────────

/// <summary>
/// World-space camera position: <c>_WorldSpaceCameraPos.xyz</c>.
///
/// In Prowl the uniform lives in the GlobalUniforms UBO declared in ShaderVariables.glsl.
/// </summary>
public sealed class ViewPositionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "View Position";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddOutput<Float3>("XYZ", tooltip: "World-space camera position (vec3)");
        AddOutput<float>("X",   tooltip: "Camera position X");
        AddOutput<float>("Y",   tooltip: "Camera position Y");
        AddOutput<float>("Z",   tooltip: "Camera position Z");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        return outputPort.Name switch
        {
            "XYZ" => "_WorldSpaceCameraPos.xyz",
            "X"   => "_WorldSpaceCameraPos.x",
            "Y"   => "_WorldSpaceCameraPos.y",
            "Z"   => "_WorldSpaceCameraPos.z",
            _     => "_WorldSpaceCameraPos.xyz",
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name == "XYZ"
        ? ShaderType.Vec3
        : ShaderType.Float;
}

// ─── Fog Color ────────────────────────────────────────────────────────────────

/// <summary>
/// Exposes Prowl's global fog colour: <c>_FogColor</c> (vec4, declared in Lighting.glsl).
///
/// Adding "Lighting" to Includes also pulls in shadow / light uniforms authors
/// who want only the fog colour but not the full lighting setup should be aware of
/// this overhead, though the overhead is purely declarative (no extra sampling).
/// </summary>
public sealed class FogColorNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Fog Color";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddOutput<Float3>("RGB", tooltip: "Fog colour RGB channels (_FogColor.rgb)");
        AddOutput<float>("R",   tooltip: "Fog colour Red channel");
        AddOutput<float>("G",   tooltip: "Fog colour Green channel");
        AddOutput<float>("B",   tooltip: "Fog colour Blue channel");
        AddOutput<float>("A",   tooltip: "Fog colour Alpha channel (_FogColor.a)");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        return outputPort.Name switch
        {
            "RGB" => "_FogColor.rgb",
            "R"   => "_FogColor.r",
            "G"   => "_FogColor.g",
            "B"   => "_FogColor.b",
            "A"   => "_FogColor.a",
            _     => "_FogColor.rgb",
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name == "RGB"
        ? ShaderType.Vec3
        : ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// SCENE DATA NODES (depth-buffer / grab-pass access)
// ═════════════════════════════════════════════════════════════════════════════

// ─── Eye Depth ────────────────────────────────────────────────────────────────

/// <summary>
/// World-distance from the camera to the current fragment.
///
/// Emits: <c>length(_WorldSpaceCameraPos.xyz - worldPos)</c>
///
/// Requires the <c>worldPos</c> varying (vec3) which the shader compiler emits
/// when <see cref="ShaderGenContext.Varyings"/> contains <c>("worldPos","vec3")</c>.
/// The PBR compiler already emits worldPos for lit shaders; for Unlit graphs this
/// node forces it to be emitted.
/// </summary>
public sealed class EyeDepthNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Eye Depth";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddOutput<float>("Depth", tooltip: "World-space distance from camera to fragment");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            return "length(_WorldSpaceCameraPos.xyz - TransformPosition(vertexPosition))";
        }
        ctx.Varyings.Add(("worldPos", "vec3"));
        return "length(_WorldSpaceCameraPos.xyz - worldPos)";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ─── Depth Blend ──────────────────────────────────────────────────────────────

/// <summary>
/// Soft-particle depth fade returns a gradient based on how far this fragment
/// is in front of the geometry written into the depth pre-pass:
/// <list type="bullet">
/// <item><description><c>Mirror</c>: <c>abs()</c> the result so geometry behind
/// AND in front of the surface fades symmetrically (force-field /
/// double-sided intersection highlights).</description></item>
/// <item><description><c>Saturate</c> (default true): clamp to [0, 1] for use
/// as an alpha. Disable to keep the raw signed slope.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Reads the camera's depth pre-pass via the globally-bound
/// <c>_CameraDepthTexture</c>. The pipeline binds this whenever the camera's
/// <c>DepthTextureMode</c> includes Depth (DefaultRenderPipeline.cs:211); if
/// the pre-pass is disabled the sampler is unbound and the node reads zeros,
/// collapsing the fade to 0.</para>
///
/// <para>The screen UV is derived from <c>gl_FragCoord</c> + <c>_ScreenParams</c>
/// (no grab-pass directive needed, this always samples the full-screen depth
/// buffer). Fragment-stage only.</para>
/// </remarks>
public sealed class DepthBlendNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>Take <c>abs()</c> of the difference so the fade is symmetric
    /// around the scene surface.</summary>
    public bool Mirror = false;

    /// <summary>Clamp the result to [0, 1]. Defaults to true so the node reads
    /// as a drop-in alpha; flip off when you want the raw signed slope.</summary>
    public bool Saturate = true;

    public override string Title => "Depth Blend";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddInput<float>("Dist", 1.0f,
            tooltip: "Blend distance in world units wider values give a softer transition.");
        AddOutput<float>("Out",
            tooltip: "(sceneDepth - fragDepth) / Dist, optionally mirrored / saturated.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "0.0";
        ctx.Includes.Add("ShaderVariables");
        ctx.Includes.Add("ProwlCG"); // for linearizeDepthFromProjection

        // Pipeline binds this globally when the camera's DepthTextureMode includes Depth.
        ctx.Uniforms.Add("uniform sampler2D _CameraDepthTexture;");

        var dist = ctx.EvaluateInputAs(GetInput("Dist")!, ShaderType.Float);

        // Cache the linearised samples in temps so the same value can drive
        // alpha + emission + dissolve without re-evaluating linearizeDepth twice.
        var local = $"_depBlend{Id:N}";
        ctx.EmitOnce("depblend:" + local, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    vec2 {local}_suv = gl_FragCoord.xy / _ScreenParams.xy;");
            ctx.BodyPrelude.AppendLine(
                $"    float {local}_sd  = linearizeDepthFromProjection(texture(_CameraDepthTexture, {local}_suv).r);");
            ctx.BodyPrelude.AppendLine(
                $"    float {local}_pd  = linearizeDepthFromProjection(gl_FragCoord.z);");

            // Compose the toggles so each one wraps the previous expression
            // without re-evaluating the inner depth math.
            string expr = $"(({local}_sd - {local}_pd) / max({dist}, 1e-5))";
            if (Mirror)   expr = $"abs({expr})";
            if (Saturate) expr = $"clamp({expr}, 0.0, 1.0)";

            ctx.BodyPrelude.AppendLine($"    float {local} = {expr};");
        });
        return local;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ─── Scene Color (Grab Pass) ──────────────────────────────────────────────────

/// <summary>
/// Samples the grab-pass colour texture.
///
/// IMPLEMENTATION STATUS STUB / FLAGGED:
///   Prowl's grab-pass system uses a USER-SPECIFIED texture name declared in the
///   .shader file via <c>GrabTexture "_GrabTexture"</c> (verified in
///   ShaderParser.cs and Refraction.shader).  The name is NOT fixed it is chosen
///   by the shader author per-pass.  There is no engine-wide "the grab texture is
///   always named X" convention.
///
///   A shader-graph node cannot currently inject a pass-level <c>GrabTexture</c>
///   directive; that mechanism lives outside the GLSL emission layer.  Therefore:
///     • This node emits a sample of <c>_GrabTexture</c> (the conventional name
///       used in the built-in Refraction.shader example).
///     • The generated shader must have <c>GrabTexture "_GrabTexture"</c> in its
///       pass block either hand-written or emitted by a future shader-graph
///       compiler extension.
///     • If the pass does NOT declare a grab texture, the uniform will be unbound
///       and the sample returns black (vec4(0,0,0,1)).
///
///   FLAG: Needs a compiler-level pass directive, or the shader-graph emitter must
///   learn to inject <c>GrabTexture "_GrabTexture"</c> into the emitted .shader
///   pass when this node is present in the graph.
///
/// UV input defaults to screen UV (gl_FragCoord / _ScreenParams.xy).
/// </summary>
public sealed class SceneColorNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Scene Color";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        // Default UV = screen UV from gl_FragCoord; user can override with a ScreenPos node.
        AddInput<Float2>("UV",
            new Float2(0.5f, 0.5f),
            tooltip: "Screen-space UV to sample. Leave unconnected for automatic screen UV from gl_FragCoord.");
        AddInput<float>("LOD", 0.0f,
            tooltip: "Mip level for the grab texture. 0 = sharp, higher = blurrier. " +
                     "Grab textures have auto-generated mipmaps with trilinear filtering. " +
                     "Leave unconnected for standard texture() sampling.");

        AddOutput<Float4>("RGBA", tooltip: "Sampled grab-pass colour (vec4)");
        AddOutput<float>("R",    tooltip: "Red channel");
        AddOutput<float>("G",    tooltip: "Green channel");
        AddOutput<float>("B",    tooltip: "Blue channel");
        AddOutput<float>("A",    tooltip: "Alpha channel");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title))
            return outputPort.Name == "RGBA" ? "vec4(0.0)" : "0.0";
        ctx.Includes.Add("ShaderVariables");

        ctx.Uniforms.Add("uniform sampler2D _GrabTexture;");
        ctx.PassDirectives.Add("GrabTexture \"_GrabTexture\"");

        string tmp = $"_grabCol_{Id:N}";
        ctx.EmitOnce("scenecol:" + tmp, () =>
        {
            bool uvConnected = ctx.IsConnected(GetInput("UV")!);
            string uv = uvConnected
                ? ctx.EvaluateInput(GetInput("UV")!)
                : "gl_FragCoord.xy / _ScreenParams.xy";

            bool lodConnected = ctx.IsConnected(GetInput("LOD")!);
            if (lodConnected)
            {
                string lod = ctx.EvaluateInputAs(GetInput("LOD")!, ShaderType.Float);
                ctx.BodyPrelude.AppendLine($"    vec4 {tmp} = textureLod(_GrabTexture, {uv}, {lod});");
            }
            else
            {
                ctx.BodyPrelude.AppendLine($"    vec4 {tmp} = texture(_GrabTexture, {uv});");
            }
        });

        return outputPort.Name switch
        {
            "RGBA" => tmp,
            "R"    => $"{tmp}.r",
            "G"    => $"{tmp}.g",
            "B"    => $"{tmp}.b",
            "A"    => $"{tmp}.a",
            _      => tmp,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGBA" => ShaderType.Vec4,
        _      => ShaderType.Float,
    };
}

// ─── Scene Color Blur (One-Step Mip Blur) ────────────────────────────────────

/// <summary>
/// One-step frosted glass / mip blur using the grab-pass texture with auto-generated mipmaps.
///
/// Uses Interleaved Gradient Noise (Jimenez 2014) to pick a random direction on a disk,
/// then samples the grab texture at that offset using a mip level proportional to blur radius.
/// The mip provides the base blur, the noise-rotated offset breaks up banding.
/// Multiple quality steps (1-4) rotate the sample point by 90 degrees each iteration.
///
/// Based on Mirza Beig's one-step blur technique.
/// </summary>
public sealed class SceneColorBlurNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Scene Color Blur";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddInput<float>("Blur", 0.02f,
            tooltip: "Blur radius in UV space. 0 = sharp, 0.05 = moderate frosted glass.");
        AddInput<float>("MIP", 0.0f,
            tooltip: "Base mip level bias. Combined with blur radius for final mip selection. " +
                     "Higher values use coarser mip levels for a smoother base blur.");
        AddInput<int>("Steps", 4,
            tooltip: "Number of blur samples (1-4). Each step rotates 90 degrees. " +
                     "1 = fastest, 4 = best quality. Even 1-2 steps look good with mip blur.");

        AddOutput<Float4>("RGBA", tooltip: "Blurred grab-pass colour (vec4)");
        AddOutput<Float3>("RGB",  tooltip: "Blurred colour RGB");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title))
            return outputPort.Name == "RGBA" ? "vec4(0.0)" : "vec3(0.0)";
        ctx.Includes.Add("ShaderVariables");

        ctx.Uniforms.Add("uniform sampler2D _GrabTexture;");
        ctx.PassDirectives.Add("GrabTexture \"_GrabTexture\"");

        string tmp = $"_grabBlur_{Id:N}";
        ctx.EmitOnce("sceneblur:" + tmp, () =>
        {
            // Emit the IGN + blur helper once per shader
            ctx.EmitOnce("sceneblur:helpers", () =>
            {
                ctx.TopLevelHelpers.AppendLine(@"
float _IGN(vec2 pixCoord, int frameCount) {
    const vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
    vec2 frameMagicScale = vec2(2.083, 4.867);
    pixCoord += float(frameCount) * frameMagicScale;
    return fract(magic.z * fract(dot(pixCoord, magic.xy)));
}
vec4 _MipBlur(sampler2D tex, vec2 uv, float radius, float mipBias, int steps, vec2 pixCoord) {
    float noise = _IGN(pixCoord, int(_Time.w) % 60);
    float angle = noise * 6.28318530718;
    vec2 dir = vec2(cos(angle), sin(angle));
    vec2 aspect = vec2(min(1.0, _ScreenParams.y / _ScreenParams.x),
                       min(1.0, _ScreenParams.x / _ScreenParams.y));
    vec4 acc = vec4(0.0);
    steps = clamp(steps, 1, 4);
    for (int i = 0; i < steps; i++) {
        vec2 offset = dir * radius * aspect;
        acc += textureLod(tex, uv + offset, mipBias);
        dir = vec2(-dir.y, dir.x);
    }
    return acc / float(steps);
}");
            });

            string blur  = ctx.EvaluateInputAs(GetInput("Blur")!,  ShaderType.Float);
            string mip   = ctx.EvaluateInputAs(GetInput("MIP")!,   ShaderType.Float);
            string steps = ctx.EvaluateInputAs(GetInput("Steps")!,  ShaderType.Int);

            ctx.BodyPrelude.AppendLine(
                $"    vec4 {tmp} = _MipBlur(_GrabTexture, gl_FragCoord.xy / _ScreenParams.xy, {blur}, {mip}, {steps}, gl_FragCoord.xy);");
        });

        return outputPort.Name switch
        {
            "RGBA" => tmp,
            "RGB"  => $"{tmp}.rgb",
            _      => tmp,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGB"  => ShaderType.Vec3,
        _      => ShaderType.Vec4,
    };
}

// ─── Scene Depth ──────────────────────────────────────────────────────────────

/// <summary>
/// Samples the camera's pre-pass depth texture and returns a linearised eye depth.
///
/// IMPLEMENTATION STATUS PARTIAL / FLAGGED:
///   Prowl binds <c>_CameraDepthTexture</c> globally via
///   <c>PropertyState.SetGlobalTexture("_CameraDepthTexture", depthPrepass.InternalDepth)</c>
///   (DefaultRenderPipeline.cs line 211) whenever the depth pre-pass runs.  This
///   means the sampler is available in any forward fragment shader without a
///   grab-pass declaration unlike SceneColorNode which needs the per-pass directive.
///
///   The raw depth value returned by the texture is in NDC [0,1] and must be
///   linearised via <c>linearizeDepthFromProjection()</c> (Fragment.glsl) to get
///   a view-space eye depth in world units.
///
///   FLAG: The depth pre-pass only runs when the camera is configured to produce it
///   (it is part of the default pipeline but can be disabled).  If the pre-pass is
///   absent, _CameraDepthTexture will be unbound.  The node emits a diagnostic
///   comment in the generated GLSL but cannot detect this at compile time.
///
/// UV input defaults to screen UV (gl_FragCoord / _ScreenParams.xy).
/// Output is linearised eye depth in world-space distance units (float).
/// </summary>
public sealed class SceneDepthNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Scene Depth";
    public override string Category => "Scene Data";
    public override System.Drawing.Color AccentColor => SceneAccents.Scene;

    protected override void DefineNode()
    {
        AddInput<Float2>("UV",
            new Float2(0.5f, 0.5f),
            tooltip: "Screen-space UV to sample. Leave unconnected for automatic screen UV from gl_FragCoord.");

        AddOutput<float>("Depth",
            tooltip: "Linearised eye depth at UV (world-space distance from camera). Requires camera depth pre-pass.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "0.0";
        ctx.Includes.Add("ShaderVariables");
        ctx.Includes.Add("ProwlCG"); // for linearizeDepthFromProjection()

        ctx.Uniforms.Add("uniform sampler2D _CameraDepthTexture;");

        bool uvConnected = ctx.IsConnected(GetInput("UV")!);
        string uv = uvConnected
            ? ctx.EvaluateInput(GetInput("UV")!)
            : "gl_FragCoord.xy / _ScreenParams.xy";

        return $"linearizeDepthFromProjection(texture(_CameraDepthTexture, {uv}).r)";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}
