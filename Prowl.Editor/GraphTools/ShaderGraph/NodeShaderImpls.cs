// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

namespace Prowl.Editor.GraphTools.ShaderGraphs;

/// <summary>
/// Editor-side <see cref="IShaderNode"/> wrappers for runtime node types. Kept out of
/// Runtime so the runtime project doesn't need to know about GLSL emission. Each
/// wrapper is referenced via a small registry that maps Node→IShaderNode at compile
/// time.
/// </summary>
internal interface IShaderNodeImpl
{
    string Evaluate(Node node, Port outputPort, ShaderStage stage, ShaderGenContext ctx);
    ShaderType GetOutputType(Node node, Port outputPort);
}

// ─── Property nodes — output their uniform name; declarations done by the compiler. ──

internal sealed class PropertyNodeImpl : IShaderNodeImpl
{
    public string Evaluate(Node node, Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        var prop = (IShaderProperty)node;
        // Uniform decl is added once per property by the compiler-driven property pass.
        return prop.PropertyName;
    }
    public ShaderType GetOutputType(Node node, Port outputPort)
        => ((IShaderProperty)node).PropertyType;
}

// ─── Tex2D — sample the bound sampler at the requested UV. ────────────────────────────

internal sealed class Tex2DSampleImpl : IShaderNodeImpl
{
    public string Evaluate(Node node, Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        // The Sampler input must be wired to a Texture2DPropertyNode (which evaluates
        // to its uniform name). The UV input falls back to texCoord0 when unconnected.
        var samplerExpr = ctx.EvaluateInput(node.GetInput("Sampler")!);
        var uvExpr = ctx.EvaluateInput(node.GetInput("UV")!);

        // Whole sample → cache as a temp once per node, not per-output, so re-using R/G/B
        // doesn't re-sample.
        var key = ($"_tex_{node.Id:N}", "RGBA");
        // Stash through the body prelude. We use a deterministic name keyed on node id.
        var local = $"_tex{node.Id:N}";
        if (!ctx.HelperFunctions.Contains(local)) // misuse of HelperFunctions as a "declared local" set
        {
            ctx.HelperFunctions.Add(local);
            // The UV input may default to (0,0) — when no wire connects it we'd rather
            // use the vertex's texCoord0. Detect by inspecting the input.
            var uvPort = node.GetInput("UV")!;
            bool uvConnected = false;
            // Scan edges directly — EvaluateInput already emits the literal if unconnected,
            // but for the texCoord fallback we need to know.
            foreach (var e in ctx.Graph.Edges)
                if (e.TargetNodeId == node.Id && e.TargetPortName == "UV") { uvConnected = true; break; }
            if (!uvConnected)
            {
                uvExpr = "texCoord0";
                ctx.Varyings.Add(("texCoord0", "vec2"));
            }
            ctx.BodyPrelude.AppendLine($"    vec4 {local} = texture({samplerExpr}, {uvExpr});");
        }

        return outputPort.Name switch
        {
            "RGBA" => local,
            "R"    => $"{local}.r",
            "G"    => $"{local}.g",
            "B"    => $"{local}.b",
            "A"    => $"{local}.a",
            _      => local,
        };
    }

    public ShaderType GetOutputType(Node node, Port outputPort)
        => outputPort.Name == "RGBA" ? ShaderType.Color : ShaderType.Float;
}

// ─── TexCoord — varying texCoord0 emitted by the standard vertex pass. ───────────────

internal sealed class TexCoordImpl : IShaderNodeImpl
{
    public string Evaluate(Node node, Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        // Channel index is on the runtime node — only channel 0 supported in MVP.
        ctx.Varyings.Add(("texCoord0", "vec2"));
        return "texCoord0";
    }
    public ShaderType GetOutputType(Node node, Port outputPort) => ShaderType.Vec2;
}

// ─── Registry mapping Node type → impl. ──────────────────────────────────────────────

internal static class NodeShaderImplRegistry
{
    private static readonly System.Collections.Generic.Dictionary<System.Type, IShaderNodeImpl> _map = new()
    {
        [typeof(ColorPropertyNode)]      = new PropertyNodeImpl(),
        [typeof(FloatPropertyNode)]      = new PropertyNodeImpl(),
        [typeof(Texture2DPropertyNode)]  = new PropertyNodeImpl(),
        [typeof(Tex2DSampleNode)]        = new Tex2DSampleImpl(),
        [typeof(TexCoordNode)]           = new TexCoordImpl(),
    };

    public static IShaderNodeImpl? Get(Node node)
    {
        // Walk base types so a future SamplerNodeBase covers all subclasses cleanly.
        var t = node.GetType();
        while (t != null && t != typeof(object))
        {
            if (_map.TryGetValue(t, out var impl)) return impl;
            t = t.BaseType;
        }
        return null;
    }
}
