// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime;
using Prowl.Vector;

using GizmoUtils = Prowl.OrigamiUI.Gizmo.GizmoUtils;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Arbitration bus for interactive scene-view handles. Every handle reports how far it is from the
/// cursor via <see cref="AddControl"/>; the nearest one wins and may take the drag.
///
/// <para>One instance per scene viewport, owned by the panel. The frame model is a single pass:
/// registrations accumulate all frame and resolve at the next <see cref="BeginFrame"/>, so handles
/// act on the previous pass's winner. Because <see cref="Hot"/> outranks <see cref="Nearest"/> for
/// the whole of a drag, the one-frame lag only ever affects hover highlighting and the initial
/// press.</para>
///
/// <para>Resolution happens in <see cref="BeginFrame"/> rather than <see cref="EndFrame"/> so that
/// controls registered from deferred draw callbacks - which run after the input pass - still take
/// part.</para>
/// </summary>
public sealed class HandleContext
{
    /// <summary>Distance to register for a large body region (a rect interior, say) so it beats the
    /// default control but loses to any real handle the cursor is actually near.</summary>
    public const float BodyDistance = 1e6f;

    /// <summary>
    /// Screen distances within this many pixels of each other count as overlapping, and the tie is
    /// settled by depth instead. Without a band, two handles stacked at the same screen point would
    /// be ordered by sub-pixel projection noise rather than by which one is actually in front.
    /// </summary>
    public const float DepthTieBandPixels = 4f;

    private readonly Dictionary<ControlID, Float2> _dragOrigin = new();

    private ControlID _nearest;
    private ControlID _pendingNearest;
    private float _pendingDistance = float.MaxValue;
    private float _pendingDepth = float.MaxValue;
    private ControlID _pendingDefault;

    private Camera _camera = null!;
    private Float4x4 _viewProjection;
    private Float3 _camPosition;
    private Float3 _camForward;

    // ================================================================
    //  Frame state
    // ================================================================

    public Camera Camera => _camera;

    /// <summary>
    /// Viewport rect in absolute Paper space. Projection and all handle distances use this space
    /// because it is the space the overlay canvas draws in, so a handle's pick region and its drawn
    /// shape stay in agreement.
    /// </summary>
    public Rect Viewport { get; private set; }

    /// <summary>Viewport dimensions in pixels.</summary>
    public Float2 ViewportSize { get; private set; }

    /// <summary>Cursor in absolute Paper space. All handle distances are measured against this.</summary>
    public Float2 MousePosition { get; private set; }

    /// <summary>Cursor relative to the viewport's top-left, for camera ray and picking helpers.</summary>
    public Float2 MouseLocal { get; private set; }

    public Ray MouseRay { get; private set; }
    public bool ViewportHovered { get; private set; }

    public bool PrimaryDown { get; private set; }
    public bool PrimaryHeld { get; private set; }
    public bool PrimaryUp { get; private set; }

    public bool Shift { get; private set; }
    public bool Ctrl { get; private set; }
    public bool Alt { get; private set; }

    /// <summary>Camera navigation is in progress: nothing may pick or start a drag.</summary>
    public bool Blocked { get; private set; }

    // ================================================================
    //  Lifecycle
    // ================================================================

    public void BeginFrame(Camera camera, Rect viewportAbsolute, Float2 mouseLocal, bool viewportHovered)
    {
        // Resolve everything registered since the last BeginFrame, then start a fresh accumulator.
        _nearest = _pendingNearest.IsValid ? _pendingNearest : _pendingDefault;
        _pendingNearest = ControlID.None;
        _pendingDefault = ControlID.None;
        _pendingDistance = float.MaxValue;
        _pendingDepth = float.MaxValue;

        _camera = camera;
        Viewport = viewportAbsolute;
        ViewportSize = new Float2((float)viewportAbsolute.Size.X, (float)viewportAbsolute.Size.Y);
        MouseLocal = mouseLocal;
        MousePosition = mouseLocal + new Float2((float)viewportAbsolute.Min.X, (float)viewportAbsolute.Min.Y);
        ViewportHovered = viewportHovered;

        _viewProjection = camera.ProjectionMatrix * camera.ViewMatrix;
        var camTransform = camera.GameObject.Transform;
        _camPosition = camTransform.Position;
        _camForward = camTransform.Forward;
        MouseRay = camera.ScreenPointToRay(mouseLocal, ViewportSize);

        PrimaryDown = Input.GetMouseButtonDown(0);
        PrimaryHeld = Input.GetMouseButton(0);
        PrimaryUp = Input.GetMouseButtonUp(0);
        Shift = Input.IsShiftPressed;
        Ctrl = Input.IsCtrlPressed;
        Alt = Input.IsAltPressed;
        Blocked = Input.IsAltPressed || Input.GetMouseButton(1) || Input.GetMouseButton(2);

        // A drag whose owner never saw the release (viewport lost focus mid-drag) would otherwise
        // hold Hot forever.
        if (Hot.IsValid && !PrimaryHeld && !PrimaryUp)
            ReleaseHot();
    }

    public void EndFrame() { }

    // ================================================================
    //  Identity
    // ================================================================

    public ControlID GetControlID(string name) => new(Hash(name));

    /// <summary>Indexed variant for handle loops - no string interpolation per handle per frame.</summary>
    public ControlID GetControlID(string name, int index)
    {
        unchecked { return new ControlID(Hash(name) * 397 ^ index); }
    }

    private static int Hash(string name)
    {
        int h = name.GetHashCode();
        return h == 0 ? 1 : h; // 0 is reserved for None
    }

    // ================================================================
    //  Arbitration
    // ================================================================

