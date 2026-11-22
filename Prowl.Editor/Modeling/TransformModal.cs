// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;

using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.Modeling;

public enum TransformKind
{
    Translate,
    Rotate,
    Scale,

    /// <summary>One point along one of its edges, the edge picked by the way the mouse first moves.</summary>
    Slide,
}

public enum ModalState
{
    Running,
    Confirmed,
    Cancelled,
}

/// <summary>Which frame an axis lock follows: the world, the object, or the selection's normal.</summary>
public enum AxisSpace
{
    Global,
    Local,
    Normal,
}

/// <summary>
/// Grab, rotate and scale by moving the mouse with no button held, through a <see cref="TransformSession"/>. X, Y and
/// Z lock to an axis, pressed again for the object's axis and then the normal's, with Shift to the plane across it.
/// Typing a number sets the amount, Shift slows the mouse, grid snapping follows the toggle with Ctrl inverting it.
/// Left click or Enter confirms, Escape or right click puts everything back.
/// </summary>
public sealed class TransformModal
{
    private const string OwnerControl = "mesh_transform_modal";
    private static readonly Color32[] AxisColors = [new(230, 80, 90, 255), new(120, 200, 80, 255), new(80, 130, 230, 255)];
    private static readonly string[] AxisNames = ["X", "Y", "Z"];
    private static readonly (KeyCode Key, char Char)[] NumberKeys =
    [
        (KeyCode.Number0, '0'), (KeyCode.Number1, '1'), (KeyCode.Number2, '2'), (KeyCode.Number3, '3'), (KeyCode.Number4, '4'),
        (KeyCode.Number5, '5'), (KeyCode.Number6, '6'), (KeyCode.Number7, '7'), (KeyCode.Number8, '8'), (KeyCode.Number9, '9'),
        (KeyCode.Keypad0, '0'), (KeyCode.Keypad1, '1'), (KeyCode.Keypad2, '2'), (KeyCode.Keypad3, '3'), (KeyCode.Keypad4, '4'),
        (KeyCode.Keypad5, '5'), (KeyCode.Keypad6, '6'), (KeyCode.Keypad7, '7'), (KeyCode.Keypad8, '8'), (KeyCode.Keypad9, '9'),
        (KeyCode.Period, '.'), (KeyCode.KeypadDecimal, '.'),
    ];

    private readonly TransformSession _session;
    private readonly Quaternion _local;
    private readonly Quaternion? _normal;
    private bool _started;
    private Float2 _start, _virtual, _last;
    private int _axis = -1;
    private bool _plane;
    private AxisSpace _space;
    private string _typed = "";
    private int _slideEdge = -1;
    private Action? _replay;

    /// <param name="local">The object's rotation, for the second press of an axis key.</param>
    /// <param name="normal">The selection's normal frame, for the third press, or null when it has none.</param>
    public TransformModal(TransformKind kind, TransformSession session, Quaternion local, Quaternion? normal)
    {
        Kind = kind;
        _session = session;
        _local = local;
        _normal = normal;
    }

    public TransformKind Kind { get; }

    /// <summary>For a slide, the far ends of the edges the point can slide along, in the world.</summary>
    public IReadOnlyList<Float3> SlideEnds { get; init; } = [];

    /// <summary>What the modal is doing, for the status line.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Puts the last previewed transform on the session's points again, as committing an edit needs.</summary>
    public void Replay() => _replay?.Invoke();

    private Quaternion Axes => _space switch
    {
        AxisSpace.Local => _local,
        AxisSpace.Normal => _normal ?? Quaternion.Identity,
        _ => Quaternion.Identity,
    };

    private Float3 Axis(int index) => Axes * (index == 0 ? Float3.UnitX : index == 1 ? Float3.UnitY : Float3.UnitZ);

