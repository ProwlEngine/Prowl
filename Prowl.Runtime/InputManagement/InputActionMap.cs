// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A collection of related input actions that can be enabled/disabled as a group.
/// Extends EngineObject so it can be saved as an asset (.inputactions).
/// </summary>
[CreateAssetMenu("Input Actions", Extension = ".inputactions", Order = 2)]
public class InputActionMap : EngineObject, ISerializable
{
    private readonly Dictionary<string, InputAction> _actions = [];

    /// <summary>All actions in this map.</summary>
    public IReadOnlyCollection<InputAction> Actions => _actions.Values;

    /// <summary>Whether any action in this map is enabled.</summary>
    public bool Enabled => _actions.Values.Any(a => a.Enabled);

    public InputActionMap() : base("New InputActionMap") { }

    public InputActionMap(string name) : base(name) { }

    /// <summary>Adds a new action to this map.</summary>
    public InputAction AddAction(string name, InputActionType type = InputActionType.Button)
    {
        if (_actions.ContainsKey(name))
            throw new ArgumentException($"Action '{name}' already exists in map '{Name}'");

        var action = new InputAction(name, type) { ActionMap = this };
        _actions[name] = action;
        return action;
    }

    /// <summary>Adds an existing action to this map.</summary>
    public void AddAction(InputAction action)
    {
        if (_actions.ContainsKey(action.Name))
            throw new ArgumentException($"Action '{action.Name}' already exists in map '{Name}'");

        action.ActionMap = this;
        _actions[action.Name] = action;
    }

    /// <summary>Finds an action by name.</summary>
    public InputAction? FindAction(string name)
    {
        _actions.TryGetValue(name, out InputAction? action);
        return action;
    }

    /// <summary>Gets an action by name, throws if not found.</summary>
    public InputAction GetAction(string name)
    {
        if (!_actions.TryGetValue(name, out InputAction? action))
            throw new KeyNotFoundException($"Action '{name}' not found in map '{Name}'");
        return action;
    }

    /// <summary>Removes an action from this map.</summary>
    public bool RemoveAction(string name)
    {
        if (_actions.TryGetValue(name, out InputAction? action))
        {
            action.Disable();
            action.ActionMap = null;
            return _actions.Remove(name);
        }
        return false;
    }

    /// <summary>Enables all actions in this map.</summary>
    public void Enable()
    {
        foreach (InputAction action in _actions.Values)
            action.Enable();
    }

    /// <summary>Disables all actions in this map.</summary>
    public void Disable()
    {
        foreach (InputAction action in _actions.Values)
            action.Disable();
    }

    /// <summary>Internal method to update all actions in this map.</summary>
    internal void UpdateActions(IInputHandler inputHandler, float currentTime)
    {
        foreach (InputAction action in _actions.Values)
        {
            if (action.Enabled)
                action.UpdateState(inputHandler, currentTime);
        }
    }

    /// <summary>Indexer to access actions by name.</summary>
    public InputAction this[string name] => GetAction(name);

    public override string ToString() => $"{Name} ({_actions.Count} actions)";

    // ================================================================
    //  Serialization save/load the action map structure as an asset
    // ================================================================

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        compound.Add("Name", new EchoObject(Name));

        var actionsList = EchoObject.NewList();
        foreach (var action in _actions.Values)
        {
            var actionTag = EchoObject.NewCompound();
            actionTag["name"] = new EchoObject(action.Name);
            actionTag["type"] = new EchoObject((int)action.ActionType);
            actionTag["valueType"] = new EchoObject(action.ExpectedValueType == typeof(Float2) ? "Float2" : "float");

            // Serialize bindings
            var bindingsList = EchoObject.NewList();
            foreach (var binding in action.Bindings)
                bindingsList.ListAdd(SerializeBinding(binding));
            actionTag["bindings"] = bindingsList;

            // Serialize composites
            var compositesList = EchoObject.NewList();
            foreach (var composite in action.CompositeBindings)
                compositesList.ListAdd(SerializeComposite(composite));
            actionTag["composites"] = compositesList;

            actionsList.ListAdd(actionTag);
        }
        compound.Add("actions", actionsList);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        if (value.TryGet("Name", out var nameTag))
            Name = nameTag.StringValue;

        _actions.Clear();

