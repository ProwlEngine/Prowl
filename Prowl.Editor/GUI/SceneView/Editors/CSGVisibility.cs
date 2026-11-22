// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime;
using Prowl.Runtime.CSG;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Which brushes are hidden in the viewport, per model. Hiding never changes the CSG result.</summary>
public static class CSGVisibility
{
    private static readonly HashSet<CSGBrush> _hidden = new();

    public static int HiddenCount => _hidden.Count;

    public static bool IsHidden(CSGBrush brush) => _hidden.Contains(brush);

    public static void Hide(IEnumerable<CSGBrush> brushes)
    {
        foreach (CSGBrush brush in brushes) _hidden.Add(brush);
    }

    /// <summary>Hides everything except the selection.</summary>
    public static void HideUnselected()
    {
        List<CSGBrush> selected = CSGSelection.Brushes();
        if (selected.Count == 0) return;

        foreach (CSGBrush brush in AllBrushes())
            if (!selected.Contains(brush)) _hidden.Add(brush);
    }

    /// <summary>Swaps hidden and shown.</summary>
    public static void ToggleAll()
    {
        var shown = new List<CSGBrush>();
        foreach (CSGBrush brush in AllBrushes())
            if (!_hidden.Contains(brush)) shown.Add(brush);

        _hidden.Clear();
        foreach (CSGBrush brush in shown) _hidden.Add(brush);
    }

    public static void UnhideAll() => _hidden.Clear();

    /// <summary>Drops brushes that have been deleted.</summary>
    public static void Prune() => _hidden.RemoveWhere(b => b.IsNotValid());

    private static IEnumerable<CSGBrush> AllBrushes()
    {
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) yield break;

        foreach (GameObject go in model.GameObject.GetChildrenDeep())
        {
            CSGBrush brush = go.GetComponent<CSGBrush>();
            if (brush.IsValid()) yield return brush;
        }
    }
}
