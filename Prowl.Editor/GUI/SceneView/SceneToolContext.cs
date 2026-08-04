// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Everything a <see cref="SceneTool"/> needs for one viewport for one frame: the arbitration
/// context, the scene, the selection, and per-viewport storage.
///
/// <para>One context exists per scene viewport and is reused across frames, so
/// <see cref="State{T}"/> is stable for as long as the viewport lives. That is what lets a single
/// tool instance serve two scene views without their drags interfering.</para>
/// </summary>
public sealed class SceneToolContext
{
    private readonly Dictionary<(Type tool, Type state), object> _state = new();

    internal SceneToolContext(HandleContext handles) => Handles = handles;

    /// <summary>Handle arbitration, cursor, ray, camera and keyboard-safe key queries.</summary>
    public HandleContext Handles { get; }

    /// <summary>Deferred 3D drawing onto the viewport overlay.</summary>
    public SceneDrawList Draw => Handles.Draw;

    public Scene Scene { get; private set; } = null!;

    public Camera Camera => Handles.Camera;

    /// <summary>The viewport this context belongs to.</summary>
    public object View { get; private set; } = null!;

    /// <summary>
    /// The tool currently being serviced. Scopes <see cref="State{T}"/> and binds the tool's
    /// protected drawing helpers, so setting this is the single act that makes a tool "current".
    /// </summary>
    internal SceneTool? CurrentTool
    {
        get => _currentTool;
        set
        {
            if (ReferenceEquals(_currentTool, value)) return;
            _currentTool?.BindContext(null);
            _currentTool = value;
            _currentTool?.BindContext(this);
        }
    }

    private SceneTool? _currentTool;

    public GameObject? ActiveObject => Selection.GetSelected<GameObject>().FirstOrDefault();

    public IReadOnlyList<GameObject> SelectedObjects => Selection.GetSelected<GameObject>().ToList();

    internal void Begin(Scene scene, object view)
    {
        Scene = scene;
        View = view;
    }

    /// <summary>
    /// Per-tool, per-viewport state. Keyed by both the tool type and the state type, so a tool
    /// instance shared across viewports still gets independent drag state in each one.
    /// </summary>
    public T State<T>() where T : class, new()
    {
        var key = (CurrentTool?.GetType() ?? typeof(SceneTool), typeof(T));
        if (!_state.TryGetValue(key, out object? existing))
        {
            existing = new T();
            _state[key] = existing;
        }
        return (T)existing;
    }

    /// <summary>Forget a tool's stored state, e.g. on deactivation.</summary>
    public void ClearState(SceneTool tool)
    {
        foreach (var key in _state.Keys.Where(k => k.tool == tool.GetType()).ToList())
            _state.Remove(key);
    }

    /// <summary>Mark the scene dirty so the change is saved.</summary>
    public void MarkDirty() => EditorSceneManager.MarkDirty();

    /// <summary>
    /// Scope a multi-frame interactive edit into one undo step. Dispose commits; call
    /// <see cref="ToolUndoScope.Cancel"/> to revert everything done inside it.
    /// </summary>
    public ToolUndoScope UndoScope(string description, params GameObject[] targets)
        => new(description, targets);
}

/// <summary>
/// Groups an interactive operation into a single undo entry. Wraps
/// <see cref="Undo.BeginContinuous"/> so a tool does not have to pair begin/end by hand across
/// early returns.
/// </summary>
public readonly struct ToolUndoScope : IDisposable
{
    private readonly bool _continuous;

    internal ToolUndoScope(string description, GameObject[] targets)
    {
        _continuous = targets.Length > 0;
        if (_continuous)
            Undo.BeginContinuous(targets, description);
        else
            Undo.IncrementGroup();
    }

    /// <summary>Abandon the operation, reverting anything recorded inside the scope.</summary>
    public void Cancel()
    {
        if (_continuous) Undo.CancelContinuous();
    }

    public void Dispose()
    {
        if (_continuous) Undo.EndContinuous();
    }
}
