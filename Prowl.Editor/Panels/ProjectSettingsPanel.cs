using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Project Settings")]
public class ProjectSettingsPanel : DockPanel
{
    public override string Title => "Project Settings";
    public override string Icon => EditorIcons.Gear;

    private int _selectedIndex;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var entries = ProjectSettingsRegistry.Entries;
        if (entries.Count == 0)
        {
            paper.Box("ps_empty").Size(width, height)
                .Text("No settings registered", font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        if (_selectedIndex >= entries.Count) _selectedIndex = 0;

        using (paper.Row("ps_root").Size(width, height).Enter())
        {
            // Left sidebar — category list
            float sidebarW = 180f;
            using (paper.Column("ps_sidebar")
                .Width(sidebarW).Height(height)
                .BackgroundColor(EditorTheme.Neutral200)
                .Enter())
            {
                paper.Box("ps_sidebar_header")
                    .Height(28).ChildLeft(8)
                    .Text("Settings", font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                EditorGUI.Separator(paper, "ps_sidebar_sep");

                for (int i = 0; i < entries.Count; i++)
                {
                    int idx = i;
                    var entry = entries[i];
                    bool isSelected = _selectedIndex == i;
                    string icon = string.IsNullOrEmpty(entry.Icon) ? EditorIcons.Gear : entry.Icon;

                    paper.Box($"ps_cat_{i}")
                        .Height(26).ChildLeft(8).Rounded(3)
                        .BackgroundColor(isSelected ? EditorTheme.Purple400 : Color.Transparent)
                        .Hovered.BackgroundColor(isSelected ? EditorTheme.Purple400 : EditorTheme.ButtonHovered).End()
                        .Text($"{icon}  {entry.Name}", font)
                        .TextColor(isSelected ? EditorTheme.Ink500 : EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 1)
                        .Alignment(TextAlignment.MiddleLeft)
                        .OnClick(idx, (id, _) => _selectedIndex = id);
                }
            }

            // Separator
            paper.Box("ps_divider").Width(1).Height(height).BackgroundColor(EditorTheme.Ink200);

            // Right content — selected settings
            float contentW = width - sidebarW - 1;
            using (ScrollView.Begin(paper, "ps_content", contentW, height))
            {
                paper.Box("ps_content_pad").Height(8);
                entries[_selectedIndex].Instance.OnGUI(paper, contentW - 16);
                paper.Box("ps_content_pad2").Height(16);
            }
        }
    }
}
