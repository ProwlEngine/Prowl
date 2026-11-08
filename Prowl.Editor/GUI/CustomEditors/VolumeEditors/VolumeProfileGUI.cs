// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Core;
using Prowl.Ember;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

/// <summary>
/// The component list of a <see cref="VolumeProfile"/>, shared by the profile asset editor, the Volume inspector and
/// the Camera inspector: a card per component with its enable toggle, a foldout, All and None override buttons and a
/// remove button, a row per parameter with its override checkbox, and an Add Override menu of every component type.
/// </summary>
public static class VolumeProfileGUI
{
    private static readonly HashSet<string> s_collapsed = [];
    private static readonly ReloadCache<int, List<Type>> s_types = new(_ => [.. EditorUtils.GetAllTypes()
        .Where(t => typeof(VolumeComponent).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
        .OrderBy(VolumeComponent.MenuPath, StringComparer.OrdinalIgnoreCase)]);

    /// <summary>
    /// Draws the profile's components. An embedded profile's edits are recorded as owned by <paramref name="holder"/>.
    /// </summary>
    public static void DrawComponents(Paper paper, string id, VolumeProfile profile, EngineObject? holder = null)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null || profile.IsNotValid()) return;
        UndoOwner owner = OwnerOf(profile, holder);
        Action<string> record = name => Undo.RecordObject(profile, owner, name);

        List<VolumeComponent> components = [.. profile.Loaded.Where(c => c != null)];
        if (components.Count == 0)
            EditorGUI.Note(paper, $"{id}_empty", "No overrides yet. Add one to change how cameras in range draw.");

        foreach (VolumeComponent component in components)
            DrawCard(paper, $"{id}_{component.GetType().Name}", profile, component, font, record);

