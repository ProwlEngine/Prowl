using System;
using System.IO;
using System.Linq;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

/// <summary>
/// PropertyEditor for all EngineObject-derived types (Mesh, Material, Shader, Texture2D, etc).
/// Shows an object reference field with name, icon, and asset selector modal.
/// </summary>
[CustomPropertyEditor(typeof(EngineObject))]
public class EngineObjectPropertyEditor : PropertyEditor
{
    // Static state for the asset selector modal
    private static bool _selectorOpen;
    private static Type? _selectorType;
    private static Action<object?>? _selectorCallback;

    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var eo = value as EngineObject;
        // Use the declared field type for the selector
        Type fieldType = _lastFieldType ?? typeof(EngineObject);
        _lastFieldType = null; // consume it

        bool isAsset = eo != null && eo.AssetID != Guid.Empty;
        string suffix = eo != null ? (isAsset ? eo.GetType().Name : "Instance") : fieldType.Name;
        string displayName = eo != null ? $"{eo.Name} ({suffix})" : $"None ({fieldType.Name})";
        string icon = eo != null ? EditorIcons.Cube : EditorIcons.Circle;

        using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(4).Enter())
        {
            // Label
            if (!string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            // Check if a compatible asset is being dragged over this field
            bool isDragTarget = DragDrop.IsDragging && DragDrop.Payload is AssetDragPayload adp
                && adp.AssetType != null && fieldType.IsAssignableFrom(adp.AssetType);

            // Object field row
            var fieldEl = paper.Row($"{id}_field")
                .Height(EditorTheme.RowHeight)
                .BackgroundColor(isDragTarget ? System.Drawing.Color.FromArgb(60, EditorTheme.Accent) : EditorTheme.Neutral300)
                .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                .Rounded(3).ChildLeft(4).ChildRight(2).RowBetween(2)
                .BorderColor(isDragTarget ? EditorTheme.Accent : EditorTheme.Ink200).BorderWidth(1)
                .OnDoubleClick((fieldType, onChange), (cap, _) => OpenSelector(cap.Item1, cap.Item2));

            using (fieldEl.Enter())
            {
                // Accept drop
                if (isDragTarget && !DragDrop.IsDragging && DragDrop.Payload is AssetDragPayload dropPayload)
                {
                    var droppedAsset = Runtime.AssetDatabase.Get(dropPayload.AssetGuid);
                    if (droppedAsset != null && fieldType.IsAssignableFrom(droppedAsset.GetType()))
                        onChange(droppedAsset);
                    DragDrop.EndDrag();
                }
                else if (!DragDrop.IsDragging && paper.IsParentHovered && DragDrop.Payload is AssetDragPayload hoveredDrop)
                {
                    if (hoveredDrop.AssetType != null && fieldType.IsAssignableFrom(hoveredDrop.AssetType))
                    {
                        var droppedAsset = Runtime.AssetDatabase.Get(hoveredDrop.AssetGuid);
                        if (droppedAsset != null)
                            onChange(droppedAsset);
                        DragDrop.EndDrag();
                    }
                }

                // Icon
                paper.Box($"{id}_ico")
                    .Width(16).Height(EditorTheme.RowHeight)
                    .IsNotInteractable()
                    .Text(icon, font)
                    .TextColor(eo != null ? EditorTheme.Accent : EditorTheme.Ink300)
                    .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                // Name
                paper.Box($"{id}_name")
                    .Height(EditorTheme.RowHeight).Clip()
                    .IsNotInteractable()
                    .Text(displayName, font)
                    .TextColor(eo != null ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                // Picker circle button
                paper.Box($"{id}_pick")
                    .Width(20).Height(EditorTheme.RowHeight)
                    .Text(EditorIcons.CircleDot, font).TextColor(EditorTheme.Ink400)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .Hovered.BackgroundColor(EditorTheme.Accent).End()
                    .Rounded(3)
                    .OnClick((fieldType, onChange), (cap, _) => OpenSelector(cap.Item1, cap.Item2));
            }
        }
    }

    /// <summary>
    /// Stores the declared field type so we search for the right asset type.
    /// Called by PropertyGrid before OnGUI.
    /// </summary>
    [ThreadStatic] private static Type? _lastFieldType;
    public static void SetFieldType(Type type) => _lastFieldType = type;

    private static void OpenSelector(Type type, Action<object?> onChange)
    {
        _selectorOpen = true;
        _selectorType = type;
        _selectorCallback = onChange;
    }

    /// <summary>
    /// Draw the asset selector modal. Call from EditorApplication.EndGui or similar overlay location.
    /// </summary>
    public static void DrawSelectorModal(Paper paper)
    {
        if (!_selectorOpen || _selectorType == null) return;

        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var db = EditorAssetDatabase.Instance;
        var matchingItems = db?.FindAllOfType(_selectorType).Take(100).ToList()
            ?? new System.Collections.Generic.List<(Guid guid, string name, string parentPath, Type assetType)>();

        // Fullscreen blocker
        paper.Box("eo_sel_overlay")
            .PositionType(PositionType.SelfDirected).Position(0, 0)
            .Size(UnitValue.Stretch(), UnitValue.Stretch())
            .BackgroundColor(Color.FromArgb(120, 0, 0, 0))
            .Layer(Layer.Overlay)
            .OnClick(0, (_, _) => _selectorOpen = false); // Click outside to close

        // Modal window
        using (paper.Column("eo_sel_modal")
            .Size(350, 400)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(8)
            .Layer(Layer.Overlay)
            .Enter())
        {
            // Header
            using (paper.Row("eo_sel_header")
                .Height(32).ChildLeft(12).ChildRight(8).RowBetween(8)
                .BackgroundColor(EditorTheme.Neutral200)
                .Enter())
            {
                paper.Box("eo_sel_title").Height(32)
                    .Text($"Select {_selectorType.Name}", font)
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                paper.Box("eo_sel_spacer");

                paper.Box("eo_sel_close")
                    .Width(24).Height(24).Rounded(4)
                    .Hovered.BackgroundColor(Color.FromArgb(255, 180, 60, 60)).End()
                    .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) => _selectorOpen = false);
            }

            // List
            using (ScrollView.Begin(paper, "eo_sel_scroll", 350, 360, paddingLeft: 4, paddingRight: 4, paddingTop: 4))
            {
                // None option
                paper.Box("eo_sel_none")
                    .Height(EditorTheme.RowHeight).ChildLeft(8)
                    .Hovered.BackgroundColor(EditorTheme.Accent).End()
                    .Rounded(3)
                    .Text("None", font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                    .OnClick(0, (_, _) =>
                    {
                        _selectorCallback?.Invoke(null);
                        _selectorOpen = false;
                    });

                // Split into built-in and project assets
                var builtInItems = matchingItems.Where(m => Runtime.BuiltInAssets.IsBuiltIn(m.guid)).ToList();
                var projectItems = matchingItems.Where(m => !Runtime.BuiltInAssets.IsBuiltIn(m.guid)).ToList();

                // Built-in assets section
                if (builtInItems.Count > 0)
                {
                    paper.Box("eo_sel_bi_hdr").Height(EditorTheme.RowHeight).ChildLeft(6)
                        .Text($"{EditorIcons.Cube}  Built-In", font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleLeft);

                    for (int i = 0; i < builtInItems.Count; i++)
                    {
                        var (guid, name, parentPath, assetType) = builtInItems[i];
                        DrawSelectorItem(paper, font, $"eo_sel_bi_{i}", guid, name, "Built-In", EditorIcons.Star);
                    }
                }

                // Project assets section
                if (projectItems.Count > 0)
                {
                    if (builtInItems.Count > 0)
                        paper.Box("eo_sel_sep2").Height(1).Margin(4, 2, 4, 2).BackgroundColor(EditorTheme.Ink200);

                    paper.Box("eo_sel_proj_hdr").Height(EditorTheme.RowHeight).ChildLeft(6)
                        .Text($"{EditorIcons.FolderOpen}  Project", font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleLeft);

                    for (int i = 0; i < projectItems.Count; i++)
                    {
                        var (guid, name, parentPath, assetType) = projectItems[i];
                        string displayPath = Path.GetFileName(parentPath);
                        DrawSelectorItem(paper, font, $"eo_sel_proj_{i}", guid, name, displayPath, EditorIcons.Cube);
                    }
                }

                if (matchingItems.Count == 0)
                {
                    paper.Box("eo_sel_empty").Height(40)
                        .Text("No assets of this type found", font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleCenter);
                }
            }
        }
    }

    private static void DrawSelectorItem(Paper paper, Prowl.Scribe.FontFile font, string id, Guid guid, string name, string pathLabel, string icon)
    {
        using (paper.Row(id)
            .Height(EditorTheme.RowHeight).ChildLeft(12).RowBetween(4)
            .Hovered.BackgroundColor(EditorTheme.Accent).End()
            .Rounded(3)
            .OnClick(guid, (g, _) =>
            {
                var asset = Runtime.AssetDatabase.Get(g);
                if (asset != null) _selectorCallback?.Invoke(asset);
                _selectorOpen = false;
            })
            .Enter())
        {
            paper.Box($"{id}_ico")
                .Width(14).Height(EditorTheme.RowHeight)
                .Text(icon, font).TextColor(EditorTheme.Ink400)
                .FontSize(9f).Alignment(TextAlignment.MiddleCenter);

            paper.Box($"{id}_name")
                .Height(EditorTheme.RowHeight).Clip()
                .Text(name, font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            paper.Box($"{id}_path")
                .Width(UnitValue.Auto).Height(EditorTheme.RowHeight).ChildRight(4)
                .Text($"({pathLabel})", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize - 4).Alignment(TextAlignment.MiddleRight);
        }
    }
}
