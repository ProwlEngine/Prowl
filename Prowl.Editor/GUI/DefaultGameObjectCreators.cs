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

internal static class DefaultGameObjectCreators
{
    [MenuItem("GameObject/Empty Object", priority: 0, Icon = EditorIcons.Cube)]
    static void CreateEmpty()
    {
        HierarchyPanel.CreateGameObject("GameObject", MenuContext.ActiveGameObject);
    }

    [MenuItem("GameObject/3D Object/Cube", priority: 10, Icon = EditorIcons.Cube, Separator = true)]
    static void CreateCube() => CreatePrimitive("Cube", DefaultModel.Cube);

    [MenuItem("GameObject/3D Object/Sphere", priority: 11, Icon = EditorIcons.CircleDot)]
    static void CreateSphere() => CreatePrimitive("Sphere", DefaultModel.Sphere);

    [MenuItem("GameObject/3D Object/Cylinder", priority: 12, Icon = EditorIcons.Circle)]
    static void CreateCylinder() => CreatePrimitive("Cylinder", DefaultModel.Cylinder);

    [MenuItem("GameObject/3D Object/Plane", priority: 13, Icon = EditorIcons.Square)]
    static void CreatePlane() => CreatePrimitive("Plane", DefaultModel.Plane);

    [MenuItem("GameObject/3D Object/Text Mesh", priority: 24, Icon = EditorIcons.Font, Separator = true)]
    static void CreateTextMesh()
    {
        var go = HierarchyPanel.CreateGameObject("Text Mesh", MenuContext.ActiveGameObject);
        var text = go.AddComponent<TextMeshComponent>();
        text.Text = "New Text";
    }

