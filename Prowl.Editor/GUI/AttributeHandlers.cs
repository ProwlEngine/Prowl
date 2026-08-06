// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Built-in attribute handlers for the Origami PropertyGrid.
// These handle Runtime attributes ([Range], [Header], [Space], etc.)
// and are registered by the editor at startup.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

namespace Prowl.Editor.GUI;

/// <summary>
/// The default property-grid row recipe (gutter padding, label width/colour/truncation) for
/// handler-drawn fields, so they align with grid-drawn rows instead of each hand-copying the
/// layout. This is the one place the recipe lives — grid metric changes go here.
/// </summary>
public static class HandlerRowLayout
{
    /// <summary>Draw a label + control row matching the default grid rows. The control is
    /// drawn inside a stretch-width, row-height box.</summary>
    public static void LabelledRow(Paper paper, string id, string label, Action drawControl)
    {
        var theme = OrigamiUI.Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(m.RowHeight)
            .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).RowBetween(m.Padding).Enter())
        {
            if (font != null && !string.IsNullOrEmpty(label))
            {
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(m.RowHeight)
                    .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .IsNotInteractable()
                    .Text(label, font).TextColor(theme.Ink.C300)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).TextTruncate();
            }

            using (paper.Box($"{id}_ctl").Width(UnitValue.Stretch()).Height(m.RowHeight).Enter())
                drawControl();
        }
    }
}

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

/// <summary>[EnableIf("memberName")] - greys the field out (visible, not editable) while the
/// named bool member is false. Interaction is blocked via BeginReadOnly, and the field is
/// drawn under a faded theme so the disabled state is visually obvious.</summary>
public class EnableIfAttributeHandler : OrigamiUI.AttributeHandler
{
    // OnBeforeDraw/OnAfterDraw run as a strict pair on one thread per field; the stack keeps
    // nested [EnableIf] objects balanced. The entry carries the pushed-theme scope to pop.
    [ThreadStatic] private static Stack<IDisposable?>? t_scopes;

    // Faded clone of the active theme, rebuilt only when the theme object changes.
    private static OrigamiUI.OrigamiTheme? _dimSource;
    private static OrigamiUI.OrigamiTheme? _dimTheme;

    private static bool EvaluateCondition(string member, object target)
    {
        var type = target.GetType();
        var condField = type.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (condField != null && condField.FieldType == typeof(bool))
            return (bool)(condField.GetValue(target) ?? false);
        var condProp = type.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (condProp != null && condProp.PropertyType == typeof(bool))
            return (bool)(condProp.GetValue(target) ?? false);
        return true; // condition not found: leave enabled
    }

    private static OrigamiUI.OrigamiTheme GetDimmedTheme(OrigamiUI.OrigamiTheme source)
    {
        if (!ReferenceEquals(_dimSource, source) || _dimTheme == null)
        {
            var dim = source.Clone();
            FadeRamp(dim.Ink);      // labels + field text
            FadeRamp(dim.Neutral);  // field backgrounds/borders
            _dimSource = source;
            _dimTheme = dim;
        }
        return _dimTheme;
    }

    private static void FadeRamp(OrigamiUI.OrigamiRamp ramp)
    {
        ramp.C100 = Fade(ramp.C100); ramp.C200 = Fade(ramp.C200); ramp.C300 = Fade(ramp.C300);
        ramp.C400 = Fade(ramp.C400); ramp.C500 = Fade(ramp.C500); ramp.C600 = Fade(ramp.C600);
        ramp.C700 = Fade(ramp.C700);
    }

    private static System.Drawing.Color Fade(System.Drawing.Color c)
        => System.Drawing.Color.FromArgb((int)(c.A * 0.45f), c.R, c.G, c.B);

    /// <summary>
    /// Draw a stretch of UI as disabled: blocks interaction (BeginReadOnly) and renders it
    /// under the faded theme so the state is visually obvious. Dispose the scope to restore.
    /// Reusable by any editor UI that wants the same look as [EnableIf].
    /// </summary>
    public static IDisposable PushDisabledScope()
    {
        OrigamiUI.Origami.BeginReadOnly();
        IDisposable themeScope = OrigamiUI.Origami.PushTheme(GetDimmedTheme(OrigamiUI.Origami.Current));
        return new DisabledScope(themeScope);
    }

