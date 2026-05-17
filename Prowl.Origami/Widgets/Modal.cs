// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Interface for anything that can be pushed onto the modal stack.
/// Implement this to create custom modals (file dialogs, asset selectors, etc.)
/// that participate in the unified stacking system.
/// </summary>
public interface IModal
{
    /// <summary>Whether clicking the backdrop closes this modal.</summary>
    bool CloseOnBackdrop { get; }

    /// <summary>Whether pressing Escape closes this modal.</summary>
    bool CloseOnEscape { get; }

    /// <summary>
    /// Draw the modal content. The system provides the backdrop and layer management;
    /// the implementation draws its own window/panel. Use the provided layer value
    /// for all elements (backdrop is already drawn at layer - 1).
    /// </summary>
    /// <param name="paper">The Paper instance.</param>
    /// <param name="layer">The layer this modal should render on (backdrop is layer - 1).</param>
    /// <param name="stackIndex">Position in the stack (0 = bottom). Use for visual offset.</param>
    void Draw(Paper paper, int layer, int stackIndex);
}

/// <summary>
/// Built-in dialog modal with title, content callback, and buttons.
/// Covers the common "confirm / message / custom dialog" use cases.
/// </summary>
public sealed class DialogModal : IModal
{
    public string Title = "";
    public Action<Paper>? DrawContent;
    public List<(string Label, Action OnClick, OrigamiVariant Variant)> Buttons = [];
    public float Width = 400f;
    public float Height;
    public bool CloseOnBackdrop { get; set; }
    public bool CloseOnEscape { get; set; } = true;

    public DialogModal Button(string label, Action onClick, OrigamiVariant variant = OrigamiVariant.Default)
    {
        Buttons.Add((label, onClick, variant));
        return this;
    }

    public void Draw(Paper paper, int layer, int stackIndex)
    {
        var theme = Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;
        var ink = theme.Ink;
        if (font == null) return;

        float screenW = (float)paper.ScreenRect.Size.X;
        float screenH = (float)paper.ScreenRect.Size.Y;
        float offsetY = stackIndex * 20f;
        float dialogX = (screenW - Width) / 2;
        float dialogY = screenH * 0.25f + offsetY;

        var dialogBuilder = paper.Column($"omd_dlg_{stackIndex}")
            .PositionType(PositionType.SelfDirected)
            .Position(dialogX, dialogY)
            .Width(Width);

        if (Height > 0) dialogBuilder.Height(Height);
        else dialogBuilder.Height(UnitValue.Auto);

        using (dialogBuilder
            .BackgroundColor(theme.Neutral.C300)
            .BorderColor(ink.C200).BorderWidth(1)
            .Rounded(m.ContainerRounding)
            .BoxShadow(0, 4, 20, 0, Color.FromArgb(80, 0, 0, 0))
            .Layer(layer)
            .StopEventPropagation()
            .Enter())
        {
            // Title bar
            paper.Box($"omd_title_{stackIndex}")
                .Height(m.RowHeight + m.PaddingLarge)
                .BackgroundColor(theme.Neutral.C200)
                .Rounded(m.ContainerRounding, m.ContainerRounding, 0, 0)
                .Text(Title, font)
                .TextColor(ink.C500)
                .FontSize(theme.Metrics.FontSize + 1)
                .Alignment(TextAlignment.MiddleCenter);

            // Content
            using (paper.Column($"omd_body_{stackIndex}")
                .Width(UnitValue.Stretch())
                .Height(UnitValue.Auto)
                .Padding(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.PaddingLarge)
                .ColBetween(m.SpacingMedium)
                .Enter())
            {
                DrawContent?.Invoke(paper);
            }

            // Buttons
            if (Buttons.Count > 0)
            {
                using (paper.Row($"omd_btns_{stackIndex}")
                    .Height(m.RowHeight + m.CompactHeight)
                    .ChildRight(m.PaddingLarge).ChildBottom(m.SpacingLarge)
                    .RowBetween(m.SpacingLarge)
                    .ChildLeft(UnitValue.Stretch())
                    .Enter())
                {
                    for (int b = 0; b < Buttons.Count; b++)
                    {
                        var (label, onClick, variant) = Buttons[b];
                        var btn = Origami.Button(paper, $"omd_btn_{stackIndex}_{b}", label, onClick).Width(80);
                        if (variant == OrigamiVariant.Primary || (variant == OrigamiVariant.Default && b == 0))
                            btn.Primary();
                        else if (variant == OrigamiVariant.Danger) btn.Danger();
                        else if (variant == OrigamiVariant.Success) btn.Success();
                        else if (variant == OrigamiVariant.Warning) btn.Warning();
                        else if (variant == OrigamiVariant.Info) btn.Info();
                        btn.Show();
                    }
                }
            }
        }
    }
}

