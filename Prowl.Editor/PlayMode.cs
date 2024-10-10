// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor;

public static class PlayMode
{
    public enum Mode { Editing, Playing, Paused, }

    public static Mode Current { get; private set; }


    public static TimeData GameTime = new();

    public static void Start()
    {

        SceneManager.StoreScene();

        Current = Mode.Playing;
        Application.IsPlaying = true; // This needs to be set immediately before the scene is loaded for Prefabs to be instantiated correctly
        SceneManager.Clear();
        SceneManager.RestoreScene(); // Resets GameObjects and Components to re-trigger things like Awake() and Start()

        GameTime = new();

        // Focus GameWindow
        if (GameWindow.LastFocused != null && GameWindow.LastFocused.IsAlive)
        {
            if (GeneralPreferences.Instance.AutoFocusGameView)
                EditorGuiManager.FocusWindow(GameWindow.LastFocused.Target as EditorWindow);
        }
    }

    public static void Pause()
    {
        Current = Mode.Paused;
    }

    public static void Resume()
    {
        Current = Mode.Playing;
    }

    public static void Stop()
    {
        Current = Mode.Editing;
        SceneManager.Clear();

        Application.IsPlaying = false; // This needs to be set immediately before the scene is loaded for Prefabs to be instantiated correctly

        SceneManager.RestoreScene();
        SceneManager.ClearStoredScene();
    }
}
