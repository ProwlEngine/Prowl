// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;

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

    /// <summary>
    /// Render this modal's window content INLINE in the current layout flow (no backdrop, no
    /// positioning, no stack) so the very same modal can be reused as an embedded panel. This is
    /// what lets the modal system as a whole support embedding — a floating modal and an embedded
    /// one share one render path. Default: not embeddable; embeddable modals override it.
    /// </summary>
    void DrawEmbedded(Paper paper, string id)
        => throw new NotSupportedException($"{GetType().Name} does not support embedding.");
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
    public float Width = 344f;
    public float Height;
    public bool CloseOnBackdrop { get; set; }
    public bool CloseOnEscape { get; set; } = true;

    /// <summary>Optional leading vector icon in the title bar (host paints into the slot rect).</summary>
    public IOrigamiIcon? Icon;

    public DialogModal Button(string label, Action onClick, OrigamiVariant variant = OrigamiVariant.Default)
    {
        Buttons.Add((label, onClick, variant));
        return this;
    }

    // Nebula .w2modal / .w2mm tokens.

    public void Draw(Paper paper, int layer, int stackIndex)
    {
        var theme = Origami.Current;
        if (theme.Font == null) return;

        float screenW = (float)paper.ScreenRect.Size.X;
        float screenH = (float)paper.ScreenRect.Size.Y;
        float offsetY = stackIndex * 20f;
        float dialogX = (screenW - Width) / 2;
        float dialogY = screenH * 0.24f + offsetY;

        var container = paper.Column($"omd_dlg_{stackIndex}")
            .PositionType(PositionType.SelfDirected)
            .Position(dialogX, dialogY)
            .Width(Width).Height(Height > 0 ? UnitValue.Pixels(Height) : UnitValue.Auto)
            .BackgroundColor(theme.Popover)
            .BorderColor(theme.BorderStrong).BorderWidth(1)
            .Rounded(13f).Clip()
            .BoxShadow(0, 24, 64, 0, Color.FromArgb(166, 0, 0, 0))
            .Layer(layer)
            .StopEventPropagation();

        using (container.Enter())
            DrawInner(paper, $"omd_{stackIndex}", embedded: false, onClose: () => Modal.Remove(this));
    }

    /// <summary>Fixed embedded width in px; null = stretch to fill. Set by <see cref="ModalBuilder.ShowEmbedded"/>.</summary>
    public float? EmbeddedWidth;

    /// <summary>
    /// Render this modal's window chrome inline in the current layout flow (no backdrop, no
    /// overlay layer) — the embedded half of the shared render path. Use for anatomy previews /
    /// embedding a dialog inside a panel.
    /// </summary>
    public void DrawEmbedded(Paper paper, string id)
    {
        if (Origami.Current.Font == null) return;
        var container = paper.Column(id)
            .Width(EmbeddedWidth.HasValue ? UnitValue.Pixels(EmbeddedWidth.Value) : UnitValue.Stretch())
            .Height(Height > 0 ? UnitValue.Pixels(Height) : UnitValue.Auto)
            .BorderColor(Origami.Current.BorderSoft).BorderWidth(1)
            .Rounded(9f).Clip();

        using (container.Enter())
            DrawInner(paper, id, embedded: true, onClose: null);
    }

    private void DrawInner(Paper paper, string idp, bool embedded, Action? onClose)
    {
        var theme = Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font!;
        var ink = theme.Ink;
        float radius = embedded ? 9f : 13f;
        float headH = m.FontSize + 18f;

        // ── Head (.w2mm-head): glass-in strip, leading icon + title + close X ──
        using (paper.Row($"{idp}_head").Width(UnitValue.Stretch()).Height(headH)
            .BackgroundColor(theme.Glass).RoundedTop(radius)
            .Padding(11, 11, 0, 0).RowBetween(8)
            .Enter())
        {
            if (Icon != null)
            {
                var icon = Icon;
                paper.Box($"{idp}_hico").Width(15).Height(headH).IsNotInteractable()
                    .OnPostLayout((h, r) => paper.Draw(ref h, (canvas, rr) =>
                    {
                        const float sz = 15f;
                        float ix = (float)(rr.Min.X + (rr.Size.X - sz) * 0.5f);
                        float iy = (float)(rr.Min.Y + (rr.Size.Y - sz) * 0.5f);
                        icon.Draw(canvas, new Prowl.Vector.Rect(ix, iy, ix + sz, iy + sz), ink.C500);
                    }));
            }

            paper.Box($"{idp}_title").Width(UnitValue.Stretch()).Height(headH)
                .Text(Title, theme.SemiBold ?? font).TextColor(ink.C500)
                .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

            if (onClose != null)
            {
                var close = onClose;
                paper.Box($"{idp}_x").Width(18).Height(18)
                    .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .Rounded(5f)
                    .Hovered.BackgroundColor(theme.Hover).End()
                    .OnClick(0, (_, _) => close())
                    .OnPostLayout((h, r) => paper.Draw(ref h, (canvas, rr) =>
                    {
                        float cx = (float)(rr.Min.X + rr.Size.X * 0.5f);
                        float cy = (float)(rr.Min.Y + rr.Size.Y * 0.5f);
                        canvas.SaveState();
                        canvas.SetStrokeColor(ink.C200);
                        canvas.SetStrokeWidth(1.4f);
                        canvas.SetStrokeCap(EndCapStyle.Round);
                        canvas.BeginPath();
                        canvas.MoveTo(cx - 3.2f, cy - 3.2f); canvas.LineTo(cx + 3.2f, cy + 3.2f);
                        canvas.MoveTo(cx + 3.2f, cy - 3.2f); canvas.LineTo(cx - 3.2f, cy + 3.2f);
                        canvas.Stroke();
                        canvas.RestoreState();
                    }));
            }
        }

        paper.Box($"{idp}_hdiv").Width(UnitValue.Stretch()).Height(1).BackgroundColor(theme.BorderSoft).IsNotInteractable();

        // ── Body (.w2mm-body): t-mid text, padding 11 ──
        using (paper.Column($"{idp}_body")
            .Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Padding(11, 11, 11, 11).ColBetween(m.SpacingMedium)
            .TextColor(ink.C300).FontSize(m.FontSize)
            .Enter())
        {
            DrawContent?.Invoke(paper);
        }

        // ── Foot (.w2mm-foot): right-aligned buttons, border-top ──
        if (Buttons.Count > 0)
        {
            paper.Box($"{idp}_fdiv").Width(UnitValue.Stretch()).Height(1).BackgroundColor(theme.BorderSoft).IsNotInteractable();
            using (paper.Row($"{idp}_foot").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Padding(11, 11, 10, 10).RowBetween(8).ChildLeft(UnitValue.Stretch())
                .Enter())
            {
                for (int b = 0; b < Buttons.Count; b++)
                {
                    var (label, onClick, variant) = Buttons[b];
                    var btn = Origami.Button(paper, $"{idp}_btn_{b}", label, onClick);
                    if (embedded) btn.Small();
                    bool primaryDefault = variant == OrigamiVariant.Primary
                        || (variant == OrigamiVariant.Default && b == Buttons.Count - 1);
                    if (primaryDefault) btn.Primary();
                    else if (variant == OrigamiVariant.Danger) btn.Danger().Soft();
                    else if (variant == OrigamiVariant.Success) btn.Success();
                    else if (variant == OrigamiVariant.Warning) btn.Warning();
                    else if (variant == OrigamiVariant.Info) btn.Info();
                    btn.Show();
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

    /// <summary>
    /// Render any embeddable modal INLINE in the current layout flow (no stack, no backdrop) — the
    /// same content it would draw when floating. Throws if the modal doesn't support embedding.
    /// </summary>
    public static void DrawEmbedded(Paper paper, string id, IModal modal) => modal.DrawEmbedded(paper, id);

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
    private float _width = 344f;
    private float _height;
    private bool _closeOnBackdrop;
    private bool _closeOnEscape = true;
    private IOrigamiIcon? _icon;
    private readonly List<(string Label, Action OnClick, OrigamiVariant Variant)> _buttons = [];

    internal ModalBuilder(string title) => _title = title;

    /// <summary>Set the modal body content via a draw callback.</summary>
    public ModalBuilder Content(Action<Paper> draw) { _content = draw; return this; }

    /// <summary>Set an optional leading vector icon in the title bar.</summary>
    public ModalBuilder Icon(IOrigamiIcon? icon) { _icon = icon; return this; }

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

    /// <summary>Build the configured <see cref="DialogModal"/> without pushing it.</summary>
    public DialogModal Build()
    {
        var entry = new DialogModal
        {
            Title = _title,
            DrawContent = _content,
            Icon = _icon,
            Width = _width,
            Height = _height,
            CloseOnBackdrop = _closeOnBackdrop,
            CloseOnEscape = _closeOnEscape,
        };
        foreach (var (label, onClick, variant) in _buttons)
            entry.Button(label, onClick, variant);
        return entry;
    }

    /// <summary>Push the configured modal onto the stack.</summary>
    public void Show() => Modal.Push(Build());

    /// <summary>Render the configured modal's window chrome inline (embedded, no overlay/backdrop).</summary>
    public void ShowEmbedded(Paper paper, string id, float? width = null)
    {
        var m = Build();
        m.EmbeddedWidth = width;
        m.DrawEmbedded(paper, id);
    }
}
