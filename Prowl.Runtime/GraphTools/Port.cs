// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.GraphTools;

public enum PortDirection { Input, Output }

/// <summary>
/// Where this port sits relative to the node's property-grid section. Above = ports come
/// before the inline editor; Below = ports come after. Default is Above so simple math
/// nodes (most common) keep their familiar "inputs at top" layout.
/// </summary>
public enum PortLayout { Above, Below }

/// <summary>
/// A typed input or output socket on a <see cref="Node"/>. The graph editor uses
/// <see cref="DataType"/> to colour-code ports and validate connections (you can only
/// wire a Float output to a Float input, or to compatible types via implicit conversion).
/// </summary>
/// <remarks>
/// Ports are NOT serialised they're rebuilt by <see cref="Node.DefineNode"/> after
/// each load. Edges reference ports by (NodeId, PortName) so renaming a port invalidates
/// existing connections, but reordering them in the source code is safe.
///
/// <see cref="DefaultValue"/> is the inline-editable value shown on an unconnected input
/// port (e.g. a literal 0.5 next to a Float input).
/// </remarks>
public sealed class Port
{
    public string Name = "";
    public Type DataType = typeof(object);
    public PortDirection Direction;
    public bool AcceptsMultiple;

    /// <summary>
    /// Fallback for unconnected inputs. Type matches <see cref="DataType"/>. Stored as
    /// object evaluators box/unbox to the actual type. Declared in
    /// <see cref="Node.DefineNode"/>; for user-editable defaults, expose a public field
    /// on the node subclass (edited via the Inspector) and read it in the evaluator.
    /// </summary>
    public object? DefaultValue;

    /// <summary>
    /// Where this port row appears in the node body relative to the inline PropertyGrid
    /// section. <see cref="PortLayout.Above"/> = before the property grid (default for inputs/outputs);
    /// <see cref="PortLayout.Below"/> = after, for nodes that want their PropertyGrid front-and-centre.
    /// </summary>
    public PortLayout Layout;

    /// <summary>
    /// When true, the editor skips drawing the port and its row, and ignores it for
    /// hit-testing and wire endpoint resolution. Lets nodes show or hide ports based
    /// on their own state (e.g. an "If" node that hides the "Else" input when in
    /// unary mode) without having to rebuild the port list.
    /// Not persisted Ports are rebuilt by <see cref="Node.DefineNode"/> on each load,
    /// so visibility is driven by the node's own state.
    /// </summary>
    public bool IsHidden;

    /// <summary>
    /// When true, validators flag the node with an error if the port has no incoming wire.
    /// Applies only to input ports output ports don't have the concept of "required".
    /// Use for ports whose default value isn't a meaningful fallback (e.g. Custom Code
    /// node operands). Not persisted rebuilt by <see cref="Node.DefineNode"/>.
    /// </summary>
    public bool IsRequired;

    /// <summary>
    /// Optional hover-tooltip text shown next to the port in the editor. Supports
    /// single-line explanatory content; longer descriptions should live on the node's
    /// Title/Category docs instead. Not persisted rebuilt by <see cref="Node.DefineNode"/>.
    /// </summary>
    public string? Tooltip;
}
