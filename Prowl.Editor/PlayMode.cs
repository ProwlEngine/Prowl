using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor;

public static class PlayMode {
    public enum Mode { Editing, Playing, Paused, }

    public static Mode Current { get; private set; }

    public static void Start() {

        SceneManager.StoreScene();

        Current = Mode.Playing;
        SceneManager.Clear();
        MonoBehaviour.PauseLogic = false;
        SceneManager.RestoreScene(); // Resets GameObjects and Components to re-trigger things like Awake() and Start()

        ImGuiNotify.InsertNotification(new ImGuiToast()
        {
            Title = "Entering Playmode!"
        });
    }
    
    public static void Pause() {
        Current = Mode.Paused;
        MonoBehaviour.PauseLogic = true;

        ImGuiNotify.InsertNotification(new ImGuiToast()
        {
            Title = "Playmode Paused!"
        });
    }
    
    public static void Resume() {
        Current = Mode.Playing;
        MonoBehaviour.PauseLogic = false;

        ImGuiNotify.InsertNotification(new ImGuiToast()
        {
            Title = "Playmode Resumed!"
        });
    }
    
    public static void Stop() {
        Current = Mode.Editing;
        SceneManager.Clear();
        MonoBehaviour.PauseLogic = true;

        SceneManager.RestoreScene();
        SceneManager.ClearStoredScene();

        ImGuiNotify.InsertNotification(new ImGuiToast()
        {
            Title = "Playmode Stopped, Scene Reloaded!"
        });
    }
}
