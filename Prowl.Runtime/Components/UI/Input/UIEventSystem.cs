// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

public static class UIEventSystem
{
    public static float DragThreshold = 4f;

    public static float MultiClickWindow = 0.4f;

    public static bool Enabled = true;

    public static KeyCode SubmitKey = KeyCode.Enter;

    public static KeyCode CancelKey = KeyCode.Escape;

    public struct HostViewport
    {
        public Float2 ReferenceSize;

        public Float2 PointerPosition;

        public bool ReceivesInput;
    }

    /// <summary>
    /// The active host viewport, or <c>null</c> when running standalone (no editor).
    /// Hosts write this every frame; <see cref="Tick"/> reads it and uses the override
    /// when present. There is no auto-reset — a host that stops being active must
    /// set <see cref="HostViewport.ReceivesInput"/> to <c>false</c> (or clear to <c>null</c>).
    /// </summary>
    public static HostViewport? Viewport { get; set; }

    /// <summary>The GameObject currently under the pointer (top-most UI hit). Null when none.</summary>
    public static GameObject? CurrentHovered { get; private set; }

    /// <summary>
    /// The currently focused element — receives keyboard navigation, submit, and cancel.
    /// Set via <see cref="SetSelected"/> (or by clicking a <see cref="Selectable"/>).
    /// </summary>
    public static GameObject? CurrentSelected { get; private set; }

    /// <summary>Pointer position in window pixels (top-left origin, +Y down).</summary>
    public static Float2 PointerPosition { get; private set; }

    /// <summary>The pointer event data for the left mouse button — usually what handlers read.</summary>
    public static PointerEventData Left => s_left;

    // -------- Per-button tracked event data --------
    private static readonly PointerEventData s_left = new() { Button = MouseButton.Left };
    private static readonly PointerEventData s_right = new() { Button = MouseButton.Right };
    private static readonly PointerEventData s_middle = new() { Button = MouseButton.Middle };

    private static GameObject? s_lastHovered;
    private static Float2 s_lastPointerPos;

    /// <summary>
    /// Clears all retained <see cref="GameObject"/> references (hover/selection/pointer targets).
    /// These outlive a <see cref="Resources.Scene"/> unload and would otherwise pin disposed
    /// GameObjects — and, during script hot-reload, the collectible AssemblyLoadContext.
    /// </summary>
    [OnAssemblyUnload]
    public static void ResetState()
    {
        CurrentHovered = null;
        CurrentSelected = null;
        s_lastHovered = null;
        s_left.Reset();
        s_right.Reset();
        s_middle.Reset();
    }

    // ============================================================
    // Public API
    // ============================================================

    /// <summary>
    /// Sets the focused element. Fires <see cref="IDeselectHandler"/> on the previous
    /// selection and <see cref="ISelectHandler"/> on the new one. Pass <c>null</c> to
    /// clear focus. Plays the <see cref="UISound.Navigate"/> sfx when the selection
    /// changes via navigation; pass <paramref name="playSfx"/>=false to suppress.
    /// </summary>
    public static void SetSelected(GameObject? go, bool playSfx = true)
    {
        if (ReferenceEquals(go, CurrentSelected)) return;

        if (CurrentSelected is { IsDisposed: false })
            ExecuteHierarchy<IDeselectHandler>(CurrentSelected, h => h.OnDeselect());

        CurrentSelected = go;

        if (go != null)
            ExecuteHierarchy<ISelectHandler>(go, h => h.OnSelect());

        if (playSfx && go != null)
            UISounds.Play(UISound.Navigate);
    }

