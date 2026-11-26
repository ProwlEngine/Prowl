// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Menu entries that create CSG objects.</summary>
public static class CSGMenuItems
{
    [MenuItem("GameObject/CSG/Model", priority: 65, Icon = EditorIcons.CubesStacked)]
    private static void CreateModel() => CSGSelection.CreateModel();

    [MenuItem("GameObject/CSG/Brush", priority: 66, Icon = EditorIcons.Cube)]
    private static void CreateBrush()
    {
        CSGModel? existing = CSGSelection.ActiveModel();
        CSGModel model = existing.IsValid() ? existing! : CSGSelection.CreateModel();

        GameObject go = CSGSelection.CreateObject("Brush", model.GameObject);
        go.AddComponent<CSGBrush>().ResetToBox(Float3.One);

        Undo.RegisterCreatedObjectUndo(go, "Create CSG Brush");
        Selection.Select(go);
    }

    [MenuItem("GameObject/CSG/Group", priority: 67, Icon = EditorIcons.LayerGroup)]
    private static void CreateGroup()
    {
        CSGModel? existing = CSGSelection.ActiveModel();
        CSGModel model = existing.IsValid() ? existing! : CSGSelection.CreateModel();

        GameObject go = CSGSelection.CreateObject("Group", model.GameObject);
        go.AddComponent<CSGGroup>();

        Undo.RegisterCreatedObjectUndo(go, "Create CSG Group");
        Selection.Select(go);
    }

    [MenuItem("GameObject/CSG/Apply Rotation and Scale", priority: 69)]
    private static void ApplyRotationScale()
        => CSGSelection.ApplyTransform(Selection.GetSelected<GameObject>(), ModelEditingMode.TransformParts.Rotation | ModelEditingMode.TransformParts.Scale);

    /// <summary>Wraps the selected brushes in a group at their centre, so they can be subtracted or intersected as one solid.</summary>
    [MenuItem("GameObject/CSG/Group Selection", priority: 68, Icon = EditorIcons.ObjectGroup)]
    internal static void GroupSelection()
    {
        List<GameObject> selected = CSGSelection.Roots();
        if (selected.Count == 0) return;

        // Hierarchy order is CSG order, so the group takes the place of the first object under their common parent
        // and keeps their order inside
        GameObject? parent = CommonParent(selected);
        List<GameObject> order = parent.IsValid() ? PreOrder(parent!) : [];
        selected.Sort((x, y) => order.IndexOf(x).CompareTo(order.IndexOf(y)));
        int index = parent.IsValid() ? ChildOf(parent!, selected[0]).SiblingIndex : selected[0].SiblingIndex;

        Float3 centre = Float3.Zero;
        foreach (GameObject go in selected) centre += go.Transform.Position;
        centre /= selected.Count;

        GameObject group = CSGSelection.CreateObject("Group", parent);
        group.Transform.Position = centre;
        group.AddComponent<CSGGroup>();
        group.SiblingIndex = index;
        Undo.RegisterCreatedObjectUndo(group, "Group CSG Selection");

        foreach (GameObject go in selected) Undo.SetTransformParent(go, group, worldPositionStays: true, "Group CSG Selection");

        Selection.Select(group);
    }

    /// <summary>The nearest object every one of them sits under.</summary>
    private static GameObject? CommonParent(List<GameObject> objects)
    {
        for (GameObject? candidate = objects[0].Parent; candidate.IsValid(); candidate = candidate!.Parent)
            if (objects.TrueForAll(go => IsUnder(go, candidate!))) return candidate;
        return null;
    }

    private static bool IsUnder(GameObject go, GameObject ancestor)
    {
        for (GameObject? current = go.Parent; current.IsValid(); current = current!.Parent)
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    /// <summary>The child of <paramref name="parent"/> that <paramref name="go"/> is, or sits under.</summary>
    private static GameObject ChildOf(GameObject parent, GameObject go)
    {
        GameObject current = go;
        while (current.Parent.IsValid() && !ReferenceEquals(current.Parent, parent)) current = current.Parent!;
        return current;
    }

    /// <summary>Everything under an object, each before its children, in sibling order.</summary>
    private static List<GameObject> PreOrder(GameObject root)
    {
        var result = new List<GameObject>();
        void Visit(GameObject go)
        {
            foreach (GameObject child in go.Children)
            {
                result.Add(child);
                Visit(child);
            }
        }
        Visit(root);
        return result;
    }
}
