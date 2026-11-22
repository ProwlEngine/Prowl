// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Menu entries that create CSG objects.</summary>
public static class CSGMenuItems
{
    [MenuItem("GameObject/CSG/Model", priority: 40)]
    private static void CreateModel() => CSGSelection.CreateModel();

    [MenuItem("GameObject/CSG/Brush", priority: 41)]
    private static void CreateBrush()
    {
        CSGModel? existing = CSGSelection.ActiveModel();
        CSGModel model = existing.IsValid() ? existing! : CSGSelection.CreateModel();

        GameObject go = CSGSelection.CreateObject("Brush", model.GameObject);
        go.AddComponent<CSGBrush>().ResetToBox(Float3.One);

        Undo.RegisterCreatedObjectUndo(go, "Create CSG Brush");
        Selection.Select(go);
    }

    [MenuItem("GameObject/CSG/Group", priority: 42)]
    private static void CreateGroup()
    {
        CSGModel? existing = CSGSelection.ActiveModel();
        CSGModel model = existing.IsValid() ? existing! : CSGSelection.CreateModel();

        GameObject go = CSGSelection.CreateObject("Group", model.GameObject);
        go.AddComponent<CSGGroup>();

        Undo.RegisterCreatedObjectUndo(go, "Create CSG Group");
        Selection.Select(go);
    }

    /// <summary>Wraps the selected brushes in a group at their centre, so they can be subtracted or intersected as one solid.</summary>
    [MenuItem("GameObject/CSG/Group Selection", priority: 43)]
    private static void GroupSelection()
    {
        List<GameObject> selected = CSGSelection.Roots();
        if (selected.Count == 0) return;

        // Sibling order is CSG order, so the group takes the place of the first object and keeps their order inside
        GameObject? parent = selected[0].Parent;
        selected.Sort((a, b) => a.SiblingIndex.CompareTo(b.SiblingIndex));
        int index = selected.Where(go => ReferenceEquals(go.Parent, parent)).Min(go => go.SiblingIndex);

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
}
