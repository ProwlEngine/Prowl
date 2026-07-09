// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;

namespace Prowl.Runtime.UI;

[AddComponentMenu("UI/Button")]
[ComponentIcon("")] // HandPointer
public class UIButton : Selectable, IPointerClickHandler, ISubmitHandler
{
    /// <summary>Code-side click callback (subscribe from scripts).</summary>
    public event Action? OnClick;

    /// <summary>Inspector-configured click calls (the "On Click ()" list). Fires alongside <see cref="OnClick"/>.
    /// This is how a button plays a sound - point a call at an <c>AudioSource.Play()</c> - now that the
    /// built-in SFX system is gone.</summary>
    [SerializeField] private ProwlAction _onClick = new();
    public ProwlAction ClickAction => _onClick;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.Button != MouseButton.Left) return;
        if (!IsInteractable()) return;
        HandleClick();
    }

    public void OnSubmit()
    {
        if (!IsInteractable()) return;
        HandleClick();
    }

    protected virtual void HandleClick()
    {
        _onClick.Invoke();

        try { OnClick?.Invoke(); }
        catch (Exception ex)
        {
            Debug.LogError($"[UIButton] OnClick handler on '{Name}' threw: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
