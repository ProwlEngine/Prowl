// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using TransitionEasing = Prowl.Motion.TransitionEasing;
using TransitionSync = Prowl.Motion.TransitionSync;

namespace Prowl.Runtime;

/// <summary>What a stored node property holds.</summary>
public enum NodeValueKind : byte
{
    Number,
    Flag,
    Integer,
    Text,
    Vector,
    Clip,
    Mask,
    Avatar,
    Graph,
    Curve,
    /// <summary>A hashed name, stored as text.</summary>
    Id,
    /// <summary>A point or bone the game sets at runtime. Nothing to store but the declaration.</summary>
    Target,
}

/// <summary>One property on a node. The kind says which field carries the value.</summary>
public sealed class NodeValue
{
    public NodeValueKind Kind;
    public float Number;
    public bool Flag;
    public int Integer;
    public string Text = string.Empty;
    public Float3 Vector;
    public AssetRef<AnimationClip> Clip;
    public AssetRef<AvatarMask> Mask;
    public AssetRef<Avatar> Avatar;
    public AssetRef<AnimationGraph> Graph;
    public AnimationCurve? Curve;

    public static NodeValue FromNumber(float value) => new() { Kind = NodeValueKind.Number, Number = value };
    public static NodeValue FromFlag(bool value) => new() { Kind = NodeValueKind.Flag, Flag = value };
    public static NodeValue FromInteger(int value) => new() { Kind = NodeValueKind.Integer, Integer = value };
    public static NodeValue FromText(string value) => new() { Kind = NodeValueKind.Text, Text = value };
    public static NodeValue FromVector(Float3 value) => new() { Kind = NodeValueKind.Vector, Vector = value };
    public static NodeValue FromClip(AssetRef<AnimationClip> value) => new() { Kind = NodeValueKind.Clip, Clip = value };
    public static NodeValue FromMask(AssetRef<AvatarMask> value) => new() { Kind = NodeValueKind.Mask, Mask = value };
    public static NodeValue FromAvatar(AssetRef<Avatar> value) => new() { Kind = NodeValueKind.Avatar, Avatar = value };
    public static NodeValue FromGraph(AssetRef<AnimationGraph> value) => new() { Kind = NodeValueKind.Graph, Graph = value };
    public static NodeValue FromCurve(AnimationCurve value) => new() { Kind = NodeValueKind.Curve, Curve = value };

    public NodeValue Clone() => (NodeValue)MemberwiseClone();
}

/// <summary>One wire into a node, and what the input carries besides, such as a threshold or a weight.</summary>
public sealed class GraphInputRecord
{
    /// <summary>The node id feeding this pin. Empty means nothing is wired here.</summary>
    public string Node = string.Empty;

    /// <summary>The number this pin carries alongside the wire: a threshold, a weight, a share.</summary>
    public float Value = 1f;

    /// <summary>Where this sample sits, for a 2D blend space.</summary>
    public Float2 Position;

    /// <summary>A per input flag, such as whether a layer is additive.</summary>
    public bool Flag;

    /// <summary>A per input name, such as the channel a driver writes or an IK effector's name.</summary>
    public string Name = string.Empty;

    /// <summary>Extra text for the few inputs that need it, such as an IK effector's bone chain.</summary>
    public string Text = string.Empty;

    public GraphInputRecord() { }

    public GraphInputRecord(string node) => Node = node;

    public GraphInputRecord(string node, float value) { Node = node; Value = value; }

    public GraphInputRecord Clone() => (GraphInputRecord)MemberwiseClone();
}

/// <summary>A node in a graph asset: what kind it is, what feeds it by id, and how it is configured.</summary>
public sealed class GraphNodeRecord
{
    public string Id = string.Empty;

    public string Type = string.Empty;

    /// <summary>Optional name, which makes the node findable at runtime.</summary>
    public string Name = string.Empty;

    /// <summary>What the node is called to a reader: its name, else its type's.</summary>
    public string Title => Name.Length > 0 ? Name : AnimationNodeRegistry.Get(Type)?.DisplayName ?? Type;

    /// <summary>The state or embedded sub graph this node belongs to, or empty for the graph itself.</summary>
    public string Owner = string.Empty;

    public List<GraphInputRecord> Inputs = new();

    public Dictionary<string, NodeValue> Properties = new();

    /// <summary>Inputs by name: settings exposed as ports, and a sub graph's parameters, with the node wired to each.</summary>
    public Dictionary<string, string> PropertyInputs = new();

