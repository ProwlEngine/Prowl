// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.GraphTools.ShaderGraphs;

/// <summary>
/// A shader type is a top-level genre of shader — Surface / PostEffect / Particle /
/// Grass / Terrain — each of which decides which master node the graph uses, which
/// passes the compiler emits, which render settings to seed, and what the initial
/// graph looks like when the user creates one.
/// </summary>
/// <remarks>
/// <para>Shader types are discovered via reflection at init time (any concrete
/// <see cref="IShaderType"/> with a parameterless constructor gets registered), so
/// new types can be added by dropping a class in user code without touching the
/// framework.</para>
///
/// <para>Nodes that only make sense inside a specific shader type declare that with
/// <see cref="ShaderTypeAttribute"/>. Nodes without the attribute are available in
/// every shader type.</para>
/// </remarks>
public interface IShaderType
{
    /// <summary>Stable identifier persisted with the graph asset. Survives renames
    /// of class / display name. Examples: "Surface", "PostEffect", "Grass".</summary>
    string Id { get; }

    /// <summary>Friendly name shown in UI (node browser header, inspector label).</summary>
    string DisplayName { get; }

    /// <summary>Concrete <see cref="MasterNodeBase"/> subclass this type uses. The
    /// seeded graph places exactly one of these, and the node is hidden from the
    /// Add-Node menu — users don't add masters manually.</summary>
    Type MasterNodeType { get; }

    /// <summary>Passes the compiler emits for graphs of this type. The compiler's
    /// job becomes a thin loop over these.</summary>
    IReadOnlyList<IShaderPass> Passes { get; }

    /// <summary>Default render state applied to freshly seeded graphs.</summary>
    ShaderGraphRenderSettings DefaultRenderSettings { get; }

    /// <summary>Create-menu entries this type contributes. Most types return a single
    /// entry; <c>Surface</c> returns three (Lit PBR / Lit Basic / Unlit) that differ
    /// only in the seeded master's Lighting mode.</summary>
    IReadOnlyList<ShaderTypeMenuEntry> MenuEntries { get; }

    /// <summary>Populate a freshly-created graph with the nodes this type wants users
    /// to start from. Should be functional enough that the user sees something render
    /// immediately — then customizes from there. <paramref name="variantKey"/> is the
    /// key from the <see cref="ShaderTypeMenuEntry"/> the user picked (e.g.
    /// "LitPBR" vs "Unlit" for Surface).</summary>
    void SeedGraph(ShaderGraph graph, string variantKey);

    /// <summary>Final filter on top of <see cref="ShaderTypeAttribute"/>. Default
    /// implementation returns true — override to gate further (e.g. require a node
    /// to implement a specific interface).</summary>
    bool AllowsNode(Type nodeType) => true;
}

/// <summary>One row in the Create menu. A shader type can expose multiple variants
/// that seed differently (Surface contributes Lit PBR / Lit Basic / Unlit from the
/// same type).</summary>
public readonly struct ShaderTypeMenuEntry
{
    /// <summary>Stable key passed to <see cref="IShaderType.SeedGraph"/>. Lets the
    /// type distinguish variants without parsing display strings.</summary>
    public readonly string VariantKey;

    /// <summary>Menu path shown in the Create menu (e.g. "Shader Graph/Lit PBR").</summary>
    public readonly string MenuPath;

    /// <summary>Sort order inside the Create menu. Lower = earlier.</summary>
    public readonly int Order;

    public ShaderTypeMenuEntry(string variantKey, string menuPath, int order)
    {
        VariantKey = variantKey;
        MenuPath = menuPath;
        Order = order;
    }
}

/// <summary>
/// One compiled output pass within a shader type. Surface has three (Standard /
/// DepthNormals / Shadow); PostEffect has one. Each pass fully owns its vertex and
/// fragment emission — there's no shared compiler-side hardcoded template.
/// </summary>
public interface IShaderPass
{
    /// <summary>Pass name written into the generated <c>Pass "X" { ... }</c> block.</summary>
    string Name { get; }

    /// <summary>Role hint so tooling / the render pipeline can recognise what this
    /// pass is for without parsing the pass name.</summary>
    ShaderPassRole Role { get; }

    /// <summary>Emit the full pass block — <c>Pass "X" { Tags {} ... GLSLPROGRAM ... ENDGLSL }</c>
    /// — as a string. The compiler concatenates these and hands the result to
    /// <c>ShaderParser</c>.</summary>
    /// <remarks>
    /// Implementations build their own <see cref="ShaderGenContext"/> per invocation
    /// (properties / uniforms / varyings are pass-scoped). They read the master
    /// node's connected inputs via the node's own Evaluate methods to pull user
    /// subtrees into the emitted GLSL.
    /// </remarks>
    string EmitPass(MasterNodeBase master, ShaderGraph graph, PassEmitSharedState shared);
}

/// <summary>What the pass is conceptually. Render pipelines and editors can filter
/// on this without parsing the pass name.</summary>
public enum ShaderPassRole
{
    /// <summary>Main forward-lit rendering.</summary>
    Forward,
    /// <summary>Depth + normals pre-pass (for SSR / GTAO / DoF etc.).</summary>
    DepthPrepass,
    /// <summary>Shadow caster into the shadow atlas.</summary>
    ShadowCaster,
    /// <summary>Fullscreen post-effect that samples scene color / depth.</summary>
    Fullscreen,
    /// <summary>Background pass (skybox / sky dome).</summary>
    Background,
    /// <summary>Anything else — catch-all so user code can add novel pass kinds.</summary>
    Custom,
}

