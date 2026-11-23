// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.OrigamiUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// The scene view's right click menu. It is built from every <c>[MenuItem("SceneView/...")]</c>, so anything can add to it,
/// and a live <see cref="SceneTool"/> can add items or replace it with its own.
/// </summary>
public static class SceneViewMenu
{
    /// <summary>The camera of the scene view the menu was opened in.</summary>
    public static EditorCamera? Camera { get; internal set; }

    /// <summary>The scene being edited in that view.</summary>
    public static Scene? Scene { get; private set; }

    /// <summary>Where the right click landed on a surface, or on the ground plane when it missed everything.</summary>
    public static Float3 Point { get; private set; }

    /// <summary>Opens the menu at the cursor. The object under the cursor is selected first unless it already is.</summary>
    internal static void Open(SceneToolContext ctx, Scene scene, EditorCamera camera, Float2 pointer, Float2 mouseLocal, Float2 panelSize)
    {
        Camera = camera;
        Scene = scene;
        Point = SceneViewPanel.GetDropPosition(scene, camera, mouseLocal, panelSize);

        SceneTool? picking = SceneToolManager.Live.FirstOrDefault(t => t.ReplacesSceneContextMenu);
        if (picking != null) ctx.CurrentTool = picking;
        if (picking == null || !picking.OnSceneContextPick(ctx, camera.ScreenPointToRay(mouseLocal, panelSize)))
        {
            GameObject? hit = SceneViewPanel.PickObjectAt(scene, camera, mouseLocal, panelSize, pickGizmos: true);
            if (hit.IsValid() && !Selection.IsSelected(hit!)) Selection.Select(hit!);
        }
        // Scene items act on the selection themselves, and new objects go to the scene root rather than under it
        MenuContext.Set(null);

        Origami.ContextMenu(pointer.X, pointer.Y, builder =>
        {
            SceneTool? replacing = SceneToolManager.Live.FirstOrDefault(t => t.ReplacesSceneContextMenu);
            if (replacing != null)
            {
                ctx.CurrentTool = replacing;
                replacing.OnSceneContextMenu(ctx, builder);
                builder.Separator();
                builder.Submenu("Scene", sub => BuildScene(sub));
                return;
            }

            BuildScene(builder);
            foreach (SceneTool tool in SceneToolManager.Live)
            {
                ctx.CurrentTool = tool;
                tool.OnSceneContextMenu(ctx, builder);
            }
        });
    }

    private static void BuildScene(ContextBuilder builder)
    {
        builder.Submenu("Create", sub => MenuItemAttribute.BuildContextMenu(sub, "GameObject", AtPoint));
        MenuItemAttribute.BuildContextMenu(builder, "SceneView");
    }

    /// <summary>
    /// Runs a create item, then moves what it made onto the point that was right clicked, so it lands where the menu
    /// was opened rather than in front of the camera. Objects keep their places relative to each other.
    /// </summary>
    private static Action AtPoint(Action create) => () =>
    {
        Scene? scene = Scene;
        if (scene.IsNotValid())
        {
            create();
            return;
        }

        var before = new HashSet<GameObject>(scene!.Objects);
        create();

        List<GameObject> made = scene.Objects.Where(go => !before.Contains(go) && (go.Parent.IsNotValid() || before.Contains(go.Parent!))).ToList();
        if (made.Count == 0) return;

        Float3 offset = Point - made[0].Transform.Position;
        foreach (GameObject go in made) go.Transform.Position += offset;
    };

    // ================================================================
    //  Utilities
    // ================================================================

    private static List<GameObject> Selected() => HierarchyPanel.ExcludeNestedSelections([.. Selection.GetSelected<GameObject>()]);

    private static bool AnySelected() => Selection.GetSelected<GameObject>().Any();

    private static void Each(string name, Action<GameObject> change)
    {
        foreach (GameObject go in Selected())
        {
            Undo.RecordObject(go, name);
            change(go);
        }
    }

    [MenuItem("SceneView/Duplicate", priority: 10, SelectionWide = true)]
    private static void Duplicate()
    {
        foreach (GameObject copy in GameObjectClipboard.Duplicate(Selected())) Undo.RegisterCreatedObjectUndo(copy, "Duplicate");
    }

    [MenuItem("SceneView/Duplicate", isValidate: true)]
    private static bool CanDuplicate() => AnySelected();

    [MenuItem("SceneView/Copy", priority: 11, SelectionWide = true)]
    private static void Copy() => GameObjectClipboard.Copy(Selected());

    [MenuItem("SceneView/Copy", isValidate: true)]
    private static bool CanCopy() => AnySelected();

