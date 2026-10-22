using System;

using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using static Prowl.PaperUI.ElementBuilder;

namespace Prowl.Editor.GUI.Popups;

/// <summary>
/// The editor's one inline rename. A caller starts it with <see cref="Begin"/> and draws it with <see cref="Draw"/> in
/// place of the item's label. Enter or a click anywhere else commits, Escape cancels. The text starts focused and
/// selected, and a rename whose item stops being drawn, such as one scrolled away or in a closed panel, cancels.
/// </summary>
public static class RenameOverlay
{
    private const int MissedFramesBeforeCancel = 3;

    private static bool _active;
    private static string _text = "";
    private static Action<string>? _onConfirm;
    private static Action? _onCancel;
    private static string? _activeId;
    private static bool _drawn;
    private static int _missedFrames;

    /// <summary> Whether a rename operation is currently active. </summary>
    public static bool IsActive => _active;

    /// <summary>
    /// Begins renaming an item, cancelling any other rename. onConfirm gets the trimmed text, onCancel runs on Escape,
    /// on blank text, and when the item stops being drawn.
    /// </summary>
    public static void Begin(string itemId, string initialText, Action<string> onConfirm, Action? onCancel = null)
    {
        Cancel();
        _active = true;
        _activeId = itemId;
        _text = initialText;
        _onConfirm = onConfirm;
        _onCancel = onCancel;
        _drawn = false;
        _missedFrames = 0;
    }

    /// <summary> Cancels the active rename operation and invokes the onCancel callback. </summary>
    public static void Cancel()
    {
        if (!_active) return;
        var onCancel = _onCancel;
        Reset();
        onCancel?.Invoke();
    }

    private static void Confirm()
    {
        if (!_active) return;
        var onConfirm = _onConfirm;
        var onCancel = _onCancel;
        string text = _text.Trim();
        Reset();
        if (text.Length > 0) onConfirm?.Invoke(text);
        else onCancel?.Invoke();
    }

    private static void Reset()
    {
        _active = false;
        _onConfirm = null;
        _onCancel = null;
        _activeId = null;
    }

    /// <summary> Returns true if a rename operation is active and targets the specified itemId. </summary>
    public static bool IsRenaming(string itemId) => _active && _activeId == itemId;

    /// <summary> Runs once per editor frame after the panels drew, to cancel a rename nobody draws any more. </summary>
    public static void EndFrame()
    {
        if (!_active) return;
        if (_drawn) _missedFrames = 0;
        else if (++_missedFrames >= MissedFramesBeforeCancel) Cancel();
        _drawn = false;
    }

    /// <summary> Draws the rename field in the current layout flow, where the item's label would be. </summary>
    public static void Draw(Paper paper, string id, float height = 0)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null || !_active) return;
        _drawn = true;

        if (paper.IsKeyPressed(PaperKey.Enter) || paper.IsKeyPressed(PaperKey.KeypadEnter)) { Confirm(); return; }
        if (paper.IsKeyPressed(PaperKey.Escape)) { Cancel(); return; }

        // A click anywhere outside the field commits, like renaming in a file explorer.
        paper.Box($"{id}_backdrop")
            .PositionType(PositionType.SelfDirected)
            .Position(-9999, -9999)
            .Size(99999, 99999)
            .Layer(Layer.Topmost)
            .IsNotFocusable()
            .StopEventPropagation()
            .OnClick(0, (_, _) => Confirm())
            .OnRightClick(0, (_, _) => Confirm());

        using (paper.Box($"{id}_field")
            .Width(UnitValue.Stretch())
            .Height(height > 0 ? height : EditorTheme.RowHeight)
            .Rounded(EditorTheme.Roundness)
            .BorderWidth(1)
            .BackgroundColor(EditorTheme.Neutral100)
            .BorderColor(EditorTheme.Purple400)
            .Layer(Layer.Topmost)
            .StopEventPropagation()
            .Enter())
        {
            ElementHandle field = paper.CurrentParent;
            TextInputSettings settings = TextInputSettings.Default;
            settings.Font = font;
            settings.TextColor = EditorTheme.Ink500;
            settings.SelectAllOnFocus = true;

            paper.Box($"{id}_tf")
                .Margin(4, UnitValue.Stretch())
                .HookToParent()
                .IsNotInteractable()
                .Alignment(TextAlignment.MiddleLeft)
                .Width(UnitValue.Stretch())
                .Height(EditorTheme.FontSize)
                .FontSize(EditorTheme.FontSizeSmall)
                .TextField(_text, settings,
                    onChange: v => _text = v,
                    intID: _activeId?.GetHashCode() ?? 0);

            // The field row takes the focus, after its text child exists so the focus reaches it and selects the text.
            // Clicks inside keep the row focused, so they place the caret instead of selecting everything again.
            if (!paper.IsElementFocused(field.Data.ID))
                paper.SetFocus(field);
        }
    }
}
