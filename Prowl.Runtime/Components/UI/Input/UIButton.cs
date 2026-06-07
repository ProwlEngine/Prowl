// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;

namespace Prowl.Runtime.UI;

[AddComponentMenu("UI/Button")]
[ComponentIcon("")] // HandPointer
public class UIButton : Selectable, IPointerClickHandler, ISubmitHandler
{

    public event Action? OnClick;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.Button != MouseButton.Left) return;
        if (!Interactable)
        {
            // The denied SFX already fired on PointerDown in Selectable
            return;
        }
        HandleClick();
    }

    public void OnSubmit()
    {
        if (!Interactable) { PlaySound(UISound.Denied, DeniedClip); return; }
        HandleClick();
    }

    protected virtual void HandleClick()
    {
        PlaySound(UISound.Click, ClickClip);

        try { OnClick?.Invoke(); }
        catch (Exception ex)
        {
            Debug.LogError($"[UIButton] OnClick handler on '{Name}' threw: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