    [MenuItem("SceneView/Paste Here", priority: 12, SelectionWide = true)]
    private static void PasteHere()
    {
        List<GameObject> pasted = GameObjectClipboard.Paste();
        if (pasted.Count == 0) return;

        Float3 centre = pasted.Aggregate(Float3.Zero, (sum, go) => sum + go.Transform.Position) / pasted.Count;
        foreach (GameObject go in pasted)
        {
            go.Transform.Position += Point - centre;
            Undo.RegisterCreatedObjectUndo(go, "Paste");
        }
    }

    [MenuItem("SceneView/Delete", priority: 13, SelectionWide = true)]
    private static void Delete()
    {
        HierarchyPanel.DeleteGameObjects(Selected());
        Selection.Clear();
    }

    [MenuItem("SceneView/Delete", isValidate: true)]
    private static bool CanDelete() => AnySelected();

    [MenuItem("SceneView/Group Under Empty Parent", priority: 20, Separator = true, SelectionWide = true)]
    private static void Group() => HierarchyPanel.CreateEmptyParent();

    [MenuItem("SceneView/Group Under Empty Parent", isValidate: true)]
    private static bool CanGroup() => AnySelected();

    [MenuItem("SceneView/Select/Parent", priority: 30, SelectionWide = true)]
    private static void SelectParent()
    {
        List<GameObject> parents = Selected().Select(go => go.Parent).Where(p => p.IsValid()).Distinct().ToList()!;
        if (parents.Count == 0) return;
        Selection.Clear();
        foreach (GameObject parent in parents) Selection.AddToSelection(parent);
    }

    [MenuItem("SceneView/Select/Children", priority: 31, SelectionWide = true)]
    private static void SelectChildren()
    {
        List<GameObject> children = Selected().SelectMany(go => go.Children).ToList();
        if (children.Count == 0) return;
        Selection.Clear();
        foreach (GameObject child in children) Selection.AddToSelection(child);
    }

    [MenuItem("SceneView/Select/Deselect All", priority: 32, SelectionWide = true)]
    private static void Deselect() => Selection.Clear();

    [MenuItem("SceneView/Transform/Move Here", priority: 40, Separator = true, SelectionWide = true)]
    private static void MoveHere()
    {
        List<GameObject> selected = Selected();
        if (selected.Count == 0) return;
        Float3 centre = selected.Aggregate(Float3.Zero, (sum, go) => sum + go.Transform.Position) / selected.Count;
        Each("Move Here", go => go.Transform.Position += Point - centre);
    }

    [MenuItem("SceneView/Transform/Drop to Ground", priority: 41, SelectionWide = true)]
    private static void DropToGround()
    {
        if (Scene.IsNotValid()) return;
        Each("Drop to Ground", go =>
        {
            if (GroundBelow(go) is Float3 ground) go.Transform.Position = ground;
        });
    }

    [MenuItem("SceneView/Transform/Snap to Grid", priority: 42, SelectionWide = true)]
    private static void SnapToGrid()
    {
        float step = SceneTools.MoveSnap;
        Each("Snap to Grid", go =>
        {
            Float3 p = go.Transform.Position;
            go.Transform.Position = new Float3(MathF.Round(p.X / step) * step, MathF.Round(p.Y / step) * step, MathF.Round(p.Z / step) * step);
        });
    }

    [MenuItem("SceneView/Transform/Move to View", priority: 43, SelectionWide = true)]
    private static void MoveToView()
    {
        if (Camera == null) return;
        Each("Move to View", go => go.Transform.Position = Camera.ViewFocusPoint);
    }

    [MenuItem("SceneView/Transform/Align With View", priority: 44, SelectionWide = true)]
    private static void AlignWithView()
    {
        if (Camera == null) return;
        Transform view = Camera.Camera.GameObject.Transform;
        Each("Align With View", go => go.Transform.SetPositionAndRotation(view.Position, view.Rotation));
    }

    [MenuItem("SceneView/Transform/Reset Position", priority: 45, Separator = true, SelectionWide = true)]
    private static void ResetPosition() => Each("Reset Position", go => go.Transform.LocalPosition = Float3.Zero);

    [MenuItem("SceneView/Transform/Reset Rotation", priority: 46, SelectionWide = true)]
    private static void ResetRotation() => Each("Reset Rotation", go => go.Transform.LocalRotation = Quaternion.Identity);

    [MenuItem("SceneView/Transform/Reset Scale", priority: 47, SelectionWide = true)]
    private static void ResetScale() => Each("Reset Scale", go => go.Transform.LocalScale = Float3.One);

    [MenuItem("SceneView/Transform/Move Here", isValidate: true)]
    private static bool CanMove() => AnySelected();