/// <summary>
/// A modal that wraps a custom draw callback. The callback is responsible for
/// rendering its own window chrome. Use for full-screen modals like file dialogs,
/// asset selectors, or anything that needs complete layout control.
/// </summary>
public sealed class CustomDrawModal : IModal
{
    private readonly Action<Paper, int, int> _draw;

    public bool CloseOnBackdrop { get; set; }
    public bool CloseOnEscape { get; set; } = true;

    /// <param name="draw">Callback: (paper, layer, stackIndex). Render your window on the given layer.</param>
    public CustomDrawModal(Action<Paper, int, int> draw) => _draw = draw;

    public void Draw(Paper paper, int layer, int stackIndex) => _draw(paper, layer, stackIndex);
}

/// <summary>
/// Static push/pop modal stack for Origami. Any <see cref="IModal"/> can be pushed.
/// Modals stack with progressively darker backdrops. Renders above Layer.Overlay + 1000.
///
/// Built-in modal types:
/// - <see cref="DialogModal"/> for confirm/message/custom dialogs
/// - <see cref="CustomDrawModal"/> for full-control modals (file dialogs, selectors)
///
/// Convenience methods: <see cref="Confirm"/>, <see cref="Message"/>, <see cref="Custom"/>.
/// </summary>
public static class Modal
{
    private static readonly List<IModal> _stack = [];

    public static int Count => _stack.Count;
    public static bool IsOpen => _stack.Count > 0;

    /// <summary>Push any modal onto the stack.</summary>
    public static void Push(IModal modal) => _stack.Add(modal);

    /// <summary>Pop the topmost modal.</summary>
    public static void Pop()
    {
        if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
    }

    /// <summary>Pop a specific modal from the stack (regardless of position).</summary>
    public static void Remove(IModal modal) => _stack.Remove(modal);

    /// <summary>Pop all modals.</summary>
    public static void PopAll() => _stack.Clear();

    // ── Convenience shortcuts ────────────────────────────────

    /// <summary>Push a confirmation dialog with Yes/No buttons.</summary>
    public static void Confirm(string title, string message, Action onYes, Action? onNo = null)
    {
        var entry = new DialogModal { Title = title, Width = 380, CloseOnEscape = true };
        entry.DrawContent = paper => Origami.Label(paper, "modal_msg", message).Show();
        entry.Button("Yes", () => { onYes(); Pop(); }, OrigamiVariant.Primary);
        entry.Button("No", () => { onNo?.Invoke(); Pop(); });
        Push(entry);
    }

    /// <summary>Push a message dialog with an OK button.</summary>
    public static void Message(string title, string message)
    {
        var entry = new DialogModal { Title = title, Width = 380, CloseOnEscape = true };
        entry.DrawContent = paper => Origami.Label(paper, "modal_msg", message).Show();
        entry.Button("OK", Pop, OrigamiVariant.Primary);
        Push(entry);
    }

    /// <summary>Push a dialog modal with caller-defined content and buttons.</summary>
    public static DialogModal Custom(string title, Action<Paper> drawContent, float width = 400, float height = 0)
    {
        var entry = new DialogModal
        {
            Title = title,
            DrawContent = drawContent,
            Width = width,
            Height = height,
        };
        Push(entry);
        return entry;
    }

    /// <summary>Push a fully custom modal where the caller controls all rendering.</summary>
    public static CustomDrawModal PushCustomDraw(Action<Paper, int, int> draw, bool closeOnEscape = true, bool closeOnBackdrop = false)
    {
        var modal = new CustomDrawModal(draw);
        modal.CloseOnEscape = closeOnEscape;
        modal.CloseOnBackdrop = closeOnBackdrop;
        Push(modal);
        return modal;
    }

