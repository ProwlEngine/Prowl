// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;

namespace Prowl.Editor.GraphTools.ShaderGraphs;

/// <summary>
/// Shared mutable state passed between every node during a single compile pass.
/// Tracks declarations (properties, uniforms, varyings, helper functions, includes),
/// caches per-port expression results so shared subgraphs only emit once, and gives
/// nodes a way to recurse into their upstream wires.
/// </summary>
/// <remarks>
/// One context per compile per stage. The compiler creates a fresh context for the
/// vertex pass and another for the fragment pass — declarations don't bleed between
/// stages. Properties + the <see cref="PropertyBlock"/> are shared globally though
/// (one Properties{} block at the top of the .shader file).
/// </remarks>
public sealed class ShaderGenContext
{
    /// <summary>The graph being compiled — used by nodes to walk upstream edges.</summary>
    public readonly Prowl.Runtime.GraphTools.Graph Graph;

    /// <summary>Which stage's main() body we're currently emitting into. Lets nodes
    /// branch on stage (e.g. ScreenPos uses gl_FragCoord in fragment, projected
    /// position in vertex).</summary>
    public readonly ShaderStage Stage;

    /// <summary>Compile-time diagnostics surfaced back to the user via Node.Messages
    /// after a build. Each entry attaches to the offending node id.</summary>
    public readonly List<(System.Guid? nodeId, string message, NodeMessageSeverity severity)> Diagnostics = new();

    /// <summary>The Properties{} block lines (e.g. `_MainTex ("Albedo", Texture2D) = "white"`).
    /// One entry per property node. Shared across both stages.</summary>
    public readonly List<string> PropertyBlock = new();

    /// <summary>`uniform Type _Name;` declarations to emit at the top of this stage.
    /// Stage-local — vertex and fragment maintain their own uniform sets so unused
    /// uniforms in one stage don't pollute the other.</summary>
    public readonly HashSet<string> Uniforms = new();

    /// <summary>`#include "X"` lines to emit at the top. Set so each include appears once.</summary>
    public readonly HashSet<string> Includes = new();

    /// <summary>Preprocessor defines emitted BEFORE includes — e.g. <c>SG_NO_SHADOWS</c>
    /// to switch off shadow sampling in Lighting.glsl. Stored as plain strings without
    /// the <c>#define</c> keyword so the compiler can format them consistently.</summary>
    public readonly HashSet<string> Defines = new();

    /// <summary>Top-level helper function definitions (e.g. user CustomCode wrapper). Set
    /// so identical bodies aren't emitted twice when the same node is referenced
    /// multiple times via Get/Set vars.</summary>
    public readonly HashSet<string> HelperFunctions = new();

    /// <summary>Cross-stage varyings flowing vertex → fragment. The compiler emits
    /// matching `out` (vertex) / `in` (fragment) pairs.</summary>
    public readonly HashSet<(string name, string type)> Varyings = new();

    /// <summary>Lines added directly to the body of main() — typically `Type _t<id> = expr;`
    /// statements emitted by nodes that prefer to compute once into a temp.</summary>
    public readonly StringBuilder BodyPrelude = new();

    // ─── per-port memoisation ─────────────────────────────────────────────────────────
    private readonly Dictionary<(System.Guid nodeId, string portName), string> _cache = new();
    private readonly HashSet<(System.Guid nodeId, string portName)> _evaluating = new();
    private int _tempCounter;

    public ShaderGenContext(Prowl.Runtime.GraphTools.Graph graph, ShaderStage stage)
    {
        Graph = graph;
        Stage = stage;
    }

    /// <summary>Return a fresh, never-before-used local variable name. Use for
    /// <c>EmitTemp</c>-style assignment when a node's expression should only be
    /// evaluated once and referenced multiple times downstream.</summary>
    public string FreshLocal(string prefix = "_t") => $"{prefix}{_tempCounter++}";

