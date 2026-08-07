// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Vector;

using Gizmo = Prowl.OrigamiUI.Gizmo;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Immediate-mode transform handles for scene tools. Wraps Origami's
/// <see cref="Gizmo.TransformGizmo"/> so an editor can manipulate an arbitrary world point/transform
/// (not just a GameObject): e.g. a light probe, a spline knot, a bounds corner.
///
/// Each handle owns a <see cref="ControlID"/>, so it arbitrates against every other handle in the
/// viewport instead of unilaterally consuming input. Call a handle method from
/// <see cref="ISceneViewEditor.OnSceneInput"/> each frame (it applies the drag to the <c>ref</c>
/// values), then call <see cref="Draw"/> from <see cref="ISceneViewEditor.DrawOverlay"/> to render
/// the handles touched this frame. Each distinct <paramref name="id"/> keeps its own drag state
/// across frames.
/// </summary>
public static class TransformHandles
{
    private static readonly Dictionary<string, Gizmo.TransformGizmo> _gizmos = new();
    private static readonly Dictionary<string, Gizmo.TransformGizmoMode> _modes = new();
    private static readonly List<Gizmo.TransformGizmo> _pending = new();

    /// <summary>A 3-axis translation handle at <paramref name="position"/>. Returns true if it moved the
    /// point this frame; <paramref name="hot"/> is true while the handle is hovered or being dragged
    /// (callers should treat input as consumed and skip their own picking when hot).</summary>
    public static bool PositionHandle(HandleContext ctx, string id, ref Float3 position, out bool hot)
    {
        Quaternion rot = Quaternion.Identity;
        Float3 scale = Float3.One;
        return DoTransform(ctx, id, Gizmo.TransformGizmoMode.Translate, ref position, ref rot, ref scale, out hot);
    }

    /// <summary>A rotation handle pivoted at <paramref name="pivot"/>. Returns true if it rotated this frame.</summary>
    public static bool RotationHandle(HandleContext ctx, string id, Float3 pivot, ref Quaternion rotation, out bool hot)
    {
        Float3 scale = Float3.One;
        return DoTransform(ctx, id, Gizmo.TransformGizmoMode.Rotate, ref pivot, ref rotation, ref scale, out hot);
    }

    /// <summary>A full translate/rotate/scale handle. <paramref name="mode"/> selects which axes/planes show.</summary>
    public static bool TransformHandle(HandleContext ctx, string id, Gizmo.TransformGizmoMode mode,
                                       ref Float3 position, ref Quaternion rotation, ref Float3 scale, out bool hot)
        => DoTransform(ctx, id, mode, ref position, ref rotation, ref scale, out hot);

    /// <summary>Draw every handle driven this frame, then clear the pending set. Call from <c>DrawOverlay</c>.</summary>
    public static void Draw(Canvas canvas)
    {
        foreach (var g in _pending) g.Draw(canvas);
        _pending.Clear();
    }

    /// <summary>Forget a handle's cached gizmo + drag state (e.g. when its target is deleted).</summary>
    public static void Forget(string id)
    {
        _gizmos.Remove(id);
        _modes.Remove(id);
    }

    private static bool DoTransform(HandleContext ctx, string id, Gizmo.TransformGizmoMode mode,
                                    ref Float3 position, ref Quaternion rotation, ref Float3 scale, out bool hot)
    {
        hot = false;
        Camera camera = ctx.Camera;
        var camGo = camera.IsValid() ? camera.GameObject : null;
        if (camGo == null) return false;

        if (!_gizmos.TryGetValue(id, out var g))
        {
            g = new Gizmo.TransformGizmo(mode);
            _gizmos[id] = g;
            _modes[id] = mode;
        }
        else if (_modes[id] != mode)
        {
            // SetMode rebuilds the sub-gizmos (dropping drag state), so only call it on a real change.
            g.SetMode(mode);
            _modes[id] = mode;
        }

        g.UpdateCamera(ctx.Viewport, camera.ViewMatrix, camera.ProjectionMatrix,
            camGo.Transform.Up, camGo.Transform.Forward, camGo.Transform.Right, camGo.Transform.Position);
        g.SetTransform(position, rotation, scale);

        ControlID control = ctx.GetControlID(id);

        g.Snapping = ctx.Ctrl;
        g.IsShiftDown = ctx.Shift;
        // The grab decision comes from arbitration, never from the gizmo's own hover.
        g.IsMouseDown = ctx.TryBeginDrag(control);
        g.IsMouseUp = ctx.PrimaryUp;

        var result = g.Update(ctx.MouseRay, ctx.MousePosition, ctx.Blocked);
        _pending.Add(g);

        // IsOver is fresh from the Update above. Guarded on Blocked because TransformGizmo only clears
        // its hover inside the un-blocked branch, so a blocked frame leaves IsOver latched.
        ctx.AddControl(control, g.IsOver && !ctx.Blocked ? 0f : float.MaxValue, ctx.DepthOf(position));
        ctx.TryEndDrag(control);

        hot = ctx.IsActive(control);

        if (!result.HasValue || !ctx.IsHot(control)) return false;
        var r = result.Value;
        bool changed = false;
        if (r.TranslationDelta.HasValue) { position += r.TranslationDelta.Value; changed = true; }
        if (r.RotationDelta.HasValue && r.RotationAxis.HasValue)
        {
            rotation = Quaternion.AxisAngle(r.RotationAxis.Value, r.RotationDelta.Value) * rotation;
            changed = true;
        }
        if (r.ScaleDelta.HasValue) { scale *= r.ScaleDelta.Value; changed = true; }
        return changed;
    }
}