    // ── Draw ─────────────────────────────────────────────────

    private const int BaseLayer = Layer.Overlay + 1000;

    public static void Draw(Paper paper)
    {
        if (_stack.Count == 0) return;

        for (int i = 0; i < _stack.Count; i++)
        {
            var modal = _stack[i];
            int layer = BaseLayer + i * 2;
            int capturedIndex = i;

            // Backdrop - progressively darker for each stacked modal
            byte alpha = (byte)Math.Min(200, 80 + i * 40);
            paper.Box($"omd_bg_{i}")
                .PositionType(PositionType.SelfDirected)
                .Position(0, 0)
                .Size(UnitValue.Stretch(), UnitValue.Stretch())
                .BackgroundColor(Color.FromArgb(alpha, 0, 0, 0))
                .Layer(layer)
                .StopEventPropagation()
                .OnClick(capturedIndex, (idx, _) =>
                {
                    if (idx < _stack.Count && _stack[idx].CloseOnBackdrop)
                        _stack.RemoveAt(idx);
                });

            // Let the modal draw itself on layer + 1
            modal.Draw(paper, layer + 1, i);

            // Escape handling for the topmost modal only
            if (i == _stack.Count - 1 && modal.CloseOnEscape && paper.IsKeyPressed(PaperKey.Escape))
            {
                _stack.RemoveAt(i);
                break;
            }
        }
    }
}

/// <summary>
/// Fluent builder for dialog modals. Construct via <see cref="Origami.Modal(string)"/>
/// and call <see cref="Show"/> to push onto the modal stack.
/// </summary>
public sealed class ModalBuilder
{
    private string _title;
    private Action<Paper>? _content;
    private float _width = 400f;
    private float _height;
    private bool _closeOnBackdrop;
    private bool _closeOnEscape = true;
    private readonly List<(string Label, Action OnClick, OrigamiVariant Variant)> _buttons = [];

    internal ModalBuilder(string title) => _title = title;

    /// <summary>Set the modal body content via a draw callback.</summary>
    public ModalBuilder Content(Action<Paper> draw) { _content = draw; return this; }

    /// <summary>Set body content to a simple text message.</summary>
    public ModalBuilder Message(string text)
    {
        _content = paper => Origami.Label(paper, "modal_msg", text).Show();
        return this;
    }

    /// <summary>Set dialog width in pixels (default 400).</summary>
    public ModalBuilder Width(float width) { _width = width; return this; }

    /// <summary>Set dialog height in pixels (0 = auto-size to content).</summary>
    public ModalBuilder Height(float height) { _height = height; return this; }

    /// <summary>Allow closing by clicking the backdrop.</summary>
    public ModalBuilder CloseOnBackdrop(bool value = true) { _closeOnBackdrop = value; return this; }

    /// <summary>Allow closing with Escape key (default true).</summary>
    public ModalBuilder CloseOnEscape(bool value = true) { _closeOnEscape = value; return this; }

    /// <summary>Add a button to the dialog footer.</summary>
    public ModalBuilder Button(string label, Action onClick, OrigamiVariant variant = OrigamiVariant.Default)
    {
        _buttons.Add((label, onClick, variant));
        return this;
    }

    /// <summary>Add a primary-styled button.</summary>
    public ModalBuilder PrimaryButton(string label, Action onClick)
        => Button(label, onClick, OrigamiVariant.Primary);

    /// <summary>Add a danger-styled button.</summary>
    public ModalBuilder DangerButton(string label, Action onClick)
        => Button(label, onClick, OrigamiVariant.Danger);

    /// <summary>Push the configured modal onto the stack.</summary>
    public void Show()
    {
        var entry = new DialogModal
        {
            Title = _title,
            DrawContent = _content,
            Width = _width,
            Height = _height,
            CloseOnBackdrop = _closeOnBackdrop,
            CloseOnEscape = _closeOnEscape,
        };
        foreach (var (label, onClick, variant) in _buttons)
            entry.Button(label, onClick, variant);
        Modal.Push(entry);
    }
}
