using Prowl.Runtime.Utils;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime.Resources
{
    /// <summary>
    /// A Scene is a collection of GameObjects that can be loaded from a file and held in memory without initializing the GameObjects.
    /// Then on demand the GameObjects can be constructed and initialized.
    /// </summary>
    public sealed class Scene : EngineObject
    {
        #region Scene Asset Data
        public GameObject[] GameObjects;
        //public string[] GameObjects;


        public Scene()
        {

        }

        public GameObject[] Instantiate()
        {
            var copy = JsonUtility.Deserialize<Scene>(JsonUtility.Serialize(this));
            return copy.GameObjects;
            //var gos = new GameObject[GameObjects.Length];
            //for (int i = 0; i < GameObjects.Length; i++)
            //    gos[i] = JsonUtility.Deserialize<GameObject>(GameObjects[i])!;
            //return gos;
        }
        #endregion

        #region Active Scene Management

        //public static readonly List<GameObject> ActiveGameObjects = new();
        //internal static HashSet<int> _dontDestroyOnLoad = new();
        //
        //public static bool AllowGameObjectConstruction = true;
        //private static Scene? switchTo = null;
        //
        //public static Scene _currentScene = new Scene();
        //public static Scene CurrentScene
        //{
        //    get => _currentScene;
        //    private set
        //    {
        //        _currentScene = value;
        //
        //        foreach (var go in ActiveGameObjects)
        //            if (!_dontDestroyOnLoad.Contains(go.InstanceID))
        //                go.Destroy();
        //        EngineObject.HandleDestroyed();
        //
        //        value?.Instantiate();
        //
        //        foreach (var go in ActiveGameObjects)
        //            foreach (var comp in go.GetComponents<MonoBehaviour>())
        //                try
        //                {
        //                    comp.Internal_OnLevelWasLoaded();
        //                }
        //                catch (Exception e)
        //                {
        //                    Debug.LogError($"Error in {comp.GetType().Name}.OnLevelWasLoaded of {go.Name}: {e.Message} \n StackTrace: {e.StackTrace}");
        //                }
        //
        //    }
        //}
        //
        //public static void LoadScene(Scene scene, bool forceImmediately = false)
        //{
        //    if (forceImmediately)
        //    {
        //        CurrentScene = scene;
        //        switchTo = null;
        //    }
        //    else
        //    {
        //        switchTo = scene;
        //    }
        //}


        #endregion

    }
}
