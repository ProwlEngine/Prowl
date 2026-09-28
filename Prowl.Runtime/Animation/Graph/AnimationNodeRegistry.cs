// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Prowl.Runtime.AnimationNodes;
using Prowl.Vector;

using MotionAvatar = Prowl.Motion.Avatar;
using MotionGraph = Prowl.Motion.AnimationGraph;
using MotionSkeleton = Prowl.Motion.Skeleton;
using StringID = Prowl.Motion.StringID;

namespace Prowl.Runtime;

/// <summary>What flows down a pin.</summary>
public enum NodePinKind : byte
{
    Pose,
    Number,
    Flag,
    Integer,
    Vector,
    /// <summary>A point or a bone to reach for.</summary>
    Target,
    /// <summary>A per bone weighting, built by the bone mask nodes.</summary>
    Mask,
    /// <summary>A hashed name.</summary>
    Id,
}

/// <summary>Every <see cref="AnimationGraphNode"/> in the loaded assemblies, built in or a project's own.</summary>
public static class AnimationNodeRegistry
{
    private static readonly Dictionary<string, AnimationGraphNode> s_nodes = new();
    private static readonly List<AnimationGraphNode> s_ordered = new();
    private static bool s_scanned;

    /// <summary>Every node type, by category then name.</summary>
    public static IReadOnlyList<AnimationGraphNode> All { get { EnsureScanned(); return s_ordered; } }

    public static AnimationGraphNode? Get(string id)
    {
        EnsureScanned();
        return s_nodes.TryGetValue(id, out AnimationGraphNode? node) ? node : null;
    }

    /// <summary>Forgets every node type, so the next lookup finds them again in the loaded assemblies.</summary>
    public static void Reset()
    {
        s_scanned = false;
        s_nodes.Clear();
        s_ordered.Clear();
        AnimationGraph.NodeTypesChanged();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Scans loaded assemblies for AnimationGraphNode subclasses, which the application's trim configuration must keep.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072:DynamicallyAccessedMembers",
        Justification = "AnimationGraphNode subclasses expose a parameterless constructor by contract.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075:DynamicallyAccessedMembers",
        Justification = "AnimationGraphNode subclasses expose a parameterless constructor by contract.")]
    private static void EnsureScanned()
    {
        if (s_scanned) return;
        s_scanned = true;

        foreach (Type type in RuntimeUtils.FindTypesImplementing(typeof(AnimationGraphNode)))
        {
            if (type.GetConstructor(Type.EmptyTypes) == null) continue;

            AnimationGraphNode node;
            try { node = (AnimationGraphNode)Activator.CreateInstance(type)!; }
            catch (Exception ex)
            {
                Debug.LogError($"[AnimationGraph] Could not create the node type {type.FullName}: {ex.InnerException?.Message ?? ex.Message}");
                continue;
            }

            if (s_nodes.TryGetValue(node.Id, out AnimationGraphNode? existing))
            {
                Debug.LogWarning($"[AnimationGraph] {type.FullName} and {existing.GetType().FullName} both use the node id '{node.Id}'; ignoring {type.FullName}.");
                continue;
            }
            s_nodes.Add(node.Id, node);
            s_ordered.Add(node);
        }

        s_ordered.Sort((a, b) =>
        {
            int byCategory = string.CompareOrdinal(a.Category, b.Category);
            return byCategory != 0 ? byCategory : string.CompareOrdinal(a.DisplayName, b.DisplayName);
        });
    }
}

/// <summary>One graph compile: the Motion graph being filled in, and the lookups node builders use.</summary>
public sealed class GraphCompileContext
{
    private static readonly char[] s_listSeparators = { ',', ';' };

    private readonly AnimationGraph _asset;
    private readonly Dictionary<string, GraphNodeRecord> _records;
    private readonly Dictionary<string, int> _compiled = new();
    private readonly HashSet<string> _compiling = new();
    private readonly HashSet<AnimationGraph> _visiting;

    // Names slots and virtual parameters are found by, which a node's own name never takes.
    private readonly HashSet<string> _lookupNames = new();

