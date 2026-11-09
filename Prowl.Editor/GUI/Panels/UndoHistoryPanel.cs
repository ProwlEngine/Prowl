// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// The live undo history, oldest first, with the current position marked and the redo steps after it greyed.
/// Clicking an undo step undoes down to and including it, clicking a redo step redoes up to and including it.
/// </summary>
public class UndoHistoryPanel : DockPanel
{
    [MenuItem("Window/General/Undo History", priority: 1)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(UndoHistoryPanel));

    public override string Title => Loc.Get("panel.undo_history");
    public override string Icon => EditorIcons.ClockRotateLeft;

    private const float RowHeight = 24;

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        string which = Application.IsPlaying ? Loc.Get("undo_history.play")
            : PrefabEditingMode.IsEditing ? Loc.Get("undo_history.prefab")
            : Loc.Get("undo_history.edit");

        using (paper.Column("uh_root").Width(width).Height(height).Clip().Enter())
        {
            paper.Box("uh_header").Height(RowHeight).Padding(8, 8, 0, 0)
                .Text(which, EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            paper.Box("uh_div").Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            IReadOnlyList<UndoStepInfo> undo = Undo.UndoSteps;
            IReadOnlyList<UndoStepInfo> redo = Undo.RedoSteps;
            bool locked = Undo.InteractionActive;

            Origami.ScrollView(paper, "uh_scroll", width, height - RowHeight - 1).Body(() =>
            {
                using (paper.Column("uh_rows").Height(UnitValue.Auto).Enter())
                {
                    Row(paper, "uh_start", Loc.Get("undo_history.start"), "", font, dim: false, current: undo.Count == 0, onClick: null);
                    for (int i = 0; i < undo.Count; i++)
                    {
                        UndoStepInfo step = undo[i];
                        long stepId = step.Id;
                        Row(paper, $"uh_u{step.Id}", step.Name, Describe(step), font, dim: false, current: i == undo.Count - 1,
                            onClick: locked ? null : () => Undo.PerformUndoTo(stepId));
                    }
                    for (int i = 0; i < redo.Count; i++)
                    {
                        UndoStepInfo step = redo[i];
                        long stepId = step.Id;
                        Row(paper, $"uh_r{step.Id}", step.Name, Describe(step), font, dim: true, current: false,
                            onClick: locked ? null : () => Undo.PerformRedoTo(stepId));
                    }
                }
            });
        }
    }

    private static string Describe(UndoStepInfo step)
        => step.OwnerNames.Count == 0 ? step.Time.ToString("HH:mm:ss") : $"{step.Time:HH:mm:ss}  {string.Join(", ", step.OwnerNames)}";

    private static void Row(Paper paper, string id, string name, string detail, Scribe.FontFile font, bool dim, bool current, System.Action? onClick)
    {
        var row = paper.Row(id).Height(RowHeight).Padding(8, 8, 0, 0).Gap(8)
            .BackgroundColor(current ? EditorTheme.Glass : System.Drawing.Color.Transparent);
        if (onClick != null)
            row.Hovered.BackgroundColor(System.Drawing.Color.FromArgb(13, EditorTheme.Purple400)).End()
               .OnClick(0, (_, _) => onClick()).Cursor(PaperCursor.Pointer);

        using (row.Enter())
        {
            paper.Box($"{id}_name").Width(UnitValue.Stretch()).Height(RowHeight).IsNotInteractable()
                .Text(name, font).TextColor(dim ? EditorTheme.Ink300 : EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
            paper.Box($"{id}_detail").Width(UnitValue.Auto).Height(RowHeight).IsNotInteractable()
                .Text(detail, font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleRight);
        }
    }
}
