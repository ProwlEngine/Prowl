// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>A kind of graph node, found by the registry. Declare pins and settings in the constructor, in saved order.</summary>
public abstract class AnimationGraphNode
{
    private readonly List<InputPin> _pins = new();
    private readonly List<NodeSetting> _settings = new();
    private readonly Dictionary<string, NodeSetting> _settingsByKey = new();
    private int _variadicStart = -1;

    /// <summary>What graphs save the node as. Never changes once graphs use it.</summary>
    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    /// <summary>The group the editor lists the node under.</summary>
    public abstract string Category { get; }

    public virtual string Description => string.Empty;

    /// <summary>What the node produces. Only a pose node can be the graph's root.</summary>
    public virtual NodePinKind Output => NodePinKind.Pose;

    /// <summary>What one particular node produces, when that depends on how it is set.</summary>
    public virtual NodePinKind OutputOf(AnimationGraph? asset, GraphNodeRecord record) => Output;

    public bool IsPose => Output == NodePinKind.Pose;

    /// <summary>True when the node only works on a humanoid rig.</summary>
    public virtual bool RequiresHumanoid => false;

    /// <summary>A node the editor places itself rather than offering in its menu.</summary>
    public virtual bool Hidden => false;

    /// <summary>Inputs taken by name, such as a sub graph's parameters, wired through <see cref="GraphNodeRecord.PropertyInputs"/>.</summary>
    public virtual IReadOnlyList<InputPin> NamedInputs(GraphNodeRecord record) => Array.Empty<InputPin>();

    /// <summary>Builds the Motion node and returns its index in the graph being compiled.</summary>
    public abstract int Build(GraphCompileContext ctx, GraphNodeRecord record);

    public IReadOnlyList<InputPin> Inputs => _pins;

    /// <summary>The settings as the editor edits them, a wired number by its fallback.</summary>
    public IReadOnlyList<NodeSetting> Properties => _settings;

    /// <summary>Where the repeating group starts, or the pin count when the node is fixed arity.</summary>
    public int VariadicStart => _variadicStart >= 0 ? _variadicStart : _pins.Count;

    /// <summary>How many pins make up one repetition, or zero when the node is fixed arity.</summary>
    public int VariadicStride => _pins.Count - VariadicStart;

    /// <summary>Whether a pin is shown for how the node is set.</summary>
    public bool ShowsPin(GraphNodeRecord record, int pin) => PinAt(pin) is not { } found || PinShown(record, found);

    /// <summary>Whether a setting is shown for how the node is set.</summary>
    public bool ShowsProperty(GraphNodeRecord record, string key)
        => !_settingsByKey.TryGetValue(key, out NodeSetting? setting) || SettingShown(record, setting);

    protected virtual bool PinShown(GraphNodeRecord record, InputPin pin) => true;

    protected virtual bool SettingShown(GraphNodeRecord record, NodeSetting setting) => true;

    /// <summary>Adds input pins, in the order graphs save them. The repeating group, if any, comes last.</summary>
    protected void Pins(params InputPin[] pins)
    {
        foreach (InputPin pin in pins)
        {
            pin.Index = _pins.Count;
            if (pin.Variadic && _variadicStart < 0) _variadicStart = pin.Index;
            pin.GroupOffset = _variadicStart >= 0 ? pin.Index - _variadicStart : -1;
            _pins.Add(pin);
        }
    }

    /// <summary>Adds settings, in the order the editor shows them.</summary>
    protected void Settings(params NodeSetting[] settings)
    {
        foreach (NodeSetting setting in settings)
        {
            _settings.Add(setting is WiredNumber wired ? wired.Fallback : setting);
            _settingsByKey[setting.Key] = setting;
        }
    }

    /// <summary>The pin at an index. Past the declared pins the repeating group carries on, or else the last pin does.</summary>
    public InputPin? PinAt(int index)
    {
        if (index < 0 || _pins.Count == 0) return null;
        if (index < _pins.Count) return _pins[index];
        return _variadicStart < 0 ? _pins[^1] : _pins[_variadicStart + (index - _variadicStart) % VariadicStride];
    }

    /// <summary>An input count rounded up to whole repetitions of the repeating group.</summary>
    public int WholeGroups(int count)
    {
        int stride = VariadicStride;
        if (stride <= 0 || count <= VariadicStart) return count;
        return VariadicStart + (count - VariadicStart + stride - 1) / stride * stride;
    }

    /// <summary>The kind of pin that carries a value of this kind.</summary>
    public static NodePinKind PinKindOf(NodeValueKind kind) => kind switch
    {
        NodeValueKind.Flag => NodePinKind.Flag,
        NodeValueKind.Integer => NodePinKind.Integer,
        NodeValueKind.Vector => NodePinKind.Vector,
        NodeValueKind.Id => NodePinKind.Id,
        NodeValueKind.Target => NodePinKind.Target,
        _ => NodePinKind.Number,
    };

    protected static InputPin PoseInput(string name = "Pose", bool variadic = false) => new(name, NodePinKind.Pose, variadic: variadic);
    protected static InputPin NumberInput(string name, bool optional = true, bool variadic = false) => new(name, NodePinKind.Number, optional, variadic);
    protected static InputPin FlagInput(string name, bool optional = true, bool variadic = false) => new(name, NodePinKind.Flag, optional, variadic);
    protected static InputPin IntegerInput(string name, bool optional = true) => new(name, NodePinKind.Integer, optional);
    protected static InputPin VectorInput(string name, bool optional = false) => new(name, NodePinKind.Vector, optional);
    protected static InputPin MaskInput(string name, bool optional = true, bool variadic = false) => new(name, NodePinKind.Mask, optional, variadic);
    protected static InputPin TargetInput(bool variadic = false) => new("Target", NodePinKind.Target, optional: variadic, variadic);
    protected static InputPin NameInput(string name) => new(name, NodePinKind.Id, optional: false);

    protected static NumberSetting Number(string key, float value = 0f, string? label = null) => new(key, value, label: label);

    /// <summary>A number that can be exposed as an input port.</summary>
    protected static NumberSetting Driven(string key, float value = 0f, string? label = null) => new(key, value, drivable: true, label: label);
    protected static FlagSetting Toggle(string key, bool value = false, string? label = null) => new(key, value, label);
    protected static IntegerSetting Count(string key, int value = 0) => new(key, value);
    protected static ChoiceSetting<T> Choice<T>(string key, T value) where T : struct, Enum => new(key, value);
    protected static TextSetting Name(string key, string? label = null) => new(key, NodeValueKind.Id, label);
    protected static TextSetting BoneName(string key) => new(key, NodeValueKind.Id) { Bone = true };

    /// <summary>A list of bones, written as names or "bone:weight" pairs.</summary>
    protected static TextSetting BoneList(string key) => new(key) { Bone = true, List = true };
    protected static TextSetting TextList(string key) => new(key) { List = true };
    protected static AssetSetting Asset(string key, NodeValueKind kind) => new(key, kind);
}
