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
    [CreateGameObjectMenu("Empty Object", Icon = EditorIcons.Cube, Order = 0, Separator = false)]
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

    // ---- Lights ----

    [CreateGameObjectMenu("Light/Directional Light", Icon = EditorIcons.Sun, Order = 20, Separator = true)]
    static void CreateDirectionalLight(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Directional Light", parent);
        go.Transform.Rotation = Quaternion.FromEuler(new Float3(50, 30, 0));
        go.AddComponent<DirectionalLight>();
    }

    // ---- Camera ----

    [CreateGameObjectMenu("Camera", Icon = EditorIcons.Camera, Order = 30)]
    static void CreateCamera(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Camera", parent);
        go.AddComponent<Camera>();
    }

    // ---- Particle System ----

    [CreateGameObjectMenu("Particle System", Icon = EditorIcons.SprayCanSparkles, Order = 40)]
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

    // ---- Terrain ----

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

    // ---- Helper ----

    private static void CreatePrimitive(string name, DefaultModel model, GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject(name, parent);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = new AssetRef<Mesh>(BuiltInAssets.GuidForMesh(model));
        renderer.Material = new AssetRef<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard));
    }
}
