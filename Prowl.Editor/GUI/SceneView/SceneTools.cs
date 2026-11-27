// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using Gizmo = Prowl.OrigamiUI.Gizmo;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>Which built-in transform manipulator the scene view is currently offering.</summary>
public enum TransformTool
{
    Translate,
    Rotate,
    Scale,
    Universal,
    /// <summary>A rectangle over the object in its own XY plane, see <see cref="RectTool"/>.</summary>
    Rect,
}

/// <summary>Where a multi-object manipulation pivots.</summary>
public enum PivotMode
{
    /// <summary>Rotate and scale the selection about the centre of its origins.</summary>
    Center,
    /// <summary>Rotate and scale the selection about the active object's origin.</summary>
    Active,
    /// <summary>Rotate and scale each object about its own origin.</summary>
    Individual,
    /// <summary>Rotate and scale the selection about the 3D cursor.</summary>
    Cursor,
}

/// <summary>Which frame handle axes align to.</summary>
public enum PivotOrientation
{
    /// <summary>Axes align to the world.</summary>
    Global,
    /// <summary>Axes align to the active object's rotation.</summary>
    Local,
    /// <summary>Axes align to the selection's normal where a tool has one, such as mesh editing, and as Local elsewhere.</summary>
    Normal,
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
    private static bool _gridSnap;
    private static bool _rotationSnap;

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

    /// <summary>The 3D cursor: a point in the world that new model objects appear at, that snaps can move things to, and the Cursor pivot turns about.</summary>
    public static Float3 Cursor { get; set; }

    public static PivotOrientation Orientation
    {
        get => _orientation;
        set => Set(ref _orientation, value);
    }

    /// <summary>Sticky snapping of moves to <see cref="MoveSnap"/>, and of scaling to <see cref="ScaleSnap"/>. Holding Ctrl inverts it.</summary>
    public static bool GridSnap
    {
        get => _gridSnap;
        set => Set(ref _gridSnap, value);
    }

    /// <summary>Sticky snapping of rotation to <see cref="RotateSnap"/>. Holding Ctrl inverts it.</summary>
    public static bool RotationSnap
    {
        get => _rotationSnap;
        set => Set(ref _rotationSnap, value);
    }

    // Drops float noise, so a size that is meant to be 1 is exactly 1 and shows without trailing decimals.
    // Six places keeps every power of two down to 1/64 exact.
    private static float Clean(float value) => (float)Math.Round(value, 6);

    /// <summary>Smallest and largest grid size, in world units.</summary>
    public const float MinMoveSnap = 1f / 64f, MaxMoveSnap = 128f;

    private static float _moveSnap = 1f;

    /// <summary>Grid increment for translation, in world units, kept within <see cref="MinMoveSnap"/> and <see cref="MaxMoveSnap"/>.</summary>
    public static float MoveSnap
    {
        get => _moveSnap;
        set => _moveSnap = float.IsFinite(value) ? Math.Clamp(Clean(value), MinMoveSnap, MaxMoveSnap) : _moveSnap;
    }

    /// <summary>
    /// Number format that shows a grid size below 1 as a fraction (1/16 rather than 0.0625) when it is one over a
    /// power of two, which every halving step is. Anything else shows as a plain decimal.
    /// </summary>
    /// <remarks>The fraction is a quoted literal, which a custom numeric format prints as is; typing it back in parses as division.</remarks>
    public static string MoveSnapFormat(float value)
    {
        if (value > 0f && value < 1f)
        {
            // Exact, so a nearby size such as 0.4999 keeps showing what it really snaps to. One over a power of
            // two is exact in a float, and MoveSnap's rounding to six places leaves every step down to 1/64 exact.
            int denominator = (int)MathF.Round(1f / value);
            if (denominator >= 2 && System.Numerics.BitOperations.IsPow2(denominator) && value == 1f / denominator)
                return $"'1/{denominator}'";
        }
        return "0.######";
    }

    /// <summary><see cref="MoveSnapFormat"/> for the current <see cref="MoveSnap"/>, worked out again only when the size changes.</summary>
    public static string CurrentMoveSnapFormat
    {
        get
        {
            // NaN never equals a size, so the first read always fills it.
            if (s_formatSnap != _moveSnap)
            {
                s_formatSnap = _moveSnap;
                s_format = MoveSnapFormat(_moveSnap);
            }
            return s_format;
        }
    }
    private static float s_formatSnap = float.NaN;
    private static string s_format = "";

    /// <summary>Doubles (<paramref name="direction"/> above zero) or halves the grid size, so it stays on powers of two.</summary>
    public static void StepMoveSnap(int direction)
        => MoveSnap = direction > 0 ? MoveSnap * 2f : direction < 0 ? MoveSnap / 2f : MoveSnap;

    /// <summary>Smallest and largest rotation increment, in degrees.</summary>
    public const float MinRotateSnap = 0.25f, MaxRotateSnap = 180f;

    /// <summary>
    /// Number format for the rotation increment. The degree sign is a literal in the format rather than a field
    /// suffix, which Origami pads far from the border.
    /// </summary>
    public const string RotateSnapFormat = "0.##°";

    /// <summary>The rotation increments the stepper moves between, in degrees.</summary>
    public static readonly float[] RotateSnapSteps = [0.25f, 0.5f, 1f, 5f, 15f, 30f, 45f, 90f, 180f];

    private static float _rotateSnap = 15f;

    /// <summary>
    /// Increment for rotation, in degrees, kept within <see cref="MinRotateSnap"/> and <see cref="MaxRotateSnap"/>.
    /// The rotate gizmo draws a tick for every increment around its ring while snapping, so a tiny step would
    /// draw tens of thousands of them every frame.
    /// </summary>
    public static float RotateSnap
    {
        get => _rotateSnap;
        set => _rotateSnap = float.IsFinite(value) ? Math.Clamp(Clean(value), MinRotateSnap, MaxRotateSnap) : _rotateSnap;
    }

    /// <summary>Moves the rotation increment to the next of <see cref="RotateSnapSteps"/> up or down from the nearest one.</summary>
    public static void StepRotateSnap(int direction)
    {
        int nearest = 0;
        for (int i = 1; i < RotateSnapSteps.Length; i++)
            if (MathF.Abs(RotateSnapSteps[i] - RotateSnap) < MathF.Abs(RotateSnapSteps[nearest] - RotateSnap))
                nearest = i;
        RotateSnap = RotateSnapSteps[Math.Clamp(nearest + Math.Sign(direction), 0, RotateSnapSteps.Length - 1)];
    }

    /// <summary>Increment for the scale factor while snapping.</summary>
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
