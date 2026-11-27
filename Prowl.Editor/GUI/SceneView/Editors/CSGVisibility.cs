// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>
/// Which brushes are hidden in the viewport. Each brush keeps its own flag, so hiding is saved with the scene and undoes
/// like any edit. Hiding never changes the CSG result or the saved mesh.
/// </summary>
public static class CSGVisibility
{
    [InitializeOnLoad]
    private static void Install() => CSGModel.EditorHidden = brush => brush.HiddenInEditor;

    /// <summary>Has the models redraw, without rebuilding them.</summary>
    private static void Changed()
    {
        foreach (CSGModel model in Models()) model.RefreshHidden();
    }

    /// <summary>Models in the edit world and in whatever world is being edited now, a prefab or play world included.</summary>
    private static IEnumerable<CSGModel> Models()
    {
        Scene edited = EditorSceneManager.EditedWorld;
        Scene? edit = EditorSceneManager.EditWorldOrNull;
        foreach (Scene world in ReferenceEquals(edit, edited) || edit.IsNotValid() ? [edited] : new[] { edited, edit! })
        {
            foreach (GameObject go in world.Objects)
            {
                CSGModel model = go.GetComponent<CSGModel>();
                if (model.IsValid()) yield return model;
            }
        }
    }

    public static int HiddenCount => Models().Sum(model => CSGSelection.BrushesIn(model).Count(IsHidden));

    public static bool IsHidden(CSGBrush brush) => brush.HiddenInEditor;

    public static void Hide(IEnumerable<CSGBrush> brushes) => Set(brushes, true, "Hide CSG Brushes");

    /// <summary>Hides everything except the selection.</summary>
    public static void HideUnselected()
    {
        List<CSGBrush> selected = CSGSelection.Brushes();
        if (selected.Count == 0) return;

        Set(ActiveBrushes().Where(brush => !selected.Contains(brush)), true, "Hide Unselected CSG Brushes");
    }

    /// <summary>Swaps hidden and shown.</summary>
    public static void ToggleAll()
    {
        foreach (CSGBrush brush in ActiveBrushes())
        {
            Undo.RecordObject(brush, "Swap Hidden CSG Brushes");
            brush.HiddenInEditor = !brush.HiddenInEditor;
        }
        Changed();
    }

    public static void UnhideAll() => Set(Models().SelectMany(CSGSelection.BrushesIn), false, "Unhide CSG Brushes");

    private static void Set(IEnumerable<CSGBrush> brushes, bool hidden, string name)
    {
        foreach (CSGBrush brush in brushes.ToList())
        {
            if (brush.HiddenInEditor == hidden) continue;
            Undo.RecordObject(brush, name);
            brush.HiddenInEditor = hidden;
        }
        Changed();
    }

    private static List<CSGBrush> ActiveBrushes()
    {
        CSGModel? model = CSGSelection.ActiveModel();
        return model.IsValid() ? CSGSelection.BrushesIn(model!) : [];
    }
}
