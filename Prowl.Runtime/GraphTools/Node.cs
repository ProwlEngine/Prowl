// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Severity level for diagnostic messages attached to a node.
/// </summary>
public enum NodeMessageSeverity { Info, Warning, Error }

/// <summary>
/// A diagnostic message displayed on a node (e.g. "input X is required", "type mismatch").
/// </summary>
public struct NodeMessage
{
    public NodeMessageSeverity Severity;
    public string Text;
}

/// <summary>
/// Abstract base for every node in a graph. Concrete subclasses define their inputs,
/// outputs, and behaviour. The framework treats nodes as opaque data containers —
/// rendering, hit-testing, and connection logic operate on the base properties (Id,
/// Position, Inputs, Outputs).
/// </summary>
/// <remarks>
/// <para>Subclasses populate <see cref="Inputs"/>/<see cref="Outputs"/> in their
/// <see cref="DefineNode"/> method. <c>DefineNode</c> is called once after
/// construction (via <see cref="EnsureDefined"/>) and again after deserialization,
/// so port lists never have to be serialized they're derived from the node's type.</para>
///
/// <para>Per-node settings (knobs the user tweaks in the inspector) should be public
/// fields/properties on the subclass the editor renders them through Prowl's standard
/// PropertyGrid just like component properties.</para>
/// </remarks>
public abstract class Node
{
    // ─── Persisted data must be public fields (Echo doesn't serialise properties) ──
    // [HideInInspector] on each so the editor's embedded PropertyGrid doesn't render
    // them only subclass fields (the user-facing knobs) should appear in the node body.

    /// <summary>Stable identifier edges reference nodes by this, not by list index.</summary>
    [HideInInspector] public Guid Id = Guid.NewGuid();

    /// <summary>Position in graph coordinates (not screen pixels).</summary>
    [HideInInspector] public Float2 Position;

    /// <summary>Diagnostic messages attached to this node (set by graph processors).</summary>
    [HideInInspector] public List<NodeMessage> Messages = new();

    // ─── Computed / display-only properties are fine because Echo skips them ───────

    /// <summary>Human-readable label shown in the title bar. Default = type name.</summary>
    public virtual string Title => GetType().Name;

    /// <summary>Category used by the node-creation menu (e.g. "Math/Trig", "Input").</summary>
    public virtual string Category => "Misc";

    /// <summary>Optional accent color for the title bar subclasses can theme by category.</summary>
    public virtual System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 90, 110, 140);

    // ─── Derived data rebuilt by DefineNode() each load explicitly NOT serialised ──

    [SerializeIgnore] private List<Port> _inputs = new();
    [SerializeIgnore] private List<Port> _outputs = new();
    [SerializeIgnore] private bool _defined;

    /// <summary>Input ports populated lazily by <see cref="DefineNode"/>. Not serialised.</summary>
    public List<Port> Inputs { get { EnsureDefined(); return _inputs; } }

    /// <summary>Output ports populated lazily by <see cref="DefineNode"/>. Not serialised.</summary>
    public List<Port> Outputs { get { EnsureDefined(); return _outputs; } }

    /// <summary>
    /// Subclasses override this to register their inputs and outputs via
    /// <see cref="AddInput{T}"/>/<see cref="AddOutput{T}"/>. Called once on demand.
    /// </summary>
    protected abstract void DefineNode();

    /// <summary>
    /// Force the node to populate its port list. Called by the graph editor before
    /// rendering / hit-testing; safe to call multiple times.
    /// </summary>
    public void EnsureDefined()
    {
        if (_defined) return;
        _defined = true; // set first to break recursion if AddInput/AddOutput touch Inputs/Outputs
        _inputs.Clear();
        _outputs.Clear();
        DefineNode();
    }

    // ─── Subclass helpers for declaring ports ────────────────────────────────────────

    /// <summary>
    /// Declare a typed input port with an optional fallback value used when the port
    /// has no incoming wire. For user-editable defaults, expose a public field on the
    /// subclass instead it'll appear in the Inspector's PropertyGrid automatically.
    /// </summary>
    protected Port AddInput<T>(string name, T? defaultValue = default,
        bool acceptsMultiple = false,
        PortLayout layout = PortLayout.Above,
        bool required = false,
        string? tooltip = null,
        bool hidden = false)
    {
        var p = new Port
        {
            Name = name,
            DataType = typeof(T),
            Direction = PortDirection.Input,
            AcceptsMultiple = acceptsMultiple,
            DefaultValue = defaultValue,
            Layout = layout,
            IsRequired = required,
            Tooltip = tooltip,
            IsHidden = hidden,
        };
        _inputs.Add(p); // Use backing field Inputs property would re-enter EnsureDefined.
        return p;
    }

    /// <summary>Declare a typed output port. Outputs default to allowing multiple connections.</summary>
    protected Port AddOutput<T>(string name, bool acceptsMultiple = true,
        PortLayout layout = PortLayout.Above,
        string? tooltip = null,
        bool hidden = false)
    {
        var p = new Port
        {
            Name = name,
            DataType = typeof(T),
            Direction = PortDirection.Output,
            AcceptsMultiple = acceptsMultiple,
            DefaultValue = null,
            Layout = layout,
            Tooltip = tooltip,
            IsHidden = hidden,
        };
        _outputs.Add(p);
        return p;
    }

    /// <summary>Find an input port by name. Returns null if not found.</summary>
    public Port? GetInput(string name)
    {
        EnsureDefined();
        for (int i = 0; i < Inputs.Count; i++) if (Inputs[i].Name == name) return Inputs[i];
        return null;
    }

    /// <summary>Find an output port by name. Returns null if not found.</summary>
    public Port? GetOutput(string name)
    {
        EnsureDefined();
        for (int i = 0; i < Outputs.Count; i++) if (Outputs[i].Name == name) return Outputs[i];
        return null;
    }
}

/// <summary>
/// Placeholder node returned by the editor when deserialization fails (e.g. the original
/// node type was renamed/removed). Preserves the raw serialized data so the user can
/// re-bind it to a new type rather than losing connections silently.
/// </summary>
public sealed class MissingNode : Node
{
    /// <summary>Type name we couldn't resolve (e.g. "ShaderGraph.Nodes.OldFooNode").</summary>
    public string MissingTypeName = "";

    /// <summary>
    /// Echo blob preserving the original node's serialised payload so it survives a
    /// re-save round-trip once the missing type is restored, the user can re-bind
    /// without losing data. Echo serializes/deserializes EchoObject natively.
    /// </summary>
    public Prowl.Echo.EchoObject? SerializedPayload;

    public override string Title => $"Missing: {MissingTypeName}";
    public override string Category => "Missing";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 200, 80, 80);

    protected override void DefineNode()
    {
        // No ports we don't know what they were. The user has to either re-create
        // the type or manually clean up.
    }
}
