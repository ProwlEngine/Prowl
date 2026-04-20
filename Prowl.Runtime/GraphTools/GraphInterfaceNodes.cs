// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Reflection;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Marks a node as part of a subgraph's external interface — either an input port the
/// containing subgraph exposes (<see cref="GraphInputNode"/>) or an output port
/// (<see cref="GraphOutputNode"/>). <see cref="SubgraphNode"/> reads these from the
/// referenced graph asset to build its own port list.
/// </summary>
public interface IGraphInterfaceNode
{
    /// <summary>Display + matching name for the corresponding port on the parent
    /// <see cref="SubgraphNode"/>. Renaming this changes the parent's port name and
    /// breaks any wires connected to the old name on the outer side.</summary>
    string PortName { get; }
    /// <summary>True for nodes that act as INPUTS into the subgraph from outside,
    /// false for OUTPUTS sent back to the parent graph.</summary>
    bool IsInput { get; }
}

/// <summary>
/// "Input from outside" node — placed inside a subgraph asset, exposes one OUTPUT port
/// (the value flows from the parent graph into this subgraph's interior). The port
/// type is dynamic: typed as <c>object</c> so any wire can connect, and the
/// <see cref="SubgraphNode"/> infers the actual type from whatever's wired downstream.
/// </summary>
[UniversalNode]
public sealed class GraphInputNode : Node, IGraphInterfaceNode
{
    public string PortName = "Input";

    public override string Title => $"Input · {PortName}";
    public override string Category => "Subgraph";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 100, 160, 220);

    string IGraphInterfaceNode.PortName => PortName;
    bool IGraphInterfaceNode.IsInput => true;

    protected override void DefineNode()
    {
        // Object-typed → accepts any wire; downstream ArePortsCompatible allows the
        // implicit unbox via the object-side rule.
        AddOutput<object>("Out");
    }
}

/// <summary>
/// "Output to outside" node — placed inside a subgraph asset, exposes one INPUT port
/// (the value flows from this subgraph's interior back out to the parent graph). The
/// port is typed as <c>object</c> so any wire can feed it; the parent
/// <see cref="SubgraphNode"/> infers the carried type from whatever's wired upstream.
/// </summary>
[UniversalNode]
public sealed class GraphOutputNode : Node, IGraphInterfaceNode
{
    public string PortName = "Output";

    public override string Title => $"Output · {PortName}";
    public override string Category => "Subgraph";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 100, 160, 220);

    string IGraphInterfaceNode.PortName => PortName;
    bool IGraphInterfaceNode.IsInput => false;

    protected override void DefineNode()
    {
        AddInput<object>("In");
    }
}

/// <summary>Reflection helpers for declaring runtime-typed ports — used by
/// <see cref="SubgraphNode"/> to mirror an inner graph's interface with the resolved
/// data types. Same generic-method-via-reflection trick <see cref="RelayNode"/> uses.</summary>
internal static class GraphInterfaceUtil
{
    public static Type ResolveType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return typeof(object);
        try { return Type.GetType(typeName) ?? typeof(object); }
        catch { return typeof(object); }
    }

    public static void AddInput(Node node, Type type, string name)
    {
        var m = typeof(Node).GetMethod("AddInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        m.MakeGenericMethod(type).Invoke(node,
            new object?[] { name, /*defaultValue*/ null, /*acceptsMultiple*/ false, /*layout*/ PortLayout.Above });
    }

    public static void AddOutput(Node node, Type type, string name)
    {
        var m = typeof(Node).GetMethod("AddOutput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        m.MakeGenericMethod(type).Invoke(node,
            new object?[] { name, /*acceptsMultiple*/ true, /*layout*/ PortLayout.Above });
    }
}
