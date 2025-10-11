// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// A collection of related input actions that can be enabled/disabled as a group.
/// Example: "PlayerActions", "UIActions", "VehicleActions"
/// </summary>
public class InputActionMap : ISerializable
{
    private readonly Dictionary<string, InputAction> _actions = new();

    /// <summary>
    /// The name of this action map.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// All actions in this map.
    /// </summary>
    public IReadOnlyCollection<InputAction> Actions => _actions.Values;

    /// <summary>
    /// Whether any action in this map is enabled.
    /// </summary>
    public bool Enabled => _actions.Values.Any(a => a.Enabled);

    /// <summary>
    /// Creates a new action map with the specified name.
    /// </summary>
    public InputActionMap(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Adds an action to this map.
    /// </summary>
    public InputAction AddAction(string name, InputActionType type = InputActionType.Button)
    {
        if (_actions.ContainsKey(name))
            throw new ArgumentException($"Action '{name}' already exists in map '{Name}'");

        var action = new InputAction(name, type) { ActionMap = this };
        _actions[name] = action;
        return action;
    }

    /// <summary>
    /// Adds an existing action to this map.
    /// </summary>
    public void AddAction(InputAction action)
    {
        if (_actions.ContainsKey(action.Name))
            throw new ArgumentException($"Action '{action.Name}' already exists in map '{Name}'");

        action.ActionMap = this;
        _actions[action.Name] = action;
    }

    /// <summary>
    /// Finds an action by name.
    /// </summary>
    public InputAction? FindAction(string name)
    {
        _actions.TryGetValue(name, out var action);
        return action;
    }

    /// <summary>
    /// Gets an action by name, throws if not found.
    /// </summary>
    public InputAction GetAction(string name)
    {
        if (!_actions.TryGetValue(name, out var action))
            throw new KeyNotFoundException($"Action '{name}' not found in map '{Name}'");
        return action;
    }

    /// <summary>
    /// Removes an action from this map.
    /// </summary>
    public bool RemoveAction(string name)
    {
        if (_actions.TryGetValue(name, out var action))
        {
            action.Disable();
            action.ActionMap = null;
            return _actions.Remove(name);
        }
        return false;
    }

    /// <summary>
    /// Enables all actions in this map.
    /// </summary>
    public void Enable()
    {
        foreach (var action in _actions.Values)
            action.Enable();
    }

    /// <summary>
    /// Disables all actions in this map.
    /// </summary>
    public void Disable()
    {
        foreach (var action in _actions.Values)
            action.Disable();
    }

    /// <summary>
    /// Internal method to update all actions in this map.
    /// </summary>
    internal void UpdateActions(IInputHandler inputHandler, double currentTime)
    {
        foreach (var action in _actions.Values)
        {
            if (action.Enabled)
                action.UpdateState(inputHandler, currentTime);
        }
    }

    /// <summary>
    /// Indexer to access actions by name.
    /// </summary>
    public InputAction this[string name] => GetAction(name);

    public override string ToString() => $"{Name} ({_actions.Count} actions)";

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {

    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {

    }
}
