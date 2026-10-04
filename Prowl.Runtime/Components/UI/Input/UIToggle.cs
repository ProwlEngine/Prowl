// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;

namespace Prowl.Runtime.UI;

/// <summary>
/// An on and off switch, such as a checkbox. Clicking or submitting flips <see cref="IsOn"/>, and the optional
/// <see cref="Checkmark"/> graphic is shown only while it is on.
/// </summary>
[AddComponentMenu("UI/Toggle")]
[ComponentIcon("\uf14a")] // SquareCheck
public class UIToggle : Selectable, IPointerClickHandler, ISubmitHandler
{
    [SerializeField] private bool _isOn;
    /// <summary>Whether the toggle is on. Setting it fires <see cref="OnValueChanged"/> when the value changes.</summary>
    public bool IsOn { get => _isOn; set => SetIsOn(value, notify: true); }

    [SerializeField] private Graphic? _checkmark;
    /// <summary>Optional graphic shown while on and hidden while off.</summary>
    public Graphic? Checkmark { get => _checkmark; set { _checkmark = value; UpdateVisuals(); } }

    /// <summary>Code-side value-changed callback (new value).</summary>
    public event Action<bool>? OnValueChanged;

    [SerializeField] private ProwlAction _onValueChanged = new();
    public ProwlAction ValueChangedAction => _onValueChanged;

    /// <summary>Sets the value without firing <see cref="OnValueChanged"/> or the inspector calls.</summary>
    public void SetIsOnWithoutNotify(bool value) => SetIsOn(value, notify: false);

    public void OnPointerClick(PointerEventData e)
    {
        if (e.Button != MouseButton.Left) return;
        e.Use();
        if (!IsInteractable()) return;
        IsOn = !_isOn;
    }

    public void OnSubmit()
    {
        if (!IsInteractable()) return;
        IsOn = !_isOn;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        UpdateVisuals();
    }

    private void SetIsOn(bool value, bool notify)
    {
        bool changed = value != _isOn;
        _isOn = value;
        UpdateVisuals();

        if (changed && notify)
        {
            try { OnValueChanged?.Invoke(_isOn); }
            catch (Exception ex) { Debug.LogError($"[UIToggle] OnValueChanged on '{Name}' threw: {ex.Message}\n{ex.StackTrace}"); }
            _onValueChanged.Invoke();
        }
    }

    private void UpdateVisuals()
    {
        if (_checkmark.IsValid()) _checkmark.Enabled = _isOn;
    }
}
