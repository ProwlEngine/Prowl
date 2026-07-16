using System;

using Prowl.Editor.GUI.Popups;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.OrigamiUI;
using Prowl.Runtime;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// PropertyEditor for all EngineObject-derived types (Mesh, Material, Shader, Texture2D, etc).
/// Shows an object reference field with name, icon, and asset selector modal.
/// </summary>
[CustomPropertyEditor(typeof(EngineObject))]
public class EngineObjectPropertyEditor : PropertyEditor
{

    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Match the Origami PropertyGrid standard row exactly so custom-typed fields line up with
        // the reflection-drawn ones (same metrics, muted label, horizontal padding).
        var m = Origami.Current.Metrics;
        float rh = m.RowHeight;

        var eo = value as EngineObject;
        // Use the declared field type for the selector
        Type fieldType = _lastFieldType ?? typeof(EngineObject);
        _lastFieldType = null; // consume it

        bool isAsset = eo != null && eo.AssetID != Guid.Empty;
        string suffix = eo != null ? (isAsset ? eo.GetType().Name : "Instance") : fieldType.Name;
        string displayName = eo != null ? $"{eo.Name} ({suffix})" : $"None ({fieldType.Name})";
        string icon = eo != null ? EditorIcons.Cube : EditorIcons.Circle;

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(rh).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).RowBetween(m.Padding).Enter())
        {
            // Label
            if (!string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(rh).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .IsNotInteractable()
                    .Text(label, font).TextColor(Origami.Current.Ink.C300)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            bool isDragTarget = EditorGUI.IsCompatibleDragTarget(fieldType);

            // Object field row
            var iconColor = eo != null ? EditorTheme.Purple400 : EditorTheme.Ink300;
            var fieldEl = paper.Row($"{id}_field")
                .Height(rh)
                .BackgroundColor(isDragTarget ? System.Drawing.Color.FromArgb(60, EditorTheme.Purple400) : EditorTheme.Glass)
                .Hovered.BorderColor(EditorTheme.BorderStrong).End()
                .Rounded(6).Padding(m.SpacingLarge, m.PaddingSmall, 0, 0).RowBetween(m.SpacingLarge)
                .BorderColor(isDragTarget ? EditorTheme.Purple400 : EditorTheme.BorderSoft).BorderWidth(1)
                .OnClick((eo, isAsset), (cap, e) =>
                {
                    // Single click: ping the asset (highlight without selecting)
                    if (cap.isAsset && cap.eo != null)
                        Selection.Ping(cap.eo.AssetID);
                })
                .OnDoubleClick((fieldType, onChange, eo), (cap, _) =>
                {
                    // Double click: select the instance or open selector
                    if (cap.eo != null)
                        Selection.Select(cap.eo);
                    else
                        OpenAssetSelector(cap.fieldType, cap.onChange);
                });

            using (fieldEl.Enter())
            {
                // Accept asset drop. This editor only handles plain (non-AssetRef) fields,
                // so assigning an asset here serializes a copy into the scene instead of a
                // reference. Confirm with the user before committing that copy.
                var assetDrop = DragDrop.AcceptDrop<AssetDragPayload>(paper.IsParentHovered,
                    dp => dp.AssetType != null && fieldType.IsAssignableFrom(dp.AssetType));
                if (assetDrop != null)
                {
                    var droppedAsset = Runtime.AssetDatabase.Get(assetDrop.AssetGuid);
                    if (droppedAsset != null)
                        ConfirmAssetCopy(assetDrop.AssetName, droppedAsset, onChange);
                }

                // Accept GameObject drop
                if (!DragDrop.IsDragging && paper.IsParentHovered && DragDrop.Payload is GameObjectDragPayload goDrop)
                {
                    var go = goDrop.GameObjects.Length > 0 ? goDrop.GameObjects[0] : null;
                    if (go != null)
                    {
                        if (typeof(GameObject).IsAssignableFrom(fieldType))
                        {
                            onChange(go);
                        }
                        else if (typeof(MonoBehaviour).IsAssignableFrom(fieldType))
                        {
                            // Search GO for matching component
                            var comp = go.GetComponent(fieldType);
                            if (comp != null) onChange(comp);
                        }
                    }
                    DragDrop.EndDrag();
                }

                // Accept Component drop
                if (!DragDrop.IsDragging && paper.IsParentHovered && DragDrop.Payload is ComponentDragPayload compDrop)
                {
                    if (fieldType.IsAssignableFrom(compDrop.Component.GetType()))
                        onChange(compDrop.Component);
                    DragDrop.EndDrag();
                }

                // Leading type icon (no chip background)
                paper.Box($"{id}_ico")
                    .Width(UnitValue.Auto).Height(rh).IsNotInteractable()
                    .Text(icon, font).TextColor(iconColor)
                    .FontSize(11f).Alignment(TextAlignment.MiddleCenter);

                // Name
                paper.Box($"{id}_name")
                    .Width(UnitValue.Stretch()).Height(rh).Clip()
                    .IsNotInteractable()
                    .Text(displayName, font)
                    .TextColor(eo != null ? EditorTheme.Ink500 : EditorTheme.Ink200)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                // Picker button
                paper.Box($"{id}_pick")
                    .Width(18).Height(18).Rounded(4).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .Text(EditorIcons.CircleDot, font).TextColor(EditorTheme.Ink200)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .Hovered.BackgroundColor(EditorTheme.Hover).End()
                    .OnClick((fieldType, onChange), (cap, _) => OpenAssetSelector(cap.Item1, cap.Item2));
            }
        }
    }

    /// <summary>
    /// Stores the declared field type so we search for the right asset type.
    /// Called by PropertyGrid before OnGUI.
    /// </summary>
    [ThreadStatic] private static Type? _lastFieldType;
    public static void SetFieldType(Type type) => _lastFieldType = type;

    /// <summary>
    /// Warns that the target field is not an AssetRef, so assigning an asset stores a copy
    /// inside the scene rather than a reference. Only applies the value if the user confirms.
    /// </summary>
    private static void ConfirmAssetCopy(string assetName, object? asset, Action<object?> onChange)
    {
        var dialog = new DialogModal { Title = "Field Is Not an Asset Reference", Width = 440 };
        dialog.DrawContent = p =>
        {
            Origami.Label(p, "ac_l1", $"'{assetName}' is an asset, but this field is not an AssetRef.").Show();
            Origami.Label(p, "ac_l2", "Assigning it stores a copy of the asset inside the scene").Show();
            Origami.Label(p, "ac_l3", "rather than a reference to the original asset.").Show();
            Origami.Label(p, "ac_l4", "Are you sure you want to continue?").Show();
        };
        dialog.Button("Assign Copy", () => { onChange(asset); Modal.Pop(); }, OrigamiVariant.Warning);
        dialog.Button("Cancel", Modal.Pop);
        Modal.Push(dialog);
    }

    internal static void OpenAssetSelector(Type type, Action<object?> onChange)
    {
        // Scene types (GameObject, MonoBehaviour subclasses) -> Scene tab
        // Asset types (Mesh, Material, etc.) -> Assets tab
        bool isSceneType = typeof(GameObject).IsAssignableFrom(type) || typeof(MonoBehaviour).IsAssignableFrom(type);
        var tabs = isSceneType ? SelectorTabs.Scene : SelectorTabs.Assets;

        SelectorModal.Open($"Select {type.Name}", type, tabs, selected =>
        {
            // Like a drag, picking an asset into a non-AssetRef field stores a copy in the
            // scene rather than a reference. Scene selections and "None" pass through unchanged.
            if (selected is EngineObject eo && eo.AssetID != Guid.Empty)
                ConfirmAssetCopy(eo.Name, eo, onChange);
            else
                onChange(selected);
        });
    }
}
