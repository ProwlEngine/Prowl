using System;

using Prowl.Runtime;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Listens for the next key/mouse/gamepad input and reports it as an InputBinding.
/// Used by the Input Action Map editor for "Press any key" binding.
/// </summary>
public static class InputBindingListener
{
    private static bool _listening;
    private static Action<InputBinding>? _callback;

    public static bool IsListening => _listening;

    /// <summary>Start listening for the next input. Calls callback with the detected binding.</summary>
    public static void Start(Action<InputBinding> onDetected)
    {
        _listening = true;
        _callback = onDetected;
    }

    public static void Cancel()
    {
        _listening = false;
        _callback = null;
    }

    /// <summary>Call each frame while listening. Returns true if input was captured.</summary>
    public static bool Update()
    {
        if (!_listening) return false;

        // Escape cancels
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cancel();
            return true;
        }

        // Check keyboard keys
        foreach (KeyCode key in Enum.GetValues<KeyCode>())
        {
            if (key == KeyCode.Unknown || key == KeyCode.Escape) continue;
            if (Input.GetKeyDown(key))
            {
                _callback?.Invoke(InputBinding.CreateKeyBinding(key));
                _listening = false;
                _callback = null;
                return true;
            }
        }

        // Check mouse buttons (API uses int)
        for (int mb = 0; mb < 5; mb++)
        {
            if (Input.GetMouseButtonDown(mb))
            {
                _callback?.Invoke(InputBinding.CreateMouseButtonBinding((MouseButton)mb));
                _listening = false;
                _callback = null;
                return true;
            }
        }

        // Check gamepad buttons
        for (int device = 0; device < Input.GetGamepadCount(); device++)
        {
            if (!Input.IsGamepadConnected(device)) continue;
            foreach (GamepadButton gb in Enum.GetValues<GamepadButton>())
            {
                if (gb == GamepadButton.Unknown) continue;
                if (Input.GetGamepadButtonDown(gb, device))
                {
                    _callback?.Invoke(InputBinding.CreateGamepadButtonBinding(gb, device));
                    _listening = false;
                    _callback = null;
                    return true;
                }
            }
        }

        return false;
    }
}