    internal GraphCompileContext(AnimationGraph asset, MotionGraph graph, MotionSkeleton skeleton, MotionAvatar? avatar, HashSet<AnimationGraph> visiting)
    {
        _asset = asset;
        _visiting = visiting;
        Graph = graph;
        Skeleton = skeleton;
        Avatar = avatar;

        _records = new Dictionary<string, GraphNodeRecord>(asset.Nodes.Count);
        foreach (GraphNodeRecord node in asset.Nodes)
        {
            _records.TryAdd(node.Id, node);
            string lookup = node.Type switch
            {
                AnimationNodeIds.ExternalGraphSlot => node.Get(ExternalGraphSlotNode.Slot),
                AnimationNodeIds.VirtualParameter => node.Get(ParameterNode.NameSetting),
                _ => string.Empty,
            };
            if (lookup.Length > 0) _lookupNames.Add(lookup);
        }
    }

    /// <summary>
    /// Whether a name a node is looked up by is still free. A second slot or virtual parameter of the
    /// same name is reported and left without one.
    /// </summary>
    public bool ClaimName(GraphNodeRecord record, string name)
    {
        if (Graph.GetNodeIndex(name) < 0) return true;
        Debug.LogWarning($"[AnimationGraph] '{_asset.Name}' has more than one node looked up as '{name}', so a {record.Type} node is left out.");
        return false;
    }

    /// <summary>A context for a state's graph asset compiled into the same Motion graph.</summary>
    private GraphCompileContext(GraphCompileContext parent, AnimationGraph asset)
        : this(asset, parent.Graph, parent.Skeleton, parent.Avatar, parent._visiting)
    {
        Parameters = parent.Parameters;
        _machines = parent._machines;
    }

    public MotionGraph Graph { get; }
    public MotionSkeleton Skeleton { get; }
    public MotionAvatar? Avatar { get; }

    /// <summary>Parameter node index per declared parameter name.</summary>
    public Dictionary<string, int> Parameters { get; } = new();

    private readonly Stack<int> _machines = new();

    /// <summary>The state machine whose state is being built, or -1 outside any.</summary>
    public int CurrentMachine => _machines.Count > 0 ? _machines.Peek() : -1;

    /// <summary>Adds the graph's parameters, leaving any name already declared as it is.</summary>
    public void DeclareParameters(IEnumerable<GraphParameterRecord> parameters)
    {
        foreach (GraphParameterRecord parameter in parameters)
        {
            if (parameter.Name.Length == 0 || Parameters.ContainsKey(parameter.Name)) continue;
            Parameters[parameter.Name] = parameter.Kind switch
            {
                NodeValueKind.Flag when parameter.Trigger => Graph.AddTriggerParameter(parameter.Name),
                NodeValueKind.Flag => Graph.AddBoolParameter(parameter.Name, parameter.Flag),
                NodeValueKind.Integer => Graph.AddIntParameter(parameter.Name, parameter.Integer),
                NodeValueKind.Vector => Graph.AddVectorParameter(parameter.Name, parameter.Vector),
                NodeValueKind.Id => Graph.AddIdParameter(parameter.Name, parameter.Text.Length > 0 ? new StringID(parameter.Text) : default),
                NodeValueKind.Target => Graph.AddTargetParameter(parameter.Name),
                _ => Graph.AddFloatParameter(parameter.Name, parameter.Number),
            };
        }
    }

    /// <summary>The Virtual Parameter node that names a value, which a Parameter node reads like a declared one.</summary>
    public GraphNodeRecord? VirtualParameter(string name)
    {
        if (name.Length == 0) return null;
        foreach (GraphNodeRecord record in _asset.Nodes)
            if (record.Type == AnimationNodeIds.VirtualParameter && record.Get(ParameterNode.NameSetting) == name) return record;
        return null;
    }

    public AnimationGraph Asset => _asset;

