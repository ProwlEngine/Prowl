// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

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
/// Tracks the pointer state machine, drives a sibling <see cref="Graphic"/>'s color
/// across the four states, fires SFX through <see cref="UISounds"/>, and exposes
/// per-instance overrides for both the colors and the audio.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class Selectable : UIBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler,
    ISelectHandler, IDeselectHandler, IMoveHandler
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
    // Target graphic - which Graphic do we tint?
    // ============================================================

    [SerializeField] private Graphic? _targetGraphic;
    /// <summary>The <see cref="Graphic"/> whose <c>Color</c> the state machine drives. Defaults to a graphic on this GameObject.</summary>
    public Graphic? TargetGraphic
    {
        get
        {
            if (_targetGraphic.IsNotValid())
                _targetGraphic = GetComponent<Graphic>();
            return _targetGraphic;
        }
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

    [SerializeField] private float _colorMultiplier = 1f;
    /// <summary>Multiplier applied to the active state color, so a tint can brighten past the source.</summary>
    public float ColorMultiplier { get => _colorMultiplier; set { _colorMultiplier = value; RefreshState(immediate: false); } }

    /// <summary>All five state colors plus the multiplier and fade time, as one value.</summary>
    public ColorBlock Colors
    {
        get => new()
        {
            NormalColor = _normalColor,
            HighlightedColor = _highlightedColor,
            PressedColor = _pressedColor,
            SelectedColor = _selectedColor,
            DisabledColor = _disabledColor,
            ColorMultiplier = _colorMultiplier,
            FadeDuration = _transitionDuration,
        };
        set
        {
            _normalColor = value.NormalColor;
            _highlightedColor = value.HighlightedColor;
            _pressedColor = value.PressedColor;
            _selectedColor = value.SelectedColor;
            _disabledColor = value.DisabledColor;
            _colorMultiplier = value.ColorMultiplier;
            _transitionDuration = Maths.Max(0f, value.FadeDuration);
            RefreshState(immediate: false);
        }
    }

    // ============================================================
    // Transition
    // ============================================================

    [SerializeField] private SelectableTransition _transition = SelectableTransition.ColorTint;
    /// <summary>How the current state is shown. Defaults to tinting the target graphic.</summary>
    public SelectableTransition Transition
    {
        get => _transition;
        set { _transition = value; RefreshState(immediate: true); }
    }

    [SerializeField] private SpriteState _spriteState;
    /// <summary>Per-state sprites used when <see cref="Transition"/> is
    /// <see cref="SelectableTransition.SpriteSwap"/>.</summary>
    public SpriteState SpriteState
    {
        get => _spriteState;
        set { _spriteState = value; RefreshState(immediate: true); }
    }

    // ============================================================
    // Navigation
    // ============================================================

    [SerializeField] private Navigation _navigation = Navigation.Default;
    /// <summary>How directional moves (arrow keys) hand focus to a neighbouring widget.</summary>
    public Navigation Navigation { get => _navigation; set => _navigation = value; }

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
    [SerializeIgnore] private AssetRef<Sprite> _authoredSprite;
    [SerializeIgnore] private bool _authoredSpriteCaptured;

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
        if (_transition != SelectableTransition.ColorTint) return;
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

    /// <summary>Re-evaluates the active <see cref="SelectionState"/> and applies the transition.</summary>
    protected void RefreshState(bool immediate)
    {
        SelectionState next = ComputeState();
        if (next == _currentState && !immediate) return;

        _currentState = next;

        if (_transition == SelectableTransition.SpriteSwap) { ApplySpriteSwap(next); return; }
        if (_transition == SelectableTransition.None) return;

        Color target = next switch
        {
            SelectionState.Disabled    => _disabledColor,
            SelectionState.Pressed     => _pressedColor,
            SelectionState.Highlighted => _highlightedColor,
            SelectionState.Selected    => _selectedColor,
            _                          => _normalColor,
        } * _colorMultiplier;

        _fromColor = _displayedColor;
        _toColor = target;
        _transitionElapsed = immediate ? float.PositiveInfinity : 0f;

        if (immediate && TargetGraphic != null)
        {
            TargetGraphic.Color = target;
            _displayedColor = target;
        }
    }

    private void ApplySpriteSwap(SelectionState state)
    {
        if (TargetGraphic is not UIImage image) return;

        // Captured on first use rather than in OnEnable: the target graphic may be added after this
        // component, and capturing an empty ref would make the Normal state wipe the real sprite.
        if (!_authoredSpriteCaptured)
        {
            _authoredSprite = image.Sprite;
            _authoredSpriteCaptured = true;
        }

        AssetRef<Sprite> next = state switch
        {
            SelectionState.Disabled    => _spriteState.DisabledSprite,
            SelectionState.Pressed     => _spriteState.PressedSprite,
            SelectionState.Highlighted => _spriteState.HighlightedSprite,
            SelectionState.Selected    => _spriteState.SelectedSprite,
            _                          => _authoredSprite,
        };

        image.Sprite = next.IsExplicitNull ? _authoredSprite : next;
    }

    private SelectionState ComputeState()
    {
        if (!IsInteractable()) return SelectionState.Disabled;
        if (_isPressed)     return SelectionState.Pressed;
        if (_isHovered)     return SelectionState.Highlighted;
        if (_isSelected)    return SelectionState.Selected;
        return SelectionState.Normal;
    }

    // ============================================================
    // Navigation
    // ============================================================

    /// <summary>Gives this widget keyboard focus through the active <see cref="EventSystem"/>.</summary>
    public void Select()
    {
        EventSystem? es = EventSystem.Current;
        if (es.IsValid()) es.SetSelected(GameObject);
    }

    /// <summary>Moves focus to the neighbour in <paramref name="direction"/>, if navigation allows it.</summary>
    public virtual void OnMove(MoveDirection direction)
    {
        Selectable? next = direction switch
        {
            MoveDirection.Left  => FindSelectableOnLeft(),
            MoveDirection.Right => FindSelectableOnRight(),
            MoveDirection.Up    => FindSelectableOnUp(),
            MoveDirection.Down  => FindSelectableOnDown(),
            _ => null,
        };
        if (next.IsValid()) next.Select();
    }

    public Selectable? FindSelectableOnLeft()  => FindForDirection(new Float2(-1f, 0f), _navigation.SelectOnLeft, horizontal: true);
    public Selectable? FindSelectableOnRight() => FindForDirection(new Float2(1f, 0f), _navigation.SelectOnRight, horizontal: true);
    public Selectable? FindSelectableOnUp()    => FindForDirection(new Float2(0f, 1f), _navigation.SelectOnUp, horizontal: false);
    public Selectable? FindSelectableOnDown()  => FindForDirection(new Float2(0f, -1f), _navigation.SelectOnDown, horizontal: false);

    private Selectable? FindForDirection(Float2 dir, Selectable? explicitTarget, bool horizontal)
    {
        switch (_navigation.Mode)
        {
            case NavigationMode.None: return null;
            case NavigationMode.Explicit: return explicitTarget.IsValid() ? explicitTarget : null;
            case NavigationMode.Horizontal when !horizontal: return null;
            case NavigationMode.Vertical when horizontal: return null;
        }
        return FindSelectable(dir);
    }

    /// <summary>
    /// The nearest interactable <see cref="Selectable"/> lying in <paramref name="dir"/> from this one.
    /// Candidates are ranked by how well their offset lines up with the direction relative to distance,
    /// so a widget straight ahead beats a nearer one off to the side.
    /// </summary>
    public Selectable? FindSelectable(Float2 dir)
    {
        Scene? scene = GameObject.Scene;
        if (scene is null) return null;
        if (!TryWorldCenter(this, out Float3 origin, out Float4x4 model)) return null;

        // Map the design-space direction through the canvas so a rotated (or world-space) canvas
        // navigates along its own axes rather than the world's.
        Float3 dirWorld = Float4x4.TransformPoint(new Float3(dir.X, dir.Y, 0f), model)
                        - Float4x4.TransformPoint(Float3.Zero, model);
        float dirLength = Float3.Length(dirWorld);
        if (dirLength < 1e-6f) return null;
        dirWorld /= dirLength;

        Selectable? best = null;
        float bestScore = float.NegativeInfinity;
        Selectable? wrap = null;
        float wrapScore = float.NegativeInfinity;

        foreach (GameObject go in scene.ActiveObjects)
        {
            foreach (Selectable candidate in go.GetComponents<Selectable>())
            {
                if (ReferenceEquals(candidate, this) || !candidate.EnabledInHierarchy) continue;
                if (!candidate.IsInteractable() || candidate._navigation.Mode == NavigationMode.None) continue;
                if (!TryWorldCenter(candidate, out Float3 center, out _)) continue;

                Float3 offset = center - origin;
                float distance = Float3.Length(offset);
                if (distance < 1e-4f) continue;

                float alignment = Float3.Dot(offset / distance, dirWorld);
                if (alignment > 0.1f)
                {
                    float score = alignment / distance;
                    if (score > bestScore) { bestScore = score; best = candidate; }
                }
                else if (_navigation.WrapAround)
                {
                    // Furthest widget in the opposite direction, so a move off one end lands on the other.
                    float score = -alignment * distance;
                    if (score > wrapScore) { wrapScore = score; wrap = candidate; }
                }
            }
        }

        return best.IsValid() ? best : wrap;
    }

    private static bool TryWorldCenter(Selectable s, out Float3 center, out Float4x4 model)
    {
        center = default;
        model = Float4x4.Identity;

        RectTransform? rt = s.GameObject.RectTransform;
        if (rt is null) return false;

        GameCanvas? canvas = s.GetCanvas();
        if (canvas.IsNotValid()) return false;

        model = canvas.CanvasToWorld * canvas.BuildRectModel(rt);
        Rect local = rt.Rect;
        Float2 localCenter = (local.Min + local.Max) * 0.5f;
        center = Float4x4.TransformPoint(new Float3(localCenter.X, localCenter.Y, 0f), model);
        return true;
    }
}