    private sealed class DisabledScope(IDisposable themeScope) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            themeScope.Dispose();
            OrigamiUI.Origami.EndReadOnly();
        }
    }

    public override bool OnBeforeDraw(Paper paper, string id, Attribute attr, FieldInfo field, object target, int depth)
    {
        bool disabled = !EvaluateCondition(((EnableIfAttribute)attr).ConditionMember, target);
        IDisposable? scope = null;
        if (disabled)
        {
            OrigamiUI.Origami.BeginReadOnly();
            scope = OrigamiUI.Origami.PushTheme(GetDimmedTheme(OrigamiUI.Origami.Current));
        }
        (t_scopes ??= new()).Push(scope); // null = field was enabled
        return true;
    }

    public override void OnAfterDraw(Paper paper, string id, Attribute attr, FieldInfo field, object target, int depth)
    {
        if (t_scopes == null || t_scopes.Count == 0) return;
        IDisposable? scope = t_scopes.Pop();
        if (scope == null) return; // was enabled
        scope.Dispose();
        OrigamiUI.Origami.EndReadOnly();
    }
}

/// <summary>[InspectorName("label")] - overrides the field's display label; on enum-typed
/// fields the dropdown also shows each member's own [InspectorName] instead of the raw name.</summary>
public class InspectorNameAttributeHandler : OrigamiUI.AttributeHandler
{
    /// <summary>Display name for an enum member: its [InspectorName] if present, else the
    /// nicified member name.</summary>
    public static string GetEnumDisplayName(Type enumType, object value)
    {
        string name = Enum.GetName(enumType, value) ?? value.ToString() ?? "";
        var attr = enumType.GetField(name)?.GetCustomAttribute<InspectorNameAttribute>();
        return attr?.DisplayName ?? PropertyGridUtils.NicifyName(name);
    }

    public override bool OnDraw(Paper paper, string id, string label, Attribute attr,
        FieldInfo field, object target, Action<object?> onChange, int depth)
    {
        string displayLabel = ((InspectorNameAttribute)attr).DisplayName;
        Type type = field.FieldType;

        // Non-flags enums get a dropdown honouring per-member display names.
        if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false))
        {
            object value = field.GetValue(target) ?? Enum.GetValues(type).GetValue(0)!;
            HandlerRowLayout.LabelledRow(paper, id, displayLabel, () =>
            {
                var values = new List<object>();
                foreach (object v in Enum.GetValues(type)) values.Add(v);
                OrigamiUI.Origami.Dropdown(paper, $"{id}_dd", value, v => onChange(v), values)
                    .Display(v => GetEnumDisplayName(type, v))
                    .Show();
            });
            return true;
        }

        // Everything else: default rendering, relabelled.
        PropertyGridUtils.DrawField(paper, id, displayLabel, type, field.GetValue(target), onChange, depth);
        return true;
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
        if (type != typeof(float) && type != typeof(int))
            return false; // unsupported type: fall through to default rendering

        HandlerRowLayout.LabelledRow(paper, id, label, () =>
        {
            if (type == typeof(float))
            {
                float f = (float)(value ?? 0f);
                OrigamiUI.Origami.Slider(paper, $"{id}_sl", f,
                    v => onChange(v), range.Min, range.Max).Format("F2").Show();
            }
            else
            {
                int i = (int)(value ?? 0);
                OrigamiUI.Origami.Slider(paper, $"{id}_sl", (float)i,
                    v => onChange((int)MathF.Round(v)), range.Min, range.Max)
                    .Format("F0").Step(1f).Show();
            }
        });

        return true;
    }
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
        registry.Register<EnableIfAttribute>(new EnableIfAttributeHandler());
        registry.Register<InspectorNameAttribute>(new InspectorNameAttributeHandler());
        registry.Register<ReadOnlyAttribute>(new ReadOnlyAttributeHandler());
        registry.Register<RangeAttribute>(new RangeAttributeHandler());
        registry.Register<TextAreaAttribute>(new TextAreaAttributeHandler());
        registry.Register<TooltipAttribute>(new TooltipAttributeHandler());
        registry.Register<NavMeshAreaAttribute>(new NavMeshAreaAttributeHandler());
        registry.Register<NavMeshAreaMaskAttribute>(new NavMeshAreaMaskAttributeHandler());
        registry.Register<NavMeshAgentTypeAttribute>(new NavMeshAgentTypeAttributeHandler());
    }
}