    /// <summary>What a state gives its machine: the pose, and the two gates a transition reads.</summary>
    internal StateEnds State(int machine, GraphStateRecord state)
    {
        _machines.Push(machine);
        try
        {
            if (!state.UsesAsset)
                return Ends(_asset.OwnedNode(state.Id, AnimationNodeIds.StateOutput));

            AnimationGraph? asset = state.Graph.Res;
            if (asset.IsNotValid()) return StateEnds.Empty;
            if (_visiting.Contains(asset!))
            {
                Debug.LogError($"[AnimationGraph] The state '{state.Name}' plays '{asset!.Name}', which leads back to itself.");
                return StateEnds.Empty;
            }

            _visiting.Add(asset!);
            try
            {
                var inner = new GraphCompileContext(this, asset!);
                inner.DeclareParameters(asset!.Parameters);

                // A graph without a state output plays its root and is never entered by a transition.
                GraphNodeRecord? output = asset.OwnedNode(string.Empty, AnimationNodeIds.StateOutput);
                return output != null ? inner.Ends(output) : new StateEnds(inner.Node(asset.RootNode), Gate.Closed, Gate.Open);
            }
            finally
            {
                _visiting.Remove(asset!);
            }
        }
        finally
        {
            _machines.Pop();
        }
    }

    private StateEnds Ends(GraphNodeRecord? output)
    {
        if (output == null) return StateEnds.Empty;
        return new StateEnds(Node(output.Id), GateOf(output, StateOutputNode.Enter, false), GateOf(output, StateOutputNode.Exit, true));
    }

    /// <summary>A bool pin of a state output: the node wired to it, or the value its toggle is set to.</summary>
    private Gate GateOf(GraphNodeRecord record, InputPin gate, bool fallback)
    {
        if (gate.Index >= record.Inputs.Count) return fallback ? Gate.Open : Gate.Closed;

        GraphInputRecord input = record.Inputs[gate.Index];
        if (input.Node.Length == 0) return input.Flag ? Gate.Open : Gate.Closed;

        int node = Node(input.Node);
        return node < 0 ? Gate.Closed : new Gate(node);
    }

    /// <summary>The condition a transition fires on: -1 for always, null for never, or the node reading both gates.</summary>
    internal int? Both(Gate leave, Gate enter)
    {
        if (leave.IsClosed || enter.IsClosed) return null;
        if (leave.IsOpen) return enter.IsOpen ? -1 : enter.Node;
        if (enter.IsOpen) return leave.Node;
        return Graph.AddAnd(leave.Node, enter.Node);
    }

    /// <summary>Where every record ended up in the compiled graph.</summary>
    internal Dictionary<string, int> Compiled => _compiled;

    /// <summary>The Motion state index of each of a machine's states, in the record's own order.</summary>
    internal Dictionary<string, int[]> States { get; } = new();

    /// <summary>Makes a node findable while it is still being built, so its own children can refer to it.</summary>
    public void Publish(GraphNodeRecord record, int index) => _compiled[record.Id] = index;

    public void MapStates(GraphNodeRecord record, int[] stateIndices) => States[record.Id] = stateIndices;

    /// <summary>The Motion index of a node, compiled on first use. -1 for an empty or unknown id, or a cycle.</summary>
    public int Node(string? id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        if (_compiled.TryGetValue(id, out int index)) return index;

        if (!_records.TryGetValue(id, out GraphNodeRecord? record))
        {
            Debug.LogWarning($"[AnimationGraph] '{_asset.Name}' references a node '{id}' it does not have.");
            return -1;
        }

        if (!_compiling.Add(id))
        {
            Debug.LogError($"[AnimationGraph] '{_asset.Name}' has a cycle through node '{id}'.");
            return -1;
        }

        try
        {
            AnimationGraphNode? type = AnimationNodeRegistry.Get(record.Type);
            if (type == null)
            {
                Debug.LogWarning($"[AnimationGraph] '{_asset.Name}' uses an unknown node type '{record.Type}'.");
                return -1;
            }

            index = type.Build(this, record);

            // Names are unique, so a second node wanting a taken name goes without.
            if (index >= 0 && record.Name.Length > 0 && !_lookupNames.Contains(record.Name))
            {
                int owner = Graph.GetNodeIndex(record.Name);
                if (owner < 0 || owner == index) Graph.NameNode(index, record.Name);
            }
            _compiled[id] = index;
            return index;
        }
        finally
        {
            _compiling.Remove(id);
        }
    }

