// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>Registration record for one discovered <see cref="SceneTool"/>.</summary>
public sealed class SceneToolEntry
{
    public required SceneTool Tool { get; init; }

    /// <summary>Component the tool is scoped to, or null for a tool that is always available.</summary>
    public Type? ComponentType { get; init; }

    public string? Group { get; init; }

    public bool IsGlobal => ComponentType == null;
}

/// <summary>
/// Tracks which <see cref="SceneTool"/>s are live. There is no exclusive "active tool": every
/// available tool ticks every frame and they coexist, because <see cref="HandleContext"/> already
/// decides which handle owns the cursor. A tool that wants to be modal owns that decision itself -
/// it draws a toggle in the tool strip and returns early while switched off, which is both simpler
/// than an activation state machine and more flexible than one.
///
/// <para>Availability is the only gating: a global tool is always available, a component-scoped tool
/// is available while a matching component is selected. <see cref="SceneTool.OnActivated"/> and
/// <see cref="SceneTool.OnDeactivated"/> fire on those transitions.</para>
/// </summary>
public static class SceneToolManager
{
    private static readonly List<SceneToolEntry> _entries = new();
    private static readonly List<SceneTool> _live = new();
    private static readonly List<SceneTool> _scratch = new();

    /// <summary>Every registered tool, in strip order.</summary>
    public static IReadOnlyList<SceneToolEntry> All => _entries;

    /// <summary>Tools ticking this frame.</summary>
    public static IReadOnlyList<SceneTool> Live => _live;

    /// <summary>Fires when a tool becomes available or stops being available.</summary>
    public static event Action<SceneTool, bool>? AvailabilityChanged;

    internal static void Clear()
    {
        _live.Clear();
        _entries.Clear();
    }

    internal static void Register(SceneToolEntry entry)
    {
        _entries.Add(entry);
        _entries.Sort((a, b) => a.Tool.Order.CompareTo(b.Tool.Order));
    }

    public static SceneToolEntry? EntryFor(SceneTool tool) => _entries.FirstOrDefault(e => e.Tool == tool);

    /// <summary>Look up a registered tool instance, for a tool that wants to talk to another.</summary>
    public static T? Get<T>() where T : SceneTool => _entries.Select(e => e.Tool).OfType<T>().FirstOrDefault();

    public static bool IsLive(SceneTool tool) => _live.Contains(tool);

    /// <summary>
    /// Recompute which tools are available and fire the lifecycle callbacks for anything that
    /// changed. Called once per viewport per frame before the tools tick.
    /// </summary>
    internal static void SyncAvailability(SceneToolContext ctx)
    {
        _scratch.Clear();
        foreach (var entry in _entries)
        {
            if (entry.ComponentType != null && !SelectionHasComponent(entry.ComponentType)) continue;

            ctx.CurrentTool = entry.Tool;
            if (!entry.Tool.IsAvailable(ctx)) continue;

            _scratch.Add(entry.Tool);
        }

        // Gone: tear down and forget any per-viewport state it accumulated.
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            SceneTool tool = _live[i];
            if (_scratch.Contains(tool)) continue;

            _live.RemoveAt(i);
            try { tool.OnDeactivated(); }
            catch (Exception ex) { LogToolError(tool, nameof(SceneTool.OnDeactivated), ex); }
            ctx.ClearState(tool);
            AvailabilityChanged?.Invoke(tool, false);
        }

        // New: set up.
        foreach (SceneTool tool in _scratch)
        {
            if (_live.Contains(tool)) continue;

            _live.Add(tool);
            ctx.CurrentTool = tool;
            try { tool.OnActivated(ctx); }
            catch (Exception ex) { LogToolError(tool, nameof(SceneTool.OnActivated), ex); }
            AvailabilityChanged?.Invoke(tool, true);
        }
    }

    /// <summary>Tick every live tool. One misbehaving tool must not take the viewport down.</summary>
    internal static void TickInput(SceneToolContext ctx)
    {
        foreach (SceneTool tool in _live)
        {
            ctx.CurrentTool = tool;
            try { tool.OnSceneInput(ctx); }
            catch (Exception ex) { LogToolError(tool, nameof(SceneTool.OnSceneInput), ex); }
        }
    }

    /// <summary>True when any live tool wants the object transform gizmo hidden.</summary>
    internal static bool AnySuppressesTransformGizmo()
    {
        foreach (SceneTool tool in _live)
            if (tool.SuppressTransformGizmo) return true;
        return false;
    }

    internal static bool AnyOverridesToolStrip()
    {
        foreach (SceneTool tool in _live)
            if (tool.OverridesToolStrip) return true;
        return false;
    }

    private static void LogToolError(SceneTool tool, string stage, Exception ex)
        => Debug.LogError($"[SceneTool] {tool.GetType().Name}.{stage} threw: {ex.Message}\n{ex.StackTrace}");

    private static bool SelectionHasComponent(Type componentType)
    {
        var go = Selection.GetSelected<GameObject>().FirstOrDefault();
        return go.IsValid() && go.GetComponent(componentType) != null;
    }

    // ================================================================
    //  Per-tool settings
    // ================================================================

    /// <summary>
    /// Persistent per-tool settings. Stored in <see cref="EditorSettings"/> rather than project
    /// settings because a tool's brush size or grid step is a per-user preference that should follow
    /// the user across projects, not something committed alongside the scene.
    /// </summary>
    public static T Settings<T>() where T : class, new()
    {
        string key = typeof(T).FullName ?? typeof(T).Name;
        var store = EditorSettings.Instance.SceneToolSettings;

        if (store.TryGetValue(key, out string? json) && !string.IsNullOrEmpty(json))
        {
            try
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<T>(json);
                if (loaded != null) return loaded;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SceneTool] Failed to read settings for {key}: {ex.Message}");
            }
        }
        return new T();
    }

    /// <summary>Persist a settings object previously obtained from <see cref="Settings{T}"/>.</summary>
    public static void SaveSettings<T>(T settings) where T : class
    {
        string key = typeof(T).FullName ?? typeof(T).Name;
        try
        {
            EditorSettings.Instance.SceneToolSettings[key] = System.Text.Json.JsonSerializer.Serialize(settings);
            EditorSettings.Instance.Save();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SceneTool] Failed to save settings for {key}: {ex.Message}");
        }
    }
}
