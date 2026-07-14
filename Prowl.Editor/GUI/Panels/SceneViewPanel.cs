using System;
using System.Linq;

using Prowl.OrigamiUI;
using Gizmo = Prowl.OrigamiUI.Gizmo;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;

namespace Prowl.Editor.GUI.Panels;

[EditorWindow("General/Scene")]
public class SceneViewPanel : DockPanel, IScriptReloadCleanup
{
    public override string Title => Loc.Get("panel.scene");
    public override string Icon => EditorIcons.Shapes;

    private EditorCamera? _editorCamera;

    // The editor camera caches the scene camera + cloned image effects (possibly user types).
    // Release them before a hot-reload so they don't pin the script AssemblyLoadContext.
    public void OnScriptReloadCleanup() => _editorCamera?.ReleaseSceneReferences();
    private Gizmo.TransformGizmo? _transformGizmo;
    private bool _wasGizmoActive;
    private Gizmo.ViewManipulatorGizmo? _viewManipulator;

    /// <summary>The most recently active SceneViewPanel's camera. Used by other panels for "Move to View" etc.</summary>
    public static EditorCamera? ActiveCamera { get; private set; }

    public static ISceneViewEditor? ActiveSceneViewEditor { get; private set; }
    public static GameObject? ActiveSceneViewTarget { get; private set; }

    public static void UpdateSceneViewEditor()
    {
        var selectedGO = Selection.GetSelected<GameObject>().FirstOrDefault();
        if (selectedGO == null || selectedGO == ActiveSceneViewTarget)
        {
            if (selectedGO == null && ActiveSceneViewEditor != null) DeactivateSceneViewEditor();
            return;
        }
        var editor = EditorRegistries.FindSceneViewEditor(selectedGO);
        if (editor != null)
        {
            if (ActiveSceneViewEditor?.GetType() == editor.GetType() && ActiveSceneViewTarget == selectedGO) return;
            DeactivateSceneViewEditor();
            ActiveSceneViewEditor = editor;
            ActiveSceneViewTarget = selectedGO;
            ActiveSceneViewEditor.OnActivate(selectedGO);
        }
        else if (ActiveSceneViewEditor != null)
            DeactivateSceneViewEditor();
    }

    public static void DeactivateSceneViewEditor()
    {
        ActiveSceneViewEditor?.OnDeactivate();
        ActiveSceneViewEditor = null;
        ActiveSceneViewTarget = null;
    }
    private Gizmo.TransformGizmoMode _gizmoMode = Gizmo.TransformGizmoMode.Translate;
    private Rect _viewportAbsoluteRect; // Cached absolute screen rect from layout
    private bool _gizmoActive; // Whether the gizmo should draw (selection exists)

    // Pose restored from disk by RestoreState; applied the first time the camera is created
    // (the camera itself is constructed lazily inside OnGUI because it needs graphics ready).
    private bool _hasPendingPose;
    private Float3 _pendingPos;
    private float _pendingYaw, _pendingPitch;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (_editorCamera == null)
        {
            _editorCamera = new EditorCamera();
            if (_hasPendingPose)
            {
                _editorCamera.SetPose(_pendingPos, _pendingYaw, _pendingPitch);
                _hasPendingPose = false;
            }
            if (_pendingGrid is bool pg) { _editorCamera.ShowGrid = pg; _pendingGrid = null; }
            if (_pendingGizmos is bool pz) { _editorCamera.ShowGizmos = pz; _pendingGizmos = null; }
        }
        ActiveCamera = _editorCamera;

