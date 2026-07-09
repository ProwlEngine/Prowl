// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Runtime.UI;
using Prowl.Vector;

namespace Prowl.Editor.GUI;

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

    [CreateGameObjectMenu("3D Object/Text Mesh", Icon = EditorIcons.Font, Order = 14)]
    static void CreateTextMesh(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Text Mesh", parent);
        var text = go.AddComponent<TextMeshComponent>();
        text.Text = "New Text";
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

        // Save as asset file this assigns a GUID to the instance,
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
        go.Transform.Rotation = Quaternion.FromEuler(new Float3(-50, 30, 0));
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

    [CreateGameObjectMenu("UI/Canvas", Icon = EditorIcons.BorderAll, Order = 50, Separator = true)]
    static void CreateCanvas(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Canvas", parent);
        go.EnsureRectTransform();
        go.AddComponent<GameCanvas>();
    }

    [CreateGameObjectMenu("UI/Text", Icon = EditorIcons.Font, Order = 51)]
    static void CreateUIText(GameObject? parent)
    {
        var go = NewUIElement("Text", parent);
        go.RectTransform!.SizeDelta = new Float2(200f, 50f);
        var text = go.AddComponent<TextComponent>();
        text.Text = "New Text";
    }

    [CreateGameObjectMenu("UI/Image", Icon = EditorIcons.Image, Order = 52)]
    static void CreateUIImage(GameObject? parent)
    {
        var go = NewUIElement("Image", parent);
        go.RectTransform!.SizeDelta = new Float2(100f, 100f);
        go.AddComponent<UIImage>();
    }


    [CreateGameObjectMenu("UI/Button", Icon = EditorIcons.MobileButton, Order = 52)]
    static void CreateUIButton(GameObject? parent)
    {
        var go = NewUIElement("Button", parent);
        go.RectTransform!.SizeDelta = new Float2(100f, 100f);
        var image = go.AddComponent<UIImage>();
        var button = go.AddComponent<UIButton>();
        button.TargetGraphic = image;
    }

    [CreateGameObjectMenu("UI/Panel", Icon = EditorIcons.WindowMaximize, Order = 53)]
    static void CreateUIPanel(GameObject? parent)
    {
        var go = NewUIElement("Panel", parent);

        // Panels stretch to fill their parent rect (anchors at the corners, zero size delta).
        var rt = go.RectTransform!;
        rt.AnchorMin = Float2.Zero;
        rt.AnchorMax = Float2.One;
        rt.SizeDelta = Float2.Zero;
        rt.AnchoredPosition = Float2.Zero;

        var img = go.AddComponent<UIImage>();
        img.Color = new Color(1f, 1f, 1f, 0.4f);
    }

    [CreateGameObjectMenu("UI/Rect Mask", Icon = EditorIcons.Square, Order = 56)]
    static void CreateUIRectMask(GameObject? parent)
    {
        var go = NewUIElement("Rect Mask", parent);
        go.RectTransform!.SizeDelta = new Float2(200f, 200f);
        go.AddComponent<RectMask>();
    }

    /// <summary>
    /// Creates a UI element GameObject parented under a Canvas with a RectTransform ready.
    /// If <paramref name="parent"/> already lives under a <see cref="GameCanvas"/> the element
    /// is nested there; otherwise the first canvas in the scene is reused, or a new one is
    /// spawned at the root.
    /// </summary>
    private static GameObject NewUIElement(string name, GameObject? parent)
    {
        GameObject uiParent = ResolveCanvasParent(parent);
        var go = HierarchyPanel.CreateGameObject(name, uiParent);
        go.EnsureRectTransform();
        return go;
    }

    private static GameObject ResolveCanvasParent(GameObject? parent)
    {
        GameCanvas? canvas = parent?.GetComponentInParent<GameCanvas>(includeSelf: true);
        if (canvas != null)
            return parent!;

        var scene = Scene.Current;
        if (scene != null)
        {
            foreach (GameCanvas? c in scene.FindObjectsOfType<GameCanvas>())
                if (c != null)
                    return c.GameObject;
        }

        // None exists create one at the root (not selected / no rename, it's a side effect).
        var canvasGo = HierarchyPanel.CreateGameObject("Canvas", null, select: false, beginRename: false);
        canvasGo.AddComponent<GameCanvas>();
        return canvasGo;
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
