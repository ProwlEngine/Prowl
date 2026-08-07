// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Built-in attribute handlers for the Origami PropertyGrid.
// These handle Runtime attributes ([Range], [Header], [Space], etc.)
// and are registered by the editor at startup.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Editor.GUI.PropertyEditors;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

namespace Prowl.Editor.GUI;

/// <summary>[Header("text")] - draws a header label above the field.</summary>
public class HeaderAttributeHandler : OrigamiUI.AttributeHandler
{
    public override bool OnBeforeDraw(Paper paper, string id, Attribute attr, FieldInfo field, object target, int depth)
    {
        var header = (HeaderAttribute)attr;
        OrigamiUI.Origami.Header(paper, $"{id}_header", header.Text).Show();
        return true;
    }
}

/// <summary>[Space] or [Space(height)] - draws vertical spacing above the field.</summary>
public class SpaceAttributeHandler : OrigamiUI.AttributeHandler
{
    public override bool OnBeforeDraw(Paper paper, string id, Attribute attr, FieldInfo field, object target, int depth)
    {
        var space = (SpaceAttribute)attr;
        paper.Box($"{id}_space").Height(space.Height);
        return true;
    }
}

/// <summary>[ShowIf("memberName")] - hides the field if the named bool member is false.</summary>
public class ShowIfAttributeHandler : OrigamiUI.AttributeHandler
{
    public override bool OnBeforeDraw(Paper paper, string id, Attribute attr, FieldInfo field, object target, int depth)
    {
        var showIf = (ShowIfAttribute)attr;
        var type = target.GetType();

        // Check field
        var condField = type.GetField(showIf.ConditionMember, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (condField != null && condField.FieldType == typeof(bool))
            return (bool)(condField.GetValue(target) ?? false);

        // Check property
        var condProp = type.GetProperty(showIf.ConditionMember, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (condProp != null && condProp.PropertyType == typeof(bool))
            return (bool)(condProp.GetValue(target) ?? false);

        return true; // Condition not found, show by default
    }
}

/// <summary>[ReadOnly] - makes the field non-editable.</summary>
public class ReadOnlyAttributeHandler : OrigamiUI.AttributeHandler
{
    public override bool OnBeforeDraw(Paper paper, string id, Attribute attr, FieldInfo field, object target, int depth)
    {
        OrigamiUI.Origami.BeginReadOnly();
        return true;
    }

    public override void OnAfterDraw(Paper paper, string id, Attribute attr, FieldInfo field, object target, int depth)
    {
        OrigamiUI.Origami.EndReadOnly();
    }
}

/// <summary>[Range(min, max)] - replaces numeric fields with a slider. The row and label still come
/// from the property grid, so they match every other field; only the control is swapped, by
/// RangeSliderDrawer.</summary>
public class RangeAttributeHandler : OrigamiUI.AttributeHandler
{
    public override bool OnDraw(Paper paper, string id, string label, Attribute attr,
        FieldInfo field, object target, Action<object?> onChange, int depth)
    {
        Type type = field.FieldType;
        if (type != typeof(float) && type != typeof(int))
            return false; // no slider for this type: let the grid draw the field untouched

        var range = (RangeAttribute)attr;
        RangeSliderDrawer.Pending = range;
        try
        {
            // The grid makes a numeric field's label a drag-scrubber that writes straight through
            // onChange, so the bounds have to be enforced here rather than by the slider alone.
            PropertyGridUtils.DrawField(paper, id, label, type, field.GetValue(target),
                v => onChange(Clamp(v, range, type)), depth);
        }
        finally
        {
            RangeSliderDrawer.Pending = null;
        }
        return true;
    }

    private static object Clamp(object? value, RangeAttribute range, Type type)
        => type == typeof(float)
            ? Math.Clamp((float)(value ?? 0f), range.Min, range.Max)
            : Math.Clamp((int)(value ?? 0), (int)MathF.Round(range.Min), (int)MathF.Round(range.Max));
}

/// <summary>[Tooltip("text")] - attaches a tooltip to the field row.</summary>
public class TooltipAttributeHandler : OrigamiUI.AttributeHandler
{
    private readonly Stack<IDisposable> _scopes = new();

    public override bool OnBeforeDraw(Paper paper, string id, Attribute attr, FieldInfo field, object target, int depth)
    {
        var tooltip = (TooltipAttribute)attr;
        _scopes.Push(paper.Column($"{id}_tip").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Tooltip(tooltip.Text).Enter());
        return true;
    }

    public override void OnAfterDraw(Paper paper, string id, Attribute attr, FieldInfo field, object target, int depth)
    {
        if (_scopes.Count > 0)
            _scopes.Pop().Dispose();
    }
}

/// <summary>[TextArea(min, max)] - replaces string field with a multiline text area.</summary>
public class TextAreaAttributeHandler : OrigamiUI.AttributeHandler
{
    public override bool OnDraw(Paper paper, string id, string label, Attribute attr,
        FieldInfo field, object target, Action<object?> onChange, int depth)
    {
        var textArea = (TextAreaAttribute)attr;
        var value = (string?)field.GetValue(target) ?? "";
        var theme = OrigamiUI.Origami.Current;
        var m = theme.Metrics;

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(m.RowHeight)
            .RowBetween(m.SpacingMedium).Margin(0, 0, 0, m.SpacingSmall).Enter())
        {
            var font = theme.Font;
            var ink = theme.Ink;
            if (font != null)
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(m.RowHeight).Padding(m.PaddingSmall, 0, 0, 0)
                    .IsNotInteractable()
                    .Text(label, font).TextColor(ink.C500)
                    .FontSize(m.FontSize);

            using (paper.Box($"{id}_ctl").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
            {
                OrigamiUI.Origami.TextArea(paper, $"{id}_ta", value, v => onChange(v), textArea.MaxLines).Show();
            }
        }

        return true;
    }
}

/// <summary>
/// Registers all built-in attribute handlers with the Origami AttributeHandlerRegistry.
/// Called once at editor startup.
/// </summary>
public static class BuiltInAttributeHandlers
{
    public static void Register(OrigamiUI.AttributeHandlerRegistry registry)
    {
        registry.Register<HeaderAttribute>(new HeaderAttributeHandler());
        registry.Register<SpaceAttribute>(new SpaceAttributeHandler());
        registry.Register<ShowIfAttribute>(new ShowIfAttributeHandler());
        registry.Register<ReadOnlyAttribute>(new ReadOnlyAttributeHandler());
        registry.Register<RangeAttribute>(new RangeAttributeHandler());
        registry.Register<TextAreaAttribute>(new TextAreaAttributeHandler());
        registry.Register<TooltipAttribute>(new TooltipAttributeHandler());
    }
}
