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
public class InputActionMap : Asset, ISerializable
{
    private readonly List<InputAction> _actions = [];

    /// <summary>All actions in this map, in their saved order.</summary>
    public IReadOnlyList<InputAction> Actions { get { EnsureLoaded(); return _actions; } }

    /// <summary>Whether any action in this map is enabled.</summary>
    public bool Enabled { get { EnsureLoaded(); return _actions.Any(a => a.Enabled); } }

    public InputActionMap() : base("New InputActionMap") { }

    public InputActionMap(string name) : base(name) { }

    /// <summary>Adds a new action to the end of this map.</summary>
    public InputAction AddAction(string name, InputActionType type = InputActionType.Button)
    {
        var action = new InputAction(name, type);
        AddAction(action);
        return action;
    }

    /// <summary>Adds an existing action to the end of this map.</summary>
    public void AddAction(InputAction action) => InsertAction(Actions.Count, action);

    /// <summary>Adds an existing action at a position in this map.</summary>
    public void InsertAction(int index, InputAction action)
    {
        EnsureLoaded();
        if (IndexOf(action.Name) >= 0)
            throw new ArgumentException($"Action '{action.Name}' already exists in map '{Name}'");

        action.ActionMap = this;
        _actions.Insert(index, action);
    }

    /// <summary>The position of an action, or -1 when this map has none by that name.</summary>
    public int IndexOf(string name)
    {
        EnsureLoaded();
        return _actions.FindIndex(a => a.Name == name);
    }

    /// <summary>Finds an action by name.</summary>
    public InputAction? FindAction(string name)
    {
        int index = IndexOf(name);
        return index >= 0 ? _actions[index] : null;
    }

    /// <summary>Gets an action by name, throws if not found.</summary>
    public InputAction GetAction(string name)
        => FindAction(name) ?? throw new KeyNotFoundException($"Action '{name}' not found in map '{Name}'");

    /// <summary>Renames an action in place, so it stays enabled and keeps its listeners.</summary>
    public bool RenameAction(string oldName, string newName)
    {
        InputAction? action = FindAction(oldName);
        if (action == null || string.IsNullOrWhiteSpace(newName) || FindAction(newName) != null)
            return false;

        action.Name = newName;
        return true;
    }

    /// <summary>Moves an action to another position in this map.</summary>
    public void MoveAction(int from, int to)
    {
        EnsureLoaded();
        InputAction action = _actions[from];
        _actions.RemoveAt(from);
        _actions.Insert(to, action);
    }

    /// <summary>Removes an action from this map.</summary>
    public bool RemoveAction(string name)
    {
        int index = IndexOf(name);
        if (index < 0)
            return false;

        InputAction action = _actions[index];
        action.Disable();
        action.ActionMap = null;
        _actions.RemoveAt(index);
        return true;
    }

    /// <summary>Enables all actions in this map.</summary>
    public void Enable()
    {
        EnsureLoaded();
        foreach (InputAction action in _actions)
            action.Enable();
    }

    /// <summary>Disables all actions in this map.</summary>
    public void Disable()
    {
        EnsureLoaded();
        foreach (InputAction action in _actions)
            action.Disable();
    }

    /// <summary>Internal method to update all actions in this map.</summary>
    internal void UpdateActions(IInputHandler inputHandler, float currentTime)
    {
        foreach (InputAction action in _actions)
        {
            if (action.Enabled)
                action.UpdateState(inputHandler, currentTime);
        }
    }

    /// <summary>Indexer to access actions by name.</summary>
    public InputAction this[string name] => GetAction(name);

    public override string ToString() => $"{Name} ({_actions.Count} actions)";

    // The actions come from the copy that was read, so they point back at it. What was enabled stays enabled.
    // Read again in place, an action keeps its object, so whatever listens to it or holds it keeps working. After an
    // unload there is nothing to keep, and the actions read are used as they are.
    protected override void TakeContent(Asset staging)
    {
        Dictionary<string, InputAction>? previous = State == AssetState.Loaded ? _actions.ToDictionary(a => a.Name) : null;

        base.TakeContent(staging);

        for (int i = 0; i < _actions.Count; i++)
        {
            InputAction read = _actions[i];
            if (previous == null || !previous.Remove(read.Name, out InputAction? kept))
            {
                read.ActionMap = this;
                continue;
            }

            bool enabled = kept.Enabled;
            if (enabled) kept.Disable();
            kept.TakeConfiguration(read);
            _actions[i] = kept;
            if (enabled) kept.Enable();
        }

        // Actions the map no longer has.
        if (previous != null)
            foreach (InputAction removed in previous.Values)
            {
                removed.Disable();
                removed.ActionMap = null;
            }
    }

    // ================================================================
    //  Serialization save/load the action map structure as an asset
    // ================================================================

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        compound.Add("Name", new EchoObject(Name));

