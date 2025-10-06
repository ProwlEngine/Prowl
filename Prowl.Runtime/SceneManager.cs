// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.SceneManagement;

public static class SceneManager
{
    public static AssetRef<Scene> Current { get; private set; } = new Scene();

    public static Scene Scene => Current.Res!;

    public static void InstantiateNewScene(out Camera camera, out DirectionalLight light)
    {
        SceneManager.Clear();

        GameObject go = new("Directional Light");
        light = go.AddComponent<DirectionalLight>();
        go.Transform.localEulerAngles = new Vector3(-80, 5, 0);
        Current.Res!.Add(go);

        GameObject cam = new("Main Camera");
        cam.tag = "Main Camera";
        cam.Transform.position = new(0, 0, -10);
        camera = cam.AddComponent<Camera>();
        camera.Depth = -1;
        camera.HDR = true;

        Current.Res!.Add(cam);
    }

    public static void Clear()
    {
        if (Current.Res != null)
        {
            Camera.Main = null; // Clear the main camera so it will re-find itself and be updated

            // The act of Destroying a active scene sets the current scene to an new one
            // During this period the previous scene is Destroyed, making Res return null, hence the ? here
            Current.Res.DestroyImmediate();

            EngineObject.HandleDestroyed();

            Current = new Scene();
        }
    }

    public static void Update()
    {
        List<GameObject> activeGOs = Scene.ActiveObjects.ToList();
        foreach (GameObject go in activeGOs)
            go.PreUpdate();

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
            x.Do(x.FixedUpdate);
            x.Do(x.UpdateFixedUpdateCoroutines);
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
            RenderPipeline pipeline = cam.Pipeline.Res ?? DefaultRenderPipeline.Default;

            // If we have a target and the Camera doesnt, draw into the target
            if (target != null && cam.Target == null)
            {
                cam.Target = target;
                pipeline.Render(cam, new());
                cam.Target = null;
            }
            else
            {
                // Have no target or the camera has its own target
                pipeline.Render(cam, new());
            }
        }

        return true;
    }

    public static void LoadScene(Scene scene)
    {
        Clear();
        Current = scene;
        IEnumerable<GameObject> activeGOs = Scene.ActiveObjects;
        ForeachComponent(activeGOs, (x) => x.Do(x.OnLevelWasLoaded));
    }

    public static void LoadScene(AssetRef<Scene> scene)
    {
        if (scene.IsAvailable == false) throw new Exception("Scene is not available.");
        Clear();
        Current = scene.Res;
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

