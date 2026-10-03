using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// PropertyEditor for AssetRef&lt;T&gt; fields. Shows the asset named without loading it, and assigns a new
/// reference from a pick or a drop. Only database assets can be referenced lazily.
/// </summary>
[CustomPropertyEditor(typeof(IAssetRef))]
public class AssetRefPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        if (value is not IAssetRef assetRef) return;

        Type refType = value.GetType();
        Type fieldType = assetRef.AssetType;
        Asset? target = assetRef.IsEmpty ? null : AssetDatabase.Get(assetRef.AssetID);

        void Assign(object? picked)
            => onChange(Activator.CreateInstance(refType, picked is Asset { IsFromDatabase: true } asset ? asset.AssetID : Guid.Empty));
        void Pick() => SelectorModal.Open($"Select {fieldType.Name}", fieldType, SelectorTabs.Assets, Assign);

        bool missing = target is null ? !assetRef.IsEmpty : target.IsMissing;
        PropertyGridUtils.ObjectField(paper, id, label,
            assetRef.IsEmpty ? EditorIcons.Circle : EditorIcons.Cube,
            missing ? EditorTheme.Red400 : assetRef.IsEmpty ? EditorTheme.Ink300 : EditorTheme.Purple400,
            target is null && !assetRef.IsEmpty ? $"Missing ({fieldType.Name})" : PropertyGridUtils.DescribeObjectRef(target, fieldType),
            !assetRef.IsEmpty,
            EditorGUI.IsCompatibleDragTarget(fieldType),
            onClick: () => { if (!assetRef.IsEmpty) Selection.Ping(assetRef.AssetID); },
            onDoubleClick: () =>
            {
                if (!assetRef.IsEmpty) Selection.Ping(assetRef.AssetID);
                else Pick();
            },
            onPick: Pick,
            acceptDrops: () =>
            {
                var drop = DragDrop.AcceptDrop<AssetDragPayload>(paper.IsParentHovered,
                    dp => dp.AssetType != null && fieldType.IsAssignableFrom(dp.AssetType));
                if (drop != null) Assign(AssetDatabase.Get(drop.AssetGuid));
            });
    }
}
