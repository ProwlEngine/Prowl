using System;

using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using static Prowl.PaperUI.ElementBuilder;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.Popups;

/// <summary>
/// Shared rename system. Same architecture as ContextMenuHelper backdrop + floating field
/// drawn at the call site using SelfDirected + Layer.Topmost.
/// </summary>
public static class RenameOverlay
{
    public enum Position
    {
        Top,
        Bottom
    }

    private static bool _active;
    private static string _text = "";
    private static Action<string>? _onConfirm;
    private static Action? _onCancel;
    private static string? _activeId;

    public static bool IsActive => _active;

    public static void Begin(string itemId, string initialText, Action<string> onConfirm, Action? onCancel = null)
    {
        _active = true;
        _activeId = itemId;
        _text = initialText;
        _onConfirm = onConfirm;
        _onCancel = onCancel;
    }

    public static void Cancel()
    {
        if (!_active) return;
        _active = false;
        _onCancel?.Invoke();
        _onConfirm = null;
        _onCancel = null;
        _activeId = null;
    }

    private static void Confirm()
    {
        if (!_active) return;
        _active = false;
        if (!string.IsNullOrWhiteSpace(_text))
            _onConfirm?.Invoke(_text);
        _onConfirm = null;
        _onCancel = null;
        _activeId = null;
    }

    public static bool IsRenaming(string itemId) => _active && _activeId == itemId;

    /// <summary>
    /// Draw the rename field at this location. Renders a fullscreen backdrop behind it
    /// and the text field on top, both on Layer.Topmost. Same pattern as ContextMenuHelper.
    /// </summary>
    public static void Draw(Paper paper, string id, Position position = Position.Top)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Handle keys
        bool confirm = paper.IsKeyDown(PaperKey.Enter) || paper.IsKeyDown(PaperKey.KeypadEnter);
        bool cancel = paper.IsKeyDown(PaperKey.Escape);
        if (confirm) { Confirm(); return; }
        if (cancel) { Cancel(); return; }

        // Fullscreen backdrop click to cancel
        paper.Box($"{id}_backdrop")
            .PositionType(PositionType.SelfDirected)
            .Position(-9999, -9999)
            .Size(99999, 99999)
            .BackgroundColor(Color.FromArgb(85, 0, 0, 0))
            .Layer(Layer.Topmost)
            .StopEventPropagation()
            .OnClick(0, (_, _) => Cancel())
            .OnRightClick(0, (_, _) => Cancel());

        // Rename field positioned at (0,0) relative to parent, on top of backdrop
        using (paper.Box($"{id}_field")
            .PositionType(PositionType.SelfDirected)
            .Position(0, (position == Position.Top ? 0 : UnitValue.Stretch()))
            .Width(UnitValue.Stretch())
            .Height(EditorTheme.RowHeight)
            .Rounded(3)
            .BorderWidth(1)
            .BackgroundColor(EditorTheme.Neutral100)
            .BorderColor(EditorTheme.Purple400)
            .Layer(Layer.Topmost)
            .StopEventPropagation()
            .TabIndex(0)
            .Enter())
        {
            TextInputSettings settings = TextInputSettings.Default;
            settings.Font = font;
            settings.TextColor = EditorTheme.Ink500;
            settings.SelectAllOnFocus = true;

            var textField = paper.Box($"{id}_tf")
                .Margin(4, UnitValue.Stretch())
                .HookToParent()
                .IsNotInteractable()
                .Alignment(TextAlignment.MiddleLeft)
                .Width(UnitValue.Stretch())
                .Height(EditorTheme.FontSize)
                .FontSize(EditorTheme.FontSize - 1)
                .TextField(_text, settings,
                    onChange: v => _text = v,
                    intID: _activeId?.GetHashCode() ?? 0);

            if (!paper.IsElementFocused(textField._handle.Data.ID))
                paper.SetFocus(textField._handle);
        }
    }
}
