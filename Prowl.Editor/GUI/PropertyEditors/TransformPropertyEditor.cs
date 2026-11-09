// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// PropertyEditor for Transform fields. Shows a reference picker that lets
/// users select a Transform from a GameObject in the current scene.
/// </summary>
[CustomPropertyEditor(typeof(Transform))]
public class TransformPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        GameObject? go = (value as Transform)?.GameObject;
        bool hasValue = go.IsValid();
        OpenScene? holder = EditorSceneManager.DrawingScene;

        PropertyGridUtils.ObjectField(paper, id, label,
            hasValue ? EditorIcons.ArrowsUpDownLeftRight : EditorIcons.Circle,
            hasValue ? EditorTheme.Purple400 : EditorTheme.Ink300,
            hasValue ? $"{go!.Name} (Transform)" : "None (Transform)",
            hasValue,
            isDragTarget: DragDrop.IsDragging && DragDrop.Payload is GameObjectDragPayload,
            onClick: () => { if (go.IsValid()) Selection.Ping(go!.Identifier); },
            onDoubleClick: () =>
            {
                if (go.IsValid()) Selection.Select(go!);
                else OpenSelector(onChange, holder);
            },
            onPick: () => OpenSelector(onChange, holder),
            acceptDrops: () =>
            {
                if (DragDrop.IsDragging || !paper.IsParentHovered || DragDrop.Payload is not GameObjectDragPayload drop) return;
                if (drop.GameObjects.Length > 0 && EditorSceneManager.RefusesReference(holder, drop.GameObjects[0], out string why))
                    Toasts.Warning(Rosetta.Loc.Get("hierarchy.scene_refused"), why);
                else if (drop.GameObjects.Length > 0) onChange(drop.GameObjects[0].Transform);
                DragDrop.EndDrag();
            });
    }

    private static void OpenSelector(Action<object?> onChange, OpenScene? holder)
        => SelectorModal.Open("Select Transform", typeof(Transform), SelectorTabs.Scene, onChange, holder);
}