    private float? Typed => float.TryParse(_typed, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : null;

    /// <summary>Runs one frame of the modal over a scene view.</summary>
    public ModalState Tick(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;

        // The click that confirms belongs to the modal, so nothing behind it picks
        ControlID owner = handles.GetControlID(OwnerControl);
        handles.AddControl(owner, HandleContext.BodyDistance, float.MaxValue - 1f);

        if (!_started)
        {
            _started = true;
            _start = _virtual = _last = handles.MousePosition;
        }
        _virtual += (handles.MousePosition - _last) * (handles.Shift ? 0.1f : 1f);
        _last = handles.MousePosition;

        ReadKeys(handles);

        switch (Kind)
        {
            case TransformKind.Translate: Translate(ctx); break;
            case TransformKind.Rotate: Rotate(ctx); break;
            case TransformKind.Scale: Scale(ctx); break;
            case TransformKind.Slide: Slide(ctx); break;
        }
        DrawLocks(ctx);

        if (handles.GetKeyDown(KeyCode.Escape) || handles.ViewportHovered && Input.GetMouseButtonDown(1))
        {
            _session.Restore();
            return ModalState.Cancelled;
        }
        if (handles.GetKeyDown(KeyCode.Enter) || handles.GetKeyDown(KeyCode.KeypadEnter))
            return ModalState.Confirmed;
        if (handles.IsNearest(owner) && handles.TryBeginDrag(owner))
        {
            handles.TryEndDrag(owner);
            return ModalState.Confirmed;
        }
        return ModalState.Running;
    }

    private void ReadKeys(HandleContext handles)
    {
        for (int i = 0; i < 3 && Kind != TransformKind.Slide; i++)
        {
            KeyCode key = i == 0 ? KeyCode.X : i == 1 ? KeyCode.Y : KeyCode.Z;
            if (handles.GetKeyDown(key)) LockAxis(i, handles.Shift);
        }

        foreach ((KeyCode key, char c) in NumberKeys)
            if (handles.GetKeyDown(key) && (c != '.' || !_typed.Contains('.'))) _typed += c;
        if (handles.GetKeyDown(KeyCode.Minus) || handles.GetKeyDown(KeyCode.KeypadSubtract))
            _typed = _typed.StartsWith('-') ? _typed[1..] : "-" + _typed;
        if (handles.GetKeyDown(KeyCode.Backspace) && _typed.Length > 0) _typed = _typed[..^1];
    }

    /// <summary>The same axis again steps from the world's axis to the object's, then the normal's, then lets go.</summary>
    private void LockAxis(int axis, bool plane)
    {
        if (_axis != axis || _plane != plane)
        {
            _axis = axis;
            _plane = plane;
            _space = AxisSpace.Global;
            return;
        }

        if (_space == AxisSpace.Global) _space = AxisSpace.Local;
        else if (_space == AxisSpace.Local && _normal != null) _space = AxisSpace.Normal;
        else _axis = -1;
    }

    private void Translate(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        Float3 forward = ctx.Camera.GameObject.Transform.Forward;
        Float3 inView = OnPlane(ctx, _virtual, forward) - OnPlane(ctx, _start, forward);
        bool snap = SceneTools.GridSnap != handles.Ctrl;
        float? typed = Typed;
        int axis = _axis < 0 && typed != null ? 0 : _axis;

        Float3 delta;
        if (axis < 0)
        {
            delta = snap ? SnapIn(inView, Quaternion.Identity) : inView;
        }
        else if (!_plane || typed != null)
        {
            // The amount along the axis that keeps the selection under the mouse, as the axis looks on screen
            Float3 a = Axis(axis);
            Float3 seen = a - forward * Float3.Dot(a, forward);
            float length = Float3.Dot(seen, seen);
            float amount = typed ?? (length > 1e-6f ? Float3.Dot(inView, seen) / length : 0f);
            delta = a * (snap && typed == null ? Round(amount, SceneTools.MoveSnap) : amount);
        }
        else
        {
            Float3 normal = Axis(axis);
            delta = OnPlane(ctx, _virtual, normal) - OnPlane(ctx, _start, normal);
            if (snap) delta = SnapIn(delta, Axes);
        }

        _replay = () => _session.Translate(delta);
        _replay();
        Status = string.Create(CultureInfo.InvariantCulture, $"Move{Lock()}  {delta.X:0.###}  {delta.Y:0.###}  {delta.Z:0.###}{TypedText()}");
    }

    private void Rotate(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        Float3 forward = ctx.Camera.GameObject.Transform.Forward;
        Float2 centre = handles.WorldToScreen(_session.Pivot) ?? _start;
        Float2 from = _start - centre, to = _virtual - centre;

        // Screen y runs down, so a positive screen angle is clockwise, which is a negative turn about the view direction
        float seen = MathF.Atan2(from.X * to.Y - from.Y * to.X, Float2.Dot(from, to));
        Float3 axis = _axis < 0 ? forward : Axis(_axis);
        float sign = _axis >= 0 && Float3.Dot(axis, forward) < 0f ? -1f : 1f;
        float degrees = Typed ?? -seen * sign * 180f / MathF.PI;
        if (Typed == null && SceneTools.GridSnap != handles.Ctrl) degrees = Round(degrees, SceneTools.RotateSnap);

        _replay = () => _session.Rotate(axis, degrees * MathF.PI / 180f);
        _replay();
        Status = string.Create(CultureInfo.InvariantCulture, $"Rotate{Lock()}  {degrees:0.#} degrees{TypedText()}");
    }

    private void Scale(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        Float2 centre = handles.WorldToScreen(_session.Pivot) ?? _start;
        float factor = Typed ?? Float2.Length(_virtual - centre) / MathF.Max(Float2.Length(_start - centre), 1f);
        if (Typed == null && SceneTools.GridSnap != handles.Ctrl) factor = Round(factor, SceneTools.ScaleSnap);

        Float3 factors = new(factor);
        if (_axis >= 0)
            for (int i = 0; i < 3; i++)
                if ((i == _axis) == _plane) factors[i] = 1f;

        Quaternion axes = Axes;
        _replay = () => _session.Scale(factors, axes);
        _replay();
        Status = string.Create(CultureInfo.InvariantCulture, $"Scale{Lock()}  {factor:0.###}{TypedText()}");
    }

    /// <summary>Slides the point part of the way along an edge, typed as a fraction of the edge.</summary>
    private void Slide(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        Float3 from = _session.Median;
        Float2 moved = _virtual - _start;
        if (handles.WorldToScreen(from) is not { } fromScreen || SlideEnds.Count == 0) return;

        // The edge is the one whose direction on screen best matches the mouse's first move
        if (_slideEdge < 0 && Float2.Length(moved) >= 4f)
        {
            float best = float.MinValue;
            Float2 direction = Float2.Normalize(moved);
            for (int i = 0; i < SlideEnds.Count; i++)
            {
                if (handles.WorldToScreen(SlideEnds[i]) is not { } end || Float2.LengthSquared(end - fromScreen) < 1e-6f) continue;
                float match = Float2.Dot(direction, Float2.Normalize(end - fromScreen));
                if (match <= best) continue;
                best = match;
                _slideEdge = i;
            }
        }

        float t = 0f;
        Float3 target = from;
        if (_slideEdge >= 0)
        {
            target = SlideEnds[_slideEdge];
            Float2 along = (handles.WorldToScreen(target) ?? fromScreen) - fromScreen;
            float length = Float2.Dot(along, along);
            t = Math.Clamp(Typed ?? (length > 1e-6f ? Float2.Dot(moved, along) / length : 0f), 0f, 1f);
            ctx.Draw.Line(from, target, AxisColors[1], 1.5f);
        }

        Float3 delta = (target - from) * t;
        _replay = () => _session.Translate(delta);
        _replay();
        Status = string.Create(CultureInfo.InvariantCulture, $"Slide  {t:0.###}{TypedText()}");
    }

    /// <summary>Where the ray through a screen point meets the plane through the pivot, or the pivot when it runs along the plane.</summary>
    private Float3 OnPlane(SceneToolContext ctx, Float2 screen, Float3 normal)
    {
        HandleContext handles = ctx.Handles;
        Float2 local = screen - (handles.MousePosition - handles.MouseLocal);
        Ray ray = ctx.Camera.ScreenPointToRay(local, handles.ViewportSize);
        float facing = Float3.Dot(ray.Direction, normal);
        if (MathF.Abs(facing) < 0.02f) return _session.Pivot;
        float distance = Float3.Dot(_session.Pivot - ray.Origin, normal) / facing;
        return distance > 0f ? ray.Origin + ray.Direction * distance : _session.Pivot;
    }

    private static Float3 SnapIn(Float3 value, Quaternion axes)
    {
        Float3 inAxes = Quaternion.Inverse(axes) * value;
        float step = SceneTools.MoveSnap;
        return axes * new Float3(Round(inAxes.X, step), Round(inAxes.Y, step), Round(inAxes.Z, step));
    }

    private static float Round(float value, float step) => step > 0f ? MathF.Round(value / step) * step : value;

    private void DrawLocks(SceneToolContext ctx)
    {
        if (_axis < 0) return;
        for (int i = 0; i < 3; i++)
        {
            if ((i == _axis) == _plane) continue;
            Float3 axis = Axis(i);
            ctx.Draw.Line(_session.Pivot - axis * 500f, _session.Pivot + axis * 500f, AxisColors[i], 1.5f);
        }
    }

    private string Lock()
    {
        if (_axis < 0) return "";
        string space = _space == AxisSpace.Global ? "" : _space == AxisSpace.Local ? " local" : " normal";
        if (!_plane) return $" along{space} {AxisNames[_axis]}";
        return $" on{space} {(_axis == 0 ? "YZ" : _axis == 1 ? "XZ" : "XY")}";
    }

    private string TypedText() => _typed.Length > 0 ? $"  [{_typed}]" : "";
}