    [MenuItem("GameObject/3D Object/Terrain", priority: 25, Icon = EditorIcons.Mountain)]
    static void CreateTerrain()
    {
        var go = HierarchyPanel.CreateGameObject("Terrain", MenuContext.ActiveGameObject);
        var terrain = go.AddComponent<TerrainComponent>();
        terrain.Material = new AssetRef<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Terrain));
        go.AddComponent<TerrainCollider>();

        var terrainData = new TerrainData();
        terrain.Data = new AssetRef<TerrainData>(terrainData);

        var db = EditorAssetDatabase.Instance;
        if (db != null)
        {
            string name = AssetCreateMenu.FindUniqueName(Project.Current.AssetsPath, "New Terrain Data", ".terraindata");
            db.CreateAsset(terrainData, name);
        }
    }

    [MenuItem("GameObject/Light/Directional Light", priority: 30, Icon = EditorIcons.Sun)]
    static void CreateDirectionalLight()
    {
        var go = HierarchyPanel.CreateGameObject("Directional Light", MenuContext.ActiveGameObject);
        go.Transform.Rotation = Quaternion.FromEuler(new Float3(-50, 30, 0));
        go.AddComponent<DirectionalLight>();
    }

    [MenuItem("GameObject/Light/Point Light", priority: 31, Icon = EditorIcons.Lightbulb)]
    static void CreatePointLight()
    {
        var go = HierarchyPanel.CreateGameObject("Point Light", MenuContext.ActiveGameObject);
        go.AddComponent<PointLight>();
    }

    [MenuItem("GameObject/Light/Spot Light", priority: 32, Icon = EditorIcons.Bullseye)]
    static void CreateSpotLight()
    {
        var go = HierarchyPanel.CreateGameObject("Spot Light", MenuContext.ActiveGameObject);
        go.Transform.Rotation = Quaternion.FromEuler(new Float3(90, 0, 0));
        go.AddComponent<SpotLight>();
    }

    [MenuItem("GameObject/Effects/Fog/Global", priority: 40, Icon = EditorIcons.Cloud)]
    static void CreateGlobalFogVolume()
    {
        var go = HierarchyPanel.CreateGameObject("Global Fog Volume", MenuContext.ActiveGameObject);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Global;
    }

    [MenuItem("GameObject/Effects/Fog/Box", priority: 41, Icon = EditorIcons.Cube)]
    static void CreateBoxFogVolume()
    {
        var go = HierarchyPanel.CreateGameObject("Box Fog Volume", MenuContext.ActiveGameObject);
        go.Transform.LocalScale = new Float3(2, 2, 2);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Box;
    }

    [MenuItem("GameObject/Effects/Fog/Sphere", priority: 42, Icon = EditorIcons.CircleDot)]
    static void CreateSphereFogVolume()
    {
        var go = HierarchyPanel.CreateGameObject("Sphere Fog Volume", MenuContext.ActiveGameObject);
        go.Transform.LocalScale = new Float3(3, 3, 3);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Sphere;
    }

    [MenuItem("GameObject/Effects/Fog/Cylinder", priority: 43, Icon = EditorIcons.Circle)]
    static void CreateCylinderFogVolume()
    {
        var go = HierarchyPanel.CreateGameObject("Cylinder Fog Volume", MenuContext.ActiveGameObject);
        go.Transform.LocalScale = new Float3(2, 3, 2);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Cylinder;
    }

    [MenuItem("GameObject/Effects/Fog/Cone", priority: 44, Icon = EditorIcons.Bullseye)]
    static void CreateConeFogVolume()
    {
        var go = HierarchyPanel.CreateGameObject("Cone Fog Volume", MenuContext.ActiveGameObject);
        go.Transform.LocalScale = new Float3(1, 4, 1);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Cone;
    }

    [MenuItem("GameObject/Effects/Particle System", priority: 55, Icon = EditorIcons.SprayCanSparkles, Separator = true)]
    static void CreateParticleSystem()
    {
        var go = HierarchyPanel.CreateGameObject("Particle System", MenuContext.ActiveGameObject);
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

    [MenuItem("GameObject/Audio/Audio Source", priority: 60, Icon = EditorIcons.VolumeHigh)]
    static void CreateAudioSource()
    {
        var go = HierarchyPanel.CreateGameObject("Audio Source", MenuContext.ActiveGameObject);
        go.AddComponent<AudioSource>();
    }

    [MenuItem("GameObject/Audio/Audio Listener", priority: 61, Icon = EditorIcons.Headphones)]
    static void CreateAudioListener()
    {
        var go = HierarchyPanel.CreateGameObject("Audio Listener", MenuContext.ActiveGameObject);
        go.AddComponent<AudioListener>();
    }

    [MenuItem("GameObject/UI/Canvas", priority: 70, Icon = EditorIcons.BorderAll)]
    static void CreateCanvas()
    {
        var go = HierarchyPanel.CreateGameObject("Canvas", MenuContext.ActiveGameObject);
        go.EnsureRectTransform();
        go.AddComponent<GameCanvas>();
    }

    [MenuItem("GameObject/UI/Text", priority: 71, Icon = EditorIcons.Font)]
    static void CreateUIText()
    {
        var go = NewUIElement("Text", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(200f, 50f);
        var text = go.AddComponent<TextComponent>();
        text.Text = "New Text";
    }

    [MenuItem("GameObject/UI/Image", priority: 72, Icon = EditorIcons.Image)]
    static void CreateUIImage()
    {
        var go = NewUIElement("Image", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(100f, 100f);
        go.AddComponent<UIImage>();
    }

    [MenuItem("GameObject/UI/Button", priority: 73, Icon = EditorIcons.MobileButton)]
    static void CreateUIButton()
    {
        var go = NewUIElement("Button", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(100f, 100f);
        var image = go.AddComponent<UIImage>();
        var button = go.AddComponent<UIButton>();
        button.TargetGraphic = image;
    }

    [MenuItem("GameObject/UI/Panel", priority: 74, Icon = EditorIcons.WindowMaximize)]
    static void CreateUIPanel()
    {
        var go = NewUIElement("Panel", MenuContext.ActiveGameObject);
        var rt = go.RectTransform!;
        rt.AnchorMin = Float2.Zero;
        rt.AnchorMax = Float2.One;
        rt.SizeDelta = Float2.Zero;
        rt.AnchoredPosition = Float2.Zero;
        var img = go.AddComponent<UIImage>();
        img.Color = new Color(1f, 1f, 1f, 0.4f);
    }

    [MenuItem("GameObject/UI/Slider", priority: 75, Icon = EditorIcons.Sliders)]
    static void CreateUISlider()
    {
        var go = NewUIElement("Slider", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(200f, 24f);
        var bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.20f, 0.20f, 0.24f, 1f);
        var slider = go.AddComponent<UISlider>();

        var fillGo = HierarchyPanel.CreateGameObject("Fill", go, select: false, beginRename: false);
        fillGo.EnsureRectTransform();
        var fill = fillGo.AddComponent<UIImage>();
        fill.Color = new Color(0.38f, 0.55f, 0.95f, 1f);
        fill.RaycastTarget = false;

        var handleGo = HierarchyPanel.CreateGameObject("Handle", go, select: false, beginRename: false);
        handleGo.EnsureRectTransform();
        handleGo.RectTransform!.SizeDelta = new Float2(20f, 0f);
        var handle = handleGo.AddComponent<UIImage>();
        handle.RaycastTarget = false;

        slider.FillRect = fillGo.RectTransform;
        slider.HandleRect = handleGo.RectTransform;
        slider.TargetGraphic = handle;
        slider.Value = 0.5f;
    }

    [MenuItem("GameObject/UI/Scroll View", priority: 76, Icon = EditorIcons.RectangleList)]
    static void CreateUIScrollView()
    {
        const float bar = 12f;

        var go = NewUIElement("Scroll View", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(240f, 180f);
        var bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.14f, 0.14f, 0.17f, 1f);
        var scroll = go.AddComponent<UIScrollRect>();

        var vpGo = HierarchyPanel.CreateGameObject("Viewport", go, select: false, beginRename: false);
        vpGo.EnsureRectTransform();
        var vpRt = vpGo.RectTransform!;
        vpRt.AnchorMin = Float2.Zero; vpRt.AnchorMax = Float2.One;
        vpRt.SizeDelta = new Float2(-bar, -bar);
        vpRt.AnchoredPosition = new Float2(-bar * 0.5f, bar * 0.5f);
        vpGo.AddComponent<RectMask>();

        var contentGo = HierarchyPanel.CreateGameObject("Content", vpGo, select: false, beginRename: false);
        contentGo.EnsureRectTransform();
        var cRt = contentGo.RectTransform!;
        cRt.AnchorMin = new Float2(0f, 1f); cRt.AnchorMax = new Float2(0f, 1f);
        cRt.Pivot = new Float2(0f, 1f);
        cRt.SizeDelta = new Float2(400f, 400f); cRt.AnchoredPosition = Float2.Zero;

        var vBar = BuildScrollbar("Scrollbar Vertical", go, UIScrollbar.ScrollbarDirection.TopToBottom);
        var vRt = vBar.GameObject.RectTransform!;
        vRt.AnchorMin = new Float2(1f, 0f); vRt.AnchorMax = new Float2(1f, 1f);
        vRt.Pivot = new Float2(1f, 0.5f);
        vRt.SizeDelta = new Float2(bar, -bar); vRt.AnchoredPosition = new Float2(0f, bar * 0.5f);

        var hBar = BuildScrollbar("Scrollbar Horizontal", go, UIScrollbar.ScrollbarDirection.LeftToRight);
        var hRt = hBar.GameObject.RectTransform!;
        hRt.AnchorMin = new Float2(0f, 0f); hRt.AnchorMax = new Float2(1f, 0f);
        hRt.Pivot = new Float2(0.5f, 0f);
        hRt.SizeDelta = new Float2(-bar, bar); hRt.AnchoredPosition = new Float2(-bar * 0.5f, 0f);

        scroll.Viewport = vpRt;
        scroll.Content = cRt;
        scroll.HorizontalScrollbar = hBar;
        scroll.VerticalScrollbar = vBar;
    }

    [MenuItem("GameObject/UI/Rect Mask", priority: 77, Icon = EditorIcons.Square)]
    static void CreateUIRectMask()
    {
        var go = NewUIElement("Rect Mask", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(200f, 200f);
        go.AddComponent<RectMask>();
    }

    [MenuItem("GameObject/UI/Input Field", priority: 78, Icon = EditorIcons.Keyboard)]
    static void CreateUIInputField()
    {
        var go = NewUIElement("Input Field", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(200f, 32f);
        var bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.12f, 0.12f, 0.15f, 1f);
        var field = go.AddComponent<UIInputField>();

        var areaGo = HierarchyPanel.CreateGameObject("Text Area", go, select: false, beginRename: false);
        areaGo.EnsureRectTransform();
        var areaRt = areaGo.RectTransform!;
        areaRt.AnchorMin = Float2.Zero; areaRt.AnchorMax = Float2.One;
        areaRt.SizeDelta = new Float2(-16f, -8f); areaRt.AnchoredPosition = Float2.Zero;
        areaGo.AddComponent<RectMask>();

        var selGo = HierarchyPanel.CreateGameObject("Selection", areaGo, select: false, beginRename: false);
        selGo.EnsureRectTransform();
        var selRt = selGo.RectTransform!;
        selRt.AnchorMin = new Float2(0f, 0f); selRt.AnchorMax = new Float2(0f, 1f);
        selRt.Pivot = new Float2(0f, 0.5f);
        selRt.SizeDelta = new Float2(0f, -4f); selRt.AnchoredPosition = Float2.Zero;
        var selImg = selGo.AddComponent<UIImage>();
        selImg.RaycastTarget = false;

        var phGo = HierarchyPanel.CreateGameObject("Placeholder", areaGo, select: false, beginRename: false);
        phGo.EnsureRectTransform();
        Stretch(phGo.RectTransform!);
        var placeholder = phGo.AddComponent<TextComponent>();
        placeholder.Text = "Enter text...";
        placeholder.Alignment = TextAlignment.CenterLeft;
        placeholder.Size = 16;
        placeholder.TextColor = new Color(0.5f, 0.5f, 0.55f, 1f);

        var textGo = HierarchyPanel.CreateGameObject("Text", areaGo, select: false, beginRename: false);
        textGo.EnsureRectTransform();
        Stretch(textGo.RectTransform!);
        var text = textGo.AddComponent<TextComponent>();
        text.Alignment = TextAlignment.CenterLeft;
        text.Size = 16;
        text.TextColor = new Color(0.90f, 0.90f, 0.92f, 1f);

        var caretGo = HierarchyPanel.CreateGameObject("Caret", areaGo, select: false, beginRename: false);
        caretGo.EnsureRectTransform();
        var caretRt = caretGo.RectTransform!;
        caretRt.AnchorMin = new Float2(0f, 0f); caretRt.AnchorMax = new Float2(0f, 1f);
        caretRt.Pivot = new Float2(0f, 0.5f);
        caretRt.SizeDelta = new Float2(1.5f, -4f); caretRt.AnchoredPosition = Float2.Zero;
        var caretImg = caretGo.AddComponent<UIImage>();
        caretImg.RaycastTarget = false;

        field.TargetGraphic = bg;
        field.TextArea = areaRt;
        field.TextComponent = text;
        field.Placeholder = placeholder;
        field.Selection = selRt;
        field.Caret = caretRt;
    }

    [MenuItem("GameObject/UI/Dropdown", priority: 79, Icon = EditorIcons.ChevronDown)]
    static void CreateUIDropdown()
    {
        var go = NewUIElement("Dropdown", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(200f, 32f);
        var bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.18f, 0.18f, 0.22f, 1f);
        var dropdown = go.AddComponent<UIDropdown>();

        var labelGo = HierarchyPanel.CreateGameObject("Label", go, select: false, beginRename: false);
        labelGo.EnsureRectTransform();
        var lrt = labelGo.RectTransform!;
        lrt.AnchorMin = Float2.Zero; lrt.AnchorMax = Float2.One;
        lrt.SizeDelta = new Float2(-16f, 0f); lrt.AnchoredPosition = new Float2(4f, 0f);
        var label = labelGo.AddComponent<TextComponent>();
        label.Alignment = TextAlignment.CenterLeft;
        label.Size = 16;
        label.TextColor = new Color(0.90f, 0.90f, 0.92f, 1f);

        var optionsGo = HierarchyPanel.CreateGameObject("Options", go, select: false, beginRename: false);
        optionsGo.EnsureRectTransform();
        var ort = optionsGo.RectTransform!;
        ort.AnchorMin = new Float2(0f, 0f); ort.AnchorMax = new Float2(1f, 0f);
        ort.Pivot = new Float2(0.5f, 1f);
        ort.SizeDelta = new Float2(0f, 0f); ort.AnchoredPosition = Float2.Zero;
        var optionsBg = optionsGo.AddComponent<UIImage>();
        optionsBg.Color = new Color(0.14f, 0.14f, 0.17f, 1f);
        optionsGo.Enabled = false;

        dropdown.Options.Add("Option A");
        dropdown.Options.Add("Option B");
        dropdown.Options.Add("Option C");
        dropdown.OptionsRoot = ort;
        dropdown.CaptionText = label;
        dropdown.TargetGraphic = bg;
    }

    [MenuItem("GameObject/UI/Event System", priority: 90, Icon = EditorIcons.ArrowPointer, Separator = true)]
    static void CreateEventSystem()
    {
        var go = HierarchyPanel.CreateGameObject("Event System", MenuContext.ActiveGameObject);
        go.AddComponent<EventSystem>();
    }

    [MenuItem("GameObject/Camera", priority: 100, Icon = EditorIcons.Camera, Separator = true)]
    static void CreateCamera()
    {
        var go = HierarchyPanel.CreateGameObject("Camera", MenuContext.ActiveGameObject);
        go.AddComponent<Camera>();
    }

    private static void CreatePrimitive(string name, DefaultModel model)
    {
        var go = HierarchyPanel.CreateGameObject(name, MenuContext.ActiveGameObject);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = new AssetRef<Mesh>(BuiltInAssets.GuidForMesh(model));
        renderer.Material = new AssetRef<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard));
    }

    private static GameObject NewUIElement(string name, GameObject? parent)
    {
        GameObject uiParent = ResolveCanvasParent(parent);
        var go = HierarchyPanel.CreateGameObject(name, uiParent);
        go.EnsureRectTransform();
        EnsureEventSystem(go.Scene);
        return go;
    }

    private static void EnsureEventSystem(Scene? scene)
    {
        scene ??= Scene.Current;
        if (scene == null) return;
        foreach (EventSystem? es in scene.FindObjectsOfType<EventSystem>())
            if (es != null) return;
        var esGo = HierarchyPanel.CreateGameObject("Event System", null, select: false, beginRename: false);
        esGo.AddComponent<EventSystem>();
    }

    private static GameObject ResolveCanvasParent(GameObject? parent)
    {
        GameCanvas? canvas = parent?.GetComponentInParent<GameCanvas>(includeSelf: true);
        if (canvas != null) return parent!;

        var scene = Scene.Current;
        if (scene != null)
        {
            foreach (GameCanvas? c in scene.FindObjectsOfType<GameCanvas>())
                if (c != null) return c.GameObject;
        }

        var canvasGo = HierarchyPanel.CreateGameObject("Canvas", null, select: false, beginRename: false);
        canvasGo.AddComponent<GameCanvas>();
        return canvasGo;
    }

    static void Stretch(RectTransform rt)
    {
        rt.AnchorMin = Float2.Zero;
        rt.AnchorMax = Float2.One;
        rt.SizeDelta = Float2.Zero;
        rt.AnchoredPosition = Float2.Zero;
    }

    static UIScrollbar BuildScrollbar(string name, GameObject parent, UIScrollbar.ScrollbarDirection dir)
    {
        var go = HierarchyPanel.CreateGameObject(name, parent, select: false, beginRename: false);
        go.EnsureRectTransform();
        var track = go.AddComponent<UIImage>();
        track.Color = new Color(0.10f, 0.10f, 0.13f, 1f);
        var bar = go.AddComponent<UIScrollbar>();
        bar.Direction = dir;
        var handleGo = HierarchyPanel.CreateGameObject("Handle", go, select: false, beginRename: false);
        handleGo.EnsureRectTransform();
        var handle = handleGo.AddComponent<UIImage>();
        handle.Color = new Color(0.42f, 0.42f, 0.48f, 1f);
        handle.RaycastTarget = false;
        bar.HandleRect = handleGo.RectTransform;
        bar.TargetGraphic = handle;
        bar.Size = 0.3f;
        return bar;
    }
}
