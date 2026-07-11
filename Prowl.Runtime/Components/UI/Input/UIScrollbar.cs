// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// A draggable scrollbar. Extends <see cref="Selectable"/> for the hover/press tint and drives a
/// <see cref="HandleRect"/> whose size reflects <see cref="Size"/> (the visible fraction of the content)
/// and whose position reflects <see cref="Value"/> (0..1 along <see cref="Direction"/>). Clicking or
/// dragging the track moves the handle. Usually wired to a <see cref="UIScrollRect"/>, which keeps the
/// value and size in sync with its content.
/// </summary>
[AddComponentMenu("UI/Scrollbar")]
[ComponentIcon("")] // GripLines
public class UIScrollbar : Selectable, IDragHandler, IBeginDragHandler
{
    public enum ScrollbarDirection { LeftToRight, RightToLeft, BottomToTop, TopToBottom }

    [SerializeField] private RectTransform? _handleRect;
    /// <summary>The moving handle; its anchors are driven from <see cref="Value"/> and <see cref="Size"/>.</summary>
    public RectTransform? HandleRect { get => _handleRect; set { _handleRect = value; UpdateVisuals(); } }

    [SerializeField] private ScrollbarDirection _direction = ScrollbarDirection.LeftToRight;
    public ScrollbarDirection Direction { get => _direction; set { _direction = value; UpdateVisuals(); } }

    [SerializeField] private float _value = 0f;
    /// <summary>Handle position as a 0..1 fraction along the track.</summary>
    public float Value { get => _value; set => SetValue(value, notify: true); }

    [SerializeField] private float _size = 0.2f;
    /// <summary>Handle length as a 0..1 fraction of the track (the visible portion of the content).</summary>
    public float Size
    {
        get => _size;
        set { _size = Maths.Clamp(value, 0.02f, 1f); UpdateVisuals(); }
    }

    /// <summary>Fires when the value changes from a drag or track click.</summary>
    public event Action<float>? OnValueChanged;

    [SerializeField] private ProwlAction _onValueChanged = new();
    public ProwlAction ValueChangedAction => _onValueChanged;

    private void SetValue(float input, bool notify)
    {
        float v = Maths.Clamp(input, 0f, 1f);
        bool changed = v != _value;
        _value = v;
        UpdateVisuals();

        if (changed && notify)
        {
            try { OnValueChanged?.Invoke(_value); }
            catch (Exception ex) { Debug.LogError($"[UIScrollbar] OnValueChanged on '{Name}' threw: {ex.Message}\n{ex.StackTrace}"); }
            _onValueChanged.Invoke();
        }
    }

    /// <summary>Sets the value without firing <see cref="OnValueChanged"/> (used by the owning scroll rect).</summary>
    public void SetValueWithoutNotify(float value)
    {
        _value = Maths.Clamp(value, 0f, 1f);
        UpdateVisuals();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        UpdateVisuals();
    }

    public override void OnPointerDown(PointerEventData e)
    {
        base.OnPointerDown(e);
        if (e.Button != MouseButton.Left || !IsInteractable()) return;
        SetValue(ValueFromPointer(e), notify: true);
        e.Use();
    }

    public void OnBeginDrag(PointerEventData e) { /* value tracks in OnDrag */ }

    public void OnDrag(PointerEventData e)
    {
        if (e.Button != MouseButton.Left || !IsInteractable()) return;
        SetValue(ValueFromPointer(e), notify: true);
        e.Use();
    }

    private float ValueFromPointer(PointerEventData e)
    {
        Rect rect = GameObject.RectTransform!.ComputedRect; // design space, +Y up
        bool horizontal = _direction is ScrollbarDirection.LeftToRight or ScrollbarDirection.RightToLeft;
        Float2 p = e.DesignPosition;

        float raw = horizontal
            ? (rect.Size.X > 0f ? (p.X - rect.Min.X) / rect.Size.X : 0f)
            : (rect.Size.Y > 0f ? (p.Y - rect.Min.Y) / rect.Size.Y : 0f);
        raw = Maths.Clamp(raw, 0f, 1f);

        // Fraction measured from the value=0 end of the track.
        float nDir = _direction switch
        {
            ScrollbarDirection.LeftToRight => raw,
            ScrollbarDirection.RightToLeft => 1f - raw,
            ScrollbarDirection.BottomToTop => raw,
            ScrollbarDirection.TopToBottom => 1f - raw,
            _ => raw,
        };

        // Map so the handle CENTER lands under the pointer over the movable range.
        float s = Maths.Clamp(_size, 0.02f, 1f);
        float span = 1f - s;
        return span > 1e-4f ? Maths.Clamp((nDir - s * 0.5f) / span, 0f, 1f) : 0f;
    }

    private void UpdateVisuals()
    {
        if (_handleRect == null) return;

        float s = Maths.Clamp(_size, 0.02f, 1f);
        float span = 1f - s;
        float v = Maths.Clamp(_value, 0f, 1f);
        bool horizontal = _direction is ScrollbarDirection.LeftToRight or ScrollbarDirection.RightToLeft;

        float aMin, aMax;
        switch (_direction)
        {
            case ScrollbarDirection.RightToLeft:
                aMin = span * (1f - v); aMax = aMin + s; break;
            case ScrollbarDirection.TopToBottom:
                aMax = 1f - v * span; aMin = aMax - s; break;
            default: // LeftToRight, BottomToTop
                aMin = v * span; aMax = aMin + s; break;
        }

        if (horizontal)
        {
            _handleRect.AnchorMin = new Float2(aMin, 0f);
            _handleRect.AnchorMax = new Float2(aMax, 1f);
        }
        else
        {
            _handleRect.AnchorMin = new Float2(0f, aMin);
            _handleRect.AnchorMax = new Float2(1f, aMax);
        }
        _handleRect.SizeDelta = Float2.Zero;
        _handleRect.AnchoredPosition = Float2.Zero;
    }
}
