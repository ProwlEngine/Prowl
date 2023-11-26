using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Newtonsoft.Json;

namespace Prowl.Editor;

public static class PlayMode {
    public enum Mode { Editing, Playing, Paused, }

    public static Mode Current { get; private set; }
    private static string PreviousScene;

    public static void Start() {

        // Serialize the Scene manually to save its state
        var s = new Scene();
        // exclude objects with the DontSave hideFlag
        s.GameObjects = GameObjectManager.AllGameObjects.Where(x => !x.hideFlags.HasFlag(HideFlags.DontSave) && !x.hideFlags.HasFlag(HideFlags.HideAndDontSave)).ToArray();
        PreviousScene = JsonUtility.Serialize(s);

        Current = Mode.Playing;
        MonoBehaviour.PauseLogic = false;

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
        MonoBehaviour.PauseLogic = true;

        var s = JsonUtility.Deserialize<Scene>(PreviousScene);
        //GameObjectManager.LoadScene(s, false);

        // TODO: come up with a clean way to maintain the selected object
        Selection.Clear();

        ImGuiNotify.InsertNotification(new ImGuiToast()
        {
            Title = "Playmode Stopped, Scene Reloaded!"
        });
    }
}
