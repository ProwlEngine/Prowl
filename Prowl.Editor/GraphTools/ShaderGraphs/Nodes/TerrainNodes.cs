// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Terrain-domain shader graph nodes, shared between Terrain and Grass shader types.
// Wrap the uniforms the TerrainComponent binds to every terrain / grass material:
// _Heightmap, _Splatmap, _TerrainSize, _TerrainHeight, _TerrainLocalToWorld,
// _TerrainWorldToLocal, _TerrainUp.
//
// These nodes hide the details of sampling + transform math so users can mix
// heightmap data into their shaders without writing GLSL.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

internal static class TerrainAccents
{
    /// <summary>Earthy green-brown groups terrain / grass nodes in the browser.</summary>
    public static readonly System.Drawing.Color Terrain =
        System.Drawing.Color.FromArgb(255, 130, 170, 110);
}

/// <summary>
/// Declares the standard terrain uniform bundle once per shader heightmap,
/// splatmap, size, height, transform matrices. Called automatically by any
/// node in this file that touches them via <see cref="EmitOnce"/> so we never
/// emit duplicate uniform declarations.
/// </summary>
internal static class TerrainUniforms
{
    public static void EmitAll(ShaderGenContext ctx)
    {
        ctx.Uniforms.Add("uniform sampler2D _Heightmap;");
        ctx.Uniforms.Add("uniform sampler2D _Splatmap;");
        ctx.Uniforms.Add("uniform float _TerrainSize;");
        ctx.Uniforms.Add("uniform float _TerrainHeight;");
        ctx.Uniforms.Add("uniform mat4  _TerrainLocalToWorld;");
        ctx.Uniforms.Add("uniform mat4  _TerrainWorldToLocal;");
        ctx.Uniforms.Add("uniform vec3  _TerrainUp;");
    }

    /// <summary>Heightmap sampling helper remaps vertex-space UV (<c>i/(N-1)</c>)
    /// to texel-center UV (<c>(i+0.5)/N</c>) so GPU sampling matches the CPU-side
    /// <c>TerrainData.GetInterpolatedHeight</c> that positioned grass/trees.</summary>
    public const string HmSampleUVFn = @"vec2 _sgTerrainHmUV(vec2 uv) {
        vec2 s = vec2(textureSize(_Heightmap, 0));
        return uv * (s - 1.0) / s + 0.5 / s;
    }
";
}

