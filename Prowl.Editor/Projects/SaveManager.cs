// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
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

    /// <summary>Whether auto-save is enabled, from the editor preferences.</summary>
    public static bool AutoSaveEnabled => EditorSettings.Instance.AutoSave;

    /// <summary>
    /// True while <see cref="OnSave"/> handlers are running as part of an auto-save rather than a
    /// manual Ctrl+S. Handlers should check this before showing user-facing prompts (e.g. a Save As
    /// dialog for a never-saved scene) - auto-save must never interrupt the user unattended.
    /// </summary>
    public static bool IsAutoSave { get; private set; }

    /// <summary>Fired to list what has unsaved changes. Each subscriber adds one label per unsaved item, worded like its save toast label.</summary>
    public static event Action<List<string>>? CollectUnsaved;

    // Set only while handlers run for an unsaved changes prompt, see TakeAfterSaveAs.
    private static Action? _afterSaveAs;
    private static bool _saveAsStarted;
    private static DialogModal? _prompt;

    private static double _timeSinceLastSave;
    private static bool _saveRequestedThisFrame;

    /// <summary>
    /// Call once per frame from the editor's update loop.
    /// Checks for Ctrl+S shortcut and auto-save timer.
    /// </summary>
    public static void Update(float deltaTime)
    {
        _saveRequestedThisFrame = false;

        // Ctrl+S saves everything, in play mode too. Handlers skip what play mode cannot save.
        if (ShortcutManager.IsPressed("Global/Save"))
            RequestSave();

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
    public static void RequestSave(bool isAutoSave = false) => RequestSave(isAutoSave, null);

    private static void RequestSave(bool isAutoSave, Action? afterSaveAs)
    {
        if (_saveRequestedThisFrame) return;
        _saveRequestedThisFrame = true;
        _timeSinceLastSave = 0;
        IsAutoSave = isAutoSave;

        if (OnSave == null)
        {
            IsAutoSave = false;
            return;
        }

        var labels = new List<string>();
        _afterSaveAs = afterSaveAs;
        _saveAsStarted = false;

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
        _afterSaveAs = null;

        if (labels.Count == 0)
        {
            if (!isAutoSave && Application.IsPlaying)
                Toasts.Warning(Loc.Get("save.cant_play_mode"), Loc.Get("save.cant_play_mode_msg"));
            else if (!isAutoSave)
                Origami.Toast(Loc.Get("save.nothing")).Message(Loc.Get("save.nothing_msg")).Info().Show();
            IsAutoSave = false;
            return;
        }

        string title = isAutoSave ? Loc.Get("save.auto_saved") : Loc.Get("save.saved");
        string message = labels.Count == 1
            ? labels[0]
            : string.Join(", ", labels);

        Origami.Toast(title).Message(message).Success().Show();
        IsAutoSave = false;
    }

    /// <summary>For a save handler about to open a Save As dialog: what to run once the file is written, so an unsaved changes prompt can carry on.</summary>
    public static Action? TakeAfterSaveAs()
    {
        _saveAsStarted = _afterSaveAs != null;
        return _afterSaveAs;
    }

    /// <summary>Everything with unsaved changes, as labels.</summary>
    public static List<string> Unsaved()
    {
        var unsaved = new List<string>();
        CollectUnsaved?.Invoke(unsaved);
        return unsaved;
    }

    /// <summary>Runs <paramref name="proceed"/> when nothing is unsaved, otherwise asks to save everything, discard it, or cancel first.</summary>
    public static void ConfirmUnsaved(Action proceed)
    {
        List<string> unsaved = Unsaved();
        if (unsaved.Count == 0)
        {
            proceed();
            return;
        }

        string? note = Application.IsPlaying ? Loc.Get("dialog.unsaved_play_mode") : null;
        AskToSave(unsaved, note,
            save: () =>
            {
                RequestSave(false, () => ConfirmUnsaved(proceed));
                // A Save As dialog carries on by itself. Anything else still unsaved is asked about again.
                if (!_saveAsStarted) ConfirmUnsaved(proceed);
            },
            discard: proceed);
    }

    /// <summary>The Save, Don't Save, Cancel prompt for a list of unsaved items. Cancel and Escape do nothing.</summary>
    public static void AskToSave(IReadOnlyList<string> items, string? note, Action save, Action discard)
    {
        if (_prompt != null && Modal.Contains(_prompt)) return;

        DialogModal dialog = Origami.Dialog(Loc.Get("dialog.unsaved_changes"), paper =>
        {
            Origami.Label(paper, "unsaved_msg", Loc.Get("dialog.unsaved_changes_body")).Show();
            for (int i = 0; i < items.Count; i++)
                Origami.Label(paper, $"unsaved_item_{i}", "  " + items[i]).Show();
            if (note != null) Origami.Label(paper, "unsaved_note", note).Show();
        }, 420);
        dialog.CloseOnEscape = true;
        dialog.CloseOnBackdrop = false;
        _prompt = dialog;

        dialog.Button(Loc.Get("dialog.save"), () => { Modal.Remove(dialog); save(); }, OrigamiVariant.Primary)
            .Button(Loc.Get("dialog.dont_save"), () => { Modal.Remove(dialog); discard(); })
            .Button(Loc.Get("common.cancel"), () => Modal.Remove(dialog));
    }

    /// <summary>Reset the auto-save timer (e.g. after a manual save).</summary>
    public static void ResetTimer() => _timeSinceLastSave = 0;
}
