// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Editor.Core;

using Prowl.Runtime;
using Prowl.Runtime.CSG;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Which brushes are hidden in the viewport. Hiding never changes the CSG result or the saved mesh.</summary>
public static class CSGVisibility
{
    private static readonly HashSet<CSGBrush> _hidden = new();

    [InitializeOnLoad]
    private static void Install()
    {
        EditorSceneManager.Capturing -= ShowForCapture;
        EditorSceneManager.Capturing += ShowForCapture;
        EditorSceneManager.Captured -= HideAfterCapture;
        EditorSceneManager.Captured += HideAfterCapture;
    }

    // Saved scenes and play copies keep every brush
    private static void ShowForCapture() => SetShowHidden(true);
    private static void HideAfterCapture() => SetShowHidden(false);

    private static void SetShowHidden(bool show)
    {
        if (CSGModel.ShowHidden == show) return;
        CSGModel.ShowHidden = show;
        foreach (CSGModel model in Models()) model.RefreshRenderer();
    }

    /// <summary>Hands the hidden set to the models and has them build what the viewport draws again.</summary>
    private static void Changed()
    {
        CSGModel.EditorHidden = _hidden.Count > 0 ? IsHidden : null;
        foreach (CSGModel model in Models()) model.SetDirty();
    }

    private static IEnumerable<CSGModel> Models()
    {
        foreach (GameObject go in EditorSceneManager.EditedWorld.Objects)
        {
            CSGModel model = go.GetComponent<CSGModel>();
            if (model.IsValid()) yield return model;
        }
    }

    public static int HiddenCount
    {
        get
        {
            Prune();
            return _hidden.Count;
        }
    }

    public static bool IsHidden(CSGBrush brush) => _hidden.Contains(brush);

    public static void Hide(IEnumerable<CSGBrush> brushes)
    {
        foreach (CSGBrush brush in brushes) _hidden.Add(brush);
        Changed();
    }

    /// <summary>Hides everything except the selection.</summary>
    public static void HideUnselected()
    {
        List<CSGBrush> selected = CSGSelection.Brushes();
        if (selected.Count == 0) return;

        foreach (CSGBrush brush in AllBrushes())
            if (!selected.Contains(brush)) _hidden.Add(brush);
        Changed();
    }

    /// <summary>Swaps hidden and shown.</summary>
    public static void ToggleAll()
    {
        var shown = new List<CSGBrush>();
        foreach (CSGBrush brush in AllBrushes())
            if (!_hidden.Contains(brush)) shown.Add(brush);

        _hidden.Clear();
        foreach (CSGBrush brush in shown) _hidden.Add(brush);
        Changed();
    }

    public static void UnhideAll()
    {
        _hidden.Clear();
        Changed();
    }

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