/// <summary>
/// Shared state passed to every pass's <see cref="IShaderPass.EmitPass"/> so they can
/// collaborate — e.g. the Standard pass builds the depth-helper subtree once and the
/// DepthNormals / Shadow passes reuse it.
/// </summary>
public sealed class PassEmitSharedState
{
    /// <summary>Per-graph diagnostics (errors / warnings) collected across all passes.</summary>
    public List<(Guid? nodeId, string message, NodeMessageSeverity severity)> Diagnostics = new();

    /// <summary>GLSL uniform declarations for every <see cref="IShaderProperty"/> node in
    /// the graph. Collected once by the compiler and handed to every pass so passes can
    /// dump them into their per-stage uniform sets without each rebuilding the list.</summary>
    public List<string> PropertyUniforms = new();

    /// <summary>Free-form bag keyed by string — used by types to share helper context
    /// between their passes (e.g. Surface stashes a depth-subtree blueprint here).</summary>
    public Dictionary<string, object> Scratch = new();
}

/// <summary>
/// Base class for every master output node, regardless of shader type. Marked
/// <c>[HiddenFromMenu]</c> — masters are auto-seeded, never user-added. Subclasses
/// define their inputs in <see cref="Node.DefineNode"/> exactly like any other node;
/// the compiler dispatches on the concrete subclass via <see cref="IShaderType.MasterNodeType"/>.
/// </summary>
[HiddenFromMenu]
public abstract class MasterNodeBase : Node, IShaderGraphNode
{
    public override string Category => "Output";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 200, 80, 100);
}

/// <summary>
/// Declares the shader type(s) a node is applicable to. Absence = universal (node
/// works in every shader type). Multiple attributes = node works in any of the listed
/// types. The node browser filters by this; the compiler emits a warning + zero
/// fallback if a node is used inside a graph of the wrong type.
/// </summary>
/// <example>
/// <code>
/// [ShaderType("Terrain"), ShaderType("Grass")]
/// public sealed class HeightmapSampleNode : Node, IShaderNode, IShaderGraphNode { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ShaderTypeAttribute : Attribute
{
    public string Id { get; }
    public ShaderTypeAttribute(string id) { Id = id; }
}

/// <summary>
/// Reflection-backed registry of every <see cref="IShaderType"/> implementation in
/// the AppDomain. Built lazily on first query; call <see cref="Reinitialize"/> after
/// loading new assemblies (e.g. after user-script recompile).
/// </summary>
public static class ShaderTypeRegistry
{
    private static List<IShaderType>? s_all;
    private static readonly Dictionary<string, IShaderType> s_byId = new(StringComparer.Ordinal);
    private static readonly object s_lock = new();

    /// <summary>Every registered shader type.</summary>
    public static IReadOnlyList<IShaderType> All
    {
        get { EnsureBuilt(); return s_all!; }
    }

    /// <summary>Resolve by stable id. Throws if unknown — that's usually a sign the
    /// asset was authored against a plugin that's since been removed.</summary>
    public static IShaderType Resolve(string id)
    {
        EnsureBuilt();
        if (s_byId.TryGetValue(id, out var t)) return t;
        throw new KeyNotFoundException($"Shader type '{id}' is not registered. Known ids: [{string.Join(", ", s_byId.Keys)}]");
    }

    /// <summary>Non-throwing resolve — returns null if the id is unknown.</summary>
    public static IShaderType? TryResolve(string id)
    {
        EnsureBuilt();
        return s_byId.TryGetValue(id, out var t) ? t : null;
    }

    /// <summary>Check if a node type is applicable to a given shader type id, using
    /// <see cref="ShaderTypeAttribute"/>. Universal (unattributed) nodes return true
    /// for every shader type.</summary>
    public static bool IsNodeApplicable(Type nodeType, string shaderTypeId)
    {
        var attrs = (ShaderTypeAttribute[])nodeType.GetCustomAttributes(typeof(ShaderTypeAttribute), inherit: false);
        if (attrs.Length == 0) return true;  // universal
        for (int i = 0; i < attrs.Length; i++)
            if (attrs[i].Id == shaderTypeId) return true;
        return false;
    }

    /// <summary>Force a full rescan — call after loading / unloading assemblies.</summary>
    public static void Reinitialize()
    {
        lock (s_lock)
        {
            s_all = null;
            s_byId.Clear();
        }
    }

    private static void EnsureBuilt()
    {
        if (s_all != null) return;
        lock (s_lock)
        {
            if (s_all != null) return;

            var list = new List<IShaderType>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(IShaderType).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                    try
                    {
                        var instance = (IShaderType)Activator.CreateInstance(t)!;
                        if (s_byId.ContainsKey(instance.Id))
                        {
                            Debug.LogWarning($"[ShaderTypeRegistry] Duplicate shader type id '{instance.Id}' — " +
                                             $"keeping '{s_byId[instance.Id].GetType().FullName}', skipping '{t.FullName}'.");
                            continue;
                        }
                        s_byId[instance.Id] = instance;
                        list.Add(instance);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[ShaderTypeRegistry] Failed to instantiate '{t.FullName}': {e.Message}");
                    }
                }
            }

            s_all = list;
        }
    }
}