    /// <summary>
    /// Report this control's screen-space distance from the cursor, in pixels, and how far in front
    /// of the camera it sits. The nearest control on screen wins; controls whose screen distances are
    /// within <see cref="DepthTieBandPixels"/> of each other count as overlapping and are settled by
    /// <paramref name="depth"/> instead, so a handle behind another cannot steal the cursor. Exact
    /// ties go to the later registration, so register smaller and more specific handles last.
    /// <para><see cref="float.MaxValue"/> (and any non-finite value) as the distance means "not a
    /// candidate this frame", so a handle that is simply not under the cursor can report
    /// unconditionally rather than the caller having to branch around the call.</para>
    /// <para>Leaving <paramref name="depth"/> unset means "depth unknown", which loses every
    /// overlap tie to a control that does report one. Screen-space overlays that always draw on top
    /// should pass 0.</para>
    /// </summary>
    public void AddControl(ControlID id, float screenDistance, float depth = float.MaxValue)
    {
        if (!id.IsValid || !float.IsFinite(screenDistance) || screenDistance >= float.MaxValue) return;

        if (_pendingNearest.IsValid)
        {
            float delta = screenDistance - _pendingDistance;
            bool wins = delta < -DepthTieBandPixels    // clearly nearer the cursor
                || (delta <= DepthTieBandPixels && depth <= _pendingDepth); // overlapping: in front wins
            if (!wins) return;
        }

        _pendingDistance = screenDistance;
        _pendingDepth = depth;
        _pendingNearest = id;
    }

    /// <summary>
    /// Register a point handle at a world position, deriving both its screen distance and its depth.
    /// The preferred overload for anything anchored in the scene, since it cannot forget the depth.
    /// </summary>
    public void AddControl(ControlID id, Float3 world, float grabRadiusPixels)
        => AddControl(id, DistanceToPoint(world, grabRadiusPixels), DepthOf(world));

    /// <summary>Register a fallback that wins only when no other control registered at all.</summary>
    public void AddDefaultControl(ControlID id) => _pendingDefault = id;

    public ControlID Nearest => _nearest;
    public ControlID Hot { get; private set; }
    public ControlID Keyboard { get; set; }

    public bool IsNearest(ControlID id) => id.IsValid && _nearest == id;
    public bool IsHot(ControlID id) => id.IsValid && Hot == id;

    /// <summary>True when this control should render as hovered or active: it owns the drag, or
    /// nothing owns a drag and it is the nearest.</summary>
    public bool IsActive(ControlID id)
        => id.IsValid && (Hot == id || (!Hot.IsValid && _nearest == id && !Blocked));

    // ================================================================
    //  Drag capture
    // ================================================================

    /// <summary>Take the drag if this control won arbitration and the primary button just went down.</summary>
    public bool TryBeginDrag(ControlID id)
    {
        if (Blocked || !PrimaryDown || Hot.IsValid || !IsNearest(id)) return false;
        Hot = id;
        _dragOrigin[id] = MousePosition;
        return true;
    }

    /// <summary>Release the drag if this control owns it and the primary button came up.</summary>
    public bool TryEndDrag(ControlID id)
    {
        if (Hot != id || !PrimaryUp) return false;
        ReleaseHot();
        return true;
    }

    /// <summary>Whether the cursor has travelled far enough since the grab to count as a drag rather
    /// than a click. Used to let a press inside a body either move it or fall through to selection.</summary>
    public bool DragExceededThreshold(ControlID id, float pixels)
        => _dragOrigin.TryGetValue(id, out Float2 origin)
           && Float2.Length(MousePosition - origin) > pixels;

    private void ReleaseHot()
    {
        _dragOrigin.Remove(Hot);
        Hot = ControlID.None;
    }

    // ================================================================
    //  Projection and distance
    // ================================================================

    /// <summary>Project a world point into viewport-local pixels. Null when behind the camera.</summary>
    public Float2? WorldToScreen(Float3 world) => GizmoUtils.WorldToScreen(Viewport, _viewProjection, world);

    /// <summary>
    /// How far in front of the camera a world point sits, along the view direction. Used to settle
    /// overlapping handles; smaller is nearer. Negative behind the camera. Measured along the view
    /// axis rather than as a straight-line distance, so handles at the edge of a wide viewport are
    /// not treated as further away than ones at its centre.
    /// </summary>
    public float DepthOf(Float3 world) => Float3.Dot(world - _camPosition, _camForward);

    /// <summary>
    /// Screen distance from the cursor to a world point, or <see cref="float.MaxValue"/> when the
    /// point is behind the camera or further away than <paramref name="grabRadiusPixels"/>.
    /// </summary>
    /// <remarks>
    /// The radius is a grab region, not a bias: outside it the handle is not a candidate at all.
    /// Returning a real distance at any range would make every registered handle outrank the
    /// viewport's default control no matter how far from the cursor it sat, which silently disables
    /// object picking.
    /// </remarks>
    public float DistanceToPoint(Float3 world, float grabRadiusPixels)
    {
        Float2? screen = WorldToScreen(world);
        return screen is null ? float.MaxValue : DistanceToScreenPoint(screen.Value, grabRadiusPixels);
    }

    /// <summary>Screen distance from the cursor to a point, or <see cref="float.MaxValue"/> when it
    /// is further away than <paramref name="grabRadiusPixels"/>. See <see cref="DistanceToPoint"/>.</summary>
    public float DistanceToScreenPoint(Float2 screen, float grabRadiusPixels)
    {
        float d = Float2.Length(MousePosition - screen);
        return d > grabRadiusPixels ? float.MaxValue : d;
    }
}
