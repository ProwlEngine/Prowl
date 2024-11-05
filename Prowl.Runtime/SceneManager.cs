// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Pipelines;
using Prowl.Runtime.Utils;

namespace Prowl.Runtime.SceneManagement;

public static class SceneManager
{
    public static AssetRef<Scene> Current { get; private set; } = new Scene();

    public static Scene Scene => Current.Res!;

    private static SerializedProperty? StoredScene;
    private static Guid StoredSceneID;

    public static void Initialize()
    {
    }

    public static void StoreScene()
    {
        Debug.If(StoredScene != null, "Scene is already stored.");
        StoredScene = Serializer.Serialize(Current.Res!);
        StoredSceneID = Current.AssetID;
    }

    public static void RestoreScene()
    {
        Debug.IfNull(StoredScene, "Scene is not stored.");
        Clear();
        Scene? deserialized = Serializer.Deserialize<Scene>(StoredScene);
        Debug.IfNull(deserialized, "Scene could not be restored! Deserialized object is null.");
        deserialized.AssetID = StoredSceneID;
        Current = deserialized;
        Current.Res!.HandlePrefabs();
    }

    public static void ClearStoredScene()
    {
        Debug.IfNull(StoredScene, "Scene is not stored.");
        StoredScene = null;
    }

    public static void InstantiateNewScene()
    {
        GameObject go = new("Directional Light");
        go.AddComponent<DirectionalLight>(); // Will auto add Transform as DirectionLight requires it
        go.Transform.localEulerAngles = new System.Numerics.Vector3(130, 45, 0);
        Current.Res!.Add(go);

        GameObject cam = new("Main Camera");
        cam.tag = "Main Camera";
        cam.Transform.position = new(0, 0, -10);
        Camera camComp = cam.AddComponent<Camera>();
        camComp.Depth = -1;
        Current.Res!.Add(cam);
    }

    [OnAssemblyUnload]
    public static void Clear()
    {
        if (Current.Res != null)
        {
            Camera.Main = null; // Clear the main camera so it will re-find itself and be updated

            Physics.Dispose();
            // The act of Destroying a active scene sets the current scene to an new one
            // During this period the previous scene is Destroyed, making Res return null, hence the ? here
            Current.Res.DestroyImmediate();

            EngineObject.HandleDestroyed();

            Current = new Scene();

            Physics.Initialize();
        }
    }

    public static void Update()
    {
        EngineObject.HandleDestroyed();

        List<GameObject> activeGOs = Scene.ActiveObjects.ToList();
        foreach (GameObject go in activeGOs)
            go.PreUpdate();

        if (Application.IsPlaying)
            Physics.Update();

        ForeachComponent(activeGOs, (x) =>
        {
            x.Do(x.UpdateCoroutines);
            x.Do(x.Update);
        });

        ForeachComponent(activeGOs, (x) => x.Do(x.LateUpdate));
        ForeachComponent(activeGOs, (x) => x.Do(x.UpdateEndOfFrameCoroutines));
    }

    public static void ForeachComponent(IEnumerable<GameObject> objs, Action<MonoBehaviour> action)
    {
        foreach (var go in objs)
            foreach (var comp in go.GetComponents(typeof(MonoBehaviour)))
                if (comp.EnabledInHierarchy)
                    action.Invoke(comp);
    }

    public static void PhysicsUpdate()
    {
        List<GameObject> activeGOs = Scene.ActiveObjects.ToList();
        ForeachComponent(activeGOs, (x) =>
        {
            x.Do(x.UpdateFixedUpdateCoroutines);
            x.Do(x.FixedUpdate);
        });
    }

    public static bool Draw(RenderTexture? target = null)
    {
        var Cameras = Scene.ActiveObjects.SelectMany(x => x.GetComponentsInChildren<Camera>()).ToList();

        Cameras.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        if (Cameras.Count == 0)
            return false;

        foreach (Camera? cam in Cameras)
        {
            Veldrid.Framebuffer t = Graphics.ScreenTarget;

            if (cam.Target.Res != null)
                t = cam.Target.Res.Framebuffer;
            else if (target != null)
                t = target.Framebuffer;

            uint width = t.Width;
            uint height = t.Height;

            RenderingData data = new RenderingData
            {
                TargetResolution = new Vector2(width, height),
            };

            RenderPipeline pipeline = cam.Pipeline.Res ?? DefaultRenderPipeline.Default;

            pipeline.Render(t, cam, data);
        }

        return true;
    }

    public static void LoadScene(Scene scene)
    {
        Clear();
        Current = scene;
        Scene.HandlePrefabs();
        IEnumerable<GameObject> activeGOs = Scene.ActiveObjects;
        ForeachComponent(activeGOs, (x) => x.Do(x.OnLevelWasLoaded));
    }

    public static void LoadScene(AssetRef<Scene> scene)
    {
        if (scene.IsAvailable == false) throw new Exception("Scene is not available.");
        Clear();
        Current = scene.Res;
        Scene.HandlePrefabs();
        IEnumerable<GameObject> activeGOs = Scene.ActiveObjects;
        ForeachComponent(activeGOs, (x) => x.Do(x.OnLevelWasLoaded));
    }

    /// <summary>
    /// Search all GameObjects in the scene for the specified one recursively
    /// </summary>
    public static bool Has(GameObject original)
    {
        foreach (GameObject go in Scene.AllObjects)
            if (go.InstanceID == original.InstanceID)
                return true;
        return false;
    }
}

