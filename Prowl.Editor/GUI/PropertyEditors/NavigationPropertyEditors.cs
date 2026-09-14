// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.PropertyEditors;

[CustomPropertyEditor(typeof(NavMeshArea))]
public class NavMeshAreaPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
        => DrawAreaField(paper, id, label, value is NavMeshArea area ? area : default, v => onChange(v));

    /// <summary>A labelled dropdown over the defined areas, for custom editors laying fields out by hand.</summary>
    public static void DrawAreaField(Paper paper, string id, string label, NavMeshArea value, Action<NavMeshArea> onChange)
    {
        List<int> areas = NavMeshAreas.GetDefinedAreas();
        if (!areas.Contains(value.Index)) areas.Add(value.Index); // never hide the current value, even if undefined

        EditorGUI.Row(paper, id, label, () =>
            Origami.Dropdown(paper, $"{id}_dd", value.Index, v => onChange(v), areas)
                .Display(AreaName)
                .Show());
    }

    internal static string AreaName(int area)
    {
        string name = NavMeshAreas.GetAreaName(area);
        return string.IsNullOrEmpty(name) ? $"Area {area}" : name;
    }
}

[CustomPropertyEditor(typeof(NavMeshAreaMask))]
public class NavMeshAreaMaskPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        NavMeshAreaMask mask = value is NavMeshAreaMask m ? m : NavMeshAreaMask.Everything;
        List<int> areas = NavMeshAreas.GetDefinedAreas();
        var selected = new List<int>();
        foreach (int area in areas)
            if (mask.HasArea(area)) selected.Add(area);

        NavigationPropertyEditors.MultiSelectRow(paper, id, label, () =>
            Origami.MultiDropdown<int>(paper, $"{id}_md", selected, picked => onChange(ApplyPicked(mask, areas, picked)), areas)
                .Display(NavMeshAreaPropertyEditor.AreaName)
                .Height(Origami.Current.Metrics.RowHeight)
                .SummaryFormat("{0} areas")
                .Searchable()
                .Show());
    }

    /// <summary>
    /// The mask after a pick. Only the areas the dropdown showed are rewritten: an area defined later
    /// keeps whatever the mask already said about it, so unticking one area never quietly excludes
    /// every area added afterwards. Everything ticked is <see cref="NavMeshAreaMask.Everything"/>.
    /// </summary>
    internal static NavMeshAreaMask ApplyPicked(NavMeshAreaMask mask, IReadOnlyList<int> shown, IReadOnlyCollection<int> picked)
    {
        if (picked.Count == shown.Count) return NavMeshAreaMask.Everything;

        foreach (int area in shown) mask.RemoveArea(area);
        foreach (int area in picked) mask.SetArea(area);
        return mask;
    }
}

[CustomPropertyEditor(typeof(NavMeshAgentTypeId))]
public class NavMeshAgentTypeIdPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        NavMeshAgentTypeId current = value is NavMeshAgentTypeId agentType ? agentType : default;
        List<int> ids = NavigationPropertyEditors.AgentTypeIds(current.Value);

        // The nicified field name reads "Agent Type Id"; the id is an implementation detail.
        string shown = label == "Agent Type Id" ? "Agent Type" : label;
        EditorGUI.Row(paper, id, shown, () =>
            Origami.Dropdown(paper, $"{id}_dd", current.Value, v => onChange(new NavMeshAgentTypeId(v)), ids)
                .Display(NavMeshAgentTypes.GetName)
                .Show());
    }
}

[CustomPropertyEditor(typeof(NavMeshAgentTypeSet))]
public class NavMeshAgentTypeSetPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        NavMeshAgentTypeSet set = value as NavMeshAgentTypeSet ?? new NavMeshAgentTypeSet();

        EditorGUI.Row(paper, $"{id}_all", label, () =>
            Origami.Switch(paper, $"{id}_all_v", set.AffectsAll, all =>
                {
                    NavMeshAgentTypeSet updated = set.Clone();
                    updated.AffectsAll = all;
                    onChange(updated);
                })
                .Primary()
                .Show());

        if (set.AffectsAll) return;

        var selected = new List<int>();
        foreach (NavMeshAgentTypeId agentType in set.Ids ?? []) selected.Add(agentType.Value);
        List<int> ids = NavigationPropertyEditors.AgentTypeIds(selected);

        NavigationPropertyEditors.MultiSelectRow(paper, $"{id}_ids", "Agent Types", () =>
            Origami.MultiDropdown<int>(paper, $"{id}_md", selected, picked =>
                {
                    NavMeshAgentTypeSet updated = set.Clone();
                    updated.Ids = [];
                    foreach (int picked1 in picked) updated.Ids.Add(picked1);
                    onChange(updated);
                }, ids)
                .Display(NavMeshAgentTypes.GetName)
                .Height(Origami.Current.Metrics.RowHeight)
                .SummaryFormat("{0} agent types")
                .Searchable()
                .Show());
    }
}

internal static class NavigationPropertyEditors
{
    /// <summary>Every defined agent type id, plus the given ones so a stale reference stays visible.</summary>
    public static List<int> AgentTypeIds(params IEnumerable<int> keep)
    {
        var ids = new List<int>(NavMeshAgentTypes.All.Count);
        foreach (NavMeshAgentType type in NavMeshAgentTypes.All)
            ids.Add(type.Id);
        foreach (int id in keep)
            if (!ids.Contains(id)) ids.Add(id);
        return ids;
    }

    public static List<int> AgentTypeIds(int keep) => AgentTypeIds([keep]);

    /// <summary>A labelled row that grows with its control, so a multi-select's wrapped chips reflow
    /// the column instead of overlapping the next field. The same layout the layer mask editor uses.</summary>
    public static void MultiSelectRow(Paper paper, string id, string label, Action drawControl)
    {
        var theme = Origami.Current;
        var m = theme.Metrics;
        var font = EditorTheme.DefaultFont;

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(m.RowHeight).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).RowBetween(m.Padding).Enter())
        {
            if (font != null && !string.IsNullOrEmpty(label))
            {
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(m.RowHeight).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .IsNotInteractable()
                    .Text(label, font).TextColor(theme.Ink.C300)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).TextTruncate();
            }

            drawControl();
        }
    }
}