        if (value.TryGet("actions", out var actionsList) && actionsList.TagType == EchoType.List)
        {
            foreach (var actionTag in actionsList.List)
            {
                string name = actionTag.Get("name").StringValue;
                var type = (InputActionType)actionTag.Get("type").IntValue;
                string valueTypeStr = actionTag.TryGet("valueType", out var vt) ? vt.StringValue : "float";

                var action = new InputAction(name, type) { ActionMap = this };
                action.ExpectedValueType = valueTypeStr == "Float2" ? typeof(Float2) : typeof(float);

                // Deserialize bindings
                if (actionTag.TryGet("bindings", out var bindingsList) && bindingsList.TagType == EchoType.List)
                    foreach (var bt in bindingsList.List)
                        action.AddBinding(DeserializeBinding(bt));

                // Deserialize composites
                if (actionTag.TryGet("composites", out var compositesList) && compositesList.TagType == EchoType.List)
                    foreach (var ct in compositesList.List)
                    {
                        var composite = DeserializeComposite(ct);
                        if (composite != null)
                            action.AddBinding(composite);
                    }

                _actions[name] = action;
            }
        }
    }

    private static EchoObject SerializeBinding(InputBinding binding)
    {
        var tag = EchoObject.NewCompound();
        tag["bindingType"] = new EchoObject((int)binding.BindingType);
        tag["interaction"] = new EchoObject((int)binding.Interaction);

        if (binding.Key.HasValue) tag["key"] = new EchoObject((int)binding.Key.Value);
        if (binding.MouseButton.HasValue) tag["mouseButton"] = new EchoObject((int)binding.MouseButton.Value);
        if (binding.GamepadButton.HasValue) tag["gamepadButton"] = new EchoObject((int)binding.GamepadButton.Value);
        if (binding.AxisIndex.HasValue) tag["axisIndex"] = new EchoObject(binding.AxisIndex.Value);
        if (binding.RequiredDeviceIndex.HasValue) tag["deviceIndex"] = new EchoObject(binding.RequiredDeviceIndex.Value);

        // Interaction parameters
        tag["holdDuration"] = new EchoObject(binding.HoldDuration);
        tag["tapCount"] = new EchoObject(binding.TapCount);
        tag["tapWindow"] = new EchoObject(binding.TapWindow);
        tag["maxTapDuration"] = new EchoObject(binding.MaxTapDuration);

        // Processors
        if (binding.Processors.Count > 0)
        {
            var procList = EchoObject.NewList();
            foreach (var proc in binding.Processors)
                procList.ListAdd(SerializeProcessor(proc));
            tag["processors"] = procList;
        }

        return tag;
    }

    private static InputBinding DeserializeBinding(EchoObject tag)
    {
        var binding = new InputBinding
        {
            BindingType = (InputBindingType)tag.Get("bindingType").IntValue,
            Interaction = (InputInteractionType)tag.Get("interaction").IntValue,
        };

        if (tag.TryGet("key", out var k)) binding.Key = (KeyCode)k.IntValue;
        if (tag.TryGet("mouseButton", out var mb)) binding.MouseButton = (MouseButton)mb.IntValue;
        if (tag.TryGet("gamepadButton", out var gb)) binding.GamepadButton = (GamepadButton)gb.IntValue;
        if (tag.TryGet("axisIndex", out var ai)) binding.AxisIndex = ai.IntValue;
        if (tag.TryGet("deviceIndex", out var di)) binding.RequiredDeviceIndex = di.IntValue;

        if (tag.TryGet("holdDuration", out var hd)) binding.HoldDuration = hd.FloatValue;
        if (tag.TryGet("tapCount", out var tc)) binding.TapCount = tc.IntValue;
        if (tag.TryGet("tapWindow", out var tw)) binding.TapWindow = tw.FloatValue;
        if (tag.TryGet("maxTapDuration", out var mtd)) binding.MaxTapDuration = mtd.FloatValue;

        // Processors
        if (tag.TryGet("processors", out var procList) && procList.TagType == EchoType.List)
            foreach (var pt in procList.List)
            {
                var proc = DeserializeProcessor(pt);
                if (proc != null) binding.Processors.Add(proc);
            }

        return binding;
    }

    private static EchoObject SerializeComposite(InputCompositeBinding composite)
    {
        var tag = EchoObject.NewCompound();

        if (composite is Vector2CompositeBinding v2)
        {
            tag["type"] = new EchoObject("Vector2");
            tag["normalize"] = new EchoObject(v2.Normalize);
        }
        else if (composite is DualAxisCompositeBinding)
            tag["type"] = new EchoObject("DualAxis");
        else if (composite is AxisCompositeBinding)
            tag["type"] = new EchoObject("Axis");

        // Serialize parts
        var partsTag = EchoObject.NewCompound();
        foreach (var (partName, partBinding) in composite.Parts)
            partsTag[partName] = SerializeBinding(partBinding);
        tag["parts"] = partsTag;

        // Composite processors
        if (composite.Processors.Count > 0)
        {
            var procList = EchoObject.NewList();
            foreach (var proc in composite.Processors)
                procList.ListAdd(SerializeProcessor(proc));
            tag["processors"] = procList;
        }

        return tag;
    }

    private static InputCompositeBinding? DeserializeComposite(EchoObject tag)
    {
        string type = tag.Get("type").StringValue;

        // Deserialize parts first
        var parts = new Dictionary<string, InputBinding>();
        if (tag.TryGet("parts", out var partsTag) && partsTag.TagType == EchoType.Compound)
            foreach (var kvp in partsTag.Tags)
                parts[kvp.Key] = DeserializeBinding(kvp.Value);

        var dummy = new InputBinding { BindingType = InputBindingType.Key, Key = KeyCode.Unknown };

        InputCompositeBinding? composite = type switch
        {
            "Vector2" => new Vector2CompositeBinding(
                parts.GetValueOrDefault("up", dummy),
                parts.GetValueOrDefault("down", dummy),
                parts.GetValueOrDefault("left", dummy),
                parts.GetValueOrDefault("right", dummy),
                tag.TryGet("normalize", out var n) && n.BoolValue),
            "DualAxis" => new DualAxisCompositeBinding(
                parts.GetValueOrDefault("x", dummy),
                parts.GetValueOrDefault("y", dummy)),
            "Axis" => new AxisCompositeBinding(
                parts.GetValueOrDefault("positive", dummy),
                parts.GetValueOrDefault("negative", dummy)),
            _ => null
        };

        // Composite processors
        if (composite != null && tag.TryGet("processors", out var cprocList) && cprocList.TagType == EchoType.List)
            foreach (var pt in cprocList.List)
            {
                var proc = DeserializeProcessor(pt);
                if (proc != null) composite.Processors.Add(proc);
            }

        return composite;
    }

    // ================================================================
    //  Processor Serialization
    // ================================================================

    private static EchoObject SerializeProcessor(IInputProcessor proc)
    {
        var tag = EchoObject.NewCompound();
        switch (proc)
        {
            case NormalizeProcessor:
                tag["type"] = new EchoObject("Normalize");
                break;
            case InvertProcessor:
                tag["type"] = new EchoObject("Invert");
                break;
            case ScaleProcessor sp:
                tag["type"] = new EchoObject("Scale");
                tag["scale"] = new EchoObject(sp.Scale);
                break;
            case ClampProcessor cp:
                tag["type"] = new EchoObject("Clamp");
                tag["min"] = new EchoObject(cp.Min);
                tag["max"] = new EchoObject(cp.Max);
                break;
            case DeadzoneProcessor dp:
                tag["type"] = new EchoObject("Deadzone");
                tag["threshold"] = new EchoObject(dp.Threshold);
                break;
            case ExponentialProcessor ep:
                tag["type"] = new EchoObject("Exponential");
                tag["exponent"] = new EchoObject(ep.Exponent);
                break;
        }
        return tag;
    }

    private static IInputProcessor? DeserializeProcessor(EchoObject tag)
    {
        string type = tag.Get("type").StringValue;
        return type switch
        {
            "Normalize" => new NormalizeProcessor(),
            "Invert" => new InvertProcessor(),
            "Scale" => new ScaleProcessor(tag.TryGet("scale", out var s) ? s.FloatValue : 1f),
            "Clamp" => new ClampProcessor(
                tag.TryGet("min", out var mn) ? mn.FloatValue : 0f,
                tag.TryGet("max", out var mx) ? mx.FloatValue : 1f),
            "Deadzone" => new DeadzoneProcessor(tag.TryGet("threshold", out var t) ? t.FloatValue : 0.2f),
            "Exponential" => new ExponentialProcessor(tag.TryGet("exponent", out var e) ? e.FloatValue : 2f),
            _ => null
        };
    }
}
