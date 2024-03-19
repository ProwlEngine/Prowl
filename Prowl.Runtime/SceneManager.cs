using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.SceneManagement;

public static class SceneManager
{
    private static readonly List<GameObject> _gameObjects = new();
    internal static HashSet<int> _dontDestroyOnLoad = new();

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
        InstantiateNewScene();
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
        go.transform.localEulerAngles = new System.Numerics.Vector3(130, 45, 0);
        var alGo = new GameObject("Ambient Light");
        var al = alGo.AddComponent<AmbientLight>();
        al.skyIntensity = 0.4f;
        al.groundIntensity = 0.1f;

        var cam = new GameObject("Main Camera");
        cam.tag = "Main Camera";
        cam.transform.position = new(0, 0, -10);
        cam.AddComponent<Camera>();
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

    public static void Clear()
    {
        for (int i = 0; i < _gameObjects.Count; i++)
            if (!_dontDestroyOnLoad.Contains(_gameObjects[i].InstanceID))
                _gameObjects[i].Destroy();
        EngineObject.HandleDestroyed();
        _gameObjects.Clear();
        Physics.Dispose();
        Physics.Initialize();
        MainScene = new();
    }

    public static void Update()
    {
        EngineObject.HandleDestroyed();

        ForeachComponent((x) => {

            MonoBehaviour.Try(x.Internal_Start);
            MonoBehaviour.Try(x.UpdateCoroutines);
            MonoBehaviour.Try(x.Internal_Update);
        });


        ForeachComponent((x) => MonoBehaviour.Try(x.Internal_LateUpdate));
        ForeachComponent((x) => MonoBehaviour.Try(x.UpdateEndOfFrameCoroutines));
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
        ForeachComponent((x) => MonoBehaviour.Try(x.Internal_FixedUpdate));
        PostFixedUpdate?.Invoke();
    }

    public static void Draw()
    {
        var Cameras = MonoBehaviour.FindObjectsOfType<Camera>().ToList();
        Cameras.Sort((a, b) => a.RenderOrder.CompareTo(b.DrawOrder));
        foreach (var cam in Cameras)
            if (cam.EnabledInHierarchy)
                cam.Render(-1, -1);
    }

    public static void LoadScene(Scene scene)
    {
        Clear();
        MainScene = scene;
        MainScene.InstantiateScene();
        ForeachComponent((x) => MonoBehaviour.Try(x.Internal_OnLevelWasLoaded));
    }

    public static void LoadScene(AssetRef<Scene> scene)
    {
        if (scene.IsAvailable == false) throw new Exception("Scene is not available.");
        Clear();
        MainScene = scene.Res;
        MainScene.InstantiateScene();
        ForeachComponent((x) => MonoBehaviour.Try(x.Internal_OnLevelWasLoaded));
    }
}

