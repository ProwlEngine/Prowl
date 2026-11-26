// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.GUI.Panels;

using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>The CSG tool in use while working on a model.</summary>
public enum CSGMode
{
    /// <summary>Select and move whole brushes with the transform gizmo.</summary>
    Select,
    /// <summary>Place new shapes.</summary>
    Create,
    /// <summary>Reshape the selection: shape handles, or vertices, edges and faces of a plain brush.</summary>
    Edit,
    /// <summary>Pick surfaces and drag their texture, moving, turning or scaling it as the surface tool's drag says.</summary>
    Surface,
}

/// <summary>The one CSG tool in force, exclusive since every tool wants the primary button.</summary>
public static class CSGEditMode
{
    private static CSGMode _mode = CSGMode.Select;

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

    /// <summary>A move of points that lands their bounds on the world grid when snapping, along the handle's axes.</summary>
    public static Float3 Move(HandleContext handles, IReadOnlyList<Float3> points, Float3 movement, Quaternion axes)
        => Active(handles) ? SceneViewPanel.SnapBoundsToGrid(points, movement, Grid, axes) : movement;

    /// <summary>A point on a surface snapped to the grid, then slid back onto the surface so a slope keeps it.</summary>
    public static Float3 OnPlane(HandleContext handles, Float3 point, Float3 normal)
    {
        Float3 snapped = Value(handles, point);
        return snapped - normal * Float3.Dot(snapped - point, normal);
    }
}

/// <summary>A short message the CSG status line shows for a moment, such as why an edit was refused.</summary>
public static class CSGStatus
{
    private static string? _message;
    private static long _until;

    public static void Flash(string message)
    {
        _message = message;
        _until = Environment.TickCount64 + 1500;
    }

    public static string? Current => Environment.TickCount64 < _until ? _message : null;
}

/// <summary>
/// CSG guides drawn as the scene's gizmos, tested against its depth so the parts behind geometry show dashed. Handles
/// and labels stay on the scene view's overlay.
/// </summary>
internal static class CSGGizmos
{
    private static Prowl.Graphite.Rendering.GizmoBatch Batch => Runtime.Resources.Scene.Active.Gizmos.Batch;

    private static float Width(float thickness) => thickness > 0f ? thickness : 2f;

    public static void Line(Float3 a, Float3 b, Color32 color, float thickness = 0f) => Batch.Line(a, b, color, Width(thickness));

    public static void Polyline(ReadOnlySpan<Float3> points, Color32 color, float thickness = 0f, bool closed = false)
        => Batch.Polyline(points, color, Width(thickness), closed);

    /// <summary>A filled convex polygon.</summary>
    public static void Polygon(ReadOnlySpan<Float3> points, Color32 color)
    {
        Prowl.Graphite.Rendering.GizmoBatch batch = Batch;
        for (int i = 1; i + 1 < points.Length; i++) batch.Triangle(points[0], points[i], points[i + 1], color);
    }

    public static void Circle(Float3 center, Float3 normal, float radius, Color32 color, float thickness = 0f)
        => Batch.Circle(center, normal, radius, color, Width(thickness));
}
