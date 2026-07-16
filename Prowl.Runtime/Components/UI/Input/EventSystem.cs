// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// Drives GameObject-UI input, Unity-style. A scene needs exactly one enabled <see cref="EventSystem"/>
/// for its <see cref="GameCanvas"/>es to receive pointer and keyboard events. Each frame it hit-tests the
/// pointer against the scene's canvases (via <see cref="UIRaycaster"/>) and dispatches enter/exit, press,
/// drag, click, scroll, submit/cancel and selection events to the components under it.
/// </summary>
/// <remarks>
/// This is entirely game-space: it ticks from <see cref="Update"/> (play mode only) and nothing in the
/// engine loop drives it. A <see cref="GameCanvas"/> has no dependency on it - delete this component and
/// canvases still render, they just stop receiving input - so you can drop in your own event system.
/// Access the active one through <see cref="Current"/>.
/// </remarks>
[AddComponentMenu("UI/Event System")]
[ComponentIcon("")] // ArrowPointer
public sealed class EventSystem : MonoBehaviour
{
    [SerializeIgnore] private static EventSystem? s_current;

    /// <summary>The active event system driving UI input, or null when none is enabled.</summary>
    public static EventSystem? Current => s_current;

    // ============================================================
    // Configuration
    // ============================================================

    [SerializeField] private float _dragThreshold = 4f;
    /// <summary>Pixels the pointer must move after a press before it counts as a drag.</summary>
    public float DragThreshold { get => _dragThreshold; set => _dragThreshold = value; }

    [SerializeField] private float _multiClickWindow = 0.4f;
    /// <summary>Seconds within which a repeat click on the same target increments the click streak.</summary>
    public float MultiClickWindow { get => _multiClickWindow; set => _multiClickWindow = value; }

    [SerializeField] private KeyCode _submitKey = KeyCode.Enter;
    public KeyCode SubmitKey { get => _submitKey; set => _submitKey = value; }

    [SerializeField] private KeyCode _cancelKey = KeyCode.Escape;
    public KeyCode CancelKey { get => _cancelKey; set => _cancelKey = value; }

    // ============================================================
    // Host viewport override
    // ============================================================

    public struct HostViewport
    {
        public Float2 ReferenceSize;
        public Float2 PointerPosition;
        public bool ReceivesInput;
    }

    /// <summary>
    /// The active host viewport, or <c>null</c> when running standalone (no editor). Hosts (the editor
    /// game view) write this every frame so input is sourced from a render-target region instead of the
    /// OS window. A host that stops being active sets <see cref="HostViewport.ReceivesInput"/> to false.
    /// </summary>
    public HostViewport? Viewport { get; set; }

    // ============================================================
    // State
    // ============================================================

    [SerializeIgnore] private GameObject? _hovered;
    /// <summary>The GameObject currently under the pointer (top-most UI hit). Null when none.</summary>
    public GameObject? Hovered => _hovered;

    [SerializeIgnore] private GameObject? _selected;
    /// <summary>The focused element - receives keyboard navigation, submit and cancel. See <see cref="SetSelected"/>.</summary>
    public GameObject? Selected => _selected;

    [SerializeIgnore] private Float2 _pointerPosition;
    /// <summary>Pointer position in window pixels (top-left origin, +Y down).</summary>
    public Float2 PointerPosition => _pointerPosition;

    [SerializeIgnore] private readonly PointerEventData _left = new() { Button = MouseButton.Left };
    [SerializeIgnore] private readonly PointerEventData _right = new() { Button = MouseButton.Right };
    [SerializeIgnore] private readonly PointerEventData _middle = new() { Button = MouseButton.Middle };
    /// <summary>The pointer event data for the left mouse button - usually what handlers read.</summary>
    public PointerEventData Left => _left;

    [SerializeIgnore] private GameObject? _lastHovered;
    [SerializeIgnore] private Float2 _lastPointerPos;

    // ============================================================
    // Lifecycle
    // ============================================================

    public override void OnEnable()
    {
        // First enabled instance wins; any extras stay inert so the tick only runs once per frame.
        s_current ??= this;
    }

