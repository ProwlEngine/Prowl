// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

namespace Prowl.Editor.GUI;

/// <summary>
/// [NavMeshArea] - draws an int field as a dropdown of the navigation areas defined in
/// project settings, so users pick "Walkable" instead of typing 0.
/// </summary>
public class NavMeshAreaAttributeHandler : OrigamiUI.AttributeHandler
{
    public override bool OnDraw(Paper paper, string id, string label, Attribute attr,
        FieldInfo field, object target, Action<object?> onChange, int depth)
    {
        if (field.FieldType != typeof(int)) return false;
        DrawAreaField(paper, id, label, (int)(field.GetValue(target) ?? 0), v => onChange(v));
        return true;
    }

    /// <summary>Labelled area dropdown over the defined areas — shared by the attribute
    /// handler and custom editors that lay fields out by hand.</summary>
    public static void DrawAreaField(Paper paper, string id, string label, int value, Action<int> onChange)
    {
        List<int> areas = NavMeshAreas.GetDefinedAreas();
        if (!areas.Contains(value)) areas.Add(value); // never hide the current value, even if undefined
        DrawIdDropdown(paper, id, label, value, onChange, areas, DisplayName);
    }

    internal static string DisplayName(int area)
    {
        string name = NavMeshAreas.GetAreaName(area);
        return string.IsNullOrEmpty(name) ? $"Area {area}" : name;
    }

    /// <summary>Shared labelled-dropdown-over-int-ids row, laid out via
    /// <see cref="HandlerRowLayout"/> so handler-drawn fields don't visually stick out of the
    /// inspector column.</summary>
    internal static void DrawIdDropdown(Paper paper, string id, string label, int value,
        Action<int> onChange, List<int> ids, Func<int, string> display)
    {
        HandlerRowLayout.LabelledRow(paper, id, label, () =>
        {
            OrigamiUI.Origami.Dropdown(paper, $"{id}_dd", value, v => onChange(v), ids)
                .Display(display)
                .Show();
        });
    }
}

/// <summary>
/// [NavMeshAgentType] - draws an int field as a dropdown of the agent types defined in
/// project settings, so users pick "Humanoid" instead of typing 0. On a List&lt;int&gt; field
/// it draws a multi-select of agent types instead (the agent-type analogue of the
/// LayerMask/[NavMeshAreaMask] editor), used by the modifier components' affected-types lists.
/// </summary>
public class NavMeshAgentTypeAttributeHandler : OrigamiUI.AttributeHandler
{
    public override bool OnDraw(Paper paper, string id, string label, Attribute attr,
        FieldInfo field, object target, Action<object?> onChange, int depth)
    {
        if (field.FieldType == typeof(List<int>))
        {
            DrawAgentTypeList(paper, id, label, (List<int>?)field.GetValue(target) ?? [], onChange);
            return true;
        }

        if (field.FieldType != typeof(int)) return false;

        var ids = new List<int>(NavMeshAgentTypes.All.Count);
        foreach (NavMeshAgentType type in NavMeshAgentTypes.All)
            ids.Add(type.Id);
        int value = (int)(field.GetValue(target) ?? 0);
        if (!ids.Contains(value)) ids.Add(value); // stale reference stays visible

        // The nicified "Agent Type Id" is noise — collapse it to Unity's "Agent Type". Any
        // other field name keeps its own label (the handler is registered globally, so a
        // third-party [NavMeshAgentType] int PatrolAgentType must not be relabelled).
        string shown = label == "Agent Type Id" ? "Agent Type" : label;
        NavMeshAreaAttributeHandler.DrawIdDropdown(paper, id, shown, value, v => onChange(v), ids, NavMeshAgentTypes.GetName);
        return true;
    }

    private static void DrawAgentTypeList(Paper paper, string id, string label, List<int> current, Action<object?> onChange)
    {
        var theme = OrigamiUI.Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;
        float rh = m.RowHeight;

        var ids = new List<int>(NavMeshAgentTypes.All.Count);
        foreach (NavMeshAgentType type in NavMeshAgentTypes.All)
            ids.Add(type.Id);
        foreach (int selected in current)
            if (!ids.Contains(selected))
                ids.Add(selected); // stale references stay visible (named "Agent Type N")

        // "Affected Agent Type Ids" nicified is noise — collapse to Unity's "Affected Agents";
        // other field names keep their own label (same rule as the int branch).
        string shown = label == "Affected Agent Type Ids" ? "Affected Agents" : label;

        // Not HandlerRowLayout.LabelledRow: the multi-select needs Height(Auto) so chip
        // wrapping reflows the column (same as the [NavMeshAreaMask] editor) — a row-height
        // control box would clip the wrapped chips. Label block mirrors the helper's recipe.
        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(rh).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).RowBetween(m.Padding).Enter())
        {
            if (font != null && !string.IsNullOrEmpty(shown))
            {
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(rh).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .IsNotInteractable()
                    .Text(shown, font).TextColor(theme.Ink.C300)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).TextTruncate();
            }

            OrigamiUI.Origami.MultiDropdown<int>(paper, $"{id}_md", current, picked => onChange(new List<int>(picked)), ids)
                .Display(NavMeshAgentTypes.GetName)
                .Height(rh)
                .SummaryFormat("{0} agent types")
                .Searchable()
                .Show();
        }
    }
}

/// <summary>
/// [NavMeshAreaMask] - draws an int field as a multi-select of the defined navigation areas
/// (the area analogue of the LayerMask editor).
/// </summary>
public class NavMeshAreaMaskAttributeHandler : OrigamiUI.AttributeHandler
{
    public override bool OnDraw(Paper paper, string id, string label, Attribute attr,
        FieldInfo field, object target, Action<object?> onChange, int depth)
    {
        if (field.FieldType != typeof(int)) return false;
        int mask = (int)(field.GetValue(target) ?? NavMeshAreas.AllAreas);

        var theme = OrigamiUI.Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;
        float rh = m.RowHeight;

        List<int> areas = NavMeshAreas.GetDefinedAreas();
        var selected = new List<int>();
        foreach (int i in areas)
            if ((mask & (1 << i)) != 0) selected.Add(i);

        // Not HandlerRowLayout.LabelledRow: the multi-select needs Height(Auto) so chip
        // wrapping reflows the column (same as the LayerMask editor) — a row-height control
        // box would clip the wrapped chips. Label block mirrors the helper's recipe.
        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(rh).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).RowBetween(m.Padding).Enter())
        {
            if (font != null && !string.IsNullOrEmpty(label))
            {
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(rh).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .IsNotInteractable()
                    .Text(label, font).TextColor(theme.Ink.C300)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).TextTruncate();
            }

            OrigamiUI.Origami.MultiDropdown<int>(paper, $"{id}_md", selected, picked =>
                {
                    // Everything selected stores as AllAreas (-1) so future areas are included
                    // automatically — matching how "Everything" behaves on layer masks.
                    if (picked.Count == areas.Count)
                    {
                        onChange(NavMeshAreas.AllAreas);
                        return;
                    }
                    int updated = 0;
                    foreach (int i in picked) updated |= 1 << i;
                    onChange(updated);
                }, areas)
                .Display(NavMeshAreaAttributeHandler.DisplayName)
                .Height(rh)
                .SummaryFormat("{0} areas")
                .Searchable()
                .Show();
        }
        return true;
    }
}
