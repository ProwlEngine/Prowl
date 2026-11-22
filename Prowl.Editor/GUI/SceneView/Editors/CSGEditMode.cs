// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>What clicking in the viewport does while working on a CSG model.</summary>
public enum CSGMode
{
    /// <summary>Select and move whole brushes.</summary>
    Object,
    /// <summary>Draw new brushes.</summary>
    Generate,
    /// <summary>Select and texture faces.</summary>
    Surface,
    /// <summary>Cut brushes with a plane.</summary>
    Clip,
    /// <summary>Edit a brush's vertices, edges and faces.</summary>
    Mesh,
}

/// <summary>The one CSG mode in force, exclusive since every mode wants the primary button.</summary>
public static class CSGEditMode
{
    private static CSGMode _mode = CSGMode.Object;

    public static event Action? Changed;

    public static CSGMode Current
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            Changed?.Invoke();
        }
    }

    public static bool Is(CSGMode mode) => _mode == mode;
}

/// <summary>Grid snapping for the CSG tools, following the scene view's grid toggle and size. Holding Ctrl inverts it.</summary>
public static class CSGSnap
{
    public static float Grid => SceneTools.MoveSnap;

    public static bool Active(HandleContext handles) => SceneTools.GridSnap != handles.Ctrl;

    public static float Round(float value) => MathF.Round(value / Grid) * Grid;

    public static Float3 Round(Float3 value) => new(Round(value.X), Round(value.Y), Round(value.Z));

    public static float Value(HandleContext handles, float value) => Active(handles) ? Round(value) : value;

    public static Float3 Value(HandleContext handles, Float3 value) => Active(handles) ? Round(value) : value;
}
