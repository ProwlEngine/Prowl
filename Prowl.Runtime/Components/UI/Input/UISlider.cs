// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// A draggable value slider. Extends <see cref="Selectable"/> so it gets the hover/press tint, and
/// drives an optional <see cref="FillRect"/> (a bar that grows with the value) and <see cref="HandleRect"/>
/// (the knob) by moving their anchors along the slider's axis. Clicking or dragging anywhere on the
/// track sets the value from the pointer position.
/// </summary>
[AddComponentMenu("UI/Slider")]
[ComponentIcon("")] // sliders
public class UISlider : Selectable, IDragHandler, IBeginDragHandler
{
    public enum SliderDirection { LeftToRight, RightToLeft, BottomToTop, TopToBottom }

    [SerializeField] private RectTransform? _fillRect;
    /// <summary>Optional fill bar; its anchors are stretched to the current fraction of the track.</summary>
    public RectTransform? FillRect { get => _fillRect; set { _fillRect = value; UpdateVisuals(); } }

    [SerializeField] private RectTransform? _handleRect;
    /// <summary>Optional knob; its anchors are moved to the current fraction of the track.</summary>
    public RectTransform? HandleRect { get => _handleRect; set { _handleRect = value; UpdateVisuals(); } }

    [SerializeField] private SliderDirection _direction = SliderDirection.LeftToRight;
    public SliderDirection Direction { get => _direction; set { _direction = value; UpdateVisuals(); } }

    [SerializeField] private float _minValue = 0f;
    public float MinValue { get => _minValue; set { _minValue = value; SetValue(_value, false); } }

    [SerializeField] private float _maxValue = 1f;
    public float MaxValue { get => _maxValue; set { _maxValue = value; SetValue(_value, false); } }

    [SerializeField] private bool _wholeNumbers = false;
    /// <summary>Constrain the value to integers.</summary>
    public bool WholeNumbers { get => _wholeNumbers; set { _wholeNumbers = value; SetValue(_value, false); } }

    [SerializeField] private float _value = 0f;
    /// <summary>Current value, clamped to [Min, Max] (and rounded when <see cref="WholeNumbers"/>).</summary>
    public float Value { get => _value; set => SetValue(value, notify: true); }

    /// <summary>Value as a 0..1 fraction of the [Min, Max] range.</summary>
    public float NormalizedValue
    {
        get
        {
            float span = _maxValue - _minValue;
            if (Maths.Abs(span) < 1e-6f) return 0f;
            return Maths.Clamp((_value - _minValue) / span, 0f, 1f);
        }
        set => SetValue(_minValue + (_maxValue - _minValue) * Maths.Clamp(value, 0f, 1f), notify: true);
    }

    /// <summary>Code-side value-changed callback (new value).</summary>
    public event Action<float>? OnValueChanged;

    /// <summary>Inspector-configured "On Value Changed ()" calls.</summary>
    [SerializeField] private ProwlAction _onValueChanged = new();
    public ProwlAction ValueChangedAction => _onValueChanged;

    private void SetValue(float input, bool notify)
    {
        float lo = Maths.Min(_minValue, _maxValue);
        float hi = Maths.Max(_minValue, _maxValue);
        float v = Maths.Clamp(input, lo, hi);
        if (_wholeNumbers) v = Maths.Round(v);

        bool changed = v != _value;
        _value = v;
        UpdateVisuals();

        if (changed && notify)
        {
            try { OnValueChanged?.Invoke(_value); }
            catch (Exception ex) { Debug.LogError($"[UISlider] OnValueChanged on '{Name}' threw: {ex.Message}\n{ex.StackTrace}"); }
            _onValueChanged.Invoke();
        }
    }

    public override void OnEnable()
    {
        base.OnEnable();
        UpdateVisuals();
    }

    // Selectable handles the press tint; we additionally set the value from the click position.
    public override void OnPointerDown(PointerEventData e)
    {
        base.OnPointerDown(e);
        if (e.Button != MouseButton.Left || !IsInteractable()) return;
        UpdateValueFromPointer(e);
        e.Use();
    }

    public void OnBeginDrag(PointerEventData e) { /* value tracks in OnDrag */ }

    public void OnDrag(PointerEventData e)
    {
        if (e.Button != MouseButton.Left || !IsInteractable()) return;
        UpdateValueFromPointer(e);
        e.Use();
    }

    private void UpdateValueFromPointer(PointerEventData e)
    {
        Rect rect = GameObject.RectTransform!.ComputedRect; // design space, +Y up, origin bottom-left
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f) return;

        Float2 p = e.DesignPosition;
        float n = _direction switch
        {
            SliderDirection.LeftToRight => (p.X - rect.Min.X) / rect.Size.X,
            SliderDirection.RightToLeft => 1f - (p.X - rect.Min.X) / rect.Size.X,
            SliderDirection.BottomToTop => (p.Y - rect.Min.Y) / rect.Size.Y,
            SliderDirection.TopToBottom => 1f - (p.Y - rect.Min.Y) / rect.Size.Y,
            _ => 0f,
        };
        NormalizedValue = n;
    }

    private void UpdateVisuals()
    {
        float n = NormalizedValue;
        bool horizontal = _direction is SliderDirection.LeftToRight or SliderDirection.RightToLeft;
        bool reversed = _direction is SliderDirection.RightToLeft or SliderDirection.TopToBottom;

        if (_fillRect != null)
        {
            // Grow the fill from the low edge to the current fraction via anchors (padding size = 0).
            Float2 aMin = Float2.Zero, aMax = Float2.One;
            if (horizontal)
            {
                if (reversed) aMin = new Float2(1f - n, 0f);
                else aMax = new Float2(n, 1f);
            }
            else
            {
                if (reversed) aMin = new Float2(0f, 1f - n);
                else aMax = new Float2(1f, n);
            }
            _fillRect.AnchorMin = aMin;
            _fillRect.AnchorMax = aMax;
            _fillRect.SizeDelta = Float2.Zero;
            _fillRect.AnchoredPosition = Float2.Zero;
        }

        if (_handleRect != null)
        {
            // Center the knob at the current fraction; it keeps its own SizeDelta on the moving axis.
            float hn = reversed ? 1f - n : n;
            if (horizontal)
            {
                _handleRect.AnchorMin = new Float2(hn, 0f);
                _handleRect.AnchorMax = new Float2(hn, 1f);
            }
            else
            {
                _handleRect.AnchorMin = new Float2(0f, hn);
                _handleRect.AnchorMax = new Float2(1f, hn);
            }
            _handleRect.AnchoredPosition = Float2.Zero;
        }
    }
}
