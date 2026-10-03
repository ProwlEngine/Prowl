// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

using Xunit;

namespace Prowl.Runtime.Test;

public class InputActionTests
{
    private sealed class HeldKeys : NullInputHandler, IInputHandler
    {
        public readonly HashSet<KeyCode> Held = new();
        bool IInputHandler.GetKey(KeyCode key) => Held.Contains(key);
    }

    [Fact]
    public void Tap_StillFiresAfterAPressTooLongToBeATap()
    {
        var input = new HeldKeys();
        var action = new InputAction("Hop");
        action.AddBinding(KeyCode.Space, InputInteractionType.Tap);
        action.Enable();
        int performed = 0;
        action.Performed += _ => performed++;

        float time = 0f;
        void Frame(bool held, float seconds)
        {
            if (held) input.Held.Add(KeyCode.Space);
            else input.Held.Remove(KeyCode.Space);
            time += seconds;
            action.UpdateState(input, time);
        }

        // Held for a second, far past the tap window, so it is not a tap.
        Frame(true, 0.016f);
        Frame(true, 1f);
        Frame(false, 0.016f);
        Assert.Equal(0, performed);

        // A quick press afterwards is.
        Frame(true, 0.5f);
        Frame(true, 0.05f);
        Frame(false, 0.05f);
        Assert.Equal(1, performed);
    }
}
