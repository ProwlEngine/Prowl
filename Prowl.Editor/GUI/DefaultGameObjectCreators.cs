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

    [CreateGameObjectMenu("3D Object/Text Mesh", Icon = EditorIcons.Font, Order = 14, Separator = true)]
    static void CreateTextMesh(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Text Mesh", parent);
        var text = go.AddComponent<TextMeshComponent>();
        text.Text = "New Text";
    }

    [CreateGameObjectMenu("3D Object/Terrain", Icon = EditorIcons.Mountain, Order = 15)]
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

    [CreateGameObjectMenu("Light/Directional Light", Icon = EditorIcons.Sun)]
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

    [CreateGameObjectMenu("Effects/Fog/Global", Icon = EditorIcons.Cloud, Order = 25)]
    static void CreateGlobalFogVolume(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Global Fog Volume", parent);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Global;
    }

    [CreateGameObjectMenu("Effects/Fog/Box", Icon = EditorIcons.Cube, Order = 26)]
    static void CreateBoxFogVolume(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Box Fog Volume", parent);
        go.Transform.LocalScale = new Float3(2, 2, 2);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Box;
    }

    [CreateGameObjectMenu("Effects/Fog/Sphere", Icon = EditorIcons.CircleDot, Order = 27)]
    static void CreateSphereFogVolume(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Sphere Fog Volume", parent);
        go.Transform.LocalScale = new Float3(3, 3, 3);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Sphere;
    }

    [CreateGameObjectMenu("Effects/Fog/Cylinder", Icon = EditorIcons.Circle, Order = 28)]
    static void CreateCylinderFogVolume(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Cylinder Fog Volume", parent);
        go.Transform.LocalScale = new Float3(2, 3, 2);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Cylinder;
    }

    [CreateGameObjectMenu("Effects/Fog/Cone", Icon = EditorIcons.Bullseye, Order = 29)]
    static void CreateConeFogVolume(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Cone Fog Volume", parent);
        go.Transform.LocalScale = new Float3(1, 4, 1);
        var v = go.AddComponent<FogVolume>();
        v.Shape = FogVolumeShape.Cone;
    }

    // ---- Particle System ----

    [CreateGameObjectMenu("Effects/Particle System", Icon = EditorIcons.SprayCanSparkles, Order = 60)]
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

    // ---- Audio ----

    [CreateGameObjectMenu("Audio/Audio Source", Icon = EditorIcons.VolumeHigh, Order = 30)]
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

    // ---- UI ----

    [CreateGameObjectMenu("UI/Canvas", Icon = EditorIcons.BorderAll, Order = 50)]
    static void CreateCanvas(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Canvas", parent);
        go.EnsureRectTransform();
        go.AddComponent<GameCanvas>();
    }

    [CreateGameObjectMenu("UI/Event System", Icon = EditorIcons.ArrowPointer, Order = 61)]
    static void CreateEventSystem(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Event System", parent);
        go.AddComponent<EventSystem>();
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


    [CreateGameObjectMenu("UI/Button", Icon = EditorIcons.MobileButton, Order = 53)]
    static void CreateUIButton(GameObject? parent)
    {
        var go = NewUIElement("Button", parent);
        go.RectTransform!.SizeDelta = new Float2(100f, 100f);
        var image = go.AddComponent<UIImage>();
        var button = go.AddComponent<UIButton>();
        button.TargetGraphic = image;
    }

    [CreateGameObjectMenu("UI/Slider", Icon = EditorIcons.Sliders, Order = 55)]
    static void CreateUISlider(GameObject? parent)
    {
        var go = NewUIElement("Slider", parent);
        go.RectTransform!.SizeDelta = new Float2(200f, 24f);
        var bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.20f, 0.20f, 0.24f, 1f);
        var slider = go.AddComponent<UISlider>();

        // Fill bar - the slider drives its anchors from the value.
        var fillGo = HierarchyPanel.CreateGameObject("Fill", go, select: false, beginRename: false);
        fillGo.EnsureRectTransform();
        var fill = fillGo.AddComponent<UIImage>();
        fill.Color = new Color(0.38f, 0.55f, 0.95f, 1f);
        fill.RaycastTarget = false; // the slider (background) receives the drag for the whole track

        // Handle / knob.
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

    [CreateGameObjectMenu("UI/Scroll View", Icon = EditorIcons.RectangleList, Order = 56)]
    static void CreateUIScrollView(GameObject? parent)
    {
        const float bar = 12f; // scrollbar thickness / gutter

        var go = NewUIElement("Scroll View", parent);
        go.RectTransform!.SizeDelta = new Float2(240f, 180f);
        var bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.14f, 0.14f, 0.17f, 1f);
        var scroll = go.AddComponent<UIScrollRect>();

        // Viewport: fills the view minus the right/bottom scrollbar gutters, and clips (RectMask) the content.
        var vpGo = HierarchyPanel.CreateGameObject("Viewport", go, select: false, beginRename: false);
        vpGo.EnsureRectTransform();
        var vpRt = vpGo.RectTransform!;
        vpRt.AnchorMin = Float2.Zero; vpRt.AnchorMax = Float2.One;
        vpRt.SizeDelta = new Float2(-bar, -bar);
        vpRt.AnchoredPosition = new Float2(-bar * 0.5f, bar * 0.5f); // gutter on the right and bottom
        vpGo.AddComponent<RectMask>();

        // Content: a fixed-size box anchored to the viewport's top-left corner, larger than the viewport on
        // both axes so it scrolls horizontally and vertically. Add children under here (or a size fitter).
        var contentGo = HierarchyPanel.CreateGameObject("Content", vpGo, select: false, beginRename: false);
        contentGo.EnsureRectTransform();
        var cRt = contentGo.RectTransform!;
        cRt.AnchorMin = new Float2(0f, 1f); cRt.AnchorMax = new Float2(0f, 1f);
        cRt.Pivot = new Float2(0f, 1f);
        cRt.SizeDelta = new Float2(400f, 400f); cRt.AnchoredPosition = Float2.Zero;

        // Vertical scrollbar down the right edge (leaving the bottom-right corner for the horizontal one).
        var vBar = BuildScrollbar("Scrollbar Vertical", go, UIScrollbar.ScrollbarDirection.TopToBottom);
        var vRt = vBar.GameObject.RectTransform!;
        vRt.AnchorMin = new Float2(1f, 0f); vRt.AnchorMax = new Float2(1f, 1f);
        vRt.Pivot = new Float2(1f, 0.5f);
        vRt.SizeDelta = new Float2(bar, -bar); vRt.AnchoredPosition = new Float2(0f, bar * 0.5f);

        // Horizontal scrollbar along the bottom edge.
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

    // Scrollbars are not a standalone widget - they exist only inside a Scroll View, which builds and
    // manages them (use a UI/Slider for a general-purpose bar). Creates a scrollbar (track + handle) as a
    // child of `parent` and returns the component.
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
        handle.RaycastTarget = false; // the track receives clicks/drags across its whole length

        bar.HandleRect = handleGo.RectTransform;
        bar.TargetGraphic = handle;
        bar.Size = 0.3f;
        return bar;
    }

    [CreateGameObjectMenu("UI/Input Field", Icon = EditorIcons.Keyboard, Order = 58)]
    static void CreateUIInputField(GameObject? parent)
    {
        var go = NewUIElement("Input Field", parent);
        go.RectTransform!.SizeDelta = new Float2(200f, 32f);
        var bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.12f, 0.12f, 0.15f, 1f);
        var field = go.AddComponent<UIInputField>();

        // Text area: inset from the box and clipping (RectMask) the scrolled text.
        var areaGo = HierarchyPanel.CreateGameObject("Text Area", go, select: false, beginRename: false);
        areaGo.EnsureRectTransform();
        var areaRt = areaGo.RectTransform!;
        areaRt.AnchorMin = Float2.Zero; areaRt.AnchorMax = Float2.One;
        areaRt.SizeDelta = new Float2(-16f, -8f); areaRt.AnchoredPosition = Float2.Zero;
        areaGo.AddComponent<RectMask>();

        // Selection highlight (behind the text), then placeholder, text, and caret on top.
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

    [CreateGameObjectMenu("UI/Dropdown", Icon = EditorIcons.ChevronDown, Order = 59)]
    static void CreateUIDropdown(GameObject? parent)
    {
        var go = NewUIElement("Dropdown", parent);
        go.RectTransform!.SizeDelta = new Float2(200f, 32f);
        var bg = go.AddComponent<UIImage>();
        bg.Color = new Color(0.18f, 0.18f, 0.22f, 1f);
        var dropdown = go.AddComponent<UIDropdown>();

        // Caption showing the current selection.
        var labelGo = HierarchyPanel.CreateGameObject("Label", go, select: false, beginRename: false);
        labelGo.EnsureRectTransform();
        var lrt = labelGo.RectTransform!;
        lrt.AnchorMin = Float2.Zero; lrt.AnchorMax = Float2.One;
        lrt.SizeDelta = new Float2(-16f, 0f); lrt.AnchoredPosition = new Float2(4f, 0f);
        var label = labelGo.AddComponent<TextComponent>();
        label.Alignment = TextAlignment.CenterLeft;
        label.Size = 16;
        label.TextColor = new Color(0.90f, 0.90f, 0.92f, 1f);

        // Options panel: hangs below the box, disabled until the dropdown opens.
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
        dropdown.CaptionText = label; // setter refreshes the caption from the options
        dropdown.TargetGraphic = bg;
    }

    // Stretches a RectTransform to fill its parent with no offset.
    static void Stretch(RectTransform rt)
    {
        rt.AnchorMin = Float2.Zero;
        rt.AnchorMax = Float2.One;
        rt.SizeDelta = Float2.Zero;
        rt.AnchoredPosition = Float2.Zero;
    }

    [CreateGameObjectMenu("UI/Panel", Icon = EditorIcons.WindowMaximize, Order = 54)]
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

    [CreateGameObjectMenu("UI/Rect Mask", Icon = EditorIcons.Square, Order = 57)]
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
        EnsureEventSystem(go.Scene);
        return go;
    }

    // Interactive UI needs a scene EventSystem to receive input; create one if the scene has none. Done
    // when a UI widget is created (not by the Canvas itself), so a bare canvas stays dependency-free.
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

    // ---- Camera ----

    [CreateGameObjectMenu("Camera", Icon = EditorIcons.Camera, Order = 80)]
    static void CreateCamera(GameObject? parent)
    {
        var go = HierarchyPanel.CreateGameObject("Camera", parent);
        go.AddComponent<Camera>();
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
