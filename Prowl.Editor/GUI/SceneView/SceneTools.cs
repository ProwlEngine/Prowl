// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Gizmo = Prowl.OrigamiUI.Gizmo;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>Which built-in transform manipulator the scene view is currently offering.</summary>
public enum TransformTool
{
    Translate,
    Rotate,
    Scale,
    Universal,
}

/// <summary>Where a multi-object manipulation pivots.</summary>
public enum PivotMode
{
    /// <summary>Pivot on the centre of the whole selection.</summary>
    Center,
    /// <summary>Pivot on the active object's own origin.</summary>
    Pivot,
}

/// <summary>Which frame handle axes align to.</summary>
public enum PivotOrientation
{
    /// <summary>Axes align to the world.</summary>
    Global,
    /// <summary>Axes align to the active object's rotation.</summary>
    Local,
}

/// <summary>
/// Editor-wide scene-view tool state, shared by the built-in transform gizmo, the toolbar, the
/// shortcut handlers and any custom scene tool. This is deliberately global rather than per-panel:
/// the active tool and snap settings are a property of the editor session, matching how the toolbar
/// and hotkeys already behave, and a custom tool needs to read them without holding a panel
/// reference.
/// </summary>
public static class SceneTools
{
    private static TransformTool _current = TransformTool.Translate;
    private static PivotMode _pivot = PivotMode.Center;
    private static PivotOrientation _orientation = PivotOrientation.Local;
    private static bool _snapEnabled;

    /// <summary>Fires whenever any tool state changes, so panels can rebuild cached gizmo state.</summary>
    public static event Action? Changed;

    public static TransformTool Transform
    {
        get => _current;
        set => Set(ref _current, value);
    }

    public static PivotMode Pivot
    {
        get => _pivot;
        set => Set(ref _pivot, value);
    }

    public static PivotOrientation Orientation
    {
        get => _orientation;
        set => Set(ref _orientation, value);
    }

    /// <summary>Sticky snapping. Holding Ctrl snaps regardless; this makes it the default.</summary>
    public static bool SnapEnabled
    {
        get => _snapEnabled;
        set => Set(ref _snapEnabled, value);
    }

    /// <summary>Grid increment for translation, in world units.</summary>
    public static float MoveSnap { get; set; } = 1f;

    /// <summary>Increment for rotation, in degrees.</summary>
    public static float RotateSnap { get; set; } = 15f;

    /// <summary>Increment for scaling, as a multiplier step.</summary>
    public static float ScaleSnap { get; set; } = 0.1f;

    /// <summary>
    /// True while the user is driving the camera (orbit / pan / fly), so tools should stand down.
    /// Mirrors <see cref="HandleContext.Blocked"/> for code that has no context to hand.
    /// </summary>
    public static bool ViewToolActive { get; internal set; }

    /// <summary>
    /// Set by a scene-view editor that wants the object transform gizmo hidden entirely, and
    /// cleared each frame by the panel. Distinct from arbitration: this is "the gizmo is meaningless
    /// here", not "something else won the cursor".
    /// </summary>
    public static bool SuppressTransformGizmo { get; internal set; }

    /// <summary>The Origami gizmo mode matching <see cref="Transform"/>.</summary>
    public static Gizmo.TransformGizmoMode GizmoMode => Transform switch
    {
        TransformTool.Rotate => Gizmo.TransformGizmoMode.Rotate,
        TransformTool.Scale => Gizmo.TransformGizmoMode.ScaleAll,
        TransformTool.Universal => Gizmo.TransformGizmoMode.Universal,
        _ => Gizmo.TransformGizmoMode.Translate,
    };

    private static void Set<T>(ref T field, T value) where T : struct
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke();
    }
}
