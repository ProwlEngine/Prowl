// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// A typed value entry on the graph's blackboard a variable that any node can read
/// or write via dedicated "Get Variable" / "Set Variable" nodes. Behaviour trees use
/// these heavily for shared state; shader graphs use them for material parameters.
/// </summary>
public sealed class BlackboardVariable
{
    public Guid Id = Guid.NewGuid();
    public string Name = "Variable";
    public string TypeName = typeof(float).AssemblyQualifiedName!;

    /// <summary>Default value shown when no node has written to this variable yet.</summary>
    public object? DefaultValue;

    /// <summary>If true, the variable is exposed externally (e.g. shader uniform / BT parameter).</summary>
    public bool Exposed = true;
}

/// <summary>Free-floating documentation note placed anywhere on the canvas.</summary>
public sealed class StickyNote
{
    public Guid Id = Guid.NewGuid();
    public Float2 Position;
    public Float2 Size = new Float2(220, 140);
    public string Title = "Note";
    public string Body = "";

    /// <summary>Background color UI maps this to a small palette of preset hues.
    /// Layout is ABGR (matches <c>Prowl.Vector.Color32(uint)</c>): A is the high byte,
    /// R is the low byte. This default unpacks to amber (R=E5 G=C3 B=6A, A=FF).</summary>
    public uint PackedColor = 0xFF6AC3E5;
}

/// <summary>
/// A titled bounding region behind a cluster of nodes. Moving the group moves all
/// nodes whose Position falls inside it (resolved at interaction time, not stored —
/// keeps the data model simple).
/// </summary>
public sealed class NodeGroup
{
    public Guid Id = Guid.NewGuid();
    public Float2 Position;
    public Float2 Size = new Float2(400, 240);
    public string Title = "Group";
    public uint PackedColor = 0xFF826A60; // muted blue (ABGR: R=60 G=6A B=82)
}