        var actionsList = EchoObject.NewList();
        foreach (InputAction action in _actions)
        {
            var actionTag = EchoObject.NewCompound();
            actionTag["name"] = new EchoObject(action.Name);
            actionTag["type"] = new EchoObject((int)action.ActionType);
            actionTag["valueType"] = new EchoObject(action.ExpectedValueType == typeof(Float2) ? "Float2" : "float");

            // Serialize bindings
            var bindingsList = EchoObject.NewList();
            foreach (InputBinding binding in action.Bindings)
                bindingsList.ListAdd(SerializeBinding(binding));
            actionTag["bindings"] = bindingsList;

            // Serialize composites
            var compositesList = EchoObject.NewList();
            foreach (InputCompositeBinding composite in action.CompositeBindings)
                compositesList.ListAdd(SerializeComposite(composite));
            actionTag["composites"] = compositesList;

            actionsList.ListAdd(actionTag);
        }
        compound.Add("actions", actionsList);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        if (value.TryGet("Name", out EchoObject? nameTag))
            Name = nameTag.StringValue;

        _actions.Clear();

        if (value.TryGet("actions", out EchoObject? actionsList) && actionsList.TagType == EchoType.List)
        {
            foreach (EchoObject actionTag in actionsList.List)
            {
                string name = actionTag.Get("name").StringValue;
                var type = (InputActionType)actionTag.Get("type").IntValue;
                string valueTypeStr = actionTag.TryGet("valueType", out EchoObject? vt) ? vt.StringValue : "float";

                var action = new InputAction(name, type) { ActionMap = this };
                action.ExpectedValueType = valueTypeStr == "Float2" ? typeof(Float2) : typeof(float);

                // Deserialize bindings
                if (actionTag.TryGet("bindings", out EchoObject? bindingsList) && bindingsList.TagType == EchoType.List)
                    foreach (EchoObject bt in bindingsList.List)
                        action.AddBinding(DeserializeBinding(bt));

                // Deserialize composites
                if (actionTag.TryGet("composites", out EchoObject? compositesList) && compositesList.TagType == EchoType.List)
                    foreach (EchoObject ct in compositesList.List)
                    {
                        InputCompositeBinding? composite = DeserializeComposite(ct);
                        if (composite != null)
                            action.AddBinding(composite);
                    }

                if (_actions.Exists(a => a.Name == name))
                    continue;
                _actions.Add(action);
            }
        }
    }

    private static EchoObject SerializeBinding(InputBinding binding)
    {
        var tag = EchoObject.NewCompound();
        tag["bindingType"] = new EchoObject((int)binding.BindingType);
        SerializeInteraction(tag, binding);

        if (binding.Key.HasValue) tag["key"] = new EchoObject((int)binding.Key.Value);
        if (binding.MouseButton.HasValue) tag["mouseButton"] = new EchoObject((int)binding.MouseButton.Value);
        if (binding.GamepadButton.HasValue) tag["gamepadButton"] = new EchoObject((int)binding.GamepadButton.Value);
        if (binding.AxisIndex.HasValue) tag["axisIndex"] = new EchoObject(binding.AxisIndex.Value);
        if (binding.RequiredDeviceIndex.HasValue) tag["deviceIndex"] = new EchoObject(binding.RequiredDeviceIndex.Value);
        if (binding.XRHand.HasValue) tag["xrHand"] = new EchoObject((int)binding.XRHand.Value);
        if (binding.XRButton.HasValue) tag["xrButton"] = new EchoObject((int)binding.XRButton.Value);
        if (binding.XRAxis.HasValue) tag["xrAxis"] = new EchoObject((int)binding.XRAxis.Value);
        if (binding.XRTouch.HasValue) tag["xrTouch"] = new EchoObject((int)binding.XRTouch.Value);

        // Processors
        if (binding.Processors.Count > 0)
        {
            var procList = EchoObject.NewList();
            foreach (IInputProcessor proc in binding.Processors)
                procList.ListAdd(SerializeProcessor(proc));
            tag["processors"] = procList;
        }

        return tag;
    }

    private static InputBinding DeserializeBinding(EchoObject tag)
    {
        var binding = new InputBinding { BindingType = (InputBindingType)tag.Get("bindingType").IntValue };
        DeserializeInteraction(tag, binding);

        if (tag.TryGet("key", out EchoObject? k)) binding.Key = (KeyCode)k.IntValue;
        if (tag.TryGet("mouseButton", out EchoObject? mb)) binding.MouseButton = (MouseButton)mb.IntValue;
        if (tag.TryGet("gamepadButton", out EchoObject? gb)) binding.GamepadButton = (GamepadButton)gb.IntValue;
        if (tag.TryGet("axisIndex", out EchoObject? ai)) binding.AxisIndex = ai.IntValue;
        if (tag.TryGet("deviceIndex", out EchoObject? di)) binding.RequiredDeviceIndex = di.IntValue;
        if (tag.TryGet("xrHand", out EchoObject? xh)) binding.XRHand = (XRHand)xh.IntValue;
        if (tag.TryGet("xrButton", out EchoObject? xb)) binding.XRButton = (XRButton)xb.IntValue;
        if (tag.TryGet("xrAxis", out EchoObject? xa)) binding.XRAxis = (XRAxis)xa.IntValue;
        if (tag.TryGet("xrTouch", out EchoObject? xt)) binding.XRTouch = (XRTouch)xt.IntValue;

        // Processors
        if (tag.TryGet("processors", out EchoObject? procList) && procList.TagType == EchoType.List)
            foreach (EchoObject pt in procList.List)
            {
                IInputProcessor? proc = DeserializeProcessor(pt);
                if (proc != null) binding.Processors.Add(proc);
            }

        return binding;
    }

    private static void SerializeInteraction(EchoObject tag, IInputInteractionSettings settings)
    {
        tag["interaction"] = new EchoObject((int)settings.Interaction);
        tag["holdDuration"] = new EchoObject(settings.HoldDuration);
        tag["tapCount"] = new EchoObject(settings.TapCount);
        tag["tapWindow"] = new EchoObject(settings.TapWindow);
        tag["maxTapDuration"] = new EchoObject(settings.MaxTapDuration);
        tag["pressPoint"] = new EchoObject(settings.PressPoint);
    }

    private static void DeserializeInteraction(EchoObject tag, IInputInteractionSettings settings)
    {
        if (tag.TryGet("interaction", out EchoObject? it)) settings.Interaction = (InputInteractionType)it.IntValue;
        if (tag.TryGet("holdDuration", out EchoObject? hd)) settings.HoldDuration = hd.FloatValue;
        if (tag.TryGet("tapCount", out EchoObject? tc)) settings.TapCount = tc.IntValue;
        if (tag.TryGet("tapWindow", out EchoObject? tw)) settings.TapWindow = tw.FloatValue;
        if (tag.TryGet("maxTapDuration", out EchoObject? mtd)) settings.MaxTapDuration = mtd.FloatValue;
        if (tag.TryGet("pressPoint", out EchoObject? pp)) settings.PressPoint = pp.FloatValue;
    }

    private static EchoObject SerializeComposite(InputCompositeBinding composite)
    {
        var tag = EchoObject.NewCompound();
        SerializeInteraction(tag, composite);

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
        foreach ((string? partName, InputBinding? partBinding) in composite.Parts)
            partsTag[partName] = SerializeBinding(partBinding);
        tag["parts"] = partsTag;

        // Composite processors
        if (composite.Processors.Count > 0)
        {
            var procList = EchoObject.NewList();
            foreach (IInputProcessor proc in composite.Processors)
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
        if (tag.TryGet("parts", out EchoObject? partsTag) && partsTag.TagType == EchoType.Compound)
            foreach (KeyValuePair<string, EchoObject> kvp in partsTag.Tags)
                parts[kvp.Key] = DeserializeBinding(kvp.Value);

        var dummy = new InputBinding { BindingType = InputBindingType.Key, Key = KeyCode.Unknown };

        InputCompositeBinding? composite = type switch
        {
            "Vector2" => new Vector2CompositeBinding(
                parts.GetValueOrDefault("up", dummy),
                parts.GetValueOrDefault("down", dummy),
                parts.GetValueOrDefault("left", dummy),
                parts.GetValueOrDefault("right", dummy),
                tag.TryGet("normalize", out EchoObject? n) && n.BoolValue),
            "DualAxis" => new DualAxisCompositeBinding(
                parts.GetValueOrDefault("x", dummy),
                parts.GetValueOrDefault("y", dummy)),
            "Axis" => new AxisCompositeBinding(
                parts.GetValueOrDefault("positive", dummy),
                parts.GetValueOrDefault("negative", dummy)),
            _ => null
        };

        if (composite != null)
            DeserializeInteraction(tag, composite);

        // Composite processors
        if (composite != null && tag.TryGet("processors", out EchoObject? cprocList) && cprocList.TagType == EchoType.List)
            foreach (EchoObject pt in cprocList.List)
            {
                IInputProcessor? proc = DeserializeProcessor(pt);
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
            "Scale" => new ScaleProcessor(tag.TryGet("scale", out EchoObject? s) ? s.FloatValue : 1f),
            "Clamp" => new ClampProcessor(
                tag.TryGet("min", out EchoObject? mn) ? mn.FloatValue : 0f,
                tag.TryGet("max", out EchoObject? mx) ? mx.FloatValue : 1f),
            "Deadzone" => new DeadzoneProcessor(tag.TryGet("threshold", out EchoObject? t) ? t.FloatValue : 0.2f),
            "Exponential" => new ExponentialProcessor(tag.TryGet("exponent", out EchoObject? e) ? e.FloatValue : 2f),
            _ => null
        };
    }
}
