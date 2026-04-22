// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Mark a <see cref="GraphValidator"/> subclass as running on graphs whose
/// <see cref="Graph.NodeMarkerInterface"/> matches <see cref="TargetMarker"/>. Pass
/// <c>null</c> (or don't set it) for a validator that runs on every graph type.
/// Discovered at startup via <see cref="GraphValidatorRegistry"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GraphValidatorAttribute : Attribute
{
    public Type? TargetMarker { get; }
    public GraphValidatorAttribute() { }
    public GraphValidatorAttribute(Type targetMarker) { TargetMarker = targetMarker; }
}

/// <summary>
/// Runs validation over a graph and appends diagnostics to <see cref="Node.Messages"/>.
/// Executed by the editor whenever the graph changes (edge add/remove, node add/remove,
/// undo/redo) and on save; consumers see the red/yellow badges the
/// <c>DefaultNodeRenderer</c> paints from <see cref="Node.Messages"/>.
/// </summary>
/// <remarks>
/// <para>Implementations should be stateless — a single instance is cached and shared
/// across every validation pass.</para>
/// <para>Standard pattern: call <see cref="Graph.ClearAllMessages"/> before your first
/// validator runs, then each validator iterates nodes/edges and appends messages.
/// The caller (<see cref="GraphValidatorRegistry.Validate"/>) handles the clear.</para>
/// </remarks>
public abstract class GraphValidator
{
    public abstract void Validate(Graph graph);
}

/// <summary>
/// Discovers and runs <see cref="GraphValidator"/>s for a given graph. Universal
/// validators (no marker) run for every graph; marker-scoped validators run only when
/// the graph's <see cref="Graph.NodeMarkerInterface"/> matches.
/// </summary>
public static class GraphValidatorRegistry
{
    private static readonly List<(Type? marker, GraphValidator instance)> _validators = new();
    private static bool _initialized;

    public static void Reinitialize()
    {
        _initialized = false;
        _validators.Clear();
        Initialize();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _validators.Clear();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle)
            {
                // Partial-load case: reflect over whatever DID resolve; skip nulls.
                types = System.Array.FindAll(rtle.Types, t => t != null)!;
                Debug.LogWarning($"GraphValidatorRegistry: '{asm.GetName().Name}' partially loaded ({rtle.LoaderExceptions?.Length ?? 0} loader errors); continuing with {types.Length} resolved type(s).");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"GraphValidatorRegistry: failed to reflect '{asm.GetName().Name}': {ex.Message}");
                continue;
            }

            foreach (var type in types)
            {
                if (!typeof(GraphValidator).IsAssignableFrom(type) || type.IsAbstract) continue;
                var attr = type.GetCustomAttribute<GraphValidatorAttribute>();
                GraphValidator instance;
                try { instance = (GraphValidator)Activator.CreateInstance(type)!; }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"GraphValidatorRegistry: could not instantiate '{type.FullName}': {ex.Message}");
                    continue;
                }
                _validators.Add((attr?.TargetMarker, instance));
            }
        }
    }

    /// <summary>Clear every node's <see cref="Node.Messages"/> and run every validator
    /// that applies to <paramref name="graph"/>. Idempotent — safe to call on every
    /// mutation.</summary>
    public static void Validate(Graph graph)
    {
        if (!_initialized) Initialize();

        // Clear first so validators start from a clean slate. If a validator wants to
        // accumulate across passes it can stash state on itself (but shouldn't).
        foreach (var n in graph.Nodes) n.Messages.Clear();

        var marker = graph.NodeMarkerInterface;
        foreach (var (targetMarker, instance) in _validators)
        {
            if (targetMarker != null && (marker == null || !targetMarker.IsAssignableFrom(marker)))
                continue;
            try { instance.Validate(graph); }
            catch (Exception ex) { Debug.LogWarning($"Validator {instance.GetType().Name} threw: {ex.Message}"); }
        }
    }
}
