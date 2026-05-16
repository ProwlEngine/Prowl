// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Built-in attribute handlers for the Origami PropertyGrid.
// These handle Runtime attributes ([Range], [Header], [Space], etc.)
// and are registered by the editor at startup.

using System;
using System.Reflection;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

namespace Prowl.Editor.AttributeHandlers;

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

/// <summary>[Range(min, max)] - replaces numeric fields with a slider.</summary>
public class RangeAttributeHandler : OrigamiUI.AttributeHandler
{
    public override bool OnDraw(Paper paper, string id, string label, Attribute attr,
        FieldInfo field, object target, Action<object?> onChange, int depth)
    {
        var range = (RangeAttribute)attr;
        var value = field.GetValue(target);
        var type = field.FieldType;
        var theme = OrigamiUI.Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;
        var ink = theme.Ink;

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(m.RowHeight)
            .RowBetween(m.SpacingMedium).Margin(0, 0, 0, m.SpacingSmall).Enter())
        {
            if (font != null && !string.IsNullOrEmpty(label))
            {
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(m.RowHeight)
                    .Padding(m.PaddingSmall, 0, 0, 0)
                    .IsNotInteractable()
                    .Text(label, font).TextColor(ink.C500)
                    .FontSize(m.FontSize);
            }

            using (paper.Box($"{id}_ctl").Width(UnitValue.Stretch())
                .Height(m.RowHeight).Enter())
            {
                if (type == typeof(float))
                {
                    float f = (float)(value ?? 0f);
                    OrigamiUI.Origami.Slider(paper, $"{id}_sl", f,
                        v => onChange(v), range.Min, range.Max).Format("F2").Show();
                }
                else if (type == typeof(int))
                {
                    int i = (int)(value ?? 0);
                    OrigamiUI.Origami.Slider(paper, $"{id}_sl", (float)i,
                        v => onChange((int)MathF.Round(v)), range.Min, range.Max)
                        .Format("F0").Step(1f).Show();
                }
                else
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>[Tooltip("text")] - attaches a tooltip to the field row.</summary>
public class TooltipAttributeHandler : OrigamiUI.AttributeHandler
{
    // Tooltips are handled by the PropertyGrid row itself checking for the attribute
    // and calling .Tooltip() on the row element. No pre/post draw needed.
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
    public static void Register()
    {
        OrigamiUI.AttributeHandlerRegistry.Register<HeaderAttribute>(new HeaderAttributeHandler());
        OrigamiUI.AttributeHandlerRegistry.Register<SpaceAttribute>(new SpaceAttributeHandler());
        OrigamiUI.AttributeHandlerRegistry.Register<ShowIfAttribute>(new ShowIfAttributeHandler());
        OrigamiUI.AttributeHandlerRegistry.Register<ReadOnlyAttribute>(new ReadOnlyAttributeHandler());
        OrigamiUI.AttributeHandlerRegistry.Register<RangeAttribute>(new RangeAttributeHandler());
        OrigamiUI.AttributeHandlerRegistry.Register<TextAreaAttribute>(new TextAreaAttributeHandler());
        // TooltipAttribute is handled inline by PropertyGrid row rendering
    }
}
