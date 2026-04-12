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
/// PropertyEditor for AssetRef&lt;T&gt; fields (via the IAssetRef interface).
/// Supports asset references, runtime instances, drag-drop from project/hierarchy/inspector.
/// </summary>
[CustomPropertyEditor(typeof(IAssetRef))]
public class AssetRefPropertyEditor : PropertyEditor
{
    private static bool _selectorOpen;
    private static Type? _selectorType;
    private static Action<object?>? _selectorCallback;

    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var assetRef = value as IAssetRef;
        if (assetRef == null) return;

        Type fieldType = assetRef.InstanceType;
        var instance = assetRef.GetInstance() as EngineObject;

        bool isAsset = instance != null && instance.AssetID != Guid.Empty;
        bool isInstance = instance != null && instance.AssetID == Guid.Empty;
        string suffix = isAsset ? instance!.GetType().Name : isInstance ? "Instance" : fieldType.Name;
        string displayName = instance != null ? $"{instance.Name} ({suffix})" : $"None ({fieldType.Name})";
        string icon = isAsset ? EditorIcons.Cube : isInstance ? EditorIcons.CircleDot : EditorIcons.Circle;
        var iconColor = isAsset ? EditorTheme.Purple400 : isInstance ? EditorTheme.Ink500 : EditorTheme.Ink300;

