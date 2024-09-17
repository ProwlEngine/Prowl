// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime.SceneManagement;

public static class SceneManager
{
    private static readonly List<GameObject> _gameObjects = new();
    internal static readonly HashSet<int> _dontDestroyOnLoad = new();

    public static Scene MainScene { get; private set; } = new();

    public static List<GameObject> AllGameObjects => _gameObjects;

    public static bool AllowGameObjectConstruction = true;

    public static event Action PreFixedUpdate;
    public static event Action PostFixedUpdate;

    private static SerializedProperty? StoredScene;
    private static Guid StoredSceneID;

    public static void Initialize()
    {
        GameObject.Internal_Constructed += OnGameObjectConstructed;
        GameObject.Internal_DestroyCommitted += OnGameObjectDestroyCommitted;
    }

    public static void StoreScene()
    {
        Debug.If(StoredScene != null, "Scene is already stored.");
        // Serialize the Scene manually to save its state
        // exclude objects with the DontSave hideFlag
        GameObject[] GameObjects = AllGameObjects.Where(x => !x.hideFlags.HasFlag(HideFlags.DontSave) && !x.hideFlags.HasFlag(HideFlags.HideAndDontSave)).ToArray();
        StoredScene = Serializer.Serialize(GameObjects);
        StoredSceneID = MainScene.AssetID;
    }

    public static void RestoreScene()
    {
        Debug.IfNull(StoredScene, "Scene is not stored.");
        Clear();
        var deserialized = Serializer.Deserialize<GameObject[]>(StoredScene);
        MainScene.AssetID = StoredSceneID;
    }

    public static void ClearStoredScene()
    {
        Debug.IfNull(StoredScene, "Scene is not stored.");
        StoredScene = null;
    }

    public static void InstantiateNewScene()
    {
        var go = new GameObject("Directional Light");
        go.AddComponent<DirectionalLight>(); // Will auto add Transform as DirectionLight requires it
        go.Transform.localEulerAngles = new System.Numerics.Vector3(130, 45, 0);

        var cam = new GameObject("Main Camera");
        cam.tag = "Main Camera";
        cam.Transform.position = new(0, 0, -10);
        var camComp = cam.AddComponent<Camera>();
        camComp.DrawOrder = -1;
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

        go.parent?.children.Remove(go);
    }

    [OnAssemblyUnload]
    public static void Clear()
    {
        List<GameObject> toRemove = new List<GameObject>();
        for (int i = 0; i < _gameObjects.Count; i++)
            if (!_dontDestroyOnLoad.Contains(_gameObjects[i].InstanceID))
            {
                _gameObjects[i].Destroy();
                toRemove.Add(_gameObjects[i]);
            }

        EngineObject.HandleDestroyed();

        for (int i = 0; i < toRemove.Count; i++)
            _gameObjects.Remove(toRemove[i]);

        Physics.Dispose();
        Physics.Initialize();
        MainScene = new();
    }

    public static void Update()
    {
        EngineObject.HandleDestroyed();

        for (int i = 0; i < _gameObjects.Count; i++)
            if (_gameObjects[i].enabledInHierarchy)
                _gameObjects[i].PreUpdate();

        if (Application.IsPlaying)
            Physics.Update();

        ForeachComponent((x) =>
        {

            x.Do(x.UpdateCoroutines);
            x.Do(x.Update);
        });

        ForeachComponent((x) => x.Do(x.LateUpdate));
        ForeachComponent((x) => x.Do(x.UpdateEndOfFrameCoroutines));
    }

    public static void ForeachComponent(Action<MonoBehaviour> action)
    {
        for (int i = 0; i < _gameObjects.Count; i++)
            if (_gameObjects[i].enabledInHierarchy)
                foreach (var comp in _gameObjects[i].GetComponents<MonoBehaviour>())
                    if (comp.EnabledInHierarchy)
                        action.Invoke(comp);
    }

    public static void PhysicsUpdate()
    {
        PreFixedUpdate?.Invoke();
        ForeachComponent((x) =>
        {
            x.Do(x.UpdateFixedUpdateCoroutines);
            x.Do(x.FixedUpdate);
        });
        PostFixedUpdate?.Invoke();
    }

    public static bool Draw(RenderTexture? target = null)
    {
        var Cameras = AllGameObjects.SelectMany(x => x.GetComponentsInChildren<Camera>()).ToList();

        Cameras.Sort((a, b) => a.DrawOrder.CompareTo(b.DrawOrder));

        if (Cameras.Count == 0)
            return false;

        //foreach (Camera? cam in Cameras)
        //{
        //    Veldrid.Framebuffer t = cam.Target.Res ?? target ?? Graphics.ScreenTarget;
        //
        //    uint width = t.Width;
        //    uint height = t.Height;
        //
        //    Camera.CameraData data = cam.GetData(new Vector2(width, height));
        //
        //    cam.Pipeline.Res.Render(t, data);
        //}

        return true;
    }

    public static void LoadScene(Scene scene)
    {
        Clear();
        MainScene = scene;
        MainScene.InstantiateScene();
        ForeachComponent((x) => x.Do(x.OnLevelWasLoaded));
    }

    public static void LoadScene(AssetRef<Scene> scene)
    {
        if (scene.IsAvailable == false) throw new Exception("Scene is not available.");
        Clear();
        MainScene = scene.Res;
        MainScene.InstantiateScene();
        ForeachComponent((x) => x.Do(x.OnLevelWasLoaded));
    }

    /// <summary>
    /// Search all GameObjects in the scene for the specified one recursively
    /// </summary>
    public static bool Has(GameObject original)
    {
        foreach (var go in _gameObjects)
            if (Has(go, original.InstanceID))
                return true;
        return false;
    }

    static bool Has(GameObject curr, int instanceID)
    {
        if (curr.InstanceID == instanceID)
            return true;
        foreach (var child in curr.children)
            if (Has(child, instanceID))
                return true;
        return false;
    }
}

