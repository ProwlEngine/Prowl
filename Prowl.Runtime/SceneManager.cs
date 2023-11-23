using Prowl.Runtime.Components;
using Prowl.Runtime.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.SceneManagement;

public static class SceneManager
{
    private static readonly List<GameObject> _gameObjects = new();
    internal static HashSet<int> _dontDestroyOnLoad = new();

    public static List<GameObject> AllGameObjects => _gameObjects;

    public static string CurrentHierarchy = null;

    public static bool AllowGameObjectConstruction = true;

    public static void Initialize()
    {
        GameObject.Internal_Constructed += OnGameObjectConstructed;
        GameObject.Internal_DestroyCommitted += OnGameObjectDestroyCommitted;
    }

    private static void OnGameObjectConstructed(GameObject go)
    {
        if (!AllowGameObjectConstruction) return;
        lock (_gameObjects)
            _gameObjects.Add(go);
    }

    private static void OnGameObjectDestroyCommitted(GameObject go)
    {
        lock (_gameObjects)
            _gameObjects.Remove(go);
    }

    public static void Clear() {
        CurrentHierarchy = null;
    }

    public static void Update()
    {
        EngineObject.HandleDestroyed();

        foreach (var go in _gameObjects)
            foreach (var comp in go.GetComponents<MonoBehaviour>())
            {
                if (!comp.HasStarted)
                {
                    try
                    {
                        comp.HasStarted = true;
#warning TODO: Awake should be called immediately after the creation of the component/gameobject not in the first frame
                        comp.Internal_Awake();
                        comp.Internal_Start();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error in {comp.GetType().Name}.Start of {go.Name}: {e.Message} \n StackTrace: {e.StackTrace}");
                    }
                }

                try
                {
                    comp.UpdateCoroutines();
                    comp.Internal_Update();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in {comp.GetType().Name}.Update of {go.Name}: {e.Message} \n StackTrace: {e.StackTrace}");
                }
            }

        foreach (var go in _gameObjects)
            foreach (var comp in go.GetComponents<MonoBehaviour>())
            {
                try
                {
                    comp.Internal_LateUpdate();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in {comp.GetType().Name}.LateUpdate of {go.Name}: {e.Message} \n StackTrace: {e.StackTrace}");
                }
            }

        foreach (var go in _gameObjects)
            foreach (var comp in go.GetComponents<MonoBehaviour>())
            {
                try
                {
                    comp.UpdateEndOfFrameCoroutines();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in {comp.GetType().Name}.UpdateEndOfFrameCoroutines of {go.Name}: {e.Message} \n StackTrace: {e.StackTrace}");
                }
            }
    }

    public static void PhysicsUpdate() {
        foreach (var go in _gameObjects)
            foreach (var comp in go.GetComponents<MonoBehaviour>())
            {
                try
                {
                    comp.Internal_FixedUpdate();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in {comp.GetType().Name}.FixedUpdate of {go.Name}: {e.Message} \n StackTrace: {e.StackTrace}");
                }
            }
    }

    public static void Draw() {
        List<Camera>  Cameras = MonoBehaviour.FindObjectsOfType<Camera>().ToList();
        Cameras.Sort((a, b) => a.RenderOrder.CompareTo(b.DrawOrder));
        foreach (var cam in Cameras) cam.Render();
    }

    public static void LoadScene(string sceneName, bool additive = false)
    {
#warning TODO: Scenes are special assets that can be loaded via name, they should be stored and loaded in level files like unity in standalone, and we need a Build Pipeline to assign scenes

        //LoadScene(Application.AssetProvider.LoadAsset<Scene>(sceneName), additive);
    }

    public static void LoadScene(Scene scene, bool additive = false)
    {
        if (additive) throw new NotImplementedException();
        if (scene == null || scene.IsDestroyed) return;
        if (!additive)
        {
            foreach (var go in _gameObjects)
                if (!_dontDestroyOnLoad.Contains(go.InstanceID))
                    go.Destroy();
            EngineObject.HandleDestroyed();
        }
        var added = scene.Instantiate();
        foreach (var go in added)
            foreach (var comp in go.GetComponents<MonoBehaviour>())
                try
                {
                    comp.Internal_OnLevelWasLoaded();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in {comp.GetType().Name}.OnLevelWasLoaded of {go.Name}: {e.Message} \n StackTrace: {e.StackTrace}");
                }
    }
    
}