    /// <summary>The node feeding one pin. Unwired, a required pin gets a neutral constant and an optional one -1.</summary>
    public int Input(GraphNodeRecord record, InputPin pin) => InputAt(record, pin.Index);

    /// <summary>The node feeding one pin of a repeating group.</summary>
    public int Input(in PinGroup group, InputPin pin) => InputAt(group.Record, group.FirstInput + pin.GroupOffset);

    private int InputAt(GraphNodeRecord record, int pin)
    {
        int index = pin >= 0 && pin < record.Inputs.Count ? Node(record.Inputs[pin].Node) : -1;
        if (index >= 0) return index;

        InputPin? described = AnimationNodeRegistry.Get(record.Type)?.PinAt(pin);
        if (described?.Kind == NodePinKind.Pose) return Graph.AddReferencePose();
        if (described == null || described.Optional) return -1;

        return described.Kind switch
        {
            NodePinKind.Flag => Graph.AddConstBool(false),
            NodePinKind.Integer => Graph.AddConstInt(0),
            NodePinKind.Vector => Graph.AddConstVector(Float3.Zero),
            NodePinKind.Target => UnsetTarget(),
            NodePinKind.Id => Graph.AddConstId(default),
            NodePinKind.Mask => Graph.AddFixedWeightBoneMask(1f),
            _ => Graph.AddConstFloat(0f),
        };
    }

    public bool IsWired(GraphNodeRecord record, InputPin pin)
        => pin.Index >= 0 && pin.Index < record.Inputs.Count && record.Inputs[pin.Index].Node.Length > 0;

    /// <summary>True when the node wired to a pin is of the given type. Warns when something else is wired there.</summary>
    public bool ExpectChild(GraphNodeRecord record, InputPin pin, string typeId, string expected)
    {
        if (!IsWired(record, pin)) return false;
        if (_records.TryGetValue(record.Inputs[pin.Index].Node, out GraphNodeRecord? wired) && wired.Type == typeId) return true;

        Debug.LogWarning($"[AnimationGraph] '{_asset.Name}': a {record.Type} node needs {expected} wired to it, so it does nothing.");
        return false;
    }

    /// <summary>What the node with this id outputs, or null for an id the graph does not have.</summary>
    public NodePinKind? OutputKind(string id)
        => _records.TryGetValue(id, out GraphNodeRecord? record) ? AnimationNodeRegistry.Get(record.Type)?.OutputOf(_asset, record) : null;

    /// <summary>Every input from <paramref name="first"/> on, for a node that takes a list.</summary>
    public int[] Inputs(GraphNodeRecord record, InputPin first)
    {
        int count = Math.Max(0, record.Inputs.Count - first.Index);
        var indices = new int[count];
        for (int i = 0; i < count; i++)
            indices[i] = InputAt(record, first.Index + i);
        return indices;
    }

    /// <summary>The wired repetitions of the group at the end of a node, in order.</summary>
    public List<PinGroup> Groups(GraphNodeRecord record)
    {
        var groups = new List<PinGroup>();
        AnimationGraphNode? type = AnimationNodeRegistry.Get(record.Type);
        int start = type?.VariadicStart ?? 0;
        int stride = Math.Max(1, type?.VariadicStride ?? 1);

        for (int i = start; i + stride <= record.Inputs.Count; i += stride)
            if (record.Inputs[i].Node.Length > 0) groups.Add(new PinGroup(record, i));
        return groups;
    }

    /// <summary>A number setting: whatever is wired to it when exposed as an input, or the value typed on the node.</summary>
    public Motion.FloatInput Get(GraphNodeRecord record, NumberSetting setting)
    {
        float typed = record.Get(setting);
        if (!record.PropertyInputs.TryGetValue(setting.Key, out string? source) || source.Length == 0)
            return Motion.FloatInput.Of(typed);

        int node = Node(source);
        return node < 0 ? Motion.FloatInput.Of(typed) : Motion.FloatInput.From(node, typed);
    }