        using (paper.Column("sv_root").Size(width, height).Enter())
        {
            DrawViewport(paper, font, width, height);
        }
    }

    // Grid / gizmo visibility live in the leaf's tab-bar header (right side)
    public override float HeaderWidth => 28f;
    public override void OnHeaderContent(Paper paper, float width, float height)
    {
        EditorGUI.HeaderIconButton(paper, "sv_hdr_settings", EditorIcons.Gear, () =>
            Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
            {
                b.Header(Loc.Get("panel.scene"));
                b.Toggle(Loc.Get("scene.show_grid"),
                    () => { if (_editorCamera != null) _editorCamera.ShowGrid = !_editorCamera.ShowGrid; },
                    () => _editorCamera?.ShowGrid ?? true);
                b.Toggle(Loc.Get("scene.show_gizmos"),
                    () => { if (_editorCamera != null) _editorCamera.ShowGizmos = !_editorCamera.ShowGizmos; },
                    () => _editorCamera?.ShowGizmos ?? true);
            }));
    }

    // Floating transform-tools panel, top-left of the viewport. The active scene-view
    // editor may replace the default gizmo-mode buttons with its own toolbar.
    private void DrawTransformTools(Paper paper, Scribe.FontFile font)
    {
        using (paper.Column("sv_tools")
            .PositionType(PositionType.SelfDirected)
            .Position(12, 12)
            .Width(34).Height(UnitValue.Auto)
            .Rounded(9).Padding(5, 5, 5, 5).ColBetween(3)
            .BackgroundColor(EditorTheme.Glass)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Enter())
        {
            var sceneEditor = ActiveSceneViewEditor;
            bool suppressDefault = sceneEditor != null && sceneEditor.DrawToolbar(paper, "sv_sce", font);
            if (!suppressDefault)
                DrawDefaultToolbar(paper, font);
        }
    }

    private void DrawDefaultToolbar(Paper paper, Scribe.FontFile font)
    {
        bool isTranslate = _gizmoMode == Gizmo.TransformGizmoMode.Translate;
        bool isRotate = _gizmoMode == Gizmo.TransformGizmoMode.Rotate;
        bool isScale = _gizmoMode == Gizmo.TransformGizmoMode.ScaleAll;
        bool isUniversal = _gizmoMode == Gizmo.TransformGizmoMode.Universal;

        paper.Box("sv_move_btn")
            .Width(24).Height(24).Rounded(6)
            .BackgroundColor(isTranslate ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(EditorIcons.ArrowsUpDownLeftRight, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => SetGizmoMode(Gizmo.TransformGizmoMode.Translate));

        paper.Box("sv_rotate_btn")
            .Width(24).Height(24).Rounded(6)
            .BackgroundColor(isRotate ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(EditorIcons.ArrowsRotate, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => SetGizmoMode(Gizmo.TransformGizmoMode.Rotate));

        paper.Box("sv_scale_btn")
            .Width(24).Height(24).Rounded(6)
            .BackgroundColor(isScale ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(EditorIcons.Maximize, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => SetGizmoMode(Gizmo.TransformGizmoMode.ScaleAll));

        paper.Box("sv_universal_btn")
            .Width(24).Height(24).Rounded(6)
            .BackgroundColor(isUniversal ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(EditorIcons.Expand, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => SetGizmoMode(Gizmo.TransformGizmoMode.Universal));
    }

    private void DrawViewport(Paper paper, Scribe.FontFile font, float width, float height)
    {
        if (_editorCamera == null || width <= 0 || height <= 0) return;

        uint rtWidth = (uint)MathF.Max(1, width);
        uint rtHeight = (uint)MathF.Max(1, height);
        _editorCamera.EnsureRenderTarget(rtWidth, rtHeight);

        var scene = Scene.Current;
        var rt = _editorCamera.RenderTarget;

        if (scene == null)
        {
            // No scene show message and create button
            using (paper.Column("sv_no_scene")
                .Size(width, height)
                .BackgroundColor(EditorTheme.Neutral300)
                .Enter())
            {
                paper.Box("sv_no_scene_spacer");

                paper.Box("sv_no_scene_text")
                    .Height(30)
                    .Text(Loc.Get("hierarchy.no_scene_loaded"), font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleCenter);

                using (paper.Row("sv_no_scene_btn_row")
                    .Height(30).RowBetween(8)
                    .Enter())
                {
                    paper.Box("sv_btn_spacer_l");
                    Origami.Button(paper, "sv_create_scene", $"{EditorIcons.Plus}  {Loc.Get("hierarchy.new_scene")}", () => CreateAndLoadDefaultScene()).Width(120).Show();
                    paper.Box("sv_btn_spacer_r");
                }

                paper.Box("sv_no_scene_spacer2");
            }
            return;
        }

        // Update scene view editor registry based on selection
        UpdateSceneViewEditor();

        // The scene view always presents and edits UI in world space (each canvas's ReferenceResolution
        // + GameObject transform) regardless of its RenderMode, so screen-space UI can be manipulated
        // like any other GameObject. This wraps input, gizmos AND the render so the canvas wireframe,
        // the element handles and the drawn mesh all agree on the same rect.
        bool sceneEditorConsumedInput = false;
        bool prevWorldSpace = GameCanvas.EditorWorldSpaceOverride;
        GameCanvas.EditorWorldSpaceOverride = true;
        try
        {
            // Let active scene editor handle input before gizmo
            var activeSceneEditor = ActiveSceneViewEditor;
            if (activeSceneEditor != null && _editorCamera != null)
            {
                var cam = _editorCamera.Camera;
                if (cam != null)
                {
                    // Both paper.PointerPos and _viewportAbsoluteRect are in Paper-logical space.
                    Float2 mouseLocal = paper.PointerPos - new Float2((float)_viewportAbsoluteRect.Min.X, (float)_viewportAbsoluteRect.Min.Y);
                    Float2 viewSize = new Float2(width, height);
                    Ray mouseRay = cam.ScreenPointToRay(mouseLocal, viewSize);
                    // Use Paper's hover state which respects overlays/popups, not just bounds
                    bool hovered = paper.IsParentHovered;
                    Rect viewport = new Rect(0, 0, (float)viewSize.X, (float)viewSize.Y);
                    sceneEditorConsumedInput = activeSceneEditor.OnSceneInput(cam, scene, viewport, mouseRay, mouseLocal, hovered);
                }
            }

            // Update transform gizmo for selected objects (skip if scene editor consumed input)
            if (!sceneEditorConsumedInput)
                UpdateTransformGizmo(paper, scene, width, height);

            // Render scene (gizmos drawn via Debug.DrawLine render into the RT)
            DrawSelectionGizmos();
            _editorCamera.Render(scene);
        }
        finally
        {
            GameCanvas.EditorWorldSpaceOverride = prevWorldSpace;
        }

        if (rt != null && rt.MainTexture != null)
        {
            paper.Box("sv_viewport")
                .Size(width, height)
                .OnPostLayout((handle, rect) =>
                {
                    // Cache absolute rect for gizmo coordinate space
                    _viewportAbsoluteRect = rect;

                    // Draw RT
                    paper.Draw(ref handle, (canvas, r) =>
                    {
                        float rx = (float)r.Min.X;
                        float ry = (float)r.Min.Y;
                        float rw = (float)r.Size.X;
                        float rh = (float)r.Size.Y;

                        // Top corners stay square so the viewport butts flush against the panel header;
                        // only the bottom two are rounded.
                        float rnd = EditorTheme.Roundness;

                        // Draw RT with flipped Y OpenGL RT has Y=0 at bottom
                        canvas.SetBrushTexture(rt.MainTexture);
                        // TextureTransform maps screen rect to UV: flip V by translating +1 and scaling -1 on Y
                        canvas.SetBrushTextureTransform(
                            Transform2D.CreateTranslation(rx, ry + rh) *
                            Transform2D.CreateScale(rw, -rh));
                        canvas.RoundedRectFilled(rx, ry, rw, rh, 0, 0, rnd, rnd, Color.White);
                        canvas.ClearBrushTexture();

                        // Inset the border by half its width so the full stroke sits on top of the texture edge.
                        canvas.RoundedRect(rx + 1, ry + 1, rw - 2, rh - 2, 0, 0, rnd, rnd);
                        canvas.SetStrokeColor(EditorTheme.Purple500);
                        canvas.SetStrokeWidth(2);
                        canvas.Stroke();
                    });

                    // Draw transform gizmo as 2D overlay (hidden when scene editor consumes input)
                    if (_transformGizmo != null && _gizmoActive && !sceneEditorConsumedInput)
                    {
                        paper.DrawForeground(ref handle, (canvas2, r2) =>
                        {
                            _transformGizmo.Draw(canvas2);
                        });
                    }

                    // Draw scene view editor overlay
                    var sceneEditorOverlay = ActiveSceneViewEditor;
                    if (sceneEditorOverlay != null)
                    {
                        paper.DrawForeground(ref handle, (canvas2, r2) =>
                        {
                            sceneEditorOverlay.DrawOverlay(canvas2, r2);
                        });
                    }
                })
                .OnClick(0, (_, e) =>
                {
                    if (!Input.IsAltPressed && !sceneEditorConsumedInput)
                    {
                        Float2 localPos = new Float2((float)e.RelativePosition.X, (float)e.RelativePosition.Y);
                        Float2 panelSize = new Float2(width, height);
                        PickObject(scene, localPos, panelSize);
                    }
                });

            // Camera input
            bool isHovered = paper.IsParentHovered;
            _editorCamera.ProcessInput(
                (float)Time.UnscaledDeltaTime,
                isHovered,
                paper.PointerPos,
                new Float2((float)_viewportAbsoluteRect.Min.X, (float)_viewportAbsoluteRect.Min.Y),
                new Float2(width, height));

            // Scene view keyboard shortcuts (only when hovered and not right-click flying)
            if (isHovered && !ShortcutManager.IsRebinding && !Input.GetMouseButton(1))
            {
                if (ShortcutManager.IsPressed("Scene/Delete"))
                {
                    // Shared with HierarchyPanel so the viewport enforces the same prefab
                    // structural-child protection and undo registration as the Hierarchy's Delete.
                    foreach (var go in HierarchyPanel.ExcludeNestedSelections(Selection.GetSelected<GameObject>().ToList()))
                        HierarchyPanel.DeleteGameObject(go);
                    Selection.Clear();
                    EditorSceneManager.IsDirty = true;
                }
                else if (ShortcutManager.IsPressed("Scene/Duplicate"))
                {
                    var dupes = GameObjectClipboard.Duplicate(Selection.GetSelected<GameObject>().ToList());
                    foreach (var d in dupes) Undo.RegisterCreatedObject(d, "Duplicate");
                }
                else if (ShortcutManager.IsPressed("Scene/Copy"))
                {
                    GameObjectClipboard.Copy(Selection.GetSelected<GameObject>().ToList());
                }
                else if (ShortcutManager.IsPressed("Scene/Paste"))
                {
                    var pasted = GameObjectClipboard.Paste();
                    foreach (var p in pasted) Undo.RegisterCreatedObject(p, "Paste");
                }

                // Gizmo tool switching
                if (ShortcutManager.IsPressed("Scene/ToolTranslate"))
                    SetGizmoMode(Gizmo.TransformGizmoMode.Translate);
                else if (ShortcutManager.IsPressed("Scene/ToolRotate"))
                    SetGizmoMode(Gizmo.TransformGizmoMode.Rotate);
                else if (ShortcutManager.IsPressed("Scene/ToolScale"))
                    SetGizmoMode(Gizmo.TransformGizmoMode.ScaleAll);
                else if (ShortcutManager.IsPressed("Scene/ToolUniversal"))
                    SetGizmoMode(Gizmo.TransformGizmoMode.Universal);
            }

            // Accept asset drops via registry-discovered handlers
            if (isHovered && DragDrop.IsDraggingType<AssetDragPayload>())
            {
                var dragPayload = (AssetDragPayload)DragDrop.Payload!;
                var handler = EditorRegistries.FindSceneDropHandler(dragPayload.AssetType);

                if (handler != null)
                {
                    paper.Box("sv_drop_indicator")
                        .PositionType(PositionType.SelfDirected)
                        .Position(0, height - 24).Size(width, 24)
                        .BackgroundColor(Color.FromArgb(150, EditorTheme.Neutral400))
                        .IsNotInteractable()
                        .Text(handler.DropHint, font)
                        .TextColor(EditorTheme.Purple400)
                        .FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleCenter);
                }
            }

            if (isHovered && !DragDrop.IsDragging && DragDrop.Payload is AssetDragPayload assetDrop)
            {
                var handler = EditorRegistries.FindSceneDropHandler(assetDrop.AssetType);
                if (handler != null)
                {
                    // Convert Paper-space pointer to viewport-local using the cached viewport
                    // rect. CurrentParent here is an enclosing container (toolbar row etc.),
                    // so subtracting its origin put the drop off to the side of the camera.
                    Float2 mouseLocal = paper.PointerPos - new Float2(
                        (float)_viewportAbsoluteRect.Min.X, (float)_viewportAbsoluteRect.Min.Y);

                    handler.Handle(assetDrop, new SceneDropContext
                    {
                        Scene = scene,
                        Camera = _editorCamera!,
                        MouseLocal = mouseLocal,
                        PanelSize = new Float2(width, height),
                    });
                    DragDrop.EndDrag();
                }
            }

            // Floating transform-tools panel (top-left)
            DrawTransformTools(paper, font);

            // Speed indicator (shows briefly when scroll changes fly speed)
            DrawSpeedIndicator(paper, font, width, height);

            // View manipulator (orientation cube) drawn as 2D overlay on top-right
            DrawViewManipulator(paper, font, width, height);
        }
    }

    internal static GameObject? PickObjectAt(Scene scene, EditorCamera camera, Float2 screenPos, Float2 panelSize)
    {
        var ray = camera.ScreenPointToRay(screenPos, panelSize);

        GameObject? bestHit = null;
        float bestDist = float.MaxValue;

        foreach (var go in scene.ActiveObjects)
        {
            if (go.HideFlags.HasFlag(HideFlags.Hide)) continue;

            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.EnabledInHierarchy && meshRenderer.Raycast(ray, out float dist))
            {
                if (dist < bestDist) { bestDist = dist; bestHit = go; }
                continue;
            }

        }

        // UI elements aren't visible to MeshRenderer raycasts but should still be selectable from the
        // scene view. The scene view lays UI out in world space (ReferenceResolution + transform), so
        // pick with the same world-space override or the design-space hit test would disagree with
        // what's drawn.
        bool prevWorldSpace = GameCanvas.EditorWorldSpaceOverride;
        GameCanvas.EditorWorldSpaceOverride = true;
        try
        {
            GameObject? uiHit = UIPicker.Pick(scene, ray);
            if (uiHit != null) return uiHit;
        }
        finally
        {
            GameCanvas.EditorWorldSpaceOverride = prevWorldSpace;
        }

        return bestHit;
    }

    private void PickObject(Scene scene, Float2 screenPos, Float2 panelSize)
    {
        var bestHit = PickObjectAt(scene, _editorCamera!, screenPos, panelSize);

        if (bestHit != null)
        {
            if (Input.IsCtrlPressed)
                Selection.ToggleSelection(bestHit);
            else
                Selection.Select(bestHit);

            // Ping so the Hierarchy scrolls to and briefly highlights the clicked GO.
            Selection.Ping(bestHit.Identifier);
        }
        else if (!Input.IsCtrlPressed && !Input.IsShiftPressed)
        {
            Selection.Clear();
        }
    }

    public override bool SerializeState(System.Text.Json.Nodes.JsonObject state)
    {
        if (_editorCamera == null) return false;
        var p = _editorCamera.Position;
        state["px"] = p.X; state["py"] = p.Y; state["pz"] = p.Z;
        state["yaw"] = _editorCamera.Yaw;
        state["pitch"] = _editorCamera.Pitch;
        state["grid"] = _editorCamera.ShowGrid;
        state["gizmos"] = _editorCamera.ShowGizmos;
        return true;
    }

    public override void RestoreState(System.Text.Json.Nodes.JsonObject state)
    {
        float px = state["px"]?.GetValue<float>() ?? 0f;
        float py = state["py"]?.GetValue<float>() ?? 5f;
        float pz = state["pz"]?.GetValue<float>() ?? -15f;
        _pendingPos = new Float3(px, py, pz);
        _pendingYaw = state["yaw"]?.GetValue<float>() ?? 0f;
        _pendingPitch = state["pitch"]?.GetValue<float>() ?? 15f;
        _hasPendingPose = true;

        // Camera is created lazily in OnGUI; stash toggles and apply when it exists.
        _pendingGrid = state["grid"]?.GetValue<bool>();
        _pendingGizmos = state["gizmos"]?.GetValue<bool>();
    }

    private bool? _pendingGrid;
    private bool? _pendingGizmos;

    /// <summary>
    /// Create a default scene with camera, light, floor, and cubes, and load it.
    /// </summary>
    public static void CreateAndLoadDefaultScene()
    {
        var scene = new Scene();
        scene.Name = "Untitled Scene";

        var defaultMat = new AssetRef<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard));
        var cubeMesh = new AssetRef<Mesh>(BuiltInAssets.GuidForMesh(DefaultModel.Cube));
        var planeMesh = new AssetRef<Mesh>(BuiltInAssets.GuidForMesh(DefaultModel.Plane));

        // Main Camera
        var camGo = new GameObject("Main Camera");
        camGo.Tag = "Main Camera";
        camGo.Transform.Position = new Float3(0, 5, -15);
        camGo.Transform.LocalEulerAngles = new Float3(15, 0, 0);
        var cam = camGo.AddComponent<Camera>();
        cam.Depth = -1;
        cam.HDR = true;
        scene.Add(camGo);

        // Directional Light
        var lightGo = new GameObject("Directional Light");
        lightGo.Transform.LocalEulerAngles = new Float3(-45, 45, 0);
        var light = lightGo.AddComponent<DirectionalLight>();
        light.Intensity = 1f;
        scene.Add(lightGo);

        // Floor
        var floorGo = new GameObject("Floor");
        floorGo.Transform.Position = new Float3(0, 0, 0);
        floorGo.Transform.LocalScale = new Float3(1, 1, 1);
        var floorRenderer = floorGo.AddComponent<MeshRenderer>();
        floorRenderer.Mesh = planeMesh;
        floorRenderer.Material = defaultMat;
        scene.Add(floorGo);

        // Cube 1
        var cube1 = new GameObject("Cube");
        cube1.Transform.Position = new Float3(0, 0.5f, 0);
        var cube1Renderer = cube1.AddComponent<MeshRenderer>();
        cube1Renderer.Mesh = cubeMesh;
        cube1Renderer.Material = defaultMat;
        scene.Add(cube1);

        // Cube 2
        var cube2 = new GameObject("Cube (1)");
        cube2.Transform.Position = new Float3(2, 0.5f, 1);
        var cube2Renderer = cube2.AddComponent<MeshRenderer>();
        cube2Renderer.Mesh = cubeMesh;
        cube2Renderer.Material = defaultMat;
        scene.Add(cube2);

        Scene.Load(scene);
        Undo.Clear();
        Runtime.Debug.Log("Created default scene.");
    }

    /// <summary>
    /// Raycast into the scene to find a drop position. Falls back to the XZ plane at Y=0.
    /// </summary>
    internal static Float3 GetDropPosition(Scene scene, EditorCamera camera, Float2 screenPos, Float2 panelSize)
    {
        var ray = camera.ScreenPointToRay(screenPos, panelSize);

        // Try raycasting against scene objects first
        float bestDist = float.MaxValue;
        Float3 bestPos = Float3.Zero;
        bool hit = false;

        foreach (var go in scene.ActiveObjects)
        {
            if (go.HideFlags.HasFlag(HideFlags.Hide)) continue;

            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.Raycast(ray, out float dist))
            {
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPos = ray.Origin + ray.Direction * dist;
                    hit = true;
                }
            }

        }

        if (hit) return bestPos;

        // Fallback: intersect with XZ plane at Y=0
        if (MathF.Abs(ray.Direction.Y) > 0.0001f)
        {
            float t = -ray.Origin.Y / ray.Direction.Y;
            if (t > 0)
                return ray.Origin + ray.Direction * t;
        }

        // Last resort: place 10 units in front of camera
        return ray.Origin + ray.Direction * 10f;
    }

    private void DrawSelectionGizmos()
    {
        foreach (var obj in Selection.Selected)
        {
            if (obj is not GameObject go) continue;

            var col = new Vector.Color(0.3f, 0.6f, 1f, 1f);

            // Collect world-space AABB from all renderers in this GO and its children
            Float3 min = new(float.MaxValue), max = new(float.MinValue);
            bool found = false;
            CollectRendererBounds(go, ref min, ref max, ref found);

            if (found)
            {
                // Draw axis-aligned wireframe box around combined world bounds
                Float3 center = (min + max) * 0.5f;
                Float3 halfExtents = (max - min) * 0.5f;
                Debug.DrawWireCube(center, halfExtents, col);
            }
            else
            {
                Float3 pos = go.Transform.Position;
                float s = 0.3f;
                Debug.DrawLine(pos - Float3.UnitX * s, pos + Float3.UnitX * s, col);
                Debug.DrawLine(pos - Float3.UnitY * s, pos + Float3.UnitY * s, col);
                Debug.DrawLine(pos - Float3.UnitZ * s, pos + Float3.UnitZ * s, col);
            }
        }
    }

    private static void CollectRendererBounds(GameObject go, ref Float3 min, ref Float3 max, ref bool found)
    {
        // Check MeshRenderer
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mr.Mesh.Res != null)
            ExpandBounds(mr.Mesh.Res.bounds, go.Transform.LocalToWorldMatrix, ref min, ref max, ref found);

        // Check SkinnedMeshRenderer
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr != null && smr.SharedMesh.Res != null)
            ExpandBounds(smr.SharedMesh.Res.bounds, go.Transform.LocalToWorldMatrix, ref min, ref max, ref found);

        // Recurse into children
        foreach (var child in go.Children)
            CollectRendererBounds(child, ref min, ref max, ref found);
    }

    private static void ExpandBounds(AABB localBounds, Float4x4 matrix, ref Float3 min, ref Float3 max, ref bool found)
    {
        // Transform all 8 corners to world space and expand the combined AABB
        for (int i = 0; i < 8; i++)
        {
            Float3 corner = Float4x4.TransformPoint(new Float3(
                (i & 1) == 0 ? localBounds.Min.X : localBounds.Max.X,
                (i & 2) == 0 ? localBounds.Min.Y : localBounds.Max.Y,
                (i & 4) == 0 ? localBounds.Min.Z : localBounds.Max.Z), matrix);
            min = new Float3(MathF.Min(min.X, corner.X), MathF.Min(min.Y, corner.Y), MathF.Min(min.Z, corner.Z));
            max = new Float3(MathF.Max(max.X, corner.X), MathF.Max(max.Y, corner.Y), MathF.Max(max.Z, corner.Z));
        }
        found = true;
    }

    // ================================================================
    //  Transform Gizmo
    // ================================================================

    private void SetGizmoMode(Gizmo.TransformGizmoMode mode)
    {
        _gizmoMode = mode;
        _transformGizmo?.SetMode(mode);
    }

    private void UpdateTransformGizmo(Paper paper, Scene scene, float width, float height)
    {
        _gizmoActive = false;
        if (_editorCamera == null) return;

        // Only show gizmo when GameObjects are selected
        var selectedGOs = Selection.GetSelected<GameObject>().GetEnumerator();
        if (!selectedGOs.MoveNext()) return;

        _gizmoActive = true;

        var firstGO = selectedGOs.Current;
        if (firstGO == null) return;

        // Create gizmo if needed
        _transformGizmo ??= new Gizmo.TransformGizmo(_gizmoMode);

        // Compute the center of all selected objects
        Float3 center = Float3.Zero;
        Quaternion rotation = Quaternion.Identity;
        Float3 scale = Float3.One;
        int count = 0;

        foreach (var go in Selection.GetSelected<GameObject>())
        {
            center += go.Transform.Position;
            count++;
        }
        if (count > 0) center /= count;

        // Use the first object's rotation/scale for the gizmo orientation
        rotation = firstGO.Transform.Rotation;
        scale = firstGO.Transform.LossyScale;

        // Update gizmo use absolute screen rect so coordinates match DrawForeground
        var cam = _editorCamera.Camera;
        var camGo = cam.GameObject;

        _transformGizmo.UpdateCamera(_viewportAbsoluteRect, cam.ViewMatrix, cam.ProjectionMatrix,
            camGo.Transform.Up, camGo.Transform.Forward, camGo.Transform.Right, camGo.Transform.Position);
        _transformGizmo.SetTransform(center, rotation, scale);

        // Mouse in Paper-logical space, matching _viewportAbsoluteRect (also Paper-logical).
        Float2 mouseAbs = paper.PointerPos;
        Float2 mouseLocal = mouseAbs - new Float2((float)_viewportAbsoluteRect.Min.X, (float)_viewportAbsoluteRect.Min.Y);
        var ray = _editorCamera.ScreenPointToRay(mouseLocal, new Float2(width, height));

        bool blockPicking = Input.GetMouseButton(1) || Input.GetMouseButton(2); // Don't pick while camera moving

        // Input state for the gizmo (Origami gizmo has no Runtime dependency)
        _transformGizmo.Snapping = Input.GetKey(KeyCode.ControlLeft) || Input.GetKey(KeyCode.ControlRight);
        _transformGizmo.IsShiftDown = Input.GetKey(KeyCode.ShiftLeft) || Input.GetKey(KeyCode.ShiftRight);
        _transformGizmo.IsMouseDown = Input.GetMouseButtonDown(0);
        _transformGizmo.IsMouseUp = Input.GetMouseButtonUp(0);

        var result = _transformGizmo.Update(ray, mouseAbs, blockPicking);

        // Gizmo drawing happens in the viewport's DrawForeground callback (needs canvas)

        // Continuous undo: track gizmo drag start/end
        bool gizmoActive = result.HasValue;
        if (gizmoActive && !_wasGizmoActive)
            Undo.BeginContinuous(Selection.GetSelected<GameObject>().ToArray(), "Transform");
        if (!gizmoActive && _wasGizmoActive)
            Undo.EndContinuous();
        _wasGizmoActive = gizmoActive;

        if (result.HasValue)
        {
            var r = result.Value;

            // Apply translation
            if (r.TranslationDelta.HasValue)
            {
                foreach (var go in Selection.GetSelected<GameObject>())
                    go.Transform.Position += r.TranslationDelta.Value;
            }

            // Apply rotation
            if (r.RotationDelta.HasValue && r.RotationAxis.HasValue)
            {
                var rotDelta = Quaternion.AxisAngle(r.RotationAxis.Value, r.RotationDelta.Value);
                foreach (var go in Selection.GetSelected<GameObject>())
                    go.Transform.Rotation = rotDelta * go.Transform.Rotation;
            }

            // Apply scale
            if (r.ScaleDelta.HasValue)
            {
                foreach (var go in Selection.GetSelected<GameObject>())
                    go.Transform.LocalScale *= r.ScaleDelta.Value;
            }

            EditorSceneManager.IsDirty = true;
        }
    }

    // ================================================================
    //  View Manipulator (orientation cube)
    // ================================================================

    private void DrawSpeedIndicator(Paper paper, Scribe.FontFile font, float width, float height)
    {
        if (_editorCamera == null) return;

        double elapsed = Time.UnscaledTotalTime - _editorCamera.SpeedChangedTime;
        if (elapsed > 1.5) return; // Show for 1.5 seconds

        float alpha = elapsed < 1.0 ? 1f : 1f - (float)(elapsed - 1.0) / 0.5f;
        byte a = (byte)(alpha * 180);
        byte ta = (byte)(alpha * 255);

        float boxW = 80, boxH = 32;
        float x = (width - boxW) / 2f;
        float y = (height - boxH) / 2f;

        paper.Box("sv_speed_hud")
            .PositionType(PositionType.SelfDirected)
            .Position(x, y).Size(boxW, boxH)
            .BackgroundColor(Color.FromArgb(a, EditorTheme.Neutral400))
            .Rounded(6)
            .IsNotInteractable()
            .Text($"{_editorCamera.MoveSpeed:F1}", font)
            .TextColor(Color.FromArgb(ta, EditorTheme.Ink500))
            .FontSize(18f)
            .Alignment(TextAlignment.MiddleCenter);
    }

    private void DrawViewManipulator(Paper paper, Scribe.FontFile font, float width, float height)
    {
        if (_editorCamera == null) return;

        _viewManipulator ??= new Gizmo.ViewManipulatorGizmo();

        float cubeSize = 80;

        _viewManipulator.SetCamera(_editorCamera.Camera.GameObject.Transform.Forward,
            _editorCamera.Camera.GameObject.Transform.Up);

        // Draw as overlay on top of the scene use SelfDirected + DrawForeground
        paper.Box("sv_view_manip")
            .PositionType(PositionType.SelfDirected)
            .Position(width - cubeSize - 8, 12)
            .Size(cubeSize, cubeSize)
            .OnPostLayout((handle, rect) => paper.DrawForeground(ref handle, (canvas, r) =>
            {
                // Use the absolute rect from layout for the view manipulator
                _viewManipulator.SetRect(r);

                bool blockPicking = _transformGizmo?.IsOver ?? false;
                bool clicked = Input.GetMouseButtonDown(0);
                Float2 mousePos = paper.PointerPos;

                if (_viewManipulator.Update(canvas, mousePos, clicked, blockPicking, out var newForward))
                {
                    // Snap camera to face direction
                    // Calculate yaw/pitch from the new forward vector
                    float yaw = MathF.Atan2(newForward.X, newForward.Z) * Gizmo.GizmoUtils.Rad2Deg;
                    float pitch = MathF.Asin(-newForward.Y) * Gizmo.GizmoUtils.Rad2Deg;
                    _editorCamera.SetOrientation(yaw, pitch);
                }
            }));
    }
}
