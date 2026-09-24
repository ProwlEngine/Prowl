using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI;

/// <summary> Utility methods for rendering and interacting with property grids in the editor. </summary>
public static class PropertyGridUtils
{
    /// <summary>
    /// Build the "Name (Type)" label an object-reference field shows in the property grid. 
    /// A scene component has no name of its own so it uses the name of the GameObject it lives on and typed
    /// by the component (e.g. "Player (Rigidbody)"). GameObjects and assets carry a real Name, so they use it.
    /// Field Type is used as a fallback for empty references.
    /// </summary>
    public static string DescribeObjectRef(EngineObject? instance, Type fieldType)
    {
        if (instance == null)
            return $"None ({fieldType.Name})";

        if (instance is MonoBehaviour mb && mb.GameObject != null)
            return $"{mb.GameObject.Name} ({instance.GetType().Name})";

        bool isAsset = instance.AssetID != Guid.Empty;
        string suffix = isAsset || instance is GameObject ? instance.GetType().Name : "Instance";
        return $"{instance.Name} ({suffix})";
    }

    /// <summary>
    /// The object reference field shared by the reference property editors: a standard property grid row
    /// holding a glass field with a type icon, the referenced name and a picker button.
    /// <paramref name="acceptDrops"/> runs inside the field, so it can check whether a drop landed on it.
    /// </summary>
    public static void ObjectField(Paper paper, string id, string label, string icon, Color iconColor, string displayName,
        bool hasValue, bool isDragTarget, Action onClick, Action onDoubleClick, Action onPick, Action acceptDrops)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var theme = Origami.Current;
        var m = theme.Metrics;
        float rh = m.RowHeight;

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(rh).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(m.Padding).Enter())
        {
            if (!string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(rh).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .IsNotInteractable()
                    .Text(label, font).TextColor(theme.Ink.C300)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            var field = paper.Row($"{id}_field")
                .Height(rh)
                .BackgroundColor(isDragTarget ? EditorTheme.WithAlpha(EditorTheme.Accent, 60) : EditorTheme.Glass)
                .Hovered.BorderColor(EditorTheme.BorderStrong).End()
                .Rounded(m.Rounding).Padding(m.SpacingLarge, m.PaddingSmall, 0, 0).Gap(m.SpacingLarge)
                .BorderColor(isDragTarget ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(1)
                .OnClick(onClick, (click, _) => click())
                .OnDoubleClick(onDoubleClick, (click, _) => click());

            using (field.Enter())
            {
                acceptDrops();

                paper.Box($"{id}_ico")
                    .Width(UnitValue.Auto).Height(rh).IsNotInteractable()
                    .Text(icon, font).TextColor(iconColor)
                    .FontSize(11f).Alignment(TextAlignment.MiddleCenter);

                paper.Box($"{id}_name")
                    .Width(UnitValue.Stretch()).Height(rh).Clip()
                    .IsNotInteractable()
                    .Text(displayName, font)
                    .TextColor(hasValue ? theme.Ink.C500 : theme.Ink.C200)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft);

                paper.Box($"{id}_pick")
                    .Width(18).Height(18).Rounded(m.SmallRounding).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .Text(EditorIcons.CircleDot, font).TextColor(theme.Ink.C200)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .Hovered.BackgroundColor(theme.Hover).End()
                    .OnClick(onPick, (pick, e) => { e.StopPropagation(); pick(); });
            }
        }
    }

    /// <summary>
    /// Set of overridden field names for the current component being drawn.
    /// Set by the inspector before drawing a prefab instance's component.
    /// </summary>
    [ThreadStatic]
    public static HashSet<string>? OverriddenFields;

    /// <summary>Draw a full property grid for an object.</summary>
    public static void Draw(Paper paper, string id, object target, Action<object>? onChanged = null, int depth = 0)
    {
        Origami.PropertyGrid(paper, id, target, EditorApplication.PropertyGridConfig)
            .OnChanged(onChanged ?? (_ => { }))
            .Overrides(OverriddenFields)
            .Depth(depth)
            .Show();
    }

    /// <summary>Draw a property grid editing several objects at once (shared fields only; differing
    /// values are flagged as mixed and edits apply to every target).</summary>
    public static void DrawMulti(Paper paper, string id, IReadOnlyList<object> targets, Action<object>? onChanged = null, int depth = 0)
    {
        Origami.PropertyGrid(paper, id, targets, EditorApplication.PropertyGridConfig)
            .OnChanged(onChanged ?? (_ => { }))
            .Overrides(OverriddenFields)
            .Depth(depth)
            .Show();
    }

    /// <summary>Draw a single field with label and control.</summary>
    public static void DrawField(Paper paper, string id, string label, Type type, object? value,
        Action<object?> onChange, int depth = 0)
    {
        PropertyGridRenderer.DrawField(paper, id, label, type, value,
            EditorApplication.PropertyGridConfig, onChange, depth);
    }

    /// <summary>Convert "myFieldName" to "My Field Name".</summary>
    public static string NicifyName(string name) => PropertyGridRenderer.FormatFieldName(name);

    /// <summary>Get serializable fields for a type.</summary>
    public static FieldInfo[] GetSerializableFields(Type type) => PropertyGridRenderer.GetSerializableFields(type);
}