    /// <summary>A number read from its pin when wired, and from the node's own setting when not.</summary>
    public Motion.FloatInput Get(GraphNodeRecord record, WiredNumber number)
    {
        float typed = record.Get(number.Fallback);
        int node = IsWired(record, number.Pin) ? Input(record, number.Pin) : -1;
        return node < 0 ? Motion.FloatInput.Of(typed) : Motion.FloatInput.From(node, typed);
    }

    /// <summary>A name setting as a hashed id, or the empty id when unset.</summary>
    public Motion.StringID Id(GraphNodeRecord record, TextSetting setting)
    {
        string text = record.Get(setting);
        return text.Length > 0 ? new Motion.StringID(text) : default;
    }

    /// <summary>The bone a setting names, which may be a humanoid bone rather than one of this rig's.</summary>
    public Motion.StringID BoneId(GraphNodeRecord record, TextSetting setting) => BoneName(record.Get(setting));

    /// <summary>The index of the bone a setting names, or -1 when this rig has no such bone.</summary>
    public int BoneIndex(GraphNodeRecord record, TextSetting setting) => Skeleton.GetBoneIndex(BoneId(record, setting));

    /// <summary>The bone a name stands for. "@LeftFoot" is a humanoid bone found through the rig's mapping.</summary>
    public Motion.StringID BoneName(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return default;
        if (name[0] != HumanoidBonePrefix) return new Motion.StringID(name);

        if (!System.Enum.TryParse(name[1..], ignoreCase: true, out Motion.HumanBodyBone bone)) return default;

        Motion.HumanoidRig? rig = Avatar?.Humanoid;
        if (rig == null || !rig.HasBone(bone))
        {
            Debug.LogWarning($"[AnimationGraph] '{_asset.Name}' asks for the humanoid bone '{name[1..]}', which this rig does not map.");
            return default;
        }
        return Skeleton.GetBoneID(rig.GetSkeletonBoneIndex(bone));
    }

    public const char HumanoidBonePrefix = '@';

    /// <summary>A comma separated list of names, for the nodes that match against a set of ids.</summary>
    public Motion.StringID[] Ids(GraphNodeRecord record, TextSetting setting)
    {
        string[] parts = Split(record.Get(setting));
        var ids = new Motion.StringID[parts.Length];
        for (int i = 0; i < parts.Length; i++) ids[i] = new Motion.StringID(parts[i]);
        return ids;
    }

