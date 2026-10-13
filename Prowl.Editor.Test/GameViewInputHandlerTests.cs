// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.GUI;
using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The key that starts or resumes play is still in the cached input when gameplay next updates, so the
/// play-mode input handler hides it until it's released. These drive the handler with a fake keyboard.
/// </summary>
public class GameViewInputHandlerTests
{
    private sealed class KeyboardInput : NullInputHandler, IInputHandler
    {
        public readonly HashSet<KeyCode> Held = [];
        public readonly HashSet<KeyCode> Pressed = [];
        public readonly HashSet<KeyCode> Released = [];
        bool IInputHandler.GetKey(KeyCode key) => Held.Contains(key);
        bool IInputHandler.GetKeyDown(KeyCode key) => Pressed.Contains(key);
        bool IInputHandler.GetKeyUp(KeyCode key) => Released.Contains(key);
        bool IInputHandler.IsAnyKeyDown => Held.Count > 0;
    }

    [Theory]
    [InlineData(KeyCode.F5)]
    [InlineData(KeyCode.F7)]
    [InlineData(KeyCode.Space)] // Rebound shortcut.
    public void Shortcut_IsHiddenUntilRelease_ForDirectInputAndActions(KeyCode key)
    {
        bool previousFocus = GameViewInputHandler.IsGameViewFocused;
        var real = new KeyboardInput();
        var map = new InputActionMap("Shortcut regression");
        InputAction action = map.AddAction("Jump").AddBinding(key);
        map.Enable();

        // Entering play suppresses the key before the play-mode wrapper exists.
        real.Held.Add(key);
        real.Pressed.Add(key);
        GameViewInputHandler.SuppressKeyUntilRelease(key);
        var game = new GameViewInputHandler(real);
        Input.PushHandler(game);
        Input.RegisterActionMap(map);
        try
        {
            GameViewInputHandler.IsGameViewFocused = true;

            // Next frame: the same press is still in the cached input.
            Input.UpdateActions(0.016f);
            Assert.False(action.IsPressed());
            Assert.False(game.GetKey(key));
            Assert.False(game.GetKeyDown(key));
            Assert.False(game.IsAnyKeyDown);

            // Other keys are unaffected.
            real.Held.Add(KeyCode.A);
            Assert.True(game.GetKey(KeyCode.A));
            Assert.True(game.IsAnyKeyDown);
            real.Held.Remove(KeyCode.A);

            // Holding keeps it hidden.
            real.Pressed.Clear();
            game.UpdateSuppressedKeys();
            Input.UpdateActions(0.016f);
            Assert.False(action.IsPressed());

            // The release edge is hidden too.
            real.Held.Clear();
            real.Released.Add(key);
            game.UpdateSuppressedKeys();
            Assert.False(game.GetKeyUp(key));

            // Once released, a fresh press works normally.
            real.Released.Clear();
            game.UpdateSuppressedKeys();
            real.Held.Add(key);
            real.Pressed.Add(key);
            Input.UpdateActions(0.016f);
            Assert.True(action.IsPressed());
            Assert.True(game.GetKeyDown(key));
        }
        finally
        {
            Input.PopHandler();
            Input.UnregisterActionMap(map);
            real.Held.Clear();
            real.Pressed.Clear();
            real.Released.Clear();
            game.UpdateSuppressedKeys();
            GameViewInputHandler.IsGameViewFocused = previousFocus;
        }
    }

    [Fact]
    public void LeavingPlay_ForgetsSuppressedKeys()
    {
        var real = new KeyboardInput();
        real.Held.Add(KeyCode.F5);
        GameViewInputHandler.SuppressKeyUntilRelease(KeyCode.F5);

        GameViewInputHandler.ClearSuppressedKeys();

        // Still held, but a new session must not inherit the old suppression.
        Assert.True(new GameViewInputHandler(real).GetKey(KeyCode.F5));
    }
}
