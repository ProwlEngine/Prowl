using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

/// <summary>
/// A modal dialog entry with title, content renderer, and buttons.
/// </summary>
public class ModalDialogEntry
{
    public string Title;
    public Action<Paper> DrawContent;
    public List<(string label, Action onClick)> Buttons = new();
    public float Width;
    public float Height;

    public ModalDialogEntry(string title, Action<Paper> drawContent, float width = 400, float height = 0)
    {
        Title = title;
        DrawContent = drawContent;
        Width = width;
        Height = height;
    }

    public ModalDialogEntry Button(string label, Action onClick)
    {
        Buttons.Add((label, onClick));
        return this;
    }
}

/// <summary>
/// Global modal dialog system. Only one modal can be open at a time.
/// Draws on the Overlay layer with a semi-transparent background blocking input.
/// </summary>
public static class ModalDialog
{
    private static ModalDialogEntry? _current;

    public static bool IsOpen => _current != null;

    /// <summary>
    /// Show a modal dialog. Replaces any existing modal.
    /// </summary>
    public static void Show(ModalDialogEntry dialog) => _current = dialog;

    /// <summary>
    /// Show a simple confirmation dialog.
    /// </summary>
    public static void Confirm(string title, string message, Action onYes, Action? onNo = null)
    {
        Show(new ModalDialogEntry(title, paper =>
        {
            EditorGUI.Label(paper, "modal_msg", message);
        }, 350)
        .Button("Yes", () => { onYes(); Close(); })
        .Button("No", () => { onNo?.Invoke(); Close(); }));
    }

    /// <summary>
    /// Show a simple message dialog with an OK button.
    /// </summary>
    public static void Message(string title, string message)
    {
        Show(new ModalDialogEntry(title, paper =>
        {
            EditorGUI.Label(paper, "modal_msg", message);
        }, 350)
        .Button("OK", Close));
    }

    public static void Close() => _current = null;

    /// <summary>
    /// Draw the modal if one is open. Call from EditorApplication.EndGui.
    /// </summary>
    public static void Draw(Paper paper)
    {
        if (_current == null) return;
        var font = EditorTheme.DefaultFont;
        var modal = _current;

        int w = Prowl.Runtime.Window.InternalWindow.Size.X;
        int h = Prowl.Runtime.Window.InternalWindow.Size.Y;

        // Fullscreen overlay (blocks input to everything behind)
        paper.Box("modal_overlay")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Size(w, h)
            .BackgroundColor(Color.FromArgb(120, 0, 0, 0))
            .Layer(Layer.Overlay);

        // Dialog box centered
        float dialogW = modal.Width;
        float dialogX = (w - dialogW) / 2;
        float dialogY = modal.Height > 0 ? (h - modal.Height) / 2 : h * 0.3f;

        var dialogBuilder = paper.Column("modal_dialog")
            .PositionType(PositionType.SelfDirected)
            .Position(dialogX, dialogY)
            .Width(dialogW);

        if (modal.Height > 0)
            dialogBuilder.Height(modal.Height);
        else
            dialogBuilder.Height(UnitValue.Auto);

        using (dialogBuilder
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1)
            .Rounded(8)
            .BoxShadow(0, 4, 20, 0, Color.FromArgb(80, 0, 0, 0))
            .Layer(Layer.Overlay)
            .Enter())
        {
            // Title bar
            if (font != null)
            {
                paper.Box("modal_title")
                    .Height(32)
                    .BackgroundColor(EditorTheme.Neutral300)
                    .Rounded(8)
                    .ChildLeft(12)
                    .Text(modal.Title, font)
                    .TextColor(EditorTheme.Text)
                    .FontSize(EditorTheme.FontSize + 1);
            }

            // Content area
            using (paper.Column("modal_content")
                .Width(UnitValue.Stretch())
                .Height(UnitValue.Auto)
                .ChildLeft(16).ChildRight(16).ChildTop(12).ChildBottom(12)
                .RowBetween(6)
                .Enter())
            {
                modal.DrawContent(paper);
            }

            // Button row
            if (modal.Buttons.Count > 0)
            {
                using (paper.Row("modal_buttons")
                    .Height(40)
                    .ChildRight(12).ChildBottom(8)
                    .RowBetween(8)
                    .ChildLeft(UnitValue.Stretch())
                    .Enter())
                {
                    for (int i = 0; i < modal.Buttons.Count; i++)
                    {
                        var (label, onClick) = modal.Buttons[i];
                        bool isPrimary = i == 0;

                        EditorGUI.Button(paper, $"modal_btn_{i}", label, width: 80)
                            .OnValueChanged(v => onClick());
                    }
                }
            }
        }
    }
}
