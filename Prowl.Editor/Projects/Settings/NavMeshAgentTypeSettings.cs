// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime.Navigation;

namespace Prowl.Editor.Projects.Settings;

/// <summary>
/// Project settings for the named agent types a <see cref="NavMeshSurface"/> can bake for and a
/// <see cref="NavMeshAgent"/> can walk. Edits a working copy and pushes it into the runtime
/// <see cref="NavMeshAgentTypes"/> registry through <see cref="Apply"/>, the same pattern
/// <see cref="TagsAndLayersSettings"/> uses for tags and layers.
/// </summary>
[ProjectSettings("Agents", EditorIcons.CircleUser, order: 60)]
public class NavMeshAgentTypeSettings : ProjectSettingsBase
{
    /// <summary>Working copy of the agent type table. Field name matters: the built player reads it
    /// by this exact name (see <see cref="PlayerSettingsLoader.ApplyNavigation"/>).</summary>
    public List<NavMeshAgentTypeInfo> AgentTypes = new(NavMeshAgentTypes.Types);

    /// <summary>Pushes <see cref="AgentTypes"/> into the runtime <see cref="NavMeshAgentTypes"/> registry.</summary>
    public override void Apply() => NavMeshAgentTypes.ReplaceAll(new List<NavMeshAgentTypeInfo>(AgentTypes));

    /// <summary>Resets the runtime registry and this working copy back to just the built-in Humanoid type.</summary>
    public override void ResetToDefaults()
    {
        NavMeshAgentTypes.ResetDefault();
        AgentTypes = new List<NavMeshAgentTypeInfo>(NavMeshAgentTypes.Types);
    }

    /// <summary>Applies and persists an edit made through this page's UI.</summary>
    private void Changed()
    {
        Apply();
        EditorRegistries.SaveSettings();
    }

    /// <summary>Draws one card per agent type: its id, an editable name, a delete button (hidden for
    /// the built-in Humanoid type), and radius/height/slope/step sliders.</summary>
    public override void OnGUI(Paper paper, float width)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        Origami.Header(paper, "nmat_header", $"{EditorIcons.CircleUser}  Agent Types").Underline().Show();

        for (int i = 0; i < AgentTypes.Count; i++)
        {
            int idx = i;
            NavMeshAgentTypeInfo type = AgentTypes[i];
            bool isBuiltin = type.Id == NavMeshAgentTypes.HumanoidId;

            using (paper.Box($"nmat_card_{i}").PaddingTop(6).PaddingBottom(6).Enter())
            {
                using (paper.Row($"nmat_row_name_{i}").Height(24).Gap(4).Enter())
                {
                    paper.Box($"nmat_id_{i}").Width(28).Height(22)
                        .Text($"#{type.Id}", font).TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                    using (paper.Box($"nmat_name_box_{i}").Width(UnitValue.Stretch()).Height(22).Enter())
                    {
                        Origami.TextField(paper, $"nmat_name_tf_{i}", type.Name, v =>
                        {
                            if (string.IsNullOrWhiteSpace(v)) return;
                            type.Name = v.Trim();
                            Changed();
                        }).Show();
                    }

                    if (!isBuiltin)
                    {
                        paper.Box($"nmat_del_{i}")
                            .Width(20).Height(22).Rounded(3)
                            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                            .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                            .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                            .OnClick(idx, (id, _) =>
                            {
                                AgentTypes.RemoveAt(id);
                                Changed();
                            });
                    }
                }

                EditorGUI.SettingsSliderField(paper, $"nmat_radius_{i}", "Radius", type.AgentRadius, 0.05f, 5f,
                    v => { type.AgentRadius = v; Changed(); });
                EditorGUI.SettingsSliderField(paper, $"nmat_height_{i}", "Height", type.AgentHeight, 0.1f, 10f,
                    v => { type.AgentHeight = v; Changed(); });
                EditorGUI.SettingsSliderField(paper, $"nmat_slope_{i}", "Max Slope", type.MaxSlopeAngle, 0f, 89f,
                    v => { type.MaxSlopeAngle = v; Changed(); });
                EditorGUI.SettingsSliderField(paper, $"nmat_step_{i}", "Max Step Height", type.MaxStepHeight, 0f, 5f,
                    v => { type.MaxStepHeight = v; Changed(); });
            }

            paper.Box($"nmat_sep_{i}").Height(1).BackgroundColor(EditorTheme.Ink100);
        }

        paper.Box("nmat_sp").Height(8);

        Origami.Button(paper, "nmat_add", $"{EditorIcons.Plus}  Add Agent Type", () =>
        {
            string name = "New Agent Type";
            int n = 1;
            while (AgentTypes.Exists(t => t.Name == name))
                name = $"New Agent Type ({n++})";

            AgentTypes.Add(new NavMeshAgentTypeInfo
            {
                Id = NextFreeId(),
                Name = name,
                AgentRadius = 0.5f,
                AgentHeight = 2.0f,
                MaxSlopeAngle = 45f,
                MaxStepHeight = 0.4f,
            });
            Changed();
        }).Show();
    }

    // Mirrors NavMeshAgentTypes.ReplaceAll's own id derivation, so an id assigned here never collides
    // with one already in the working list even before Apply() runs.
    private int NextFreeId()
    {
        int next = NavMeshAgentTypes.HumanoidId + 1;
        foreach (NavMeshAgentTypeInfo type in AgentTypes)
            if (type.Id >= next) next = type.Id + 1;
        return next;
    }
}
