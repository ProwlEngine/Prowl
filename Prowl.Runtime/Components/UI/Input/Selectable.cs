// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

public enum SelectionState
{
    Normal,
    Highlighted,
    Pressed,
    Selected,
    Disabled,
}

/// <summary>
/// Base class for every interactive UI widget - buttons, toggles, sliders, dropdowns.
/// Tracks the pointer state machine, drives a sibling <see cref="UIImage"/>'s color
/// across the four states, fires SFX through <see cref="UISounds"/>, and exposes
/// per-instance overrides for both the colors and the audio.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class Selectable : UIBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler,
    ISelectHandler, IDeselectHandler
{
    // ============================================================
    // Interactability
    // ============================================================

    [SerializeField] private bool _interactable = true;
    /// <summary>When false, the widget is rendered in the Disabled state and ignores clicks (but plays the denied SFX).</summary>
    public bool Interactable
    {
        get => _interactable;
        set
        {
            if (_interactable == value) return;
            _interactable = value;
            RefreshState(immediate: false);
        }
    }

    /// <summary>
    /// The effective interactable state: the local <see cref="Interactable"/> flag AND no enclosing
    /// <see cref="CanvasGroup"/> (up to the canvas root, or the nearest one that ignores its parents)
    /// has <see cref="CanvasGroup.Interactable"/> turned off. Pointer/submit handling gates on this, so
    /// a non-interactable group makes its whole subtree inert while still blocking click-through.
    /// </summary>
    public bool IsInteractable()
    {
        if (!_interactable) return false;

        GameObject? node = GameObject;
        while (node != null)
        {
            CanvasGroup? grp = node.GetComponent<CanvasGroup>();
            if (grp != null && grp.EnabledInHierarchy)
            {
                if (!grp.Interactable) return false;
                if (grp.IgnoreParentGroups) break;
            }
            if (node.GetComponent<GameCanvas>() != null) break; // reached the canvas root
            node = node.Parent;
        }
        return true;
    }

    /// <summary>Re-evaluate the visual state - called when an ancestor <see cref="CanvasGroup"/>
    /// toggles interactivity (there is no automatic notification for that).</summary>
    public void RefreshInteractable() => RefreshState(immediate: false);

    // ============================================================
    // Target graphic - which UIImage do we tint?
    // ============================================================

    [SerializeField] private UIImage? _targetGraphic;
    /// <summary>The <see cref="UIImage"/> whose <c>Color</c> the state machine drives. Defaults to a UIImage on this GameObject.</summary>
    public UIImage? TargetGraphic
    {
        get => _targetGraphic ??= GetComponent<UIImage>();
        set => _targetGraphic = value;
    }

    // ============================================================
    // Color tinting per state
    // ============================================================

    [SerializeField] private Color _normalColor      = new(1f, 1f, 1f, 1f);
    [SerializeField] private Color _highlightedColor = new(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color _pressedColor     = new(0.78f, 0.78f, 0.78f, 1f);
    [SerializeField] private Color _selectedColor    = new(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color _disabledColor    = new(0.78f, 0.78f, 0.78f, 0.5f);

    public Color NormalColor      { get => _normalColor;      set { _normalColor = value;      RefreshState(immediate: false); } }
    public Color HighlightedColor { get => _highlightedColor; set { _highlightedColor = value; RefreshState(immediate: false); } }
    public Color PressedColor     { get => _pressedColor;     set { _pressedColor = value;     RefreshState(immediate: false); } }
    public Color SelectedColor    { get => _selectedColor;    set { _selectedColor = value;    RefreshState(immediate: false); } }
    public Color DisabledColor    { get => _disabledColor;    set { _disabledColor = value;    RefreshState(immediate: false); } }

    /// <summary>Seconds the tint takes to lerp to a new target color. 0 = snap.</summary>
    [SerializeField] private float _transitionDuration = 0.08f;
    public float TransitionDuration { get => _transitionDuration; set => _transitionDuration = Maths.Max(0f, value); }

    // ============================================================
    // Runtime state
    // ============================================================

    [SerializeIgnore] private bool _isHovered;
    [SerializeIgnore] private bool _isPressed;
    [SerializeIgnore] private bool _isSelected;
    [SerializeIgnore] private SelectionState _currentState = SelectionState.Normal;
    [SerializeIgnore] private Color _displayedColor = Color.White;
    [SerializeIgnore] private Color _fromColor = Color.White;
    [SerializeIgnore] private Color _toColor = Color.White;
    [SerializeIgnore] private float _transitionElapsed;

    /// <summary>The current high-level state. Read-only for derived classes.</summary>
    public SelectionState CurrentState => _currentState;

    /// <summary>Whether the pointer is currently hovering this widget.</summary>
    public bool IsHovered => _isHovered;

    /// <summary>Whether the widget is currently held down.</summary>
    public bool IsPressed => _isPressed;

    // ============================================================
    // UIBehaviour overrides - Selectable has no geometry of its own.
    // ============================================================

    /// <inheritdoc/>
    public override void GenerateMesh(UIMeshBuilder builder, in UIContext context) { /* no geometry */ }

    public override void OnEnable()
    {
        base.OnEnable();
        RefreshState(immediate: true);
    }

    /// <summary>Drives the color lerp toward the current state's target. Called every frame.</summary>
    public override void Update()
    {
        if (!Application.IsPlaying) return;
        if (TargetGraphic == null) return;

        float dur = _transitionDuration;
        if (dur <= 0f || _transitionElapsed >= dur)
        {
            if (TargetGraphic.Color != _toColor)
                TargetGraphic.Color = _toColor;
            _displayedColor = _toColor;
            return;
        }

        _transitionElapsed += Time.DeltaTime;
        float t = Maths.Clamp(_transitionElapsed / dur, 0f, 1f);
        _displayedColor = Color.Lerp(_fromColor, _toColor, t);
        TargetGraphic.Color = _displayedColor;
    }

    // ============================================================
    // Pointer
    // ============================================================

    public virtual void OnPointerEnter(PointerEventData e)
    {
        _isHovered = true;
        RefreshState(immediate: false);
    }

    public virtual void OnPointerExit(PointerEventData e)
    {
        _isHovered = false;
        // A press-then-leave keeps the pressed visual until release, matching common UI behavior.
        if (!_isPressed) RefreshState(immediate: false);
    }

    public virtual void OnPointerDown(PointerEventData e)
    {
        if (e.Button != MouseButton.Left) return;

        if (!IsInteractable()) return;

        _isPressed = true;
        RefreshState(immediate: false);
    }

    public virtual void OnPointerUp(PointerEventData e)
    {
        if (e.Button != MouseButton.Left) return;
        _isPressed = false;
        RefreshState(immediate: false);
    }

    public virtual void OnSelect()
    {
        _isSelected = true;
        RefreshState(immediate: false);
    }

    public virtual void OnDeselect()
    {
        _isSelected = false;
        RefreshState(immediate: false);
    }

    // ============================================================
    // Helpers
    // ============================================================

    /// <summary>Re-evaluates the active <see cref="SelectionState"/> and starts a tint lerp toward it.</summary>
    protected void RefreshState(bool immediate)
    {
        SelectionState next = ComputeState();
        if (next == _currentState && !immediate) return;

        _currentState = next;
        Color target = next switch
        {
            SelectionState.Disabled    => _disabledColor,
            SelectionState.Pressed     => _pressedColor,
            SelectionState.Highlighted => _highlightedColor,
            SelectionState.Selected    => _selectedColor,
            _                          => _normalColor,
        };

        _fromColor = _displayedColor;
        _toColor = target;
        _transitionElapsed = immediate ? float.PositiveInfinity : 0f;

        if (immediate && TargetGraphic != null)
        {
            TargetGraphic.Color = target;
            _displayedColor = target;
        }
    }

    private SelectionState ComputeState()
    {
        if (!IsInteractable()) return SelectionState.Disabled;
        if (_isPressed)     return SelectionState.Pressed;
        if (_isHovered)     return SelectionState.Highlighted;
        if (_isSelected)    return SelectionState.Selected;
        return SelectionState.Normal;
    }
}