    /// <summary>
    /// Resolve an INPUT port to a GLSL expression: if a wire is connected, recurse into
    /// the source node's <see cref="IShaderNode.Evaluate"/>; otherwise emit a literal
    /// using <paramref name="input"/>'s default value. Auto-promotes types to match
    /// <paramref name="input"/>'s declared type.
    /// </summary>
    public string EvaluateInput(Port input)
    {
        // Find the edge targeting this port (input ports are single-source by convention).
        Edge? edge = null;
        foreach (var e in Graph.Edges)
        {
            if (e.TargetNodeId == FindOwner(input).Id && e.TargetPortName == input.Name)
            {
                edge = e; break;
            }
        }

        var inputType = ResolveType(input);
        if (edge == null)
            return LiteralForDefault(input, inputType);

        var sourceNode = Graph.FindNode(edge.SourceNodeId);
        if (sourceNode == null) return LiteralForDefault(input, inputType);

        var impl = NodeShaderImplRegistry.Get(sourceNode);
        if (impl == null)
        {
            Diagnostics.Add((sourceNode.Id, $"No shader emission for node type '{sourceNode.GetType().Name}'.", NodeMessageSeverity.Error));
            return LiteralForDefault(input, inputType);
        }

        var sourcePort = sourceNode.GetOutput(edge.SourcePortName);
        if (sourcePort == null) return LiteralForDefault(input, inputType);

        var key = (sourceNode.Id, sourcePort.Name);
        if (!_cache.TryGetValue(key, out var expr))
        {
            // Cycle guard: a node whose evaluation is already on the call stack is being
            // asked to evaluate itself (directly or transitively). Break the recursion
            // with a zero literal + diagnostic rather than stack-overflowing the importer.
            // Editor wire creation already prevents cycles at author time, but a corrupted
            // save or a future self-feedback-loop node could slip through.
            if (!_evaluating.Add(key))
            {
                Diagnostics.Add((sourceNode.Id, $"Cycle detected while evaluating '{sourceNode.GetType().Name}.{sourcePort.Name}'.", NodeMessageSeverity.Error));
                return LiteralForDefault(input, inputType);
            }
            try { expr = impl.Evaluate(sourceNode, sourcePort, Stage, this); }
            finally { _evaluating.Remove(key); }
            _cache[key] = expr;
        }

        var sourceType = impl.GetOutputType(sourceNode, sourcePort);
        return ShaderTypeUtil.Promote(expr, sourceType, inputType);
    }

    /// <summary>Find the Node that owns <paramref name="port"/> by scanning the graph —
    /// EvaluateInput needs this because Port doesn't back-reference its parent.</summary>
    private Node FindOwner(Port port)
    {
        foreach (var n in Graph.Nodes)
        {
            foreach (var p in n.Inputs)  if (p == port) return n;
            foreach (var p in n.Outputs) if (p == port) return n;
        }
        throw new InvalidOperationException($"Port '{port.Name}' is not part of this graph.");
    }

    private static ShaderType ResolveType(Port port)
    {
        var t = port.DataType;
        if (t == typeof(float)) return ShaderType.Float;
        if (t == typeof(int))   return ShaderType.Int;
        if (t == typeof(bool))  return ShaderType.Bool;
        if (t == typeof(Prowl.Vector.Float2)) return ShaderType.Vec2;
        if (t == typeof(Prowl.Vector.Float3)) return ShaderType.Vec3;
        if (t == typeof(Prowl.Vector.Float4)) return ShaderType.Vec4;
        if (t == typeof(Prowl.Vector.Color))  return ShaderType.Color;
        if (t == typeof(Prowl.Vector.Float3x3)) return ShaderType.Mat3;
        if (t == typeof(Prowl.Vector.Float4x4)) return ShaderType.Mat4;
        if (t == typeof(Prowl.Runtime.Resources.Texture2D)) return ShaderType.Sampler2D;
        return ShaderType.Float;
    }

    private static string LiteralForDefault(Port port, ShaderType type)
    {
        // Convert the port's DefaultValue to a GLSL literal of the target type. Boxes
        // through dynamic-style switches; falls back to a typed zero when the default
        // is null (every type has a sensible "neutral" zero/identity).
        var v = port.DefaultValue;
        switch (type)
        {
            case ShaderType.Float:
                return Fmt(v is float f ? f : 0f);
            case ShaderType.Int:
                return (v is int i ? i : 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case ShaderType.Bool:
                return (v is bool b && b) ? "true" : "false";
            case ShaderType.Vec2:
                {
                    var v2 = v is Prowl.Vector.Float2 vv ? vv : Prowl.Vector.Float2.Zero;
                    return $"vec2({Fmt(v2.X)}, {Fmt(v2.Y)})";
                }
            case ShaderType.Vec3:
                {
                    var v3 = v is Prowl.Vector.Float3 vv ? vv : Prowl.Vector.Float3.Zero;
                    return $"vec3({Fmt(v3.X)}, {Fmt(v3.Y)}, {Fmt(v3.Z)})";
                }
            case ShaderType.Vec4:
            case ShaderType.Color:
                {
                    var v4 = v is Prowl.Vector.Float4 vv ? vv
                          : (v is Prowl.Vector.Color c ? new Prowl.Vector.Float4(c.R, c.G, c.B, c.A) : new Prowl.Vector.Float4(0, 0, 0, 1));
                    return $"vec4({Fmt(v4.X)}, {Fmt(v4.Y)}, {Fmt(v4.Z)}, {Fmt(v4.W)})";
                }
            default:
                return "0.0";
        }
    }

    /// <summary>Format a float as GLSL — invariant culture + always include a decimal
    /// point so GLSL parses the literal as a float (not an int).</summary>
    public static string Fmt(float v)
    {
        var s = v.ToString("0.0######", System.Globalization.CultureInfo.InvariantCulture);
        if (!s.Contains('.')) s += ".0";
        return s;
    }
}
