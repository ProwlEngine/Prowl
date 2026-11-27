// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.PaperUI;
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

    private readonly Dictionary<ControlID, Float2> _dragOrigin = [];

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

    /// <summary>The camera ray through a point in absolute Paper space.</summary>
    public Ray RayThrough(Float2 screen) => _camera.ScreenPointToRay(screen - (MousePosition - MouseLocal), ViewportSize);
    public bool ViewportHovered { get; private set; }

    public bool PrimaryDown { get; private set; }
    public bool PrimaryHeld { get; private set; }
    public bool PrimaryUp { get; private set; }

    public bool Shift { get; private set; }
    public bool Ctrl { get; private set; }
    public bool Alt { get; private set; }

    /// <summary>Camera navigation is in progress: nothing may pick or start a drag.</summary>
    public bool Blocked { get; private set; }

    /// <summary>
    /// A UI widget is consuming typing this frame (a text field is being edited). Scene-view
    /// keyboard shortcuts must stand down, or typing "d" into an inspector field also duplicates
    /// whatever the scene editor has selected.
    /// </summary>
    public bool KeyboardCaptured { get; private set; }

    /// <summary>Key-down that yields to text editing. Use this instead of <see cref="Input"/>
    /// directly for any scene-view shortcut.</summary>
    public bool GetKeyDown(KeyCode key) => !KeyboardCaptured && Input.GetKeyDown(key);

    /// <summary>Key-held that yields to text editing. See <see cref="GetKeyDown"/>.</summary>
    public bool GetKey(KeyCode key) => !KeyboardCaptured && Input.GetKey(key);

    /// <summary>Whether the pointer was over the view's panel last frame, overlays and toolbars included. Set by the view.</summary>
    public bool PanelHovered { get; internal set; }

    /// <summary>Tool keys belong to this view: the pointer is over it, nothing is typing and the camera is not flying.</summary>
    public bool KeysActive => PanelHovered && !KeyboardCaptured && !Input.GetMouseButton(1) && !Input.GetMouseButton(2);

    /// <summary>
    /// A tool's modal operation owns the viewport: the camera does not move, the right click menu does not open and no
    /// other shortcuts fire. The tool sets it when the operation starts and clears it when it ends.
    /// </summary>
    public bool Modal { get; set; }

    /// <summary>A tool acted on a key this frame, so the viewport's own shortcuts stand down.</summary>
    public bool KeysUsed { get; private set; }

    /// <summary>A shortcut for this view, marked as used when it fires.</summary>
    public bool IsPressed(string shortcut)
    {
        if (!KeysActive || !ShortcutManager.IsPressed(shortcut)) return false;
        KeysUsed = true;
        return true;
    }

    /// <summary>Whether a shortcut's key is held down over this view, for hold to use bindings.</summary>
    public bool IsHeld(string shortcut)
        => KeysActive && ShortcutManager.GetBinding(shortcut) is { IsBound: true } binding && Input.GetKey(binding.Key);

    /// <summary>A raw key for this view, marked as used when it fires.</summary>
    public bool KeyDown(KeyCode key)
    {
        if (!KeysActive || !Input.GetKeyDown(key)) return false;
        KeysUsed = true;
        return true;
    }

    /// <summary>
    /// Cursor shape requested by handles this frame, or <see cref="PaperCursor.Inherit"/> when none
    /// asked. The viewport applies it to its own element, so Paper resolves it exactly like any UI
    /// hover cursor rather than fighting it.
    /// </summary>
    public PaperCursor Cursor { get; private set; }

    /// <summary>
    /// Ask for a cursor shape while this control owns the cursor. Ignored unless the control is
    /// active (hovered with nothing dragging, or itself dragging), so a handle can call it
    /// unconditionally and only the one the user is actually on gets to change the pointer.
    /// </summary>
    public void RequestCursor(ControlID id, PaperCursor shape)
    {
        if (shape == PaperCursor.Inherit || !IsActive(id)) return;

        // A drag in progress outranks a mere hover, so the shape does not flicker when the cursor
        // strays over a different handle mid-drag.
        if (Cursor == PaperCursor.Inherit || IsHot(id))
            Cursor = shape;
    }

    // ================================================================
    //  Lifecycle
    // ================================================================

    /// <summary> Resolve the previous frame's registered controls, set all frame state (camera, viewport, mouse, input), and begin the deferred draw list for this frame. </summary>
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
        Transform camTransform = camera.GameObject.Transform;
        _camPosition = camTransform.Position;
        _camForward = camTransform.Forward;
        MouseRay = camera.ScreenPointToRay(mouseLocal, ViewportSize);
        Draw.Begin(this);

        PrimaryDown = Input.GetMouseButtonDown(0);
        PrimaryHeld = Input.GetMouseButton(0);
        PrimaryUp = Input.GetMouseButtonUp(0);
        Shift = Input.IsShiftPressed;
        Ctrl = Input.IsCtrlPressed;
        Alt = Input.IsAltPressed;
        Blocked = Input.IsAltPressed || Input.GetMouseButton(1) || Input.GetMouseButton(2);
        KeyboardCaptured = EditorApplication.Instance?.PaperInstance?.WantsCaptureKeyboard == true;
        KeysUsed = false;
        Cursor = PaperCursor.Inherit;

        // Registrations resolve a frame late, so a cursor that just moved onto UI over the viewport would still hold
        // last frame's winner and let a press reach the handle behind it
        if (!ViewportHovered && !Hot.IsValid) _nearest = ControlID.None;

        // A drag whose owner never saw the release (viewport lost focus mid-drag) would otherwise
        // hold Hot forever.
        if (Hot.IsValid && !PrimaryHeld && !PrimaryUp)
            ReleaseHot();
    }

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
        if (!AcceptsRegistrations) return;
        if (!id.IsValid || !float.IsFinite(screenDistance) || screenDistance >= float.MaxValue) return;

        if (_pendingNearest.IsValid)
        {
            float delta = screenDistance - _pendingDistance;
            bool wins = delta < -DepthTieBandPixels    // clearly nearer the cursor
                            || (MathF.Abs(delta) <= DepthTieBandPixels && depth <= _pendingDepth); // overlapping: in front wins
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

    /// <summary>
    /// Register a line handle grabbed anywhere along a world segment. <paramref name="endGapPixels"/> leaves that much
    /// of each end to point handles sitting there, and a segment too short for the gaps is grabbed at its middle.
    /// </summary>
    public void AddControl(ControlID id, Float3 a, Float3 b, float grabRadiusPixels, float endGapPixels = 0f)
    {
        Float2? sa = WorldToScreen(a), sb = WorldToScreen(b);
        if (sa is null || sb is null)
        {
            const float near = 1e-3f;
            float da = DepthOf(a), db = DepthOf(b);
            if (da < near && db < near) return;
            if (da < near) a += (b - a) * ((near - da) / (db - da));
            else b += (a - b) * ((near - db) / (da - db));
            sa = WorldToScreen(a);
            sb = WorldToScreen(b);
            if (sa is null || sb is null) return;
        }

        Float2 ab = sb.Value - sa.Value;
        float length = Float2.Length(ab);
        float t = 0.5f;
        if (length > 2f * endGapPixels && length > 1e-4f)
        {
            float gap = endGapPixels / length;
            t = Maths.Clamp(Float2.Dot(MousePosition - sa.Value, ab) / (length * length), gap, 1f - gap);
        }

        AddControl(id, DistanceToScreenPoint(sa.Value + ab * t, grabRadiusPixels), DepthOf(a + (b - a) * t));
    }

    /// <summary>Register a fallback that wins only when no other control registered at all.</summary>
    public void AddDefaultControl(ControlID id)
    {
        if (!AcceptsRegistrations) return;
        _pendingDefault = id;
    }

    /// <summary>
    /// Whether anything in the scene may become hovered or pressed now. UI drawn over the viewport, such as a tool's
    /// panel or the view cube, takes the cursor, so nothing behind it lights up or can be clicked through it. A drag
    /// already under way keeps going when the cursor passes over UI.
    /// </summary>
    private bool AcceptsRegistrations => ViewportHovered || Hot.IsValid;

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

    /// <summary>Combined projection * view for this frame, for callers that project themselves.</summary>
    public Float4x4 ViewProjection => _viewProjection;

    /// <summary>Deferred 3D drawing, replayed onto the viewport overlay after the input pass.</summary>
    public SceneDrawList Draw { get; } = new();

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

/// <summary>A pointer that follows the mouse, a tenth as far while Shift is held, for fine drags.</summary>
public sealed class SlowPointer
{
    private Float2 _last;

    public Float2 Position { get; private set; }

    public void Reset(Float2 at) => Position = _last = at;

    /// <summary>Moves on by how far the mouse moved since the last call.</summary>
    public Float2 Follow(Float2 mouse, bool slow)
    {
        Position += (mouse - _last) * (slow ? 0.1f : 1f);
        _last = mouse;
        return Position;
    }

    public Float2 Follow(HandleContext handles) => Follow(handles.MousePosition, handles.Shift);
}
