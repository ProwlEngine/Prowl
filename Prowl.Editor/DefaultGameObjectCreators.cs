// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Panels;
using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary>
/// Built-in GameObject creation entries registered via <see cref="CreateGameObjectMenuAttribute"/>.
/// These populate the Hierarchy create menu and the GameObject menu bar.
/// </summary>
internal static class DefaultGameObjectCreators
{
    [CreateGameObjectMenu("Empty Object", Icon = EditorIcons.Cube, Order = 0)]
    static void CreateEmpty(GameObject? parent)
    {
        HierarchyPanel.CreateGameObject("GameObject", parent);
    }

    // ---- 3D Objects ----

    [CreateGameObjectMenu("3D Object/Cube", Icon = EditorIcons.Cube, Order = 10, Separator = true)]
    static void CreateCube(GameObject? parent)
    {
        CreatePrimitive("Cube", DefaultModel.Cube, parent);
    }

    [CreateGameObjectMenu("3D Object/Sphere", Icon = EditorIcons.CircleDot, Order = 11)]
    static void CreateSphere(GameObject? parent)
    {
        CreatePrimitive("Sphere", DefaultModel.Sphere, parent);
    }

    [CreateGameObjectMenu("3D Object/Cylinder", Icon = EditorIcons.Circle, Order = 12)]
    static void CreateCylinder(GameObject? parent)
    {
        CreatePrimitive("Cylinder", DefaultModel.Cylinder, parent);
    }

    [CreateGameObjectMenu("3D Object/Plane", Icon = EditorIcons.Square, Order = 13)]
    static void CreatePlane(GameObject? parent)
    {
        CreatePrimitive("Plane", DefaultModel.Plane, parent);
    }

    [CreateGameObjectMenu("3D Object/Terrain", Icon = EditorIcons.Mountain, Order = 15, Separator = true)]
    static void CreateTerrain(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Terrain", parent);
        var terrain = go.AddComponent<TerrainComponent>();
        terrain.Material = new AssetRef<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Terrain));
        go.AddComponent<TerrainCollider>();

        // Create TerrainData instance, assign it, then persist as an asset
        var terrainData = new TerrainData();
        terrain.Data = new AssetRef<TerrainData>(terrainData);

        // Save as asset file — this assigns a GUID to the instance,
        // which the AssetRef picks up automatically
        var db = EditorAssetDatabase.Instance;
        if (db != null)
        {
            string name = AssetCreateMenu.FindUniqueName(
                Project.Current.AssetsPath, "New Terrain Data", ".terraindata");
            db.CreateAsset(terrainData, name);
        }
    }

    // ---- Lights ----

    [CreateGameObjectMenu("Light/Directional Light", Icon = EditorIcons.Sun, Order = 20, Separator = true)]
    static void CreateDirectionalLight(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Directional Light", parent);
        go.Transform.Rotation = Quaternion.FromEuler(new Float3(50, 30, 0));
        go.AddComponent<DirectionalLight>();
    }

    [CreateGameObjectMenu("Light/Point Light", Icon = EditorIcons.Lightbulb, Order = 21)]
    static void CreatePointLight(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Point Light", parent);
        go.AddComponent<PointLight>();
    }

    [CreateGameObjectMenu("Light/Spot Light", Icon = EditorIcons.Bullseye, Order = 22)]
    static void CreateSpotLight(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Spot Light", parent);
        go.Transform.Rotation = Quaternion.FromEuler(new Float3(90, 0, 0));
        go.AddComponent<SpotLight>();
    }

    // ---- Fog Volumes ----

    [CreateGameObjectMenu("Fog Volume/Global", Icon = EditorIcons.Cloud, Order = 25, Separator = true)]
    static void CreateGlobalFogVolume(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Global Fog Volume", parent);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Global;
    }

    [CreateGameObjectMenu("Fog Volume/Box", Icon = EditorIcons.Cube, Order = 26)]
    static void CreateBoxFogVolume(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Box Fog Volume", parent);
        go.Transform.LocalScale = new Float3(2, 2, 2);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Box;
    }

    [CreateGameObjectMenu("Fog Volume/Sphere", Icon = EditorIcons.CircleDot, Order = 27)]
    static void CreateSphereFogVolume(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Sphere Fog Volume", parent);
        go.Transform.LocalScale = new Float3(3, 3, 3);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Sphere;
    }

    [CreateGameObjectMenu("Fog Volume/Cylinder", Icon = EditorIcons.Circle, Order = 28)]
    static void CreateCylinderFogVolume(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Cylinder Fog Volume", parent);
        go.Transform.LocalScale = new Float3(2, 3, 2);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Cylinder;
    }

    [CreateGameObjectMenu("Fog Volume/Cone", Icon = EditorIcons.Bullseye, Order = 29)]
    static void CreateConeFogVolume(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Cone Fog Volume", parent);
        go.Transform.LocalScale = new Float3(1, 4, 1);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Cone;
    }

    // ---- Audio ----

    [CreateGameObjectMenu("Audio/Audio Source", Icon = EditorIcons.VolumeHigh, Order = 30, Separator = true)]
    static void CreateAudioSource(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Audio Source", parent);
        go.AddComponent<AudioSource>();
    }

    [CreateGameObjectMenu("Audio/Audio Listener", Icon = EditorIcons.Headphones, Order = 31)]
    static void CreateAudioListener(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Audio Listener", parent);
        go.AddComponent<AudioListener>();
    }

    // ---- Camera ----

    [CreateGameObjectMenu("Camera", Icon = EditorIcons.Camera, Order = 40, Separator = true)]
    static void CreateCamera(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Camera", parent);
        go.AddComponent<Camera>();
    }

    // ---- UI ----

    [CreateGameObjectMenu("UI/World Canvas", Icon = EditorIcons.Display, Order = 50, Separator = true)]
    static void CreateWorldCanvas(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("World Canvas", parent);
        go.AddComponent<WorldCanvas>();
    }

    // ---- Particle System ----

    [CreateGameObjectMenu("Particle System", Icon = EditorIcons.SprayCanSparkles, Order = 60)]
    static void CreateParticleSystem(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Particle System", parent);
        var ps = go.AddComponent<ParticleSystemComponent>();
        ps.Material = new AssetRef<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Particle));
        ps.Emission.Enabled = true;
        ps.Emission.RateOverTime = new MinMaxCurve(10f);
        ps.Emission.Shape = EmissionShape.Cone;
        ps.Initial.Enabled = true;
        ps.Initial.StartLifetime = new MinMaxCurve(2f);
        ps.Initial.StartSpeed = new MinMaxCurve(3f);
        ps.Initial.StartSize = new MinMaxCurve(0.2f);
    }

    // ---- Helper ----

    private static void CreatePrimitive(string name, DefaultModel model, GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject(name, parent);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = new AssetRef<Mesh>(BuiltInAssets.GuidForMesh(model));
        renderer.Material = new AssetRef<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard));
    }
}