    [MenuItem("SceneView/Transform/Drop to Ground", isValidate: true)]
    private static bool CanDrop() => AnySelected();

    [MenuItem("SceneView/Transform/Snap to Grid", isValidate: true)]
    private static bool CanSnap() => AnySelected();

    [MenuItem("SceneView/View/Frame Selected", priority: 60, Separator = true, SelectionWide = true)]
    private static void FrameSelected() => Camera?.FocusSelection();

    [MenuItem("SceneView/View/Top", priority: 61, SelectionWide = true)]
    internal static void ViewTop() => Look(new Float3(0, -1, 0));

    [MenuItem("SceneView/View/Bottom", priority: 62, SelectionWide = true)]
    internal static void ViewBottom() => Look(new Float3(0, 1, 0));

    [MenuItem("SceneView/View/Front", priority: 63, SelectionWide = true)]
    internal static void ViewFront() => Look(new Float3(0, 0, 1));

    [MenuItem("SceneView/View/Back", priority: 64, SelectionWide = true)]
    internal static void ViewBack() => Look(new Float3(0, 0, -1));

    [MenuItem("SceneView/View/Left", priority: 65, SelectionWide = true)]
    internal static void ViewLeft() => Look(new Float3(1, 0, 0));

    [MenuItem("SceneView/View/Right", priority: 66, SelectionWide = true)]
    internal static void ViewRight() => Look(new Float3(-1, 0, 0));

    [MenuItem("SceneView/View/Toggle Perspective", priority: 67, Separator = true, SelectionWide = true)]
    internal static void TogglePerspective() => Camera?.ToggleProjection();

    [MenuItem("SceneView/View/Show Grid", priority: 68, SelectionWide = true)]
    private static void ToggleGrid()
    {
        if (Camera != null) Camera.ShowGrid = !Camera.ShowGrid;
    }

    [MenuItem("SceneView/View/Show Gizmos", priority: 69, SelectionWide = true)]
    private static void ToggleGizmos()
    {
        if (Camera != null) Camera.ShowGizmos = !Camera.ShowGizmos;
    }

    [MenuItem("SceneView/Tool/Move", priority: 80, Separator = true, SelectionWide = true)]
    private static void ToolMove() => SceneTools.Transform = TransformTool.Translate;

    [MenuItem("SceneView/Tool/Rotate", priority: 81, SelectionWide = true)]
    private static void ToolRotate() => SceneTools.Transform = TransformTool.Rotate;

    [MenuItem("SceneView/Tool/Scale", priority: 82, SelectionWide = true)]
    private static void ToolScale() => SceneTools.Transform = TransformTool.Scale;

    [MenuItem("SceneView/Tool/Universal", priority: 83, SelectionWide = true)]
    private static void ToolUniversal() => SceneTools.Transform = TransformTool.Universal;

    [MenuItem("SceneView/Tool/Grid Snap", priority: 84, Separator = true, SelectionWide = true)]
    private static void ToggleGridSnap() => SceneTools.GridSnap = !SceneTools.GridSnap;

    [MenuItem("SceneView/Tool/Rotation Snap", priority: 85, SelectionWide = true)]
    private static void ToggleRotationSnap() => SceneTools.RotationSnap = !SceneTools.RotationSnap;

    /// <summary>Turns the camera to look along <paramref name="forward"/>, as the view cube does.</summary>
    private static void Look(Float3 forward)
    {
        if (Camera == null) return;
        float yaw = MathF.Atan2(forward.X, forward.Z) * (180f / MathF.PI);
        float pitch = MathF.Asin(-forward.Y) * (180f / MathF.PI);
        Camera.SetOrientation(yaw, pitch);
    }

    /// <summary>The first surface straight below an object that is not part of it.</summary>
    private static Float3? GroundBelow(GameObject go)
    {
        var ray = new Ray(go.Transform.Position + Float3.UnitY * 0.01f, -Float3.UnitY);
        float best = float.MaxValue;
        foreach (GameObject other in Scene!.Objects)
        {
            if (IsWithin(other, go) || !other.EnabledInHierarchy) continue;
            MeshRenderer renderer = other.GetComponent<MeshRenderer>();
            if (renderer.IsValid() && renderer.Raycast(ray, out float distance) && distance < best) best = distance;
        }
        return best < float.MaxValue ? ray.Origin + ray.Direction * best : null;
    }

    private static bool IsWithin(GameObject candidate, GameObject root)
    {
        for (GameObject? current = candidate; current.IsValid(); current = current!.Parent)
            if (ReferenceEquals(current, root)) return true;
        return false;
    }
}
