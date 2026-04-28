// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Pass-through "waypoint" node. Used to bend a wire cleanly through a point in the
/// graph so long routes stay readable. One typed input → one typed output of the
/// same type, so it's functionally transparent to any evaluator.
/// </summary>
/// <remarks>
/// Intended to be created by alt+clicking an existing wire in the editor: the wire is
/// split at the click point, a relay is inserted, and the two halves reconnect through
/// it. <see cref="CarriedTypeName"/> is set to the wire's data type at creation time so
/// the relay's ports match. Not bound to any specific marker interface relay nodes
/// are useful in every graph type, so they implement every registered marker via
/// runtime-type-check (handled in <see cref="NodeRegistry"/>'s marker filter).
/// </remarks>
[UniversalNode]
[HiddenFromMenu]
public sealed class RelayNode : Node, IAutoPruneNode
{
    /// <summary>A relay is a wire waypoint the moment it has no incoming AND no
    /// outgoing wire, it's just clutter. Auto-prune removes it and the editor cleans
    /// up any lingering dangling edges on the next validation pass.</summary>
    public bool ShouldPrune(Graph graph)
    {
        bool hasIn = false, hasOut = false;
        foreach (var e in graph.Edges)
        {
            if (e.TargetNodeId == Id) hasIn = true;
            else if (e.SourceNodeId == Id) hasOut = true;
            if (hasIn && hasOut) return false;
        }
        return !hasIn && !hasOut;
    }

    /// <summary>Assembly-qualified name of the type carried through this relay. Persisted
    /// so the ports can rebuild with the right type after load.</summary>
    public string CarriedTypeName = typeof(object).AssemblyQualifiedName!;

    public override string Title => "";
    public override string Category => "Utility";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 120, 120, 135);

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Relay ports require runtime generic instantiation to mirror the carried type. NOT AOT-compatible — AOT consumers must avoid RelayNode (TODO: replace with dispatcher table).")]
    protected override void DefineNode()
    {
        var t = ResolveCarriedType();
        var addInput = typeof(RelayNode).GetMethod(nameof(AddInputGeneric),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var addOutput = typeof(RelayNode).GetMethod(nameof(AddOutputGeneric),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        addInput.MakeGenericMethod(t).Invoke(this, null);
        addOutput.MakeGenericMethod(t).Invoke(this, null);
    }

    private void AddInputGeneric<T>() => AddInput<T>("In");
    private void AddOutputGeneric<T>() => AddOutput<T>("Out");

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2057:Type.GetType",
        Justification = "Relay carries a user-chosen type by serialized name; user types must be preserved by the consuming application's trim configuration.")]
    private Type ResolveCarriedType()
    {
        if (string.IsNullOrEmpty(CarriedTypeName)) return typeof(object);
        Type? t = null;
        try { t = Type.GetType(CarriedTypeName); }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"RelayNode: failed to resolve carried type '{CarriedTypeName}': {ex.Message}");
            return typeof(object);
        }
        if (t == null)
        {
            // Type used to exist but no longer does relay degrades to object and will
            // accept any wire. Surface it so the user knows their graph lost fidelity.
            Debug.LogWarning($"RelayNode: carried type '{CarriedTypeName}' no longer exists; relay will act as an untyped passthrough.");
            return typeof(object);
        }
        return t;
    }
}