    /// <summary>The states of a state machine node. Empty on every other kind.</summary>
    public List<GraphStateRecord> States = new();

    /// <summary>Where the node sits in the editor. The compiler ignores it.</summary>
    public Float2 EditorPosition;

    public bool Collapsed;

    /// <summary>Wires a node into the next free pin and returns the entry, for configuring in place.</summary>
    public GraphInputRecord Wire(string nodeId)
    {
        var input = new GraphInputRecord(nodeId);
        Inputs.Add(input);
        return input;
    }

    /// <summary>A deep copy, ids and wires untouched.</summary>
    public GraphNodeRecord Clone()
    {
        var copy = (GraphNodeRecord)MemberwiseClone();
        copy.Inputs = Inputs.ConvertAll(input => input.Clone());
        copy.Properties = new Dictionary<string, NodeValue>(Properties.Count);
        foreach (KeyValuePair<string, NodeValue> property in Properties)
            copy.Properties[property.Key] = property.Value.Clone();
        copy.PropertyInputs = new Dictionary<string, string>(PropertyInputs);
        copy.States = States.ConvertAll(state => state.Clone());
        return copy;
    }
}

/// <summary>One state of a state machine: its own nodes, owned by its id, or a graph asset.</summary>
public sealed class GraphStateRecord
{
    /// <summary>Unique within the graph, and what the state's embedded nodes name as their owner.</summary>
    public string Id = string.Empty;
    public string Name = string.Empty;

    /// <summary>A graph asset this state plays instead of its embedded nodes, when set.</summary>
    public AssetRef<AnimationGraph> Graph;

    public bool IsDefault;

    /// <summary>The machine's Any State, whose transitions leave every other state on their target's Enter.</summary>
    public bool IsAny;

    public List<GraphTransitionRecord> Transitions = new();
    public Float2 EditorPosition;

    public bool UsesAsset => !Graph.IsExplicitNull;

    public GraphStateRecord Clone()
    {
        var copy = (GraphStateRecord)MemberwiseClone();
        copy.Transitions = Transitions.ConvertAll(transition => transition.Clone());
        return copy;
    }
}

/// <summary>A transition, which fires when the state it leaves lets go and the state it leads to wants in.</summary>
public sealed class GraphTransitionRecord
{
    public string To = string.Empty;
    public float Duration = 0.2f;
    public TransitionEasing Easing = TransitionEasing.Linear;
    public TransitionSync Sync = TransitionSync.Frozen;

    /// <summary>Keeps the blend from outlasting what is left of the state being left.</summary>
    public bool ClampToSource;

    /// <summary>Lets this transition cut in while its target is still blending in from another.</summary>
    public bool CanInterrupt;

    public GraphTransitionRecord Clone() => (GraphTransitionRecord)MemberwiseClone();
}

/// <summary>A titled box drawn behind a set of nodes. Editor only, the compiler never reads it.</summary>
public sealed class GraphGroupRecord
{
    public string Id = string.Empty;
    /// <summary>The state machine node this sits inside, or empty for the graph itself.</summary>
    public string Owner = string.Empty;
    public string Title = "Group";
    public Float2 Position;
    public Float2 Size = new(260f, 180f);

    public GraphGroupRecord Clone() => (GraphGroupRecord)MemberwiseClone();
}

/// <summary>A note left on the canvas. Editor only, the compiler never reads it.</summary>
public sealed class GraphNoteRecord
{
    public string Id = string.Empty;
    /// <summary>The state machine node this sits inside, or empty for the graph itself.</summary>
    public string Owner = string.Empty;
    public string Text = string.Empty;
    public Float2 Position;
    public Float2 Size = new(190f, 130f);

    public GraphNoteRecord Clone() => (GraphNoteRecord)MemberwiseClone();
}

/// <summary>A value the game sets on the animator each frame, which nodes read.</summary>
public sealed class GraphParameterRecord
{
    public string Name = string.Empty;
    public NodeValueKind Kind = NodeValueKind.Number;
    public float Number;
    public bool Flag;

    /// <summary>For a flag: turns itself off once a transition fires on it.</summary>
    public bool Trigger;

    public int Integer;
    public Float3 Vector;
    public string Text = string.Empty;

    public GraphParameterRecord Clone() => (GraphParameterRecord)MemberwiseClone();
}