    public override void OnDisable()
    {
        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
            ClearState(); // drop focus/hover so nothing dangles once input stops (e.g. leaving play mode)
        }
    }

    public override void Update()
    {
        // Reclaim the role if the active system went away, so a surviving instance keeps input alive.
        if (s_current is null || s_current.IsDisposed) s_current = this;
        if (!ReferenceEquals(s_current, this)) return;
        Tick(Time.TimeSinceStartup);
    }

    // Clear the static reference on script hot-reload so it doesn't pin a disposed component (and its
    // collectible AssemblyLoadContext).
    [OnAssemblyUnload]
    public static void ResetStatics() => s_current = null;

    private void ClearState()
    {
        _hovered = null;
        _selected = null;
        _lastHovered = null;
        _left.Reset();
        _right.Reset();
        _middle.Reset();
    }

    // ============================================================
    // Selection
    // ============================================================

    /// <summary>
    /// Sets the focused element. Fires <see cref="IDeselectHandler"/> on the previous selection and
    /// <see cref="ISelectHandler"/> on the new one. Pass <c>null</c> to clear focus.
    /// </summary>
    public void SetSelected(GameObject? go)
    {
        if (ReferenceEquals(go, _selected)) return;

        if (_selected is { IsDisposed: false })
            ExecuteHierarchy<IDeselectHandler>(_selected, h => h.OnDeselect());

        _selected = go;

        if (go != null)
            ExecuteHierarchy<ISelectHandler>(go, h => h.OnSelect());
    }

    // ============================================================
    // Dispatch helpers (stateless)
    // ============================================================

    /// <summary>
    /// Fires <typeparamref name="TInterface"/> on the first component up the GameObject hierarchy (starting
    /// from <paramref name="root"/>) that implements it. Returns the consuming GameObject, or <c>null</c>.
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
                    catch (Exception ex) { Debug.LogError($"[EventSystem] {typeof(TInterface).Name} threw on {comp.Name}: {ex.Message}\n{ex.StackTrace}"); }
                    return node;
                }
            }
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// Bubbling variant: dispatches to every handler up the hierarchy until one sets
    /// <see cref="PointerEventData.Used"/>. Used for pointer events that may want to be observed by
    /// ancestors regardless of whether a leaf handled them.
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
                    catch (Exception ex) { Debug.LogError($"[EventSystem] {typeof(TInterface).Name} threw on {comp.Name}: {ex.Message}\n{ex.StackTrace}"); }
                    if (e.Used) return first;
                }
            }
            node = node.Parent;
        }
        return first;
    }

    /// <summary>Fires <typeparamref name="TInterface"/> on every enabled handler on a SINGLE node (no
    /// hierarchy walk). Used for the pointer enter/exit chain, which visits each node explicitly.</summary>
    private static void DispatchNode<TInterface>(GameObject node, PointerEventData e, Action<TInterface, PointerEventData> action)
        where TInterface : class
    {
        foreach (MonoBehaviour comp in node.GetComponents<MonoBehaviour>())
        {
            if (comp is not TInterface handler || !comp.EnabledInHierarchy) continue;
            try { action(handler, e); }
            catch (Exception ex) { Debug.LogError($"[EventSystem] {typeof(TInterface).Name} threw on {comp.Name}: {ex.Message}\n{ex.StackTrace}"); }
        }
    }

    /// <summary>True if <paramref name="node"/> is <paramref name="of"/> or one of its ancestors. Null
    /// <paramref name="of"/> is never contained.</summary>
    private static bool IsAncestorOrSelf(GameObject node, GameObject? of)
    {
        for (GameObject? c = of; c != null; c = c.Parent)
            if (ReferenceEquals(c, node)) return true;
        return false;
    }

    /// <summary>Walks up from <paramref name="from"/> and returns the GameObject that owns the first
    /// <typeparamref name="T"/> component encountered, or null.</summary>
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

    // ============================================================
    // Frame tick
    // ============================================================

    private void Tick(float currentTime)
    {
        Scene? scene = Scene.Current;

        // Source the pointer from the active host viewport when present; otherwise the OS window + mouse.
        Float2 winSize;
        Float2 pos;
        bool gated; // when true, suppress all hits this frame (panel not focused)
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

        Float2 delta = pos - _lastPointerPos;
        _lastPointerPos = pos;
        _pointerPosition = pos;

        // 1) Hit-test the cursor against every screen-space canvas. When a host viewport is active we push
        //    its ReferenceSize as the canvas screen-size override for the pick, so each canvas lays out
        //    against the same size the renderer used (e.g. a 1920x1080 RT inside a letterboxed panel).
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

        _hovered = hovered;

        // 2) Hover transitions - Exit on the old chain, Enter on the new, each stopping at the lowest
        //    common ancestor so a shared parent panel doesn't flicker as the pointer moves between children.
        if (!ReferenceEquals(hovered, _lastHovered))
        {
            GameObject? oldHover = _lastHovered is { IsDisposed: false } ? _lastHovered : null;
            FillCommon(_left, hovered, hoveredCanvas, pos, delta, designPos);

            for (GameObject? n = oldHover; n != null && !IsAncestorOrSelf(n, hovered); n = n.Parent)
                DispatchNode<IPointerExitHandler>(n, _left, static (h, e) => h.OnPointerExit(e));
            for (GameObject? n = hovered; n != null && !IsAncestorOrSelf(n, oldHover); n = n.Parent)
                DispatchNode<IPointerEnterHandler>(n, _left, static (h, e) => h.OnPointerEnter(e));

            _lastHovered = hovered;
        }

        // 3) Per-button presses, releases, clicks, drags.
        UpdateButton(_left, MouseButton.Left, 0, hovered, hoveredCanvas, pos, delta, designPos, winSize, currentTime);
        UpdateButton(_right, MouseButton.Right, 1, hovered, hoveredCanvas, pos, delta, designPos, winSize, currentTime);
        UpdateButton(_middle, MouseButton.Middle, 2, hovered, hoveredCanvas, pos, delta, designPos, winSize, currentTime);

        // 4) Scroll dispatch - only when the pointer is over something hittable.
        float scroll = Input.MouseWheelDelta;
        if (Maths.Abs(scroll) > 0.0001f && hovered != null)
        {
            FillCommon(_left, hovered, hoveredCanvas, pos, delta, designPos);
            _left.ScrollDelta = scroll;
            Bubble<IScrollHandler>(hovered, _left, static (h, e) => h.OnScroll(e));
            _left.ScrollDelta = 0f;
        }

        // 5) Keyboard: Submit / Cancel / directional Move on the focused element.
        if (_selected is { IsDisposed: false })
        {
            if (Input.GetKeyDown(_submitKey))
                ExecuteHierarchy<ISubmitHandler>(_selected, h => h.OnSubmit());
            if (Input.GetKeyDown(_cancelKey))
            {
                ExecuteHierarchy<ICancelHandler>(_selected, h => h.OnCancel());
                SetSelected(null);
            }

            if (Input.GetKeyDown(KeyCode.Left))  DispatchMove(MoveDirection.Left);
            if (Input.GetKeyDown(KeyCode.Right)) DispatchMove(MoveDirection.Right);
            if (Input.GetKeyDown(KeyCode.Up))    DispatchMove(MoveDirection.Up);
            if (Input.GetKeyDown(KeyCode.Down))  DispatchMove(MoveDirection.Down);
        }
    }

    private void DispatchMove(MoveDirection dir)
    {
        if (_selected == null) return;
        ExecuteHierarchy<IMoveHandler>(_selected, h => h.OnMove(dir));
    }

    private void UpdateButton(
        PointerEventData e,
        MouseButton button,
        int buttonIndex,
        GameObject? hovered,
        GameCanvas? hoveredCanvas,
        Float2 pos,
        Float2 delta,
        Float2 designPos,
        Float2 winSize,
        float currentTime)
    {
        FillCommon(e, hovered, hoveredCanvas, pos, delta, designPos);

        // When the pointer leaves every raycast target mid-interaction the hit-test yields no design
        // position (it falls back to zero). Recover it from the active drag/press target's canvas so
        // drags keep tracking the pointer instead of snapping the value to the canvas origin.
        if (hovered == null)
        {
            GameObject? tracked = e.Dragging ?? e.PressedOn;
            GameCanvas? canvas = tracked?.GetComponentInParent<GameCanvas>(includeSelf: true);
            if (canvas != null && UIRaycaster.TryProjectPointer(canvas, pos, winSize, out Float2 dp))
                e.DesignPosition = dp;
        }

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

            if (hovered != null &&
                currentTime - e.LastClickTime <= _multiClickWindow &&
                ReferenceEquals(hovered, e.LastClickTarget))
                e.ClickCount++;
            else
                e.ClickCount = 1;

            if (hovered != null)
            {
                Bubble<IPointerDownHandler>(hovered, e, static (h, ev) => h.OnPointerDown(ev));

                // Auto-focus a Selectable on press so subsequent keyboard input goes to that widget.
                // Non-Selectable presses don't change focus.
                if (button == MouseButton.Left)
                    SetSelected(FindAncestor<Selectable>(hovered));
            }
        }

        // ---- Drag detection / continuation ----
        if (held && e.PressedOn != null)
        {
            if (!e.IsDragging)
            {
                Float2 fromPress = pos - e.PressPosition;
                float distSqr = fromPress.X * fromPress.X + fromPress.Y * fromPress.Y;
                if (distSqr >= _dragThreshold * _dragThreshold)
                {
                    e.IsDragging = true;
                    e.Dragging = e.PressedOn;
                    Bubble<IBeginDragHandler>(e.Dragging, e, static (h, ev) => h.OnBeginDrag(ev));
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
            // PointerUp goes to the element that received PointerDown, wherever the pointer is now, so a
            // widget released off-target doesn't get stranded in its Pressed state.
            if (e.PressedOn != null)
                Bubble<IPointerUpHandler>(e.PressedOn, e, static (h, ev) => h.OnPointerUp(ev));

            if (e.IsDragging && e.Dragging != null)
            {
                Bubble<IEndDragHandler>(e.Dragging, e, static (h, ev) => h.OnEndDrag(ev));
                if (hovered != null && !ReferenceEquals(hovered, e.Dragging))
                    Bubble<IDropHandler>(hovered, e, static (h, ev) => h.OnDrop(ev));
            }
            else if (e.PressedOn != null && ReferenceEquals(e.PressedOn, hovered))
            {
                // Click: down + up on the same target without a drag.
                e.LastClickTime = currentTime;
                e.LastClickTarget = hovered;
                Bubble<IPointerClickHandler>(hovered, e, static (h, ev) => h.OnPointerClick(ev));
            }

            e.PressedOn = null;
            e.Dragging = null;
            e.IsDragging = false;
        }
    }
}
