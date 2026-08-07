using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite;
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

public class SceneViewPanel : DockPanel
{
    [MenuItem("Window/General/Scene", priority: 5)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(SceneViewPanel));

    public override string Title => Loc.Get("panel.scene");
    public override string Icon => EditorIcons.Shapes;

    private const string TransformControl = "sceneview_transform";
    private const string PickControl = "sceneview_pick";
    private const string ViewCubeControl = "sceneview_viewcube";

    private readonly HandleContext _handles = new();
    private readonly SceneToolContext _toolContext;
    private EditorCamera? _editorCamera;
    private Gizmo.TransformGizmo? _transformGizmo;
    private bool _wasGizmoActive;
    private Gizmo.ViewManipulatorGizmo? _viewManipulator;

    // Screen rect of the view manipulator, cached from layout so its control can register during the
    // input pass even though the widget itself updates inside the draw callback.
    private Float2 _viewCubeCenter;
    private float _viewCubeRadius;

    // Marquee (box) selection, driven by the same control as click-picking.
    private const float MarqueeThreshold = 4f;
    private bool _marqueeActive;
    private Float2 _marqueeStart;

    public SceneViewPanel() => _toolContext = new SceneToolContext(_handles);

    /// <summary>Handle arbitration context for this viewport.</summary>
    public HandleContext Handles => _handles;

    /// <summary>The most recently active SceneViewPanel's camera. Used by other panels for "Move to View" etc.</summary>
    public static EditorCamera? ActiveCamera { get; private set; }

    /// <summary>Tools ticking in the scene view this frame.</summary>
    public static IReadOnlyList<SceneTool> LiveTools => SceneToolManager.Live;
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

                b.Header(Loc.Get("scene.tool_handles"));
                b.Toggle(Loc.Get("scene.pivot_center"),
                    () => SceneTools.Pivot = SceneTools.Pivot == PivotMode.Center ? PivotMode.Pivot : PivotMode.Center,
                    () => SceneTools.Pivot == PivotMode.Center);
                b.Toggle(Loc.Get("scene.orientation_local"),
                    () => SceneTools.Orientation = SceneTools.Orientation == PivotOrientation.Local ? PivotOrientation.Global : PivotOrientation.Local,
                    () => SceneTools.Orientation == PivotOrientation.Local);
                b.Toggle(Loc.Get("scene.snap_enabled"),
                    () => SceneTools.SnapEnabled = !SceneTools.SnapEnabled,
                    () => SceneTools.SnapEnabled);
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
            // The viewport owns the strip and draws the built-in buttons, so they exist even with no
            // tool doing anything. Live tools append their own; a tool can also replace the built-ins.
            if (!SceneToolManager.AnyOverridesToolStrip())
                DrawDefaultToolbar(paper, font);

