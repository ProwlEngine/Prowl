using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.Rendering;
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

    [MenuItem("GameObject/Empty Child", priority: 1, Icon = EditorIcons.Sitemap)]
    static void CreateEmptyChild()
    {
        GameObject? parent = MenuContext.ActiveGameObject;
        if (parent.IsNotValid()) return;
        HierarchyPanel.CreateGameObject("GameObject", parent);
    }

    // Nothing to parent to from the scene-root menu, or with an empty selection.
    [MenuItem("GameObject/Empty Child", isValidate: true)]
    static bool ValidateCreateEmptyChild() => MenuContext.ActiveGameObject.IsValid();

    [MenuItem("GameObject/Empty Parent", priority: 2, Icon = EditorIcons.ObjectGroup, SelectionWide = true)]
    static void CreateEmptyParent() => HierarchyPanel.CreateEmptyParent();

    // Wrapping is defined by what's selected, not by where the menu was opened.
    [MenuItem("GameObject/Empty Parent", isValidate: true)]
    static bool ValidateCreateEmptyParent() => Selection.GetSelected<GameObject>().Any();

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
        GameObject go = HierarchyPanel.CreateGameObject("Text Mesh", MenuContext.ActiveGameObject);
        TextMeshComponent text = go.AddComponent<TextMeshComponent>();
        text.Text = "New Text";
    }

    [MenuItem("GameObject/3D Object/Terrain", priority: 25, Icon = EditorIcons.Mountain)]
    static void CreateTerrain()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Terrain", MenuContext.ActiveGameObject);
        TerrainComponent terrain = go.AddComponent<TerrainComponent>();
        terrain.Material = AssetDatabase.Get<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Terrain));
        go.AddComponent<TerrainCollider>();

        var terrainData = new TerrainData();
        terrain.Data = terrainData;

        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db != null)
        {
            string name = AssetCreateMenu.FindUniqueName(Project.Current.AssetsPath, "New Terrain Data", ".terraindata");
            db.CreateAsset(terrainData, name);
        }
    }

    [MenuItem("GameObject/Light/Directional Light", priority: 30, Icon = EditorIcons.Sun)]
    static void CreateDirectionalLight() => CreateLight("Directional Light", LightType.Directional, new Float3(50, 210, 0));

    [MenuItem("GameObject/Light/Point Light", priority: 31, Icon = EditorIcons.Lightbulb)]
    static void CreatePointLight() => CreateLight("Point Light", LightType.Point, Float3.Zero);

    [MenuItem("GameObject/Light/Spot Light", priority: 32, Icon = EditorIcons.Bullseye)]
    static void CreateSpotLight() => CreateLight("Spot Light", LightType.Spot, new Float3(90, 0, 0));

    [MenuItem("GameObject/Light/Rect Light", priority: 33, Icon = EditorIcons.Lightbulb)]
    static void CreateRectLight() => CreateLight("Rect Light", LightType.Rect, new Float3(90, 0, 0));

    [MenuItem("GameObject/Light/Disk Light", priority: 34, Icon = EditorIcons.Lightbulb)]
    static void CreateDiskLight() => CreateLight("Disk Light", LightType.Disk, new Float3(90, 0, 0));

    [MenuItem("GameObject/Light/Tube Light", priority: 35, Icon = EditorIcons.Lightbulb)]
    static void CreateTubeLight() => CreateLight("Tube Light", LightType.Tube, Float3.Zero);

    static void CreateLight(string name, LightType type, Float3 euler)
    {
        GameObject go = HierarchyPanel.CreateGameObject(name, MenuContext.ActiveGameObject);
        go.Transform.Rotation = Quaternion.FromEuler(euler);
        Light light = go.AddComponent<Light>();
        light.Type = type;
        light.Intensity = Light.TypicalIntensity(type);
    }

    [MenuItem("GameObject/Effects/Fog/Global", priority: 40, Icon = EditorIcons.Cloud)]
    static void CreateGlobalFogVolume()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Global Fog Volume", MenuContext.ActiveGameObject);
        FogVolume v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Global;
    }

    [MenuItem("GameObject/Effects/Fog/Box", priority: 41, Icon = EditorIcons.Cube)]
    static void CreateBoxFogVolume()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Box Fog Volume", MenuContext.ActiveGameObject);
        go.Transform.LocalScale = new Float3(2, 2, 2);
        FogVolume v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Box;
    }

    [MenuItem("GameObject/Effects/Fog/Sphere", priority: 42, Icon = EditorIcons.CircleDot)]
    static void CreateSphereFogVolume()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Sphere Fog Volume", MenuContext.ActiveGameObject);
        go.Transform.LocalScale = new Float3(3, 3, 3);
        FogVolume v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Sphere;
    }

    [MenuItem("GameObject/Effects/Fog/Cylinder", priority: 43, Icon = EditorIcons.Circle)]
    static void CreateCylinderFogVolume()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Cylinder Fog Volume", MenuContext.ActiveGameObject);
        go.Transform.LocalScale = new Float3(2, 3, 2);
        FogVolume v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Cylinder;
    }

    [MenuItem("GameObject/Effects/Fog/Cone", priority: 44, Icon = EditorIcons.Bullseye)]
    static void CreateConeFogVolume()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Cone Fog Volume", MenuContext.ActiveGameObject);
        go.Transform.LocalScale = new Float3(1, 4, 1);
        FogVolume v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Cone;
    }

    [MenuItem("GameObject/Effects/Particle System", priority: 55, Icon = EditorIcons.SprayCanSparkles, Separator = true)]
    static void CreateParticleSystem()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Particle System", MenuContext.ActiveGameObject);
        ParticleSystemComponent ps = go.AddComponent<ParticleSystemComponent>();
        ps.Renderer.Material = AssetDatabase.Get<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Particle));
        ps.Initial.StartLifetime = new MinMaxCurve(2f);
        ps.Initial.StartSpeed = new MinMaxCurve(3f);
        ps.Initial.StartSize = new MinMaxCurve(0.2f);
    }

    [MenuItem("GameObject/Audio/Audio Source", priority: 60, Icon = EditorIcons.VolumeHigh)]
    static void CreateAudioSource()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Audio Source", MenuContext.ActiveGameObject);
        go.AddComponent<AudioSource>();
    }

    [MenuItem("GameObject/Audio/Audio Listener", priority: 61, Icon = EditorIcons.Headphones)]
    static void CreateAudioListener()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Audio Listener", MenuContext.ActiveGameObject);
        go.AddComponent<AudioListener>();
    }

    [MenuItem("GameObject/UI/Canvas", priority: 70, Icon = EditorIcons.BorderAll)]
    static void CreateCanvas()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Canvas", MenuContext.ActiveGameObject);
        go.EnsureRectTransform();
        go.AddComponent<GameCanvas>();
    }

    [MenuItem("GameObject/UI/Text", priority: 71, Icon = EditorIcons.Font)]
    static void CreateUIText()
    {
        GameObject go = NewUIElement("Text", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(200f, 50f);
        TextComponent text = go.AddComponent<TextComponent>();
        text.Text = "New Text";
    }

    [MenuItem("GameObject/UI/Image", priority: 72, Icon = EditorIcons.Image)]
    static void CreateUIImage()
    {
        GameObject go = NewUIElement("Image", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(100f, 100f);
        go.AddComponent<UIImage>();
    }

    [MenuItem("GameObject/UI/Button", priority: 73, Icon = EditorIcons.MobileButton)]
    static void CreateUIButton()
    {
        GameObject go = NewUIElement("Button", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(100f, 100f);
        UIImage image = go.AddComponent<UIImage>();
        UIButton button = go.AddComponent<UIButton>();
        button.TargetGraphic = image;
    }

    [MenuItem("GameObject/UI/Panel", priority: 74, Icon = EditorIcons.WindowMaximize)]
    static void CreateUIPanel()
    {
        GameObject go = NewUIElement("Panel", MenuContext.ActiveGameObject);
        RectTransform rt = go.RectTransform!;
        rt.AnchorMin = Float2.Zero;
        rt.AnchorMax = Float2.One;
        rt.SizeDelta = Float2.Zero;
        rt.AnchoredPosition = Float2.Zero;
        UIImage img = go.AddComponent<UIImage>();
        img.Color = new Color(1f, 1f, 1f, 0.4f);
    }

    [MenuItem("GameObject/UI/Slider", priority: 75, Icon = EditorIcons.Sliders)]
    static void CreateUISlider()
    {
        GameObject go = NewUIElement("Slider", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(200f, 24f);
        UIImage bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.20f, 0.20f, 0.24f, 1f);
        UISlider slider = go.AddComponent<UISlider>();

        GameObject fillGo = HierarchyPanel.CreateGameObject("Fill", go, select: false, beginRename: false);
        fillGo.EnsureRectTransform();
        UIImage fill = fillGo.AddComponent<UIImage>();
        fill.Color = new Color(0.38f, 0.55f, 0.95f, 1f);
        fill.RaycastTarget = false;

        GameObject handleGo = HierarchyPanel.CreateGameObject("Handle", go, select: false, beginRename: false);
        handleGo.EnsureRectTransform();
        handleGo.RectTransform!.SizeDelta = new Float2(20f, 0f);
        UIImage handle = handleGo.AddComponent<UIImage>();
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

        GameObject go = NewUIElement("Scroll View", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(240f, 180f);
        UIImage bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.14f, 0.14f, 0.17f, 1f);
        UIScrollRect scroll = go.AddComponent<UIScrollRect>();

        GameObject vpGo = HierarchyPanel.CreateGameObject("Viewport", go, select: false, beginRename: false);
        vpGo.EnsureRectTransform();
        RectTransform vpRt = vpGo.RectTransform!;
        vpRt.AnchorMin = Float2.Zero; vpRt.AnchorMax = Float2.One;
        vpRt.SizeDelta = new Float2(-bar, -bar);
        vpRt.AnchoredPosition = new Float2(-bar * 0.5f, bar * 0.5f);
        vpGo.AddComponent<RectMask>();

        GameObject contentGo = HierarchyPanel.CreateGameObject("Content", vpGo, select: false, beginRename: false);
        contentGo.EnsureRectTransform();
        RectTransform cRt = contentGo.RectTransform!;
        cRt.AnchorMin = new Float2(0f, 1f); cRt.AnchorMax = new Float2(0f, 1f);
        cRt.Pivot = new Float2(0f, 1f);
        cRt.SizeDelta = new Float2(400f, 400f); cRt.AnchoredPosition = Float2.Zero;

        UIScrollbar vBar = BuildScrollbar("Scrollbar Vertical", go, UIScrollbar.ScrollbarDirection.TopToBottom);
        RectTransform vRt = vBar.GameObject.RectTransform!;
        vRt.AnchorMin = new Float2(1f, 0f); vRt.AnchorMax = new Float2(1f, 1f);
        vRt.Pivot = new Float2(1f, 0.5f);
        vRt.SizeDelta = new Float2(bar, -bar); vRt.AnchoredPosition = new Float2(0f, bar * 0.5f);

        UIScrollbar hBar = BuildScrollbar("Scrollbar Horizontal", go, UIScrollbar.ScrollbarDirection.LeftToRight);
        RectTransform hRt = hBar.GameObject.RectTransform!;
        hRt.AnchorMin = new Float2(0f, 0f); hRt.AnchorMax = new Float2(1f, 0f);
        hRt.Pivot = new Float2(0.5f, 0f);
        hRt.SizeDelta = new Float2(-bar, bar); hRt.AnchoredPosition = new Float2(-bar * 0.5f, 0f);

        scroll.Viewport = vpRt;
        scroll.Content = cRt;
        scroll.HorizontalScrollbar = hBar;
        scroll.VerticalScrollbar = vBar;
    }

    [MenuItem("GameObject/UI/Toggle", priority: 78, Icon = EditorIcons.SquareCheck)]
    static void CreateUIToggle()
    {
        GameObject go = NewUIElement("Toggle", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(24f, 24f);
        UIImage box = go.AddComponent<UIImage>();
        box.Color = new Color(0.20f, 0.20f, 0.24f, 1f);
        UIToggle toggle = go.AddComponent<UIToggle>();

        GameObject checkGo = HierarchyPanel.CreateGameObject("Checkmark", go, select: false, beginRename: false);
        checkGo.EnsureRectTransform();
        RectTransform checkRt = checkGo.RectTransform!;
        checkRt.AnchorMin = Float2.Zero;
        checkRt.AnchorMax = Float2.One;
        checkRt.SizeDelta = new Float2(-8f, -8f);
        UIImage check = checkGo.AddComponent<UIImage>();
        check.Color = new Color(0.38f, 0.55f, 0.95f, 1f);
        check.RaycastTarget = false;

        toggle.TargetGraphic = box;
        toggle.Checkmark = check;
        toggle.IsOn = true;
    }

    [MenuItem("GameObject/UI/Rect Mask", priority: 77, Icon = EditorIcons.Square)]
    static void CreateUIRectMask()
    {
        GameObject go = NewUIElement("Rect Mask", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(200f, 200f);
        go.AddComponent<RectMask>();
    }

    [MenuItem("GameObject/UI/Input Field", priority: 78, Icon = EditorIcons.Keyboard)]
    static void CreateUIInputField()
    {
        GameObject go = NewUIElement("Input Field", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(200f, 32f);
        UIImage bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.12f, 0.12f, 0.15f, 1f);
        UIInputField field = go.AddComponent<UIInputField>();

        GameObject areaGo = HierarchyPanel.CreateGameObject("Text Area", go, select: false, beginRename: false);
        areaGo.EnsureRectTransform();
        RectTransform areaRt = areaGo.RectTransform!;
        areaRt.AnchorMin = Float2.Zero; areaRt.AnchorMax = Float2.One;
        areaRt.SizeDelta = new Float2(-16f, -8f); areaRt.AnchoredPosition = Float2.Zero;
        areaGo.AddComponent<RectMask>();

        GameObject selGo = HierarchyPanel.CreateGameObject("Selection", areaGo, select: false, beginRename: false);
        selGo.EnsureRectTransform();
        RectTransform selRt = selGo.RectTransform!;
        selRt.AnchorMin = new Float2(0f, 0f); selRt.AnchorMax = new Float2(0f, 1f);
        selRt.Pivot = new Float2(0f, 0.5f);
        selRt.SizeDelta = new Float2(0f, -4f); selRt.AnchoredPosition = Float2.Zero;
        UIImage selImg = selGo.AddComponent<UIImage>();
        selImg.RaycastTarget = false;

        GameObject phGo = HierarchyPanel.CreateGameObject("Placeholder", areaGo, select: false, beginRename: false);
        phGo.EnsureRectTransform();
        Stretch(phGo.RectTransform!);
        TextComponent placeholder = phGo.AddComponent<TextComponent>();
        placeholder.Text = "Enter text...";
        placeholder.Alignment = TextAlignment.CenterLeft;
        placeholder.Size = 16;
        placeholder.Color = new Color(0.5f, 0.5f, 0.55f, 1f);

        GameObject textGo = HierarchyPanel.CreateGameObject("Text", areaGo, select: false, beginRename: false);
        textGo.EnsureRectTransform();
        Stretch(textGo.RectTransform!);
        TextComponent text = textGo.AddComponent<TextComponent>();
        text.Alignment = TextAlignment.CenterLeft;
        text.Size = 16;
        text.Color = new Color(0.90f, 0.90f, 0.92f, 1f);

        GameObject caretGo = HierarchyPanel.CreateGameObject("Caret", areaGo, select: false, beginRename: false);
        caretGo.EnsureRectTransform();
        RectTransform caretRt = caretGo.RectTransform!;
        caretRt.AnchorMin = new Float2(0f, 0f); caretRt.AnchorMax = new Float2(0f, 1f);
        caretRt.Pivot = new Float2(0f, 0.5f);
        caretRt.SizeDelta = new Float2(1.5f, -4f); caretRt.AnchoredPosition = Float2.Zero;
        UIImage caretImg = caretGo.AddComponent<UIImage>();
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
        GameObject go = NewUIElement("Dropdown", MenuContext.ActiveGameObject);
        go.RectTransform!.SizeDelta = new Float2(200f, 32f);
        UIImage bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.18f, 0.18f, 0.22f, 1f);
        UIDropdown dropdown = go.AddComponent<UIDropdown>();

        GameObject labelGo = HierarchyPanel.CreateGameObject("Label", go, select: false, beginRename: false);
        labelGo.EnsureRectTransform();
        RectTransform lrt = labelGo.RectTransform!;
        lrt.AnchorMin = Float2.Zero; lrt.AnchorMax = Float2.One;
        lrt.SizeDelta = new Float2(-16f, 0f); lrt.AnchoredPosition = new Float2(4f, 0f);
        TextComponent label = labelGo.AddComponent<TextComponent>();
        label.Alignment = TextAlignment.CenterLeft;
        label.Size = 16;
        label.Color = new Color(0.90f, 0.90f, 0.92f, 1f);

        GameObject optionsGo = HierarchyPanel.CreateGameObject("Options", go, select: false, beginRename: false);
        optionsGo.EnsureRectTransform();
        RectTransform ort = optionsGo.RectTransform!;
        ort.AnchorMin = new Float2(0f, 0f); ort.AnchorMax = new Float2(1f, 0f);
        ort.Pivot = new Float2(0.5f, 1f);
        ort.SizeDelta = new Float2(0f, 0f); ort.AnchoredPosition = Float2.Zero;
        UIImage optionsBg = optionsGo.AddComponent<UIImage>();
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
        GameObject go = HierarchyPanel.CreateGameObject("Event System", MenuContext.ActiveGameObject);
        go.AddComponent<EventSystem>();
    }

    [MenuItem("GameObject/Camera", priority: 100, Icon = EditorIcons.Camera, Separator = true)]
    static void CreateCamera()
    {
        GameObject go = HierarchyPanel.CreateGameObject("Camera", MenuContext.ActiveGameObject);
        go.AddComponent<Camera>().Profile = VolumeProfile.CreateCameraDefault("Camera Profile");
    }

    private static void CreatePrimitive(string name, DefaultModel model)
    {
        GameObject go = HierarchyPanel.CreateGameObject(name, MenuContext.ActiveGameObject);
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = AssetDatabase.Get<Mesh>(BuiltInAssets.GuidForMesh(model));
        renderer.Material = AssetDatabase.Get<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard));
    }

    private static GameObject NewUIElement(string name, GameObject? parent)
    {
        GameObject uiParent = ResolveCanvasParent(parent);
        GameObject go = HierarchyPanel.CreateGameObject(name, uiParent);
        go.EnsureRectTransform();
        EnsureEventSystem(go.Scene);
        return go;
    }

    private static void EnsureEventSystem(Scene? scene)
    {
        if (scene.IsNotValid()) scene = Scene.Current;
        if (scene.IsNotValid()) return;
        foreach (EventSystem? es in scene.FindObjectsOfType<EventSystem>())
            if (es.IsValid()) return;
        GameObject esGo = HierarchyPanel.CreateGameObject("Event System", null, select: false, beginRename: false);
        esGo.AddComponent<EventSystem>();
    }

    private static GameObject ResolveCanvasParent(GameObject? parent)
    {
        GameCanvas? canvas = parent.IsValid() ? parent.GetComponentInParent<GameCanvas>(includeSelf: true) : null;
        if (canvas.IsValid()) return parent!;

        Scene scene = Scene.Current;
        if (scene.IsValid())
        {
            foreach (GameCanvas? c in scene.FindObjectsOfType<GameCanvas>())
                if (c.IsValid()) return c.GameObject;
        }

        GameObject canvasGo = HierarchyPanel.CreateGameObject("Canvas", null, select: false, beginRename: false);
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
        GameObject go = HierarchyPanel.CreateGameObject(name, parent, select: false, beginRename: false);
        go.EnsureRectTransform();
        UIImage track = go.AddComponent<UIImage>();
        track.Color = new Color(0.10f, 0.10f, 0.13f, 1f);
        UIScrollbar bar = go.AddComponent<UIScrollbar>();
        bar.Direction = dir;
        GameObject handleGo = HierarchyPanel.CreateGameObject("Handle", go, select: false, beginRename: false);
        handleGo.EnsureRectTransform();
        UIImage handle = handleGo.AddComponent<UIImage>();
        handle.Color = new Color(0.42f, 0.42f, 0.48f, 1f);
        handle.RaycastTarget = false;
        bar.HandleRect = handleGo.RectTransform;
        bar.TargetGraphic = handle;
        bar.Size = 0.3f;
        return bar;
    }
}
