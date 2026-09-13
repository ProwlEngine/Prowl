// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime.Navigation;

namespace Prowl.Editor.Projects.Settings;

/// <summary>
/// Project settings for the 32-slot navmesh area table (see <see cref="NavMeshAreas"/> for why a
/// slot's index, not a separately stored id, is its identity). Edits a working copy and pushes it
/// into the runtime registry through <see cref="Apply"/>.
/// </summary>
[ProjectSettings("Areas", EditorIcons.Route, order: 61)]
public class NavMeshAreaSettings : ProjectSettingsBase
{
    /// <summary>Working copy of all 32 slots. Field name matters: the built player reads it by this
    /// exact name (see <see cref="PlayerSettingsLoader.ApplyNavigation"/>).</summary>
    public NavMeshAreaInfo[] Areas = CopyLive();

    /// <summary>Snapshots the live <see cref="NavMeshAreas"/> table into a fresh working copy.</summary>
    private static NavMeshAreaInfo[] CopyLive()
    {
        var copy = new NavMeshAreaInfo[NavMeshAreas.MaxAreas];
        for (int i = 0; i < NavMeshAreas.MaxAreas; i++)
            copy[i] = new NavMeshAreaInfo { Name = NavMeshAreas.GetAreaName(i), Cost = NavMeshAreas.GetAreaCost(i) };
        return copy;
    }

    /// <summary>Pushes <see cref="Areas"/> into the runtime <see cref="NavMeshAreas"/> registry.</summary>
    public override void Apply() => NavMeshAreas.ReplaceAll(Areas);

    /// <summary>Resets the runtime registry and this working copy back to the built-in defaults.</summary>
    public override void ResetToDefaults()
    {
        NavMeshAreas.ResetDefault();
        Areas = CopyLive();
    }

    /// <summary>Applies and persists an edit made through this page's UI.</summary>
    private void Changed()
    {
        Apply();
        EditorRegistries.SaveSettings();
    }

    /// <summary>Draws one row per area slot: its index, an editable name (reserved slots show a fixed
    /// label instead), and a cost slider (disabled for an unnamed, non-reserved slot).</summary>
    public override void OnGUI(Paper paper, float width)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        Origami.Header(paper, "nma_header", $"{EditorIcons.Route}  Areas").Underline().Show();

        for (int i = 0; i < Areas.Length; i++)
        {
            int idx = i;
            bool isReserved = i == NavMeshAreas.Walkable || i == NavMeshAreas.NotWalkable || i == NavMeshAreas.Jump;
            bool isEmpty = !isReserved && string.IsNullOrEmpty(Areas[i].Name);

            using (paper.Row($"nma_row_{i}").Height(24).Gap(4).PaddingLeft(4).Enter())
            {
                paper.Box($"nma_idx_{i}").Width(24).Height(22)
                    .Text(i.ToString(), font).TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);

                using (paper.Box($"nma_name_box_{i}").Width(160).Height(22).Enter())
                {
                    if (isReserved)
                    {
                        paper.Box($"nma_name_lbl_{i}").Height(22)
                            .Text(Areas[i].Name, font).TextColor(EditorTheme.Ink400)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                    }
                    else
                    {
                        Origami.TextField(paper, $"nma_name_tf_{i}", Areas[i].Name, v =>
                        {
                            Areas[idx].Name = v?.Trim() ?? "";
                            Changed();
                        }).Show();
                    }
                }

                using (paper.Box($"nma_cost_box_{i}").Width(UnitValue.Stretch()).Height(22).Enter())
                {
                    IDisposable? dim = isEmpty ? EnableIfAttributeHandler.PushDisabledScope() : null;
                    try
                    {
                        EditorGUI.SettingsSliderField(paper, $"nma_cost_{i}", "Cost", Areas[idx].Cost, 1f, 20f,
                            v => { Areas[idx].Cost = v; Changed(); });
                    }
                    finally { dim?.Dispose(); }
                }
            }
        }
    }
}