    /// <summary>
    /// Fires <typeparamref name="TInterface"/> on the first component up the GameObject
    /// hierarchy (starting from <paramref name="root"/>) that implements it. Mirrors
    /// Unity's <c>ExecuteEvents.ExecuteHierarchy</c>. Returns the consuming GameObject,
    /// or <c>null</c> if no handler was found.
    /// </summary>
    public static GameObject? ExecuteHierarchy<TInterface>(GameObject? root, Action<TInterface> action)
        where TInterface : class
    {
        if (root == null || action == null) return null;

        GameObject? node = root;
        while (node != null)
        {
            foreach (MonoBehaviour comp in node.GetComponents<MonoBehaviour>())
            {
                if (comp is TInterface handler && comp.EnabledInHierarchy)
                {
                    try { action(handler); }
                    catch (Exception ex) { Debug.LogError($"[UIEventSystem] {typeof(TInterface).Name} threw on {comp.Name}: {ex.Message}\n{ex.StackTrace}"); }
                    return node;
                }
            }
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// Bubbling variant: dispatches to every handler up the hierarchy until one sets
    /// <see cref="PointerEventData.Used"/>. Used for pointer events that may want to
    /// be observed by ancestors regardless of whether a leaf handled them.
    /// </summary>
    private static GameObject? Bubble<TInterface>(GameObject? root, PointerEventData e, Action<TInterface, PointerEventData> action)
        where TInterface : class
    {
        if (root == null || action == null) return null;
        e.Used = false;

        GameObject? node = root;
        GameObject? first = null;
        while (node != null)
        {
            foreach (MonoBehaviour comp in node.GetComponents<MonoBehaviour>())
            {
                if (comp is TInterface handler && comp.EnabledInHierarchy)
                {
                    first ??= node;
                    try { action(handler, e); }
                    catch (Exception ex) { Debug.LogError($"[UIEventSystem] {typeof(TInterface).Name} threw on {comp.Name}: {ex.Message}\n{ex.StackTrace}"); }
                    if (e.Used) return first;
                }
            }
            node = node.Parent;
        }
        return first;
    }

    // ============================================================
    // Frame tick — called by Game.cs once per Update
    // ============================================================

    /// <summary>
    /// Runs one frame of UI input. Safe to call when no scene is loaded. No-op when
    /// <see cref="Enabled"/> is false.
    /// </summary>
    public static void Tick(float currentTime)
    {
        if (!Enabled) return;

        Scene? scene = Scene.Current;

        // ------------------------------------------------------------------------
        // Source the pointer state from the active host viewport when present;
        // otherwise fall back to the OS window + raw mouse position.
        // ------------------------------------------------------------------------
        Float2 winSize;
        Float2 pos;
        bool gated; // when true, suppress all hits this frame (panel not focused).
        if (Viewport is { } vp)
        {
            winSize = vp.ReferenceSize;
            pos = vp.PointerPosition;
            gated = !vp.ReceivesInput;
        }
        else
        {
            winSize = new(Window.InternalWindow.Size.X, Window.InternalWindow.Size.Y);
            Int2 mp = Input.MousePosition;
            pos = new(mp.X, mp.Y);
            gated = false;
        }

        Float2 delta = pos - s_lastPointerPos;
        s_lastPointerPos = pos;
        PointerPosition = pos;

        // ------------------------------------------------------------------------
        // 1) Hit-test the cursor against every screen-space canvas in the scene.
        //    When a host viewport is active we push its ReferenceSize as the
        //    canvas screen-size override for the duration of the pick, so each
        //    canvas's RebuildIfDirty lays out against the same size the renderer
        //    used (e.g. 1920x1080 RT inside a smaller letterboxed panel).
        // ------------------------------------------------------------------------
        bool hadHit = false;
        UIRaycaster.Hit hit = default;
        if (!gated)
        {
            Float2? prevOverride = GameCanvas.ScreenSizeOverride;
            if (Viewport != null) GameCanvas.ScreenSizeOverride = winSize;
            try { hadHit = UIRaycaster.TryPick(scene, pos, winSize, out hit); }
            finally { if (Viewport != null) GameCanvas.ScreenSizeOverride = prevOverride; }
        }
        GameObject? hovered = hadHit ? hit.GameObject : null;
        GameCanvas? hoveredCanvas = hadHit ? hit.Canvas : null;
        Float2 designPos = hadHit ? hit.DesignPosition : Float2.Zero;

        CurrentHovered = hovered;

        // ------------------------------------------------------------------------
        // 2) Hover transitions — Exit on previous, Enter on new.
        // ------------------------------------------------------------------------
        if (!ReferenceEquals(hovered, s_lastHovered))
        {
            if (s_lastHovered is { IsDisposed: false })
            {
                FillCommon(s_left, hovered, hoveredCanvas, pos, delta, designPos);
                ExecuteHierarchy<IPointerExitHandler>(s_lastHovered, h => h.OnPointerExit(s_left));
            }

            if (hovered != null)
            {
                FillCommon(s_left, hovered, hoveredCanvas, pos, delta, designPos);
                ExecuteHierarchy<IPointerEnterHandler>(hovered, h => h.OnPointerEnter(s_left));
            }
            s_lastHovered = hovered;
        }

        // ------------------------------------------------------------------------
        // 3) Per-button presses, releases, clicks, drags.
        // ------------------------------------------------------------------------
        UpdateButton(s_left, MouseButton.Left, 0, hovered, hoveredCanvas, pos, delta, designPos, currentTime);
        UpdateButton(s_right, MouseButton.Right, 1, hovered, hoveredCanvas, pos, delta, designPos, currentTime);
        UpdateButton(s_middle, MouseButton.Middle, 2, hovered, hoveredCanvas, pos, delta, designPos, currentTime);

        // ------------------------------------------------------------------------
        // 4) Scroll dispatch — only when the pointer is over something hittable.
        // ------------------------------------------------------------------------
        float scroll = Input.MouseWheelDelta;
        if (Maths.Abs(scroll) > 0.0001f && hovered != null)
        {
            FillCommon(s_left, hovered, hoveredCanvas, pos, delta, designPos);
            s_left.ScrollDelta = scroll;
            Bubble<IScrollHandler>(hovered, s_left, static (h, e) => h.OnScroll(e));
            s_left.ScrollDelta = 0f;
        }

        // ------------------------------------------------------------------------
        // 5) Keyboard navigation: Submit / Cancel on the focused element.
        //    Directional Move is opt-in via IMoveHandler — wired here for parity.
        // ------------------------------------------------------------------------
        if (CurrentSelected is { IsDisposed: false })
        {
            if (Input.GetKeyDown(SubmitKey))
            {
                ExecuteHierarchy<ISubmitHandler>(CurrentSelected, h => h.OnSubmit());
                UISounds.Play(UISound.Submit);
            }
            if (Input.GetKeyDown(CancelKey))
            {
                ExecuteHierarchy<ICancelHandler>(CurrentSelected, h => h.OnCancel());
                UISounds.Play(UISound.Cancel);
                SetSelected(null, playSfx: false);
            }

            if (Input.GetKeyDown(KeyCode.Left))  DispatchMove(MoveDirection.Left);
            if (Input.GetKeyDown(KeyCode.Right)) DispatchMove(MoveDirection.Right);
            if (Input.GetKeyDown(KeyCode.Up))    DispatchMove(MoveDirection.Up);
            if (Input.GetKeyDown(KeyCode.Down))  DispatchMove(MoveDirection.Down);
        }
    }

    private static void DispatchMove(MoveDirection dir)
    {
        if (CurrentSelected == null) return;
        ExecuteHierarchy<IMoveHandler>(CurrentSelected, h => h.OnMove(dir));
    }

    private static void FillCommon(PointerEventData e, GameObject? hovered, GameCanvas? canvas, Float2 pos, Float2 delta, Float2 designPos)
    {
        e.PreviousPosition = e.Position;
        e.Position = pos;
        e.Delta = delta;
        e.DesignPosition = designPos;
        e.Hovered = hovered;
        e.HitCanvas = canvas;
        e.Used = false;
    }

    private static void UpdateButton(
        PointerEventData e,
        MouseButton button,
        int buttonIndex,
        GameObject? hovered,
        GameCanvas? hoveredCanvas,
        Float2 pos,
        Float2 delta,
        Float2 designPos,
        float currentTime)
    {
        FillCommon(e, hovered, hoveredCanvas, pos, delta, designPos);

        bool down = Input.GetMouseButtonDown(buttonIndex);
        bool up = Input.GetMouseButtonUp(buttonIndex);
        bool held = Input.GetMouseButton(buttonIndex);

        // ---- Press ----
        if (down)
        {
            e.PressedOn = hovered;
            e.PressPosition = pos;
            e.PressTime = currentTime;
            e.IsDragging = false;
            e.Dragging = null;

            // Multi-click streak: same widget pressed inside MultiClickWindow.
            if (hovered != null &&
                currentTime - e.LastClickTime <= MultiClickWindow &&
                ReferenceEquals(hovered, s_lastHovered))
                e.ClickCount++;
            else
                e.ClickCount = 1;

            if (hovered != null)
            {
                Bubble<IPointerDownHandler>(hovered, e, static (h, ev) => h.OnPointerDown(ev));

                // Auto-focus a Selectable on press — Unity does this, and it's what every
                // player expects when they click a button: subsequent keyboard input goes
                // to that widget. Non-Selectable presses don't change focus.
                if (button == MouseButton.Left)
                {
                    GameObject? sel = FindAncestor<Selectable>(hovered);
                    SetSelected(sel, playSfx: false);
                }
            }
        }

        // ---- Drag detection / continuation ----
        if (held && e.PressedOn != null)
        {
            if (!e.IsDragging)
            {
                Float2 fromPress = pos - e.PressPosition;
                float distSqr = fromPress.X * fromPress.X + fromPress.Y * fromPress.Y;
                if (distSqr >= DragThreshold * DragThreshold)
                {
                    e.IsDragging = true;
                    e.Dragging = e.PressedOn;
                    Bubble<IBeginDragHandler>(e.Dragging, e, static (h, ev) => h.OnBeginDrag(ev));
                    UISounds.Play(UISound.DragStart);
                }
            }
            else
            {
                // Send drag every frame while the button is held *and* the pointer moved.
                if (delta.X != 0f || delta.Y != 0f)
                    Bubble<IDragHandler>(e.Dragging, e, static (h, ev) => h.OnDrag(ev));
            }
        }

        // ---- Release ----
        if (up)
        {
            // PointerUp goes to whatever's currently under the pointer (release-anywhere).
            if (hovered != null)
                Bubble<IPointerUpHandler>(hovered, e, static (h, ev) => h.OnPointerUp(ev));

            if (e.IsDragging && e.Dragging != null)
            {
                Bubble<IEndDragHandler>(e.Dragging, e, static (h, ev) => h.OnEndDrag(ev));
                if (hovered != null && !ReferenceEquals(hovered, e.Dragging))
                    Bubble<IDropHandler>(hovered, e, static (h, ev) => h.OnDrop(ev));
                UISounds.Play(UISound.DragEnd);
            }
            else if (e.PressedOn != null && ReferenceEquals(e.PressedOn, hovered))
            {
                // Click: down + up on the same target without a drag.
                e.LastClickTime = currentTime;
                Bubble<IPointerClickHandler>(hovered, e, static (h, ev) => h.OnPointerClick(ev));
            }

            e.PressedOn = null;
            e.Dragging = null;
            e.IsDragging = false;
        }
    }

    /// <summary>
    /// Walks up from <paramref name="from"/> and returns the GameObject that owns the
    /// first <typeparamref name="T"/> component encountered, or null.
    /// </summary>
    private static GameObject? FindAncestor<T>(GameObject from) where T : MonoBehaviour
    {
        GameObject? node = from;
        while (node != null)
        {
            if (node.GetComponent<T>() != null) return node;
            node = node.Parent;
        }
        return null;
    }

    // Called by tests / shutdown paths that want a clean slate.
    internal static void ResetForTests()
    {
        s_left.Reset();
        s_right.Reset();
        s_middle.Reset();
        s_lastHovered = null;
        s_lastPointerPos = Float2.Zero;
        CurrentHovered = null;
        CurrentSelected = null;
    }
}