            foreach (var tool in SceneToolManager.Live)
            {
                _toolContext.CurrentTool = tool;
                tool.OnToolStripGUI(_toolContext, paper, $"sv_tool_{tool.GetType().Name}");
            }
        }
    }

    private void DrawDefaultToolbar(Paper paper, Scribe.FontFile font)
    {
        bool isTranslate = SceneTools.Transform == TransformTool.Translate;
        bool isRotate = SceneTools.Transform == TransformTool.Rotate;
        bool isScale = SceneTools.Transform == TransformTool.Scale;
        bool isUniversal = SceneTools.Transform == TransformTool.Universal;

        paper.Box("sv_move_btn")
            .Width(24).Height(24).Rounded(6)
            .BackgroundColor(isTranslate ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(EditorIcons.ArrowsUpDownLeftRight, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => SetGizmoMode(TransformTool.Translate));

        paper.Box("sv_rotate_btn")
            .Width(24).Height(24).Rounded(6)
            .BackgroundColor(isRotate ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(EditorIcons.ArrowsRotate, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => SetGizmoMode(TransformTool.Rotate));

        paper.Box("sv_scale_btn")
            .Width(24).Height(24).Rounded(6)
            .BackgroundColor(isScale ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(EditorIcons.Maximize, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => SetGizmoMode(TransformTool.Scale));

        paper.Box("sv_universal_btn")
            .Width(24).Height(24).Rounded(6)
            .BackgroundColor(isUniversal ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(EditorIcons.Expand, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => SetGizmoMode(TransformTool.Universal));
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
                    Origami.Button(paper, "sv_create_scene", $"{EditorIcons.Plus}  {Loc.Get("hierarchy.new_scene")}", () => EditorSceneManager.CreateAndLoadDefaultScene()).Width(120).Show();
                    paper.Box("sv_btn_spacer_r");
                }

                paper.Box("sv_no_scene_spacer2");
            }
            return;
        }


        // The scene view always presents and edits UI in world space (each canvas's ReferenceResolution
        // + GameObject transform) regardless of its RenderMode, so screen-space UI can be manipulated
        // like any other GameObject. This wraps input, gizmos AND the render so the canvas wireframe,
        // the element handles and the drawn mesh all agree on the same rect.
        bool prevWorldSpace = GameCanvas.EditorWorldSpaceOverride;
        GameCanvas.EditorWorldSpaceOverride = true;
        try
        {
            // Everything interactive in the viewport registers a control with the same context, so
            // the nearest one to the cursor wins rather than an editor claiming the whole frame.
            var cam = _editorCamera.Camera;
            if (cam != null)
            {
                // Both paper.PointerPos and _viewportAbsoluteRect are in Paper-logical space.
                Float2 origin = new((float)_viewportAbsoluteRect.Min.X, (float)_viewportAbsoluteRect.Min.Y);
                Float2 mouseLocal = paper.PointerPos - origin;
                // Use Paper's hover state which respects overlays/popups, not just bounds
                _handles.BeginFrame(cam, _viewportAbsoluteRect, mouseLocal, paper.IsParentHovered);

                _toolContext.Begin(scene, this);
                SceneToolManager.SyncAvailability(_toolContext);

                // Republished each frame so a tool that reads them without a context or a panel
                // reference still sees the current state.
                SceneTools.ViewToolActive = _handles.Blocked;
                SceneTools.SuppressTransformGizmo = SceneToolManager.AnySuppressesTransformGizmo();

                SceneToolManager.TickInput(_toolContext);
                UpdateTransformGizmo();
                // Depth 0: the view cube is a screen-space overlay drawn on top of the scene, so it
                // wins every overlap rather than competing on scene depth.
                if (_viewCubeRadius > 0f)
                {
                    ControlID viewCube = _handles.GetControlID(ViewCubeControl);
                    _handles.AddControl(viewCube, _handles.DistanceToScreenPoint(_viewCubeCenter, _viewCubeRadius), 0f);
                    _handles.RequestCursor(viewCube, PaperCursor.Pointer);
                }
                UpdatePickControl(scene, new Float2(width, height));

                _handles.EndFrame();
            }

            // Render scene (gizmos drawn via Debug.DrawLine render into the RT)
            DrawSelectionGizmos();
            _editorCamera.Render(scene);
        }
        catch (Exception ex)
        {
            // A broken scene view render or scene editor must not crash the editor. Keep the panel alive.
            Runtime.Debug.LogError($"[SceneView] Scene render/input threw and was skipped: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            GameCanvas.EditorWorldSpaceOverride = prevWorldSpace;
        }

        if (rt != null && rt.MainTexture != null)
        {
            paper.Box("sv_viewport")
                .Size(width, height)
                // Handles ask for a shape while they own the cursor; Paper resolves it from the
                // hovered element exactly as it does for any UI widget.
                .Cursor(_handles.Cursor)
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

                        canvas.SetBrushTexture(rt.MainTexture);
                        // TextureTransform maps screen rect to UV. Vulkan is top-left-origin, so the
                        // render target already samples upright relative to the canvas.
                        canvas.SetBrushTextureTransform(
                            Transform2D.CreateTranslation(rx, ry) * Transform2D.CreateScale(rw, rh));
                        canvas.RoundedRectFilled(rx, ry, rw, rh, 0, 0, rnd, rnd, Color.White);
                        canvas.ClearBrushTexture();

                        // Inset the border by half its width so the full stroke sits on top of the texture edge.
                        canvas.RoundedRect(rx + 1, ry + 1, rw - 2, rh - 2, 0, 0, rnd, rnd);
                        canvas.SetStrokeColor(EditorTheme.Purple500);
                        canvas.SetStrokeWidth(2);
                        canvas.Stroke();
                    });

                    // Draw transform gizmo as 2D overlay
                    if (_transformGizmo != null && _gizmoActive)
                    {
                        paper.DrawForeground(ref handle, (canvas2, r2) =>
                        {
                            _transformGizmo.Draw(canvas2);
                        });
                    }

                    // Tool overlays, then everything recorded into the shared draw list this frame
                    // (by tools, by handles, and by the viewport itself).
                    paper.DrawForeground(ref handle, (canvas2, r2) =>
                    {
                        foreach (var overlayTool in SceneToolManager.Live)
                        {
                            _toolContext.CurrentTool = overlayTool;
                            try { overlayTool.OnDrawOverlay(_toolContext, canvas2); }
                            catch (Exception ex) { Runtime.Debug.LogError($"[SceneTool] {overlayTool.GetType().Name}.OnDrawOverlay threw: {ex.Message}"); }
                        }
                        _handles.Draw.Replay(canvas2);
                    });
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
                    EditorSceneManager.MarkDirty();
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
                    SetGizmoMode(TransformTool.Translate);
                else if (ShortcutManager.IsPressed("Scene/ToolRotate"))
                    SetGizmoMode(TransformTool.Rotate);
                else if (ShortcutManager.IsPressed("Scene/ToolScale"))
                    SetGizmoMode(TransformTool.Scale);
                else if (ShortcutManager.IsPressed("Scene/ToolUniversal"))
                    SetGizmoMode(TransformTool.Universal);
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
                // The active tool sees the drop first, so a tool can accept assets its own way
                // (a material onto a face, say) before the generic handlers spawn an object.
                bool consumedByTool = false;
                foreach (var dropTool in SceneToolManager.Live)
                {
                    _toolContext.CurrentTool = dropTool;
                    if (!dropTool.OnAssetDropped(_toolContext, assetDrop)) continue;
                    consumedByTool = true;
                    break;
                }
                if (consumedByTool)
                {
                    DragDrop.EndDrag();
                    return;
                }

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

    /// <summary>
    /// Object picking and marquee selection, sharing one control: a press that never travels is a
    /// click-select, a press that drags out a rectangle is a box-select. Registered as the default
    /// control, so it only runs when no handle anywhere in the viewport claimed the cursor.
    /// </summary>
    private void UpdatePickControl(Scene scene, Float2 panelSize)
    {
        ControlID pick = _handles.GetControlID(PickControl);
        _handles.AddDefaultControl(pick);

        bool owned = _handles.IsHot(pick);

        // Released first and unconditionally: a press that starts in the viewport and releases
        // outside it would otherwise hold the drag and block every other handle until next frame.
        if (owned && _handles.PrimaryUp)
        {
            if (_marqueeActive)
                ApplyMarquee(scene, panelSize);
            else
                PickObject(scene, _handles.MouseLocal, panelSize);

            _marqueeActive = false;
            _handles.TryEndDrag(pick);
            return;
        }

        if (owned && _handles.PrimaryHeld)
        {
            // Promote to a marquee once the cursor has travelled far enough to mean it.
            if (!_marqueeActive && _handles.DragExceededThreshold(pick, MarqueeThreshold))
            {
                _marqueeActive = true;
                _marqueeStart = _handles.MousePosition;
            }
            if (_marqueeActive)
                _handles.Draw.ScreenRect(_marqueeStart, _handles.MousePosition,
                    Color32.FromArgb(40, 120, 170, 255), Color32.FromArgb(200, 140, 190, 255));
            return;
        }

        if (!_handles.ViewportHovered || _handles.Blocked) return;
        if (_handles.TryBeginDrag(pick))
            _marqueeStart = _handles.MousePosition;
    }

    /// <summary>
    /// Select everything whose screen-projected bounds centre falls inside the marquee. Shift adds to
    /// the selection, Ctrl toggles, plain replaces.
    /// </summary>
    private void ApplyMarquee(Scene scene, Float2 panelSize)
    {
        Float2 a = _marqueeStart, b = _handles.MousePosition;
        float minX = MathF.Min(a.X, b.X), maxX = MathF.Max(a.X, b.X);
        float minY = MathF.Min(a.Y, b.Y), maxY = MathF.Max(a.Y, b.Y);

        bool additive = _handles.Shift || _handles.Ctrl;
        if (!additive) Selection.Clear();

        foreach (var go in scene.ActiveObjects)
        {
            if (go.HideFlags.HasFlag(HideFlags.Hide)) continue;
            if (!TryGetSelectionAnchor(go, out Float3 anchor)) continue;

            Float2? screen = _handles.WorldToScreen(anchor);
            if (screen is null) continue;
            if (screen.Value.X < minX || screen.Value.X > maxX) continue;
            if (screen.Value.Y < minY || screen.Value.Y > maxY) continue;

            if (_handles.Ctrl) Selection.ToggleSelection(go);
            else Selection.AddToSelection(go);
        }
    }

    /// <summary>The point a marquee tests against: renderer bounds centre, else the transform.</summary>
    private static bool TryGetSelectionAnchor(GameObject go, out Float3 anchor)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mr.EnabledInHierarchy && mr.Mesh.Res != null)
        {
            AABB b = mr.Mesh.Res.bounds;
            anchor = Float4x4.TransformPoint((b.Min + b.Max) * 0.5f, go.Transform.LocalToWorldMatrix);
            return true;
        }

        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr != null && smr.EnabledInHierarchy && smr.SharedMesh.Res != null)
        {
            AABB b = smr.SharedMesh.Res.bounds;
            anchor = Float4x4.TransformPoint((b.Min + b.Max) * 0.5f, go.Transform.LocalToWorldMatrix);
            return true;
        }

        // Everything else (lights, cameras, empties) selects on its origin.
        anchor = go.Transform.Position;
        return true;
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

    private void SetGizmoMode(TransformTool tool)
    {
        SceneTools.Transform = tool;
        _transformGizmo?.SetMode(SceneTools.GizmoMode);
    }

    private void UpdateTransformGizmo()
    {
        _gizmoActive = false;
        if (_editorCamera == null) return;
        if (SceneTools.SuppressTransformGizmo) return;

        // Only show gizmo when GameObjects are selected
        var selectedGOs = Selection.GetSelected<GameObject>().GetEnumerator();
        if (!selectedGOs.MoveNext()) return;

        _gizmoActive = true;

        var firstGO = selectedGOs.Current;
        if (firstGO == null) return;

        // Create gizmo if needed
        _transformGizmo ??= new Gizmo.TransformGizmo(SceneTools.GizmoMode);
        _transformGizmo.GizmoSize = 100f;

        // Pivot mode picks what the handle sits on: the selection's centre, or the active object.
        Float3 center;
        if (SceneTools.Pivot == PivotMode.Pivot)
        {
            center = firstGO.Transform.Position;
        }
        else
        {
            center = Float3.Zero;
            int count = 0;
            foreach (var go in Selection.GetSelected<GameObject>())
            {
                center += go.Transform.Position;
                count++;
            }
            if (count > 0) center /= count;
        }

        Quaternion rotation = firstGO.Transform.Rotation;
        Float3 scale = firstGO.Transform.LossyScale;

        // The gizmo derives its axis directions from this, so drive the widget's own orientation
        // rather than flattening the rotation, which would also flatten the drawn axes.
        _transformGizmo.Orientation = SceneTools.Orientation == PivotOrientation.Local
            ? Gizmo.TransformGizmo.GizmoOrientation.Local
            : Gizmo.TransformGizmo.GizmoOrientation.Global;

        // Update gizmo use absolute screen rect so coordinates match DrawForeground
        var cam = _editorCamera.Camera;
        var camGo = cam.GameObject;

        _transformGizmo.UpdateCamera(_handles.Viewport, cam.ViewMatrix, cam.ProjectionMatrix,
            camGo.Transform.Up, camGo.Transform.Forward, camGo.Transform.Right, camGo.Transform.Position);
        _transformGizmo.SetTransform(center, rotation, scale);

        ControlID control = _handles.GetControlID(TransformControl);

        // Ctrl always snaps; SceneTools.SnapEnabled makes it sticky.
        _transformGizmo.Snapping = SceneTools.SnapEnabled || _handles.Ctrl;
        _transformGizmo.SnapDistance = SceneTools.MoveSnap;
        _transformGizmo.SnapAngle = SceneTools.RotateSnap;
        _transformGizmo.IsShiftDown = _handles.Shift;
        // The grab decision comes from arbitration, so a handle nearer the cursor wins instead of the
        // gizmo taking every press it happens to be over.
        _transformGizmo.IsMouseDown = _handles.TryBeginDrag(control);
        _transformGizmo.IsMouseUp = _handles.PrimaryUp;

        var result = _transformGizmo.Update(_handles.MouseRay, _handles.MousePosition, _handles.Blocked);

        // IsOver is fresh from the Update above. Guarded on Blocked because the gizmo only clears its
        // hover inside the un-blocked branch, so a blocked frame would leave IsOver latched on.
        // Origami exposes only a bool, not a per-axis screen distance, so a hover registers at zero
        // and the gizmo's own depth decides overlaps against handles stacked on top of it.
        bool gizmoHovered = _transformGizmo.IsOver && !_handles.Blocked;
        _handles.AddControl(control, gizmoHovered ? 0f : float.MaxValue, _handles.DepthOf(center));
        _handles.RequestCursor(control, PaperCursor.ResizeAll);

        // Gizmo drawing happens in the viewport's DrawForeground callback (needs canvas)

        // Continuous undo spans the whole drag. Keyed off control ownership rather than whether the
        // gizmo produced a delta this frame - a sub-gizmo returns no result at degenerate view
        // angles, which would otherwise split one drag into two undo steps.
        bool dragging = _handles.IsHot(control);
        if (dragging && !_wasGizmoActive)
            Undo.BeginContinuous(Selection.GetSelected<GameObject>().ToArray(), "Transform");
        if (!dragging && _wasGizmoActive)
            Undo.EndContinuous();
        _wasGizmoActive = dragging;

        _handles.TryEndDrag(control);

        if (result.HasValue && dragging)
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

            EditorSceneManager.MarkDirty();
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

                // Cache the rect so the control can register from next frame's input pass; the widget
                // fuses hover, click and drawing into one call that needs a canvas, so it can only
                // run here.
                _viewCubeCenter = new Float2((float)(r.Min.X + r.Size.X / 2), (float)(r.Min.Y + r.Size.Y / 2));
                _viewCubeRadius = (float)(r.Size.X / 2);

                // Arbitration decides whether the cube may take the click; its own IsOver cannot be
                // used for this, since it reports true whenever the cursor is inside the background
                // circle even while the camera is being driven.
                bool mayClick = _handles.IsNearest(_handles.GetControlID(ViewCubeControl)) && !_handles.Blocked;
                bool clicked = Input.GetMouseButtonDown(0);
                Float2 mousePos = paper.PointerPos;

                if (_viewManipulator.Update(canvas, mousePos, clicked && mayClick, !mayClick, out var newForward))
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
