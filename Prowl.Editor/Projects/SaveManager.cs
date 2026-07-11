// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.OrigamiUI;
using Prowl.Rosetta;
using Prowl.Runtime;

namespace Prowl.Editor.Projects;

/// <summary>
/// Centralized save system for the editor. Handles Ctrl+S, auto-save, and
/// compiles results from all save handlers into a single toast notification.
///
/// Subsystems register via <see cref="OnSave"/> and return what they saved.
/// The manager fires all handlers, collects labels, and shows one combined toast.
/// </summary>
public static class SaveManager
{
    /// <summary>
    /// Save handler delegate. Return a human-readable label for what was saved
    /// (e.g. "Scene: MyLevel"), or null/empty if nothing was saved.
    /// </summary>
    public delegate string? SaveHandler();

    /// <summary>
    /// Event fired when a project save is requested (Ctrl+S or auto-save).
    /// Each subscriber should save its dirty state and return a label, or null if clean.
    /// </summary>
    public static event SaveHandler? OnSave;

    /// <summary>Auto-save interval in seconds. Set to 0 to disable. Default 300 (5 minutes).</summary>
    public static float AutoSaveInterval = 300f;

    /// <summary>Whether auto-save is enabled.</summary>
    public static bool AutoSaveEnabled = true;

    /// <summary>
    /// True while <see cref="OnSave"/> handlers are running as part of an auto-save rather than a
    /// manual Ctrl+S. Handlers should check this before showing user-facing prompts (e.g. a Save As
    /// dialog for a never-saved scene) - auto-save must never interrupt the user unattended.
    /// </summary>
    public static bool IsAutoSave { get; private set; }

    private static double _timeSinceLastSave;
    private static bool _saveRequestedThisFrame;

    /// <summary>
    /// Call once per frame from the editor's update loop.
    /// Checks for Ctrl+S shortcut and auto-save timer.
    /// </summary>
    public static void Update(float deltaTime)
    {
        _saveRequestedThisFrame = false;

        // Ctrl+S shortcut
        if (ShortcutManager.IsPressed("Global/Save"))
        {
            if (Application.IsPlaying)
            {
                Toasts.Warning(Loc.Get("save.cant_play_mode"),
                    Loc.Get("save.cant_play_mode_msg"));
                return;
            }

            RequestSave();
        }

        // Auto-save timer
        if (AutoSaveEnabled && AutoSaveInterval > 0 && !Application.IsPlaying)
        {
            _timeSinceLastSave += deltaTime;
            if (_timeSinceLastSave >= AutoSaveInterval)
            {
                RequestSave(isAutoSave: true);
            }
        }
    }

    /// <summary>
    /// Trigger a save. Fires all <see cref="OnSave"/> handlers and shows a combined toast.
    /// Can be called manually from menu items or other systems.
    /// </summary>
    public static void RequestSave(bool isAutoSave = false)
    {
        if (_saveRequestedThisFrame) return;
        _saveRequestedThisFrame = true;
        _timeSinceLastSave = 0;
        IsAutoSave = isAutoSave;

        if (OnSave == null) return;

        var labels = new List<string>();

        foreach (SaveHandler handler in OnSave.GetInvocationList())
        {
            try
            {
                string? label = handler();
                if (!string.IsNullOrWhiteSpace(label))
                    labels.Add(label);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Save handler failed: {ex.Message}");
            }
        }

        if (labels.Count == 0)
        {
            if (!isAutoSave)
                Origami.Toast(Loc.Get("save.nothing")).Message(Loc.Get("save.nothing_msg")).Info().Show();
            return;
        }

        string title = isAutoSave ? Loc.Get("save.auto_saved") : Loc.Get("save.saved");
        string message = labels.Count == 1
            ? labels[0]
            : string.Join(", ", labels);

        Origami.Toast(title).Message(message).Success().Show();
    }

    /// <summary>Reset the auto-save timer (e.g. after a manual save).</summary>
    public static void ResetTimer() => _timeSinceLastSave = 0;
}