    public float[] Numbers(GraphNodeRecord record, TextSetting setting)
    {
        string[] parts = Split(record.Get(setting));
        var values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i]);
        return values;
    }

    /// <summary>A list of "bone:weight" pairs. A pair with no weight counts as one.</summary>
    public (Motion.StringID Bone, float Weight)[] Bones(GraphNodeRecord record, TextSetting setting)
    {
        string[] parts = Split(record.Get(setting));
        var pairs = new (Motion.StringID, float)[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            (string bone, float weight) = SplitWeight(parts[i]);
            pairs[i] = (BoneName(bone), weight);
        }
        return pairs;
    }

    /// <summary>Bone indices for a comma separated chain of bone names, skipping any the rig lacks.</summary>
    public int[] BoneChain(string names)
    {
        string[] parts = Split(names);
        var chain = new List<int>(parts.Length);
        foreach (string part in parts)
        {
            int bone = Skeleton.GetBoneIndex(BoneName(part));
            if (bone != MotionSkeleton.InvalidIndex) chain.Add(bone);
        }
        return chain.ToArray();
    }

    /// <summary>A clip setting resolved onto the rig being compiled for, or null when it cannot be.</summary>
    public Motion.AnimationClipBase? Clip(GraphNodeRecord record, AssetSetting setting)
    {
        AnimationClip? asset = ClipAsset(record, setting);
        if (asset == null) return null;

        Motion.AnimationClipBase? clip = asset.GetClip(Avatar);
        if (clip == null)
            Debug.LogWarning($"[AnimationGraph] '{_asset.Name}' could not resolve the clip '{asset.Name}' onto this rig.");
        return clip;
    }

    public AnimationClip? ClipAsset(GraphNodeRecord record, AssetSetting setting)
    {
        AnimationClip? asset = record.Get(setting)?.Clip.Res;
        return asset.IsValid() ? asset : null;
    }

    public Motion.BoneMask? Mask(GraphNodeRecord record, AssetSetting setting)
        => record.Get(setting)?.Mask.Res is { } mask && mask.IsValid() ? mask.GetBoneMask(Skeleton) : null;

    public Motion.HumanPoseMask? HumanMask(GraphNodeRecord record, AssetSetting setting)
        => record.Get(setting)?.Mask.Res is { } mask && mask.IsValid() ? mask.GetHumanMask() : null;

    public Avatar? AvatarAsset(GraphNodeRecord record, AssetSetting setting)
    {
        Avatar? asset = record.Get(setting)?.Avatar.Res;
        return asset.IsValid() ? asset : null;
    }

    public AnimationCurve? Curve(GraphNodeRecord record, AssetSetting setting) => record.Get(setting)?.Curve;

    /// <summary>Compiles a referenced graph for the same rig. Null when missing or when it leads back to itself.</summary>
    public MotionGraph? SubGraph(GraphNodeRecord record, AssetSetting setting)
    {
        AnimationGraph? asset = record.Get(setting)?.Graph.Res;
        if (asset.IsNotValid()) return null;

        if (_visiting.Contains(asset!))
        {
            Debug.LogError($"[AnimationGraph] '{_asset.Name}' references '{asset!.Name}', which references it back.");
            return null;
        }
        return asset!.Compile(Skeleton, Avatar, _visiting);
    }

    /// <summary>The unset target parameter every unwired target pin shares.</summary>
    private int UnsetTarget()
    {
        const string name = "$unsetTarget";
        if (Parameters.TryGetValue(name, out int index)) return index;

        index = Graph.AddTargetParameter(name);
        Parameters[name] = index;
        return index;
    }

    /// <summary>A "bone:weight" entry split in two. An entry with no weight counts as one.</summary>
    public static (string Bone, float Weight) SplitWeight(string entry)
    {
        int colon = entry.IndexOf(':');
        if (colon < 0) return (entry, 1f);

        float.TryParse(entry[(colon + 1)..].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float weight);
        return (entry[..colon].Trim(), weight);
    }

    /// <summary>The entries of a list separated by commas or semicolons.</summary>
    public static string[] Split(string text)
        => text.Length == 0 ? Array.Empty<string>() : text.Split(s_listSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>One wired repetition of the pin group at the end of a node.</summary>
public readonly record struct PinGroup(GraphNodeRecord Record, int FirstInput)
{
    /// <summary>The record of the group's first pin, which carries its threshold, weight, name and the like.</summary>
    public GraphInputRecord Entry => Record.Inputs[FirstInput];
}

/// <summary>One of a state's bool pins: fixed open, fixed closed, or a node read each frame.</summary>
internal readonly struct Gate
{
    public static readonly Gate Open = new(-1, true);
    public static readonly Gate Closed = new(-1, false);

    public readonly int Node;
    private readonly bool _value;

    public Gate(int node) : this(node, false) { }

    private Gate(int node, bool value) { Node = node; _value = value; }

    public bool IsOpen => Node < 0 && _value;
    public bool IsClosed => Node < 0 && !_value;
}

/// <summary>What a state gives its machine: its pose, and whether it can be entered and left.</summary>
internal readonly struct StateEnds
{
    public static readonly StateEnds Empty = new(-1, Gate.Closed, Gate.Open);

    public readonly int Pose;
    public readonly Gate Enter;
    public readonly Gate Exit;

    public StateEnds(int pose, Gate enter, Gate exit) { Pose = pose; Enter = enter; Exit = exit; }
}