/// <summary>Samples the terrain heightmap at a given UV, returning the world-space
/// height in metres.</summary>
[ShaderType("Terrain"), ShaderType("Grass")]
public sealed class HeightmapSampleNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Heightmap Sample";
    public override string Category => "Terrain";
    public override System.Drawing.Color AccentColor => TerrainAccents.Terrain;

    protected override void DefineNode()
    {
        AddInput<Float2>("UV", Float2.Zero,
            tooltip: "Terrain-local UV in [0,1]² usually from Terrain UV node.");
        AddOutput<float>("Height", tooltip: "Height in world units.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        TerrainUniforms.EmitAll(ctx);
        ctx.EmitOnce("terrain.hmSampleUV", () => ctx.TopLevelHelpers.Append(TerrainUniforms.HmSampleUVFn));
        var uv = ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2);
        return $"(texture(_Heightmap, _sgTerrainHmUV({uv})).r * _TerrainHeight)";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>
/// Computes the world-space terrain normal at a UV via central-differences sampling
/// of the heightmap. Matches the formula the built-in terrain + grass shaders use.
/// </summary>
[ShaderType("Terrain"), ShaderType("Grass")]
public sealed class TerrainNormalNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Terrain Normal";
    public override string Category => "Terrain";
    public override System.Drawing.Color AccentColor => TerrainAccents.Terrain;

    protected override void DefineNode()
    {
        AddInput<Float2>("UV", Float2.Zero,
            tooltip: "Terrain-local UV. Defaults to the fragment's terrain UV when unconnected.");
        AddOutput<Float3>("Normal", tooltip: "World-space normal of the terrain surface at UV.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        TerrainUniforms.EmitAll(ctx);
        ctx.EmitOnce("terrain.hmSampleUV", () => ctx.TopLevelHelpers.Append(TerrainUniforms.HmSampleUVFn));
        // Helper func emitted once and reused by every TerrainNormalNode in the graph.
        ctx.EmitOnce("terrain.normalFn", () => ctx.TopLevelHelpers.Append(@"vec3 _sgTerrainNormal(vec2 uv) {
        float hmSize = float(textureSize(_Heightmap, 0).x);
        float vStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;
        float hR = texture(_Heightmap, _sgTerrainHmUV(uv + vec2(vStep, 0.0))).r * _TerrainHeight;
        float hL = texture(_Heightmap, _sgTerrainHmUV(uv - vec2(vStep, 0.0))).r * _TerrainHeight;
        float hU = texture(_Heightmap, _sgTerrainHmUV(uv + vec2(0.0, vStep))).r * _TerrainHeight;
        float hD = texture(_Heightmap, _sgTerrainHmUV(uv - vec2(0.0, vStep))).r * _TerrainHeight;
        float wStep = vStep * _TerrainSize;
        vec3 local = normalize(vec3(-(hR - hL) / (wStep * 2.0), 1.0, -(hU - hD) / (wStep * 2.0)));
        return normalize((_TerrainLocalToWorld * vec4(local, 0.0)).xyz);
    }
"));

        var uv = ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2);
        return $"_sgTerrainNormal({uv})";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Vec3;
}

/// <summary>Returns the terrain-local UV at the current fragment simply
/// <c>texCoord0</c>, which the Terrain and Grass vertex stages pre-populate with
/// <c>terrainLocal.xz / _TerrainSize</c>. Provided as a named node so users don't
/// have to know the convention.</summary>
[ShaderType("Terrain"), ShaderType("Grass")]
public sealed class TerrainUVNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Terrain UV";
    public override string Category => "Terrain";
    public override System.Drawing.Color AccentColor => TerrainAccents.Terrain;

    protected override void DefineNode() { AddOutput<Float2>("UV"); }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "vec2(0.0)";
        ctx.Varyings.Add(("texCoord0", "vec2"));
        return "texCoord0";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Vec2;
}

/// <summary>Returns the <c>_Splatmap</c> RGBA weights at a UV. Typical use:
/// multiply each channel by the corresponding layer texture and sum.</summary>
[ShaderType("Terrain")]
public sealed class SplatmapWeightsNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Splatmap Weights";
    public override string Category => "Terrain";
    public override System.Drawing.Color AccentColor => TerrainAccents.Terrain;

    protected override void DefineNode()
    {
        AddInput<Float2>("UV", Float2.Zero,
            tooltip: "Terrain-local UV. Defaults to the fragment's terrain UV when unconnected.");
        AddOutput<Float4>("Weights",
            tooltip: "RGBA weights for layers 0..3. Not normalised sums to splatmap's painted intensity.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        TerrainUniforms.EmitAll(ctx);
        var uv = ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2);
        return $"texture(_Splatmap, {uv})";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Vec4;
}

/// <summary>Scalar uniforms for terrain size and height. Useful for authored
/// effects (e.g. tiling world-space UV by <c>_TerrainSize</c>, colouring by
/// altitude via <c>worldPos.y / _TerrainHeight</c>).</summary>
[ShaderType("Terrain"), ShaderType("Grass")]
public sealed class TerrainSizeNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Terrain Size";
    public override string Category => "Terrain";
    public override System.Drawing.Color AccentColor => TerrainAccents.Terrain;

    protected override void DefineNode()
    {
        AddOutput<float>("Size",   tooltip: "Width/length of the terrain in world units.");
        AddOutput<float>("Height", tooltip: "Max height the heightmap can represent, in world units.");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        TerrainUniforms.EmitAll(ctx);
        return outputPort.Name == "Height" ? "_TerrainHeight" : "_TerrainSize";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx) => ShaderType.Float;
}
