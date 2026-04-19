// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// A typed value entry on the graph's blackboard — a variable that any node can read
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

    /// <summary>Background color — UI maps this to a small palette of preset hues.</summary>
    public uint PackedColor = 0xFFE5C36A; // amber
}

/// <summary>
/// A titled bounding region behind a cluster of nodes. Moving the group moves all
/// nodes whose Position falls inside it (resolved at interaction time, not stored —
/// keeps the data model simple, matches Unity GraphToolkit behaviour).
/// </summary>
public sealed class NodeGroup
{
    public Guid Id = Guid.NewGuid();
    public Float2 Position;
    public Float2 Size = new Float2(400, 240);
    public string Title = "Group";
    public uint PackedColor = 0xFF606A82; // muted blue
}
