using System;
using System.Collections.Generic;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Editor.Theming;

using Prowl.Editor.GUI;

namespace Prowl.Editor.Projects.Settings;

/// <summary>
/// Project-wide navigation configuration: the area table (names + default path costs)
/// applied to <see cref="NavMeshAreas"/>. Walkable, Not Walkable, and Jump are built-in;
/// users add up to 29 more, Tags-style. Removal clears the slot rather than shifting later
/// areas — masks and baked polygons reference areas by index, so indices must stay stable.
/// Written to Navigation.yaml for the built player, where
/// PlayerSettingsLoader.ApplyNavigation restores it.
/// </summary>
[ProjectSettings("Navigation", EditorIcons.Compass, order: 21)]
public class NavigationSettings : ProjectSettingsBase
{
    public List<string> AreaNames = CreateDefaultNames();
    public List<float> AreaCosts = CreateDefaultCosts();
    public List<NavMeshAgentType> AgentTypes = [new NavMeshAgentType { Id = NavMeshAgentTypes.Humanoid, Name = "Humanoid" }];

    /// <summary>Monotonic id counter for Add Agent Type. Persisted so deleting the
    /// highest-id type can never hand its id to a later, unrelated type — surfaces and
    /// agents still referencing the deleted id would silently rebind to the new one.</summary>
    public int NextAgentTypeId = 1;

    private int _activeTab; // 0 = Agents, 1 = Areas

    private static List<string> CreateDefaultNames()
    {
        var names = new List<string>(NavMeshAreas.MaxAreas);
        for (int i = 0; i < NavMeshAreas.MaxAreas; i++)
            names.Add(NavMeshAreas.GetAreaName(i));
        return names;
    }

    private static List<float> CreateDefaultCosts()
    {
        var costs = new List<float>(NavMeshAreas.MaxAreas);
        for (int i = 0; i < NavMeshAreas.MaxAreas; i++)
            costs.Add(NavMeshAreas.GetAreaCost(i));
        return costs;
    }

    private void EnsureSize()
    {
        while (AreaNames.Count < NavMeshAreas.MaxAreas) AreaNames.Add(string.Empty);
        while (AreaCosts.Count < NavMeshAreas.MaxAreas) AreaCosts.Add(1f);
    }

    public override void Apply()
    {
        EnsureSize();
        // Not Walkable is never traversed, so its cost is meaningless; pin it (matches Unity).
        AreaCosts[NavMeshAreas.NotWalkable] = 1f;
        NavMeshAreas.ApplyTable(AreaNames, AreaCosts);
        NavMeshAgentTypes.ApplyTable(AgentTypes);
    }

    public override void ResetToDefaults()
    {
        AreaNames = CreateDefaultNames();
        AreaCosts = CreateDefaultCosts();
        AreaNames[NavMeshAreas.Walkable] = "Walkable";
        AreaNames[NavMeshAreas.NotWalkable] = "Not Walkable";
        AreaNames[NavMeshAreas.Jump] = "Jump";
        AgentTypes = [new NavMeshAgentType { Id = NavMeshAgentTypes.Humanoid, Name = "Humanoid" }];
        NextAgentTypeId = 1;
        Apply();
    }

    private void Changed()
    {
        Apply();
        EditorRegistries.SaveSettings();
    }

    /// <summary>Row swatch: the same per-area color the scene-view navmesh overlay draws
    /// with (opaque for the UI).</summary>
    private static System.Drawing.Color AreaSwatchColor(int areaIndex)
    {
        Prowl.Vector.Color c = NavMeshSurface.AreaColor(areaIndex);
        return System.Drawing.Color.FromArgb(255,
            (int)(Math.Clamp(c.R, 0f, 1f) * 255),
            (int)(Math.Clamp(c.G, 0f, 1f) * 255),
            (int)(Math.Clamp(c.B, 0f, 1f) * 255));
    }

    public override void OnGUI(Paper paper, float width)
    {
        EnsureSize();
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Unity-style tab toolbar: Agents | Areas.
        Origami.ButtonGroup(paper, "nav_tabs", _activeTab, t => _activeTab = t)
            .Item("Agents")
            .Item("Areas")
            .Show();
        paper.Box("nav_tabs_sp").Height(8);

        if (_activeTab == 0) DrawAgentsTab(paper, font);
        else DrawAreasTab(paper, font);
    }

    // ── Agents tab ──────────────────────────────────────────────────────

