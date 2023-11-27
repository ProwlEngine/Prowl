using Prowl.Runtime.Components;
using Prowl.Runtime.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.SceneManagement;

public static class GameObjectManager
{
    private static readonly List<GameObject> _gameObjects = new();
    internal static HashSet<int> _dontDestroyOnLoad = new();

    public static List<GameObject> AllGameObjects => _gameObjects;

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

        go.Parent?.Children.Remove(go);
    }

    public static void Clear()
    {
        foreach (var go in _gameObjects)
            if (!_dontDestroyOnLoad.Contains(go.InstanceID))
                go.Destroy();
        EngineObject.HandleDestroyed();
        _gameObjects.Clear();
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
        var Cameras = MonoBehaviour.FindObjectsOfType<Camera>().ToList();
        Cameras.Sort((a, b) => a.RenderOrder.CompareTo(b.DrawOrder));
        foreach (var cam in Cameras) 
            if(cam.EnabledInHierarchy)
                cam.Render();

        Prowl.Runtime.Draw.Clear();
    }
    
}

