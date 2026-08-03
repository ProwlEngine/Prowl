// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Build;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.Popups;

/// <summary>
/// Covers the editor while a build runs, showing its progress and the only control that makes sense
/// during one.
/// </summary>
/// <remarks>
/// A build reads the asset database and the script assemblies from a background thread. Leaving the
/// editor usable during one invites importing or deleting an asset the build is halfway through
/// packaging, so the backdrop is the point rather than decoration. It is also why this modal cannot be
/// dismissed: closing it would lift the lock without stopping the build.
/// </remarks>
public sealed class BuildProgressModal : IModal
{
    private const float Width = 420f;

    private readonly BuildProgress _progress;

    private BuildProgressModal(BuildProgress progress) => _progress = progress;

    /// <summary>
    /// Shows the lock for a running build. It takes itself down when the build finishes, and does not
    /// appear at all for one that already has: a build run on the calling thread is over by the time
    /// anything can show a modal for it.
    /// </summary>
    public static void Show(BuildProgress progress)
    {
        if (!progress.IsComplete)
            Modal.Push(new BuildProgressModal(progress));
    }

    public bool CloseOnBackdrop => false;
    public bool CloseOnEscape => false;

    public void Draw(Paper paper, int layer, int stackIndex)
    {
        // Owned here rather than by the Build window, so the lock is correct even when that window is
        // closed and nothing else is polling the build.
        if (_progress.IsComplete)
        {
            Modal.Remove(this);
            return;
        }

        var theme = Origami.Current;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        float screenW = (float)paper.ScreenRect.Size.X;
        float screenH = (float)paper.ScreenRect.Size.Y;

        var container = paper.Column("bpm_root")
            .PositionType(PositionType.SelfDirected)
            .Position((screenW - Width) / 2, screenH * 0.36f)
            .Width(Width).Height(UnitValue.Auto)
            .BackgroundColor(theme.Popover)
            .BorderColor(theme.BorderStrong).BorderWidth(1)
            .Rounded(13f).Clip()
            .DropShadow(0, 24, 64, 0, Color.FromArgb(166, 0, 0, 0))
            .Layer(layer)
            .StopEventPropagation();

        using (container.Enter())
        {
            DrawHeader(paper, font);
            DrawBody(paper, font);
        }
    }

    private static void DrawHeader(Paper paper, FontFile font)
    {
        var theme = Origami.Current;
        float headH = theme.Metrics.FontSize + 18f;

        using (paper.Row("bpm_head").Width(UnitValue.Stretch()).Height(headH)
            .BackgroundColor(theme.Glass).RoundedTop(13f)
            .Padding(13, 13, 0, 0).RowBetween(8).Enter())
        {
            paper.Box("bpm_ico").Width(16).Height(headH).IsNotInteractable()
                .Text(EditorIcons.Hammer, font).TextColor(EditorTheme.Accent)
                .FontSize(theme.Metrics.FontSize).Alignment(TextAlignment.MiddleCenter);

            paper.Box("bpm_title").Width(UnitValue.Stretch()).Height(headH).IsNotInteractable()
                .Text(Loc.Get("build.building"), EditorTheme.FontSemiBold ?? font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(theme.Metrics.FontSize).Alignment(TextAlignment.MiddleLeft);
        }

        paper.Box("bpm_hdiv").Width(UnitValue.Stretch()).Height(1)
            .BackgroundColor(theme.BorderSoft).IsNotInteractable();
    }

    private void DrawBody(Paper paper, FontFile font)
    {
        bool cancelling = _progress.IsCancelled;
        var mono = EditorTheme.FontMono ?? font;

        using (paper.Column("bpm_body").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Padding(13, 13, 13, 13).ColBetween(10).Enter())
        {
            // The last line the build logged, which is the only part of it worth a glance mid build.
            paper.Box("bpm_state").Width(UnitValue.Stretch()).Height(16).IsNotInteractable()
                .Text(StatusLine(), mono).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            Origami.ProgressBar(paper, "bpm_bar", _progress.ProgressValue)
                .Thickness(8).ShowPercent("F0").Show();

            using (paper.Row("bpm_actions").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .ChildLeft(UnitValue.Stretch()).Enter())
            {
                if (cancelling)
                {
                    paper.Box("bpm_cancelling").Width(UnitValue.Auto).Height(22).IsNotInteractable()
                        .Text(Loc.Get("build.cancelling"), font).TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
                }
                else
                {
                    EditorGUI.CtaButton(paper, "bpm_cancel", $"{EditorIcons.Xmark}  {Loc.Get("build.cancel")}",
                        EditorTheme.Red500, _progress.Cancel);
                }
            }
        }
    }

    private string StatusLine()
    {
        string message = _progress.GetState()?.Message ?? "";
        int newline = message.IndexOf('\n');
        return (newline >= 0 ? message[..newline] : message).Trim();
    }
}
