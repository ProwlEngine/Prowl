// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>An input pin, declared once and used both to describe a node type and to read what is wired to it.</summary>
public sealed class InputPin
{
    public InputPin(string name, NodePinKind kind = NodePinKind.Pose, bool optional = true, bool variadic = false)
    {
        Name = name; Kind = kind; Optional = optional; Variadic = variadic;
    }

    public string Name { get; }
    public NodePinKind Kind { get; }

    /// <summary>True when the node works without this pin wired. A required pin compiles to a neutral constant.</summary>
    public bool Optional { get; }

    /// <summary>Part of the repeating group of pins at the end of the node.</summary>
    public bool Variadic { get; }

    /// <summary>Where the pin sits on its node, set when the node type is defined.</summary>
    public int Index { get; internal set; } = -1;

    /// <summary>Where the pin sits within its repeating group, or -1 when it does not repeat.</summary>
    public int GroupOffset { get; internal set; } = -1;
}

/// <summary>A setting on a node type: the key it is saved under, the label shown for it, and what it starts as.</summary>
public abstract class NodeSetting
{
    protected NodeSetting(string key, string? label)
    {
        Key = key;
        Label = label ?? key;
    }

    /// <summary>What the value is saved under. Never changes once graphs use it.</summary>
    public string Key { get; }

    /// <summary>What the editor shows, which may change freely.</summary>
    public string Label { get; }

    public abstract NodeValueKind Kind { get; }

    /// <summary>Whether the setting can be exposed as an input port.</summary>
    public virtual bool Drivable => false;

    /// <summary>The names a text setting picks between, or null for free text.</summary>
    public virtual string[]? Choices => null;

    /// <summary>A fresh value holding the default.</summary>
    public virtual NodeValue CreateValue() => new() { Kind = Kind };

    protected NodeValue? Stored(GraphNodeRecord record) => record.Properties.TryGetValue(Key, out NodeValue? value) ? value : null;
}

/// <summary>A setting read as one kind of value.</summary>
public abstract class NodeSetting<T> : NodeSetting
{
    protected NodeSetting(string key, string? label) : base(key, label) { }

    public abstract T Read(GraphNodeRecord record);
}

/// <summary>A number, which a drivable one lets the editor expose as an input port.</summary>
public sealed class NumberSetting(string key, float value = 0f, bool drivable = false, string? label = null) : NodeSetting<float>(key, label)
{
    public float Default { get; } = value;
    public override NodeValueKind Kind => NodeValueKind.Number;
    public override bool Drivable { get; } = drivable;

    public override float Read(GraphNodeRecord record) => Stored(record)?.Number ?? Default;

    public override NodeValue CreateValue() => NodeValue.FromNumber(Default);
}

public sealed class FlagSetting(string key, bool value = false, string? label = null) : NodeSetting<bool>(key, label)
{
    public bool Default { get; } = value;
    public override NodeValueKind Kind => NodeValueKind.Flag;

    public override bool Read(GraphNodeRecord record) => Stored(record)?.Flag ?? Default;

    public override NodeValue CreateValue() => NodeValue.FromFlag(Default);
}

public sealed class IntegerSetting(string key, int value = 0, string? label = null) : NodeSetting<int>(key, label)
{
    public int Default { get; } = value;
    public override NodeValueKind Kind => NodeValueKind.Integer;

    public override int Read(GraphNodeRecord record) => Stored(record)?.Integer ?? Default;

    public override NodeValue CreateValue() => NodeValue.FromInteger(Default);
}

/// <summary>One of an enum's values, saved by name.</summary>
public sealed class ChoiceSetting<T>(string key, T value, string? label = null) : NodeSetting<T>(key, label) where T : struct, Enum
{
    private static readonly string[] s_names = Enum.GetNames<T>();

    public T Default { get; } = value;
    public override NodeValueKind Kind => NodeValueKind.Text;
    public override string[] Choices => s_names;

    public override T Read(GraphNodeRecord record)
        => Stored(record) is { } stored && Enum.TryParse(stored.Text, ignoreCase: true, out T parsed) ? parsed : Default;

    public override NodeValue CreateValue() => NodeValue.FromText(Default.ToString());
}

public sealed class VectorSetting(string key, Float3 value = default, string? label = null) : NodeSetting<Float3>(key, label)
{
    public Float3 Default { get; } = value;
    public override NodeValueKind Kind => NodeValueKind.Vector;

    public override Float3 Read(GraphNodeRecord record) => Stored(record)?.Vector ?? Default;

    public override NodeValue CreateValue() => NodeValue.FromVector(Default);
}

/// <summary>Text: free text, a name, a bone, or a comma separated list of them.</summary>
public sealed class TextSetting(string key, NodeValueKind kind = NodeValueKind.Text, string? label = null) : NodeSetting<string>(key, label)
{
    public override NodeValueKind Kind { get; } = kind;

    /// <summary>Whether it names a bone, or a list of them, so the editor offers the rig's own.</summary>
    public bool Bone { get; init; }

    /// <summary>Whether it is a list, which the editor edits a row at a time.</summary>
    public bool List { get; init; }

    /// <summary>A list whose entries pair up with this one's, edited side by side.</summary>
    public TextSetting? PairedWith { get; init; }

    public override string Read(GraphNodeRecord record) => Stored(record)?.Text ?? string.Empty;
}

/// <summary>A reference to an asset: a clip, a mask, an avatar, a graph or a curve.</summary>
public sealed class AssetSetting(string key, NodeValueKind kind, string? label = null) : NodeSetting<NodeValue?>(key, label)
{
    public override NodeValueKind Kind { get; } = kind;

    public override NodeValue? Read(GraphNodeRecord record) => Stored(record);
}

/// <summary>A number read from a pin when something is wired to it, and from a setting on the node when not.</summary>
public sealed class WiredNumber : NodeSetting
{
    public WiredNumber(string pin, float value, string fallbackKey = "Default", string? fallbackLabel = null) : base(fallbackKey, fallbackLabel)
    {
        Pin = new InputPin(pin, NodePinKind.Number);
        Fallback = new NumberSetting(fallbackKey, value, label: fallbackLabel);
    }

    public InputPin Pin { get; }
    public NumberSetting Fallback { get; }
    public override NodeValueKind Kind => NodeValueKind.Number;
}

public static class NodeSettingReading
{
    /// <summary>What a setting reads on a node, or its default when the node has none saved.</summary>
    public static T Get<T>(this GraphNodeRecord record, NodeSetting<T> setting) => setting.Read(record);
}