    private void DrawAgentsTab(Paper paper, Prowl.Scribe.FontFile font)
    {
        Origami.Header(paper, "nav_agents_hdr", $"{EditorIcons.Compass}  Agent Types").Underline().Show();

        const float NumW = 64, DelW = 20;

        // Column headers, aligned with the rows below.
        using (paper.Row("nav_agent_cols").Height(20).RowBetween(6).ChildLeft(8).ChildRight(4).Enter())
        {
            paper.Box("nav_agent_cols_name")
                .Width(UnitValue.Stretch()).Height(18).ChildLeft(4)
                .Text("Name", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            DrawAgentColHeader(paper, "nav_agent_cols_r", "Radius", NumW, font);
            DrawAgentColHeader(paper, "nav_agent_cols_h", "Height", NumW, font);
            DrawAgentColHeader(paper, "nav_agent_cols_s", "Slope°", NumW, font);
            DrawAgentColHeader(paper, "nav_agent_cols_c", "Climb", NumW, font);
            paper.Box("nav_agent_cols_del").Width(DelW).Height(18);
        }

        for (int i = 0; i < AgentTypes.Count; i++)
        {
            int idx = i;
            NavMeshAgentType type = AgentTypes[i];
            bool isBuiltin = type.Id == NavMeshAgentTypes.Humanoid;

            using (paper.Row($"nav_agent_{type.Id}").Height(26).RowBetween(6).ChildLeft(8).ChildRight(4).Enter())
            {
                // Name: same control for every row; the built-in Humanoid's name is locked.
                using (paper.Box($"nav_agent_name_{type.Id}").Width(UnitValue.Stretch()).Height(22).Enter())
                {
                    IDisposable? dim = isBuiltin ? EnableIfAttributeHandler.PushDisabledScope() : null;
                    try
                    {
                        Origami.TextField(paper, $"nav_agent_name_tf_{type.Id}", type.Name, v =>
                            {
                                if (isBuiltin || string.IsNullOrWhiteSpace(v)) return;
                                string trimmed = v.Trim();
                                // Duplicate names would make name lookup ambiguous.
                                foreach (NavMeshAgentType other in AgentTypes)
                                    if (other != AgentTypes[idx] && string.Equals(other.Name, trimmed, StringComparison.Ordinal))
                                        return;
                                AgentTypes[idx].Name = trimmed;
                                Changed();
                            }).Show();
                    }
                    finally { dim?.Dispose(); }
                }

                DrawAgentNumField(paper, $"nav_agent_r_{type.Id}", NumW, type.Radius, v => { AgentTypes[idx].Radius = MathF.Max(0.01f, v); Changed(); });
                DrawAgentNumField(paper, $"nav_agent_h_{type.Id}", NumW, type.Height, v => { AgentTypes[idx].Height = MathF.Max(0.01f, v); Changed(); });
                DrawAgentNumField(paper, $"nav_agent_s_{type.Id}", NumW, type.MaxSlope, v => { AgentTypes[idx].MaxSlope = Math.Clamp(v, 0f, 89f); Changed(); });
                DrawAgentNumField(paper, $"nav_agent_c_{type.Id}", NumW, type.MaxClimb, v => { AgentTypes[idx].MaxClimb = MathF.Max(0f, v); Changed(); });

                if (!isBuiltin)
                {
                    paper.Box($"nav_agent_del_{type.Id}")
                        .Width(DelW).Height(22).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                        .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                        .OnClick(idx, (id, _) =>
                        {
                            // Ids are persistent - removing an entry never renumbers others.
                            AgentTypes.RemoveAt(id);
                            Changed();
                        });
                }
                else
                {
                    paper.Box($"nav_agent_del_{type.Id}").Width(DelW).Height(22); // column alignment spacer
                }
            }
        }

        paper.Box("nav_agents_sp").Height(4);

        Origami.Button(paper, "nav_add_agent", $"{EditorIcons.Plus}  Add Agent Type", () =>
        {
            // Max() guards settings saved before NextAgentTypeId existed (counter at default 1
            // with higher ids already in the table).
            int nextId = NextAgentTypeId;
            foreach (NavMeshAgentType t in AgentTypes) nextId = Math.Max(nextId, t.Id + 1);
            NextAgentTypeId = nextId + 1;

            string name = "New Agent";
            for (int n = 1; AgentTypes.Exists(t => t.Name == name); n++)
                name = $"New Agent ({n})";

            AgentTypes.Add(new NavMeshAgentType { Id = nextId, Name = name });
            Changed();
        }).Show();
    }

    private static void DrawAgentColHeader(Paper paper, string id, string label, float width, Prowl.Scribe.FontFile font)
    {
        paper.Box(id)
            .Width(width).Height(18).ChildLeft(4)
            .Text(label, font).TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
    }

    private static void DrawAgentNumField(Paper paper, string id, float width, float value, Action<float> setter)
    {
        using (paper.Box(id).Width(width).Height(22).Enter())
        {
            Origami.NumericField<float>(paper, $"{id}_nf", value, setter).Show();
        }
    }

    // ── Areas tab ───────────────────────────────────────────────────────

    private void DrawAreasTab(Paper paper, Prowl.Scribe.FontFile font)
    {
        Origami.Header(paper, "nav_areas_hdr", $"{EditorIcons.Compass}  Navigation Areas").Underline().Show();

        // Column layout shared by the header and every row (mirrors Unity's Areas tab):
        // [swatch 6] [slot label 76] [Name stretch] [Cost 70] [delete 20]
        const float SwatchW = 6, SlotW = 76, CostW = 70, DelW = 20;

        using (paper.Row("nav_area_cols").Height(20).RowBetween(6).ChildLeft(8).ChildRight(4).Enter())
        {
            paper.Box("nav_area_cols_swatch").Width(SwatchW).Height(18);
            paper.Box("nav_area_cols_slot").Width(SlotW).Height(18);
            paper.Box("nav_area_cols_name")
                .Width(UnitValue.Stretch()).Height(18).ChildLeft(4)
                .Text("Name", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            paper.Box("nav_area_cols_cost")
                .Width(CostW).Height(18).ChildLeft(4)
                .Text("Cost", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            paper.Box("nav_area_cols_del").Width(DelW).Height(18);
        }

        for (int i = 0; i < NavMeshAreas.MaxAreas; i++)
        {
            int idx = i;
            bool isBuiltin = i <= NavMeshAreas.Jump;
            if (!isBuiltin && string.IsNullOrEmpty(AreaNames[i])) continue; // empty slot: hidden

            using (paper.Row($"nav_area_{i}").Height(26).RowBetween(6).ChildLeft(8).ChildRight(4).Enter())
            {
                // Swatch in the same color the scene-view overlay uses for this area.
                paper.Box($"nav_area_swatch_{i}")
                    .Width(SwatchW).Height(22).Rounded(2)
                    .BackgroundColor(AreaSwatchColor(i));

                paper.Box($"nav_area_slot_{i}")
                    .Width(SlotW).Height(22)
                    .Text(isBuiltin ? $"Built-in {i}" : $"User {i}", font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                // Name: always the same field control so the column lines up; built-ins are
                // rendered disabled instead of as bare labels.
                using (paper.Box($"nav_area_name_{i}").Width(UnitValue.Stretch()).Height(22).Enter())
                {
                    IDisposable? dim = isBuiltin ? EnableIfAttributeHandler.PushDisabledScope() : null;
                    try
                    {
                        Origami.TextField(paper, $"nav_area_name_tf_{i}", AreaNames[i], v =>
                            {
                                if (isBuiltin || string.IsNullOrWhiteSpace(v)) return;
                                string trimmed = v.Trim();
                                // Duplicate names would make GetAreaFromName ambiguous.
                                int existing = AreaNames.IndexOf(trimmed);
                                if (existing >= 0 && existing != idx) return;
                                AreaNames[idx] = trimmed;
                                Changed();
                            }).Show();
                    }
                    finally { dim?.Dispose(); }
                }

                // Cost: plain float field, clamped to >= 1 on apply (Detour's A* heuristic
                // assumes cost >= 1; smaller values make paths suboptimal). Not Walkable is
                // never traversed, so its cost is pinned and disabled.
                using (paper.Box($"nav_area_cost_{i}").Width(CostW).Height(22).Enter())
                {
                    bool costLocked = i == NavMeshAreas.NotWalkable;
                    IDisposable? dim = costLocked ? EnableIfAttributeHandler.PushDisabledScope() : null;
                    try
                    {
                        Origami.NumericField<float>(paper, $"nav_area_cost_nf_{i}", AreaCosts[i], v =>
                            {
                                if (costLocked) return;
                                AreaCosts[idx] = MathF.Max(1f, v);
                                Changed();
                            }).Show();
                    }
                    finally { dim?.Dispose(); }
                }

                if (!isBuiltin)
                {
                    paper.Box($"nav_area_del_{i}")
                        .Width(DelW).Height(22).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                        .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                        .OnClick(idx, (id, _) =>
                        {
                            // Clear the slot in place; shifting would re-index later areas
                            // under existing masks and baked navmeshes.
                            AreaNames[id] = string.Empty;
                            AreaCosts[id] = 1f;
                            Changed();
                        });
                }
                else
                {
                    paper.Box($"nav_area_del_{i}").Width(DelW).Height(22); // spacer keeps columns aligned
                }
            }
        }

        paper.Box("nav_areas_sp").Height(4);

        // Add a new area into the first empty slot with a unique placeholder name; rename it
        // in the row's name field. (A text-entry "add" would fire per keystroke.)
        Origami.Button(paper, "nav_add_area", $"{EditorIcons.Plus}  Add Area", () =>
        {
            int slot = AreaNames.FindIndex(NavMeshAreas.Jump + 1, string.IsNullOrEmpty);
            if (slot < 0)
            {
                Runtime.Debug.LogWarning($"[Navigation] All {NavMeshAreas.MaxAreas} area slots are in use.");
                return;
            }

            string name = "New Area";
            for (int n = 1; AreaNames.Contains(name); n++)
                name = $"New Area ({n})";

            AreaNames[slot] = name;
            AreaCosts[slot] = 1f;
            Changed();
        }).Show();
    }
}