        using (paper.Row($"{id}_add_row").Height(28).Padding(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 0, 0)
            .Margin(0, 0, Origami.Current.Metrics.Spacing, Origami.Current.Metrics.SpacingLarge).Enter())
        {
            Origami.Button(paper, $"{id}_add", $"{EditorIcons.Plus}  Add Override", () =>
                Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b => BuildAddMenu(b, profile, record)))
                .FullWidth().Show();
        }
    }

    /// <summary>Every volume component type, built in and user, sorted by menu path.</summary>
    public static IReadOnlyList<Type> ComponentTypes => s_types[0];

    private static void BuildAddMenu(ContextBuilder builder, VolumeProfile profile, Action<string> record)
    {
        foreach (IGrouping<string, Type> group in ComponentTypes.GroupBy(t => Category(VolumeComponent.MenuPath(t))))
        {
            builder.Submenu(group.Key, sub =>
            {
                foreach (Type type in group)
                {
                    Type captured = type;
                    sub.Item(Leaf(VolumeComponent.MenuPath(type)), () =>
                    {
                        record("Add Override");
                        profile.Add(captured);
                    }, enabled: !profile.Has(type));
                }
            });
        }
    }

    private static string Category(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash > 0 ? path[..slash] : "Other";
    }

    private static string Leaf(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static void DrawCard(Paper paper, string id, VolumeProfile profile, VolumeComponent component, Scribe.FontFile font, Action<string> record)
    {
        bool expanded = !s_collapsed.Contains(id);
        Scribe.FontFile semi = EditorTheme.FontSemiBold ?? font;
        string title = Leaf(VolumeComponent.MenuPath(component.GetType()));

        using (paper.Column($"{id}_card").Height(UnitValue.Auto).Margin(Origami.Current.Metrics.Padding, Origami.Current.Metrics.Padding, 0, Origami.Current.Metrics.Spacing)
            .Rounded(EditorTheme.Roundness).BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            using (paper.Row($"{id}_head").Height(30).Padding(8, 6, 0, 0).Gap(6).Enter())
            {
                using (paper.Box($"{id}_en_wrap").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                    Origami.Checkbox(paper, $"{id}_en", component.Active, v => { record("Toggle Override"); component.Active = v; }).NoLabel().Show();

                using (paper.Row($"{id}_click").Width(UnitValue.Stretch()).Height(30).Gap(6).Rounded(EditorTheme.Roundness)
                    .Hovered.BackgroundColor(Color.FromArgb(13, EditorTheme.Purple400)).End()
                    .OnClick(id, (key, _) => { if (!s_collapsed.Remove(key)) s_collapsed.Add(key); }).Enter())
                {
                    paper.Box($"{id}_chev").Width(11).Height(30).IsNotInteractable()
                        .Text(expanded ? EditorIcons.ChevronDown : EditorIcons.ChevronRight, font)
                        .TextColor(EditorTheme.Ink300).FontSize(10f).Alignment(TextAlignment.MiddleCenter);
                    paper.Box($"{id}_title").Width(UnitValue.Stretch()).Height(30).IsNotInteractable()
                        .Text(title, semi).TextColor(component.Active ? EditorTheme.Ink500 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
                }

                HeaderText(paper, $"{id}_all", "All", () => { record("Override All"); component.SetAllOverrides(true); });
                HeaderText(paper, $"{id}_none", "None", () => { record("Override None"); component.SetAllOverrides(false); });
                EditorGUI.HeaderIconButton(paper, $"{id}_remove", EditorIcons.Trash, Loc.Get("inspector.remove"), () =>
                {
                    record("Remove Override");
                    profile.Remove(component.GetType());
                });
            }

            if (!expanded) return;

            using (paper.Column($"{id}_body").Height(UnitValue.Auto).Padding(0, 0, 2, Origami.Current.Metrics.Spacing).Enter())
            {
                CustomEditor? custom = EditorRegistries.GetCustomEditor(component.GetType());
                if (custom != null) custom.OnGUI(paper, $"{id}_custom", component);
                else DrawParameters(paper, id, component, record);
            }
        }
    }

    private static void HeaderText(Paper paper, string id, string text, Action onClick)
    {
        using (paper.Box($"{id}_w").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
            Origami.Button(paper, id, text, onClick).Small().Height(22).Ghost().Show();
    }

    /// <summary>A row per parameter of <paramref name="component"/>, for custom component editors too.</summary>
    public static void DrawParameters(Paper paper, string id, VolumeComponent component, Action<string>? record = null)
    {
        foreach (FieldInfo field in VolumeComponent.ParameterFields(component.GetType()))
            DrawParameter(paper, $"{id}_{field.Name}", component, field, record);
    }

    /// <summary>One parameter's row: its override checkbox, then its value, greyed out until overridden.</summary>
    public static void DrawParameter(Paper paper, string id, VolumeComponent component, FieldInfo field, Action<string>? record = null)
    {
        if (field.GetValue(component) is not VolumeParameter parameter) return;
        if (field.GetCustomAttribute<HeaderAttribute>() is { } header)
            Origami.Header(paper, $"{id}_hdr", header.Text).Show();

        OrigamiMetrics m = Origami.Current.Metrics;
        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(m.RowHeight).Padding(m.PaddingLarge, 0, 0, 0).Gap(2).Enter())
        {
            using (paper.Box($"{id}_ov_wrap").Width(UnitValue.Auto).Height(m.RowHeight).Enter())
                using (paper.Box($"{id}_ov_c").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                    Origami.Checkbox(paper, $"{id}_ov", parameter.Override, v => { record?.Invoke("Toggle Parameter Override"); parameter.Override = v; }).NoLabel().Show();

            using (paper.Column($"{id}_v").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
            {
                IDisposable? disabled = parameter.Override ? null : EnableIfAttributeHandler.PushDisabledScope();
                try { DrawValue(paper, $"{id}_f", PropertyGridUtils.NicifyName(field.Name), parameter, field, record); }
                finally { disabled?.Dispose(); }
            }
        }
    }

    private static void DrawValue(Paper paper, string id, string label, VolumeParameter parameter, FieldInfo field, Action<string>? record)
    {
        Type type = parameter.ValueType;
        RangeAttribute? range = field.GetCustomAttribute<RangeAttribute>();
        bool ranged = range != null && (type == typeof(float) || type == typeof(int));
        if (ranged) RangeSliderDrawer.Pending = range;
        try
        {
            PropertyGridUtils.DrawField(paper, id, label, type, parameter.BoxedValue, value =>
            {
                record?.Invoke($"Change {label}");
                parameter.BoxedValue = ranged ? Clamp(value, range!, type) : value;
            });
        }
        finally
        {
            if (ranged) RangeSliderDrawer.Pending = null;
        }
    }

    private static object? Clamp(object? value, RangeAttribute range, Type type)
        => type == typeof(float)
            ? Math.Clamp((float)(value ?? 0f), range.Min, range.Max)
            : Math.Clamp((int)(value ?? 0), (int)MathF.Round(range.Min), (int)MathF.Round(range.Max));

    /// <summary>
    /// A profile field with New and Clone beside it, and Save As Asset for a profile embedded in the scene.
    /// <paramref name="set"/> receives the new profile.
    /// </summary>
    public static void DrawProfileField(Paper paper, string id, string label, VolumeProfile? profile, string ownerName, Action<VolumeProfile?> set)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        PropertyGridUtils.DrawField(paper, id, label, typeof(VolumeProfile), profile, value => set(value as VolumeProfile));

        using (paper.Row($"{id}_btns").Height(UnitValue.Auto).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(m.Padding)
            .Margin(0, 0, 0, m.SpacingLarge).Enter())
        {
            Origami.Button(paper, $"{id}_new", $"{EditorIcons.Plus}  New", () =>
                set(new VolumeProfile { Name = $"{ownerName} Profile" })).Small().Show();

            if (profile.IsValid())
                Origami.Button(paper, $"{id}_clone", $"{EditorIcons.Clone}  Clone", () =>
                {
                    VolumeProfile copy = profile.Clone();
                    copy.Name = $"{profile.Name} Copy";
                    set(copy);
                }).Small().Show();

            if (profile.IsValid() && !profile.IsFromDatabase)
                Origami.Button(paper, $"{id}_save", $"{EditorIcons.FloppyDisk}  Save As Asset", () => SaveAsAsset(profile)).Small().Show();
        }
    }

    /// <summary>Writes an embedded profile to the project as a .volume asset, which the owner keeps referencing.</summary>
    public static void SaveAsAsset(VolumeProfile profile)
    {
        EditorAssetBackend? backend = EditorAssetBackend.Instance;
        if (backend == null) return;
        string folder = AssetCreateMenu.GetCurrentFolder();
        string absolute = AssetCreateMenu.GetAbsoluteFolder(folder);
        if (!Directory.Exists(absolute)) return;
        string name = AssetCreateMenu.FindUniqueName(absolute, string.IsNullOrWhiteSpace(profile.Name) ? "Volume Profile" : profile.Name, ".volume");
        backend.CreateAsset(profile, string.IsNullOrEmpty(folder) ? name : $"{folder}/{name}");
        EditorSceneManager.MarkDirty();
    }

    /// <summary>An embedded profile's edits belong to the holder's scene, a database profile's to the profile itself.</summary>
    private static UndoOwner OwnerOf(VolumeProfile profile, EngineObject? holder)
        => profile.IsFromDatabase || holder.IsNotValid() ? UndoOwner.Of(profile) : UndoOwner.Of(holder);
}