        using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(4).Enter())
        {
            // Label
            if (!string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            // Check for compatible drags
            bool isDragTarget = false;
            if (DragDrop.IsDragging)
            {
                if (DragDrop.Payload is AssetDragPayload adp && adp.AssetType != null && fieldType.IsAssignableFrom(adp.AssetType))
                    isDragTarget = true;
                else if (DragDrop.Payload is GameObjectDragPayload && typeof(GameObject).IsAssignableFrom(fieldType))
                    isDragTarget = true;
                else if (DragDrop.Payload is GameObjectDragPayload && typeof(MonoBehaviour).IsAssignableFrom(fieldType))
                    isDragTarget = true; // Will search GO for matching component
                else if (DragDrop.Payload is ComponentDragPayload cdp && fieldType.IsAssignableFrom(cdp.Component.GetType()))
                    isDragTarget = true;
            }

            var fieldEl = paper.Row($"{id}_field")
                .Height(EditorTheme.RowHeight)
                .BackgroundColor(isDragTarget ? Color.FromArgb(60, EditorTheme.Purple400) : EditorTheme.Neutral300)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(3).ChildLeft(4).ChildRight(2).RowBetween(2)
                .BorderColor(isDragTarget ? EditorTheme.Purple400 : EditorTheme.Ink200).BorderWidth(1)
                .OnClick((fieldType, assetRef, onChange, instance, isAsset, isInstance), (cap, e) =>
                {
                    if (cap.instance != null)
                        Selection.Ping(cap.instance!.AssetID);
                })
                .OnDoubleClick((fieldType, assetRef, onChange, instance, isAsset, isInstance), (cap, _) =>
                {
                    if (cap.isAsset)
                    {
                        // Asset → focus in project
                        Selection.Ping(cap.instance!.AssetID);
                    }
                    else if (cap.isInstance)
                    {
                        // Instance → select it in inspector
                        Selection.Select(cap.instance!);
                    }
                    else
                    {
                        // None → open selector
                        OpenSelector(cap.fieldType, asset => { cap.assetRef.SetInstance(asset); cap.onChange(cap.assetRef); });
                    }
                });

            using (fieldEl.Enter())
            {
                // Accept drops
                HandleDrops(paper, assetRef, fieldType, onChange);

                // Icon
                paper.Box($"{id}_ico")
                    .Width(16).Height(EditorTheme.RowHeight)
                    .IsNotInteractable()
                    .Text(icon, font).TextColor(iconColor)
                    .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                // Name
                paper.Box($"{id}_name")
                    .Height(EditorTheme.RowHeight).Clip()
                    .IsNotInteractable()
                    .Text(displayName, font)
                    .TextColor(instance != null ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                // Picker button (circle)
                paper.Box($"{id}_pick")
                    .Width(20).Height(EditorTheme.RowHeight)
                    .Text(EditorIcons.CircleDot, font).TextColor(EditorTheme.Ink400)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                    .Rounded(3)
                    .OnClick((fieldType, assetRef, onChange), (cap, e) =>
                    {
                        e.StopPropagation();
                        OpenSelector(cap.Item1, asset => { cap.Item2.SetInstance(asset); cap.Item3(cap.Item2); });
                    });
            }
        }
    }

    private static void HandleDrops(Paper paper, IAssetRef assetRef, Type fieldType, Action<object?> onChange)
    {
        if (!paper.IsParentHovered || DragDrop.IsDragging) return;

        // Asset drop
        if (DragDrop.Payload is AssetDragPayload adp && adp.AssetType != null && fieldType.IsAssignableFrom(adp.AssetType))
        {
            var droppedAsset = Runtime.AssetDatabase.Get(adp.AssetGuid);
            if (droppedAsset != null)
            {
                assetRef.SetInstance(droppedAsset);
                onChange(assetRef);
            }
            DragDrop.EndDrag();
            return;
        }

        // GameObject drop
        if (DragDrop.Payload is GameObjectDragPayload goDrop && goDrop.GameObjects.Length > 0)
        {
            var go = goDrop.GameObjects[0];

            if (typeof(GameObject).IsAssignableFrom(fieldType))
            {
                // Direct GO reference
                assetRef.SetInstance(go);
                onChange(assetRef);
            }
            else if (typeof(MonoBehaviour).IsAssignableFrom(fieldType))
            {
                // Search GO for matching component
                var comp = go.GetComponent(fieldType);
                if (comp != null)
                {
                    assetRef.SetInstance(comp);
                    onChange(assetRef);
                }
            }
            DragDrop.EndDrag();
            return;
        }

        // Component drop
        if (DragDrop.Payload is ComponentDragPayload cdp && fieldType.IsAssignableFrom(cdp.Component.GetType()))
        {
            assetRef.SetInstance(cdp.Component);
            onChange(assetRef);
            DragDrop.EndDrag();
            return;
        }
    }

    private static void OpenSelector(Type type, Action<object?> onChange)
    {
        _selectorOpen = true;
        _selectorType = type;
        _selectorCallback = onChange;
    }

    /// <summary>
    /// Draw the asset selector modal. Call from EditorApplication.EndGui.
    /// </summary>
    public static void DrawSelectorModal(Paper paper)
    {
        if (!_selectorOpen || _selectorType == null) return;

        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var db = EditorAssetDatabase.Instance;
        var matchingItems = db?.FindAllOfType(_selectorType).Take(100).ToList()
            ?? new System.Collections.Generic.List<(Guid guid, string name, string parentPath, Type assetType)>();

        paper.Box("ar_sel_overlay")
            .PositionType(PositionType.SelfDirected).Position(0, 0)
            .Size(UnitValue.Stretch(), UnitValue.Stretch())
            .BackgroundColor(Color.FromArgb(120, 0, 0, 0))
            .Layer(Layer.Overlay)
            .OnClick(0, (_, _) => _selectorOpen = false);

        using (paper.Column("ar_sel_modal")
            .Size(350, 400)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(8)
            .Layer(Layer.Overlay)
            .Enter())
        {
            using (paper.Row("ar_sel_header")
                .Height(32).ChildLeft(12).ChildRight(8).RowBetween(8)
                .BackgroundColor(EditorTheme.Neutral200)
                .Enter())
            {
                paper.Box("ar_sel_title").Height(32)
                    .Text($"Select {_selectorType.Name}", font)
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                paper.Box("ar_sel_spacer");

                paper.Box("ar_sel_close")
                    .Width(24).Height(24).Rounded(4)
                    .Hovered.BackgroundColor(Color.FromArgb(255, 180, 60, 60)).End()
                    .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) => _selectorOpen = false);
            }

            using (ScrollView.Begin(paper, "ar_sel_scroll", 350, 360, paddingLeft: 4, paddingRight: 4, paddingTop: 4))
            {
                paper.Box("ar_sel_none")
                    .Height(EditorTheme.RowHeight).ChildLeft(8)
                    .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                    .Rounded(3)
                    .Text("None", font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                    .OnClick(0, (_, _) =>
                    {
                        _selectorCallback?.Invoke(null);
                        _selectorOpen = false;
                    });

                var builtInItems = matchingItems.Where(m => Runtime.BuiltInAssets.IsBuiltIn(m.guid)).ToList();
                var projectItems = matchingItems.Where(m => !Runtime.BuiltInAssets.IsBuiltIn(m.guid)).ToList();

                if (builtInItems.Count > 0)
                {
                    paper.Box("ar_sel_bi_hdr").Height(EditorTheme.RowHeight).ChildLeft(6)
                        .Text($"{EditorIcons.Star}  Built-In", font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleLeft);

                    for (int i = 0; i < builtInItems.Count; i++)
                    {
                        var (guid, name, parentPath, _) = builtInItems[i];
                        DrawSelectorItem(paper, font, $"ar_sel_bi_{i}", guid, name, "Built-In", EditorIcons.Star);
                    }
                }

                if (projectItems.Count > 0)
                {
                    if (builtInItems.Count > 0)
                        paper.Box("ar_sel_sep2").Height(1).Margin(4, 2, 4, 2).BackgroundColor(EditorTheme.Ink200);

                    paper.Box("ar_sel_proj_hdr").Height(EditorTheme.RowHeight).ChildLeft(6)
                        .Text($"{EditorIcons.FolderOpen}  Project", font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleLeft);

                    for (int i = 0; i < projectItems.Count; i++)
                    {
                        var (guid, name, parentPath, _) = projectItems[i];
                        DrawSelectorItem(paper, font, $"ar_sel_proj_{i}", guid, name, Path.GetFileName(parentPath), EditorIcons.Cube);
                    }
                }

                if (matchingItems.Count == 0)
                {
                    paper.Box("ar_sel_empty").Height(40)
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
            .Hovered.BackgroundColor(EditorTheme.Purple400).End()
            .Rounded(3)
            .OnClick(guid, (g, _) =>
            {
                var asset = Runtime.AssetDatabase.Get(g);
                _selectorCallback?.Invoke(asset);
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
