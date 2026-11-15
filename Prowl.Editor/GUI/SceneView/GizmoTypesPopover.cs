// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// The list of component types that draw gizmos, dropped down from the Scene view toolbar. Each category has a
/// checkbox for all its types, and each type a row that shows or hides its gizmos.
/// </summary>
public static class GizmoTypesPopover
{
    private const float Width = 300f;
    private const float MaxHeight = 480f;
    private const float Pad = 5f;

    // Room kept clear down the right for the scrollbar, so it never covers the checkboxes and the rows do not
    // shift sideways when the list grows long enough to scroll.
    private const float ScrollbarGutter = 10f;

    /// <summary>Draws the popover below <paramref name="trigger"/>. Call from inside the trigger's scope.</summary>
    public static void Draw(Paper paper, string id, ElementHandle trigger)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        float triggerHeight = trigger.Data.LayoutRect.Size.Y > 0 ? (float)trigger.Data.LayoutRect.Size.Y : 26f;

        using (paper.Column($"{id}_pop")
            .PositionType(PositionType.SelfDirected)
            .Position(0, triggerHeight + 4f)
            .Width(Width)
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Popover)
            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1)
            .DropShadow(0, 14, 40, -6, EditorTheme.Shadow)
            .Rounded(Origami.Current.Metrics.ContainerRounding)
            .Padding(Pad, Pad, Pad, Pad)
            .HookToParent()
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .StopEventPropagation()
            .IsNotFocusable()
            .Enter())
        {
            if (GizmoTypes.All.Count == 0)
            {
                EditorGUI.EmptyState(paper, $"{id}_empty", Loc.Get("scene.gizmos.none"), font);
                return;
            }

            Origami.ScrollView(paper, $"{id}_scroll", Width - Pad * 2, MaxHeight)
                .Padding(0)
                .Body(() =>
                {
                    using var body = paper.Column($"{id}_body").Height(UnitValue.Auto).PaddingRight(ScrollbarGutter).Enter();
                    int index = 0;
                    foreach (IGrouping<string, GizmoType> group in GizmoTypes.All.GroupBy(t => t.Category))
                    {
                        GizmoType[] types = [.. group];
                        DrawCategory(paper, font, $"{id}_cat{index}", group.Key, types, first: index == 0);
                        for (int i = 0; i < types.Length; i++)
                            DrawType(paper, font, $"{id}_cat{index}_t{i}", types[i]);
                        index++;
                    }
                });
        }
    }

    private static void DrawCategory(Paper paper, Scribe.FontFile font, string id, string category, GizmoType[] types, bool first)
    {
        int enabled = types.Count(t => GizmoFilter.IsEnabled(t.Type));
        bool all = enabled == types.Length;

        // A filled, bold title bar, so categories stand apart from the type rows under them.
        using (paper.Row(id).Height(EditorTheme.RowHeight + 4).PaddingLeft(9).PaddingRight(6).Gap(9)
            .Margin(0, 0, first ? 0 : 6, 2)
            .Rounded(EditorTheme.Roundness)
            .BackgroundColor(EditorTheme.Neutral400)
            .Enter())
        {
            paper.Box($"{id}_name").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight + 4)
                .Text(category, EditorTheme.FontBold ?? font).TextColor(EditorTheme.Ink700)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable();

            Origami.Checkbox(paper, $"{id}_check", all, v => GizmoPreferences.SetEnabled(types.Select(t => t.Type), v))
                .Indeterminate(enabled > 0 && !all)
                .Show();
        }
    }

    private static void DrawType(Paper paper, Scribe.FontFile font, string id, GizmoType type)
    {
        bool on = GizmoFilter.IsEnabled(type.Type);

        using (paper.Row(id).Height(EditorTheme.RowHeight).PaddingLeft(9).PaddingRight(6).Gap(9)
            .Rounded(EditorTheme.Roundness)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .IsNotFocusable()
            .OnClick(type, (t, _) => GizmoPreferences.SetEnabled(t.Type, !GizmoFilter.IsEnabled(t.Type)))
            .Enter())
        {
            paper.Box($"{id}_name").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                .Text(type.Name, font).TextColor(on ? EditorTheme.Ink500 : EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable();

            paper.Box($"{id}_icon").Width(16).Height(EditorTheme.RowHeight)
                .Text(type.Icon, font).TextColor(on ? EditorTheme.Ink400 : EditorTheme.Ink200)
                .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
                .IsNotInteractable();

            Origami.Checkbox(paper, $"{id}_check", on, v => GizmoPreferences.SetEnabled(type.Type, v)).Show();
        }
    }
}
