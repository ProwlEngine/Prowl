using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Core.Diagnostics;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.OrigamiUI.Gizmo;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;
using DebugViewMode = Prowl.Runtime.Rendering.DebugViewMode;
using Gizmo = Prowl.OrigamiUI.Gizmo;

namespace Prowl.Editor.GUI.Panels;

public class SceneViewPanel : DockPanel
{
    [MenuItem("Window/General/Scene", priority: 5)]
    static void Open() => EditorApplication.Instance?.OpenPanel<SceneViewPanel>();

    public override string Title => Loc.Get("panel.scene");
    public override string Icon => EditorIcons.Shapes;

    private const string TransformControl = "sceneview_transform";
    private const string FreeMoveControl = "sceneview_freemove";
    private const string PickControl = "sceneview_pick";
    private const string ViewCubeControl = "sceneview_viewcube";

    private readonly HandleContext _handles = new();
    private readonly CameraPreview _cameraPreview = new();
    private readonly SceneToolContext _toolContext;
    private EditorCamera? _editorCamera;

    /// <summary>The camera this view is seen through.</summary>
    internal EditorCamera? EditorCamera => _editorCamera;
    /// <summary>Includes restored settings before the camera is created on the first draw.</summary>
    internal bool ShowGizmos => _editorCamera?.ShowGizmos ?? _pendingGizmos ?? true;
    private Gizmo.TransformGizmo? _transformGizmo;
    private bool _wasGizmoActive;
    private bool _gizmoHasMoved;
    private GameObject[]? _gizmoTargets;
    private GameObject[]? _gizmoSelection;
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
    private int _viewCubeElementId;
    private int _viewportElementId; // Paper hit target, excluding the floating toolbar and other UI overlays
    private bool _gizmoActive; // Whether the gizmo should draw (selection exists)

    // Pose restored from disk by RestoreState; applied the first time the camera is created
    // (the camera itself is constructed lazily inside OnGUI because it needs graphics ready).
    private bool _hasPendingPose;
    private Float3 _pendingPos;
    private float _pendingYaw, _pendingPitch;
    private bool _pending2D;
    private float? _pendingOrthoSize;
    private EditorCamera.View3D? _pendingBefore2D;

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
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
            if (_pending2D)
            {
                _editorCamera.Set2D(true, _pendingBefore2D);
                if (_pendingOrthoSize is float size) _editorCamera.OrthographicSize = size;
                _pending2D = false;
            }
            if (_pendingLens is { } lens)
            {
                if (lens.Fov is float fov) _editorCamera.FieldOfView = fov;
                if (lens.Far is float far) _editorCamera.FarClip = far;
                if (lens.Near is float near) _editorCamera.NearClip = near;
                if (lens.Speed is float speed) _editorCamera.SetMoveSpeed(speed);
                if (lens.Smoothing is float smoothing) _editorCamera.MoveSmoothing = smoothing;
                _pendingLens = null;
            }
            if (_pendingGizmos is bool pz) { _editorCamera.ShowGizmos = pz; _pendingGizmos = null; }
            if (_pendingPost is bool pp) { _editorCamera.PostProcessing = pp; _pendingPost = null; }
            if (_pendingDebugView is DebugViewMode dv) { _editorCamera.DebugView = dv; _pendingDebugView = null; }
        }
        ActiveCamera = _editorCamera;

        using (paper.Column("sv_root").Size(width, height).Enter())
        {
            DrawViewport(paper, font, width, height);
        }
    }

    // The scene view toolbar lives in the leaf's tab-bar header (right side): handle orientation, gizmos,
    // rotation and grid snapping, then the view settings.
    public override float HeaderWidth => 386f;
    public override void OnHeaderContent(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (Time.FrameCount > _headerFrame + 1) _gizmoListOpen = false;
        _headerFrame = Time.FrameCount;

        using (paper.Row("sv_hdr").Width(width).Height(height).Gap(6).Enter())
        {
            paper.Box("sv_hdr_spacer").Width(UnitValue.StretchOne);

            using (HeaderGroup(paper, "sv_hdr_orient_grp"))
            {
                PivotOrientation orientation = SceneTools.Orientation;
                HeaderToggle(paper, font, "sv_hdr_orient",
                    orientation switch { PivotOrientation.Global => EditorIcons.Globe, PivotOrientation.Local => EditorIcons.Cube, _ => EditorIcons.ArrowUpLong },
                    Loc.Get(orientation switch { PivotOrientation.Global => "scene.orientation_global", PivotOrientation.Local => "scene.orientation_local", _ => "scene.orientation_normal" }),
                    orientation == PivotOrientation.Global,
                    () => SceneTools.Orientation = orientation switch
                    {
                        PivotOrientation.Global => PivotOrientation.Local,
                        PivotOrientation.Local => PivotOrientation.Normal,
                        _ => PivotOrientation.Global,
                    });
                PivotButton(paper, font);
            }

            using (HeaderGroup(paper, "sv_hdr_gizmo_grp"))
            {
                ElementHandle trigger = paper.CurrentParent;
                HeaderToggle(paper, font, "sv_hdr_gizmos", EditorIcons.CompassDrafting, Loc.Get("scene.show_gizmos"), ShowGizmos,
                    () => { if (_editorCamera != null) _editorCamera.ShowGizmos = !_editorCamera.ShowGizmos; });
                paper.Box("sv_hdr_gizmo_menu").Width(12).Height(ToggleSize).Rounded(EditorTheme.Roundness)
                    .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .Hovered.BackgroundColor(EditorTheme.Hover).End()
                    .Text(EditorIcons.CaretDown, font).TextColor(EditorTheme.Ink300).FontSize(10f).Alignment(TextAlignment.MiddleCenter)
                    .Tooltip(Loc.Get("scene.gizmos"))
                    .IsNotFocusable()
                    .OnClick(_ =>
                    {
                        _gizmoListOpen = !_gizmoListOpen;
                        if (_gizmoListOpen) GizmoTypesPopover.ResetSearch();
                    });

                if (_gizmoListOpen)
                {
                    if (paper.IsKeyPressed(PaperKey.Escape))
                        _gizmoListOpen = false;
                    else
                    {
                        MenuTreePopup.Backdrop(paper, "sv_gizmo_list", () => _gizmoListOpen = false);
                        GizmoTypesPopover.Draw(paper, "sv_gizmo_list", trigger);
                    }
                }
            }

            using (HeaderGroup(paper, "sv_hdr_rot_grp"))
            {
                HeaderToggle(paper, font, "sv_hdr_rot_snap", EditorIcons.ArrowsRotate, Loc.Get("scene.rotation_snap"), SceneTools.RotationSnap,
                    () => SceneTools.RotationSnap = !SceneTools.RotationSnap);
                // "45°" does not parse, but the field selects all on focus, so typing replaces the whole value.
                HeaderField(paper, font, "sv_hdr_rot", RotateSnapLabels, () =>
                    Origami.NumericField<float>(paper, "sv_hdr_rot_field", SceneTools.RotateSnap, v => SceneTools.RotateSnap = v)
                        .Min(SceneTools.MinRotateSnap).Max(SceneTools.MaxRotateSnap).Format(SceneTools.RotateSnapFormat).DragHandle(false).Show());
                HeaderStepper(paper, font, "sv_hdr_rot_step", SceneTools.StepRotateSnap);
            }

            using (HeaderGroup(paper, "sv_hdr_grid_grp"))
            {
                HeaderToggle(paper, font, "sv_hdr_grid_snap", EditorIcons.TableCells, Loc.Get("scene.grid_snap"), SceneTools.GridSnap,
                    () => SceneTools.GridSnap = !SceneTools.GridSnap);
                HeaderField(paper, font, "sv_hdr_grid", MoveSnapLabels, () =>
                    Origami.NumericField<float>(paper, "sv_hdr_grid_field", SceneTools.MoveSnap, v => SceneTools.MoveSnap = v)
                        .Min(SceneTools.MinMoveSnap).Max(SceneTools.MaxMoveSnap).Format(SceneTools.CurrentMoveSnapFormat).DragHandle(false).Show());
                HeaderStepper(paper, font, "sv_hdr_grid_step", SceneTools.StepMoveSnap);
            }

            DrawSettingsButton(paper);
        }
    }

    // Open state of this view's gizmo type list, and the frame its header last drew, to close the list when the tab is hidden.
    private bool _gizmoListOpen;
    private long _headerFrame;

    private const float ToggleSize = 22f;

    // A pill holding a toggle and the controls that go with it.
    private static IDisposable HeaderGroup(Paper paper, string id)
        => paper.Row(id).Width(UnitValue.Auto).Height(ToggleSize + 4).Gap(2).Padding(2, 2, 2, 2)
            .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Rounded(EditorTheme.Roundness + 2)
            .BackgroundColor(EditorTheme.Neutral200)
            .Enter();

    // A square icon toggle, filled with the accent while its setting is on. Drawn as a plain box rather than a
    // button so the glyph sits dead centre and nothing animates or takes focus when it is clicked.
    private static void HeaderToggle(Paper paper, Scribe.FontFile font, string id, string glyph, string tooltip, bool on, Action onClick)
    {
        paper.Box(id).Size(ToggleSize, ToggleSize).Rounded(EditorTheme.Roundness)
            .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .BackgroundColor(on ? EditorTheme.Accent : Color.Transparent)
            .Hovered.BackgroundColor(on ? EditorTheme.AccentBright : EditorTheme.Hover).End()
            .Text(glyph, font).TextColor(on ? Color.White : EditorTheme.Ink300)
            .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
            .Tooltip(tooltip)
            .IsNotFocusable()
            .OnClick(_ => onClick());
    }

    // Opens a menu of the pivot modes, showing the current one's icon.
    private static void PivotButton(Paper paper, Scribe.FontFile font)
    {
        PivotMode mode = SceneTools.Pivot;
        paper.Box("sv_hdr_pivot").Size(ToggleSize, ToggleSize).Rounded(EditorTheme.Roundness)
            .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(PivotIcon(mode), font).TextColor(EditorTheme.Ink300)
            .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
            .Tooltip(Loc.Get(PivotLabel(mode)))
            .IsNotFocusable()
            .OnClick(e => ContextMenu.Show((float)e.ElementRect.Min.X, (float)e.ElementRect.Max.Y + 4f, b =>
            {
                foreach (PivotMode m in Enum.GetValues<PivotMode>())
                    b.Item(Loc.Get(PivotLabel(m)), () => SceneTools.Pivot = m, icon: PivotIcon(m), on: SceneTools.Pivot == m);
            }));
    }

    private static string PivotIcon(PivotMode mode) => mode switch
    {
        PivotMode.Active => EditorIcons.Crosshairs,
        PivotMode.Individual => EditorIcons.Cubes,
        PivotMode.Cursor => EditorIcons.LocationCrosshairs,
        _ => EditorIcons.CircleDot,
    };

    // The cursor shows while a model is open, where most things use it, or while it is the pivot
    private static bool ShowsCursor => ModelEditingMode.IsEditing || SceneTools.Pivot == PivotMode.Cursor;

    /// <summary>Shift right click: the 3D cursor goes onto what is under the mouse, or along the view plane it is on.</summary>
    private void PlaceCursor(Ray ray)
    {
        Float3? best = null;
        foreach (SceneTool tool in SceneToolManager.Live)
            if (tool.SurfaceUnder(_toolContext, ray) is { } hit && (best == null || Float3.Distance(ray.Origin, hit) < Float3.Distance(ray.Origin, best.Value)))
                best = hit;
        if (best == null && RayPlane(ray, SceneTools.Cursor, _handles.Camera.GameObject.Transform.Forward, out Float3 onPlane)) best = onPlane;
        if (best is { } at) SceneTools.Cursor = at;
    }

    private static Texture2D? s_cursorIcon;

    // Kept the same size on screen at any distance
    private void DrawCursor()
    {
        if (s_cursorIcon.IsNotValid() && EditorApplication.GetEmbeddedResource("cursor-3d.png") is { } stream)
            using (stream) s_cursorIcon = Texture2D.FromStream(stream, true);
        Float3 at = SceneTools.Cursor;
        Debug.DrawIcon(s_cursorIcon!, at, MathF.Max(_handles.DepthOf(at), 0.01f) * 0.07f, Vector.Color.White);
    }

    private static string PivotLabel(PivotMode mode) => $"scene.pivot_{mode.ToString().ToLowerInvariant()}";

    // Up and down arrows stacked beside a value field. They step through the field's preset values rather than
    // adding a fixed amount, so the grid doubles and halves.
    private static void HeaderStepper(Paper paper, Scribe.FontFile font, string id, Action<int> step)
    {
        using (paper.Column(id).Width(12).Height(ToggleSize).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
        {
            StepperArrow(paper, font, $"{id}_up", EditorIcons.CaretUp, () => step(1));
            StepperArrow(paper, font, $"{id}_down", EditorIcons.CaretDown, () => step(-1));
        }
    }

    private static void StepperArrow(Paper paper, Scribe.FontFile font, string id, string glyph, Action onClick)
        => paper.Box(id).Width(12).Height(ToggleSize / 2f).Rounded(2)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Cursor(PaperCursor.Pointer)
            .Text(glyph, font).TextColor(EditorTheme.Ink300).FontSize(9f).Alignment(TextAlignment.MiddleCenter)
            .IsNotFocusable()
            .OnClick(_ => onClick());

    // Every value the steppers move between, as the fields show them.
    private static readonly string[] RotateSnapLabels =
        [.. SceneTools.RotateSnapSteps.Select(v => v.ToString(SceneTools.RotateSnapFormat))];
    private static readonly string[] MoveSnapLabels =
        [.. Enumerable.Range(0, 14).Select(i => SceneTools.MinMoveSnap * MathF.Pow(2, i))
            .Where(v => v <= SceneTools.MaxMoveSnap).Select(v => v.ToString(SceneTools.MoveSnapFormat(v)))];

    // Widths already measured, keyed by field and font size.
    private static readonly Dictionary<(string, float), float> s_headerFieldWidths = [];

    // A compact value field beside a toggle, wide enough for the widest of its stepper values, so the width stays put
    // and the stepper beside it does not shift while clicking through. Measured against the numeric field's own
    // layout: monospace text inside a 1px border, 8 before the text and a 6 end pad after it, plus a couple of
    // pixels so the caret does not scroll the text.
    private static void HeaderField(Paper paper, Scribe.FontFile font, string id, string[] labels, Action drawField)
    {
        float fontSize = Origami.Current.Metrics.FontSize;
        if (!s_headerFieldWidths.TryGetValue((id, fontSize), out float width))
        {
            Scribe.FontFile mono = Origami.Current.Mono ?? font;
            float text = labels.Max(l => (float)paper.MeasureText(l, fontSize, mono).X);
            width = MathF.Ceiling(1f + 8f + text + 6f + 1f + 2f);
            s_headerFieldWidths[(id, fontSize)] = width;
        }

        using (paper.Row(id).Width(width).Height(ToggleSize).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
            drawField();
    }

    private void DrawSettingsButton(Paper paper)
    {
        EditorGUI.HeaderIconButton(paper, "sv_hdr_settings", EditorIcons.Gear, Loc.Get("scene.settings"), () =>
            Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
            {
                b.Header(Loc.Get("panel.scene"));
                b.Toggle(Loc.Get("scene.show_grid"),
                    () => { if (_editorCamera != null) _editorCamera.ShowGrid = !_editorCamera.ShowGrid; },
                    () => _editorCamera?.ShowGrid ?? true);
                b.Toggle(Loc.Get("scene.post_processing"),
                    () => { if (_editorCamera != null) _editorCamera.PostProcessing = !_editorCamera.PostProcessing; },
                    () => _editorCamera?.PostProcessing ?? true);
                b.Submenu(Loc.Get("scene.debug_view"), sub =>
                {
                    foreach (DebugViewMode mode in Enum.GetValues<DebugViewMode>())
                        sub.Toggle(Loc.Get($"scene.debug_view.{mode.ToString().ToLowerInvariant()}"),
                            () => { if (_editorCamera != null) _editorCamera.DebugView = mode; },
                            () => (_editorCamera?.DebugView ?? DebugViewMode.Lit) == mode);
                });

                if (_editorCamera != null)
                {
                    EditorCamera cam = _editorCamera;
                    b.Header(Loc.Get("scene.camera"));
                    b.Custom(p => DrawCameraSettings(p, cam));
                }

                b.Header(Loc.Get("scene.navigation"));
                b.Toggle(Loc.Get("scene.navmesh_always_show"),
                    () => NavMeshDebugDisplay.AlwaysShow = !NavMeshDebugDisplay.AlwaysShow,
                    () => NavMeshDebugDisplay.AlwaysShow);
                b.Toggle(Loc.Get("scene.navmesh_show_detail"),
                    () => NavMeshDebugDisplay.ShowDetail = !NavMeshDebugDisplay.ShowDetail,
                    () => NavMeshDebugDisplay.ShowDetail);
                b.Toggle(Loc.Get("scene.navmesh_show_vertices"),
                    () => NavMeshDebugDisplay.ShowVertices = !NavMeshDebugDisplay.ShowVertices,
                    () => NavMeshDebugDisplay.ShowVertices);
            }));
    }

    // Lens settings only: what the camera sees from where it is, rather than where it is, which the
    // navigation controls own.
    private static void DrawCameraSettings(Paper paper, EditorCamera cam)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        CameraRow(paper, font, "sv_cam_proj", Loc.Get("scene.camera_orthographic"), () =>
            Origami.Switch(paper, "sv_cam_proj_v", cam.IsOrthographic, _ => cam.ToggleProjection()).NoLabel().Show());

        if (cam.IsOrthographic)
            CameraRow(paper, font, "sv_cam_size", Loc.Get("scene.camera_ortho_size"), () =>
                Origami.NumericField<float>(paper, "sv_cam_size_v", cam.OrthographicSize, v => cam.OrthographicSize = v).Show());
        else
            CameraRow(paper, font, "sv_cam_fov", Loc.Get("scene.camera_fov"), () =>
                Origami.Slider(paper, "sv_cam_fov_v", cam.FieldOfView, v => cam.FieldOfView = v, EditorCamera.MinFieldOfView, EditorCamera.MaxFieldOfView).Format("F0").Show());

        CameraRow(paper, font, "sv_cam_near", Loc.Get("scene.camera_near"), () =>
            Origami.NumericField<float>(paper, "sv_cam_near_v", cam.NearClip, v => cam.NearClip = v).Show());

        CameraRow(paper, font, "sv_cam_far", Loc.Get("scene.camera_far"), () =>
            Origami.NumericField<float>(paper, "sv_cam_far_v", cam.FarClip, v => cam.FarClip = v).Show());

        CameraRow(paper, font, "sv_cam_speed", Loc.Get("scene.camera_speed"), () =>
            Origami.Slider(paper, "sv_cam_speed_v", cam.MoveSpeed, cam.SetMoveSpeed, 0.5f, 100f).Format("F1").Show());

        CameraRow(paper, font, "sv_cam_smoothing", Loc.Get("scene.camera_smoothing"), () =>
            // Shown in milliseconds so the steps read as whole numbers; stored in seconds.
            Origami.Slider(paper, "sv_cam_smoothing_v", cam.MoveSmoothing * 1000f, v => cam.MoveSmoothing = v / 1000f,
                0f, EditorCamera.MaxMoveSmoothing * 1000f).Step(5f).Format("0 ms").Show());

        Origami.Button(paper, "sv_cam_reset", Loc.Get("scene.camera_reset"), cam.ResetLens).FullWidth().Height(22).Show();
    }

    private static void CameraRow(Paper paper, Scribe.FontFile font, string id, string label, Action drawControl)
    {
        float rh = Origami.Current.Metrics.RowHeight;
        using (paper.Row(id).Height(rh).Gap(6).Enter())
        {
            paper.Box($"{id}_l").Width(70).Height(rh)
                .Text(label, font).TextColor(EditorTheme.Ink300).TextTruncate()
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            using (paper.Row($"{id}_c").Width(UnitValue.StretchOne).Height(rh).Enter())
                drawControl();
        }
    }

    // Floating tool groups stacked down the top left of the viewport. The active scene view
    // editor may replace the default gizmo mode buttons with groups of its own.
    private void DrawTransformTools(Paper paper)
    {
        using (paper.Column("sv_tools")
            .PositionType(PositionType.SelfDirected)
            .Position(12, 12)
            .Width(UnitValue.Auto).Height(UnitValue.Auto).Gap(6)
            .Enter())
        {
            // The viewport owns the strip and draws the built-in buttons, so they exist even with no
            // tool doing anything. Live tools append their own; a tool can also replace the built-ins.
            if (!SceneToolManager.AnyOverridesToolStrip())
                DrawDefaultToolbar(paper);

            foreach (SceneTool tool in SceneToolManager.Live)
            {
                _toolContext.CurrentTool = tool;
                tool.OnToolStripGUI(_toolContext, paper, $"sv_tool_{tool.GetType().Name}");
            }
        }
    }

    private void DrawToolOverlays(Paper paper, float width, float height)
    {
        foreach (SceneTool tool in SceneToolManager.Live)
        {
            _toolContext.CurrentTool = tool;
            try { tool.OnSceneOverlayGUI(_toolContext, paper, $"sv_ov_{tool.GetType().Name}", new Float2(width, height)); }
            catch (Exception ex) { Runtime.Debug.LogError($"[SceneTool] {tool.GetType().Name}.OnSceneOverlayGUI threw: {ex.Message}"); }
        }
    }

    private void DrawDefaultToolbar(Paper paper)
    {
        bool isTranslate = SceneTools.Transform == TransformTool.Translate;
        bool isRotate = SceneTools.Transform == TransformTool.Rotate;
        bool isScale = SceneTools.Transform == TransformTool.Scale;
        bool isUniversal = SceneTools.Transform == TransformTool.Universal;
        bool isRect = SceneTools.Transform == TransformTool.Rect;

        using IDisposable group = EditorGUI.ToolStripGroup(paper, "sv_transform_grp");
        EditorGUI.ToolStripButton(paper, "sv_move_btn", EditorIcons.ArrowsUpDownLeftRight_I,
            ShortcutManager.GetTooltip(Loc.Get("scene.tool_move"), "Scene/ToolTranslate"), isTranslate,
            () => SetGizmoMode(TransformTool.Translate));
        EditorGUI.ToolStripButton(paper, "sv_rotate_btn", EditorIcons.ArrowsRotate_I,
            ShortcutManager.GetTooltip(Loc.Get("scene.tool_rotate"), "Scene/ToolRotate"), isRotate,
            () => SetGizmoMode(TransformTool.Rotate));
        EditorGUI.ToolStripButton(paper, "sv_scale_btn", EditorIcons.Maximize_I,
            ShortcutManager.GetTooltip(Loc.Get("scene.tool_scale"), "Scene/ToolScale"), isScale,
            () => SetGizmoMode(TransformTool.Scale));
        EditorGUI.ToolStripButton(paper, "sv_universal_btn", EditorIcons.Expand_I,
            ShortcutManager.GetTooltip(Loc.Get("scene.tool_universal"), "Scene/ToolUniversal"), isUniversal,
            () => SetGizmoMode(TransformTool.Universal));
        EditorGUI.ToolStripButton(paper, "sv_rect_btn", EditorIcons.VectorSquare_I,
            ShortcutManager.GetTooltip(Loc.Get("scene.tool_rect"), "Scene/ToolRect"), isRect,
            () => SetGizmoMode(TransformTool.Rect));
    }

    private void DrawViewport(Paper paper, Scribe.FontFile font, float width, float height)
    {
        if (_editorCamera == null || width <= 0 || height <= 0) return;

        uint rtWidth = (uint)MathF.Max(1, width);
        uint rtHeight = (uint)MathF.Max(1, height);
        _editorCamera.EnsureRenderTarget(rtWidth, rtHeight);

        Scene scene = EditorSceneManager.EditedWorld;
        RenderTexture? rt = _editorCamera.RenderTarget;

        if (scene.IsNotValid())
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
                    .Height(30).Gap(8)
                    .Enter())
                {
                    paper.Box("sv_btn_spacer_l");
                    Origami.Button(paper, "sv_create_scene", $"{EditorIcons.Plus}  {Loc.Get("hierarchy.new_scene")}", EditorSceneManager.NewWithPrompt).Width(120).Show();
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
            Camera cam = _editorCamera.Camera;
            if (cam.IsValid())
            {
                // Both paper.PointerPos and _viewportAbsoluteRect are in Paper-logical space.
                Float2 origin = new((float)_viewportAbsoluteRect.Min.X, (float)_viewportAbsoluteRect.Min.Y);
                Float2 mouseLocal = paper.PointerPos - origin;

                // Only the viewport surface accepts scene picks. The enclosing panel is also
                // hovered over toolbar buttons, which would let their raw mouse press start a pick.
                // The view cube is a handle of its own drawn over the viewport, so it counts as scene surface too
                int hoveredId = paper.HoveredElementId;
                bool viewportHovered = hoveredId != 0 && (hoveredId == _viewportElementId || hoveredId == _viewCubeElementId);
                _handles.BeginFrame(cam, _viewportAbsoluteRect, mouseLocal, viewportHovered);

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
            DrawSceneGizmos(EditorApplication.Instance, scene);
            _editorCamera.Render(scene);
        }
        catch (Exception ex)
        {
            // A broken scene view render or scene editor must not crash the editor. Keep the panel alive.
            Runtime.Debug.LogException($"[SceneView] Scene render/input threw and was skipped", ex);
        }
        finally
        {
            GameCanvas.EditorWorldSpaceOverride = prevWorldSpace;
        }

        // The selected camera, for the corner preview.
        _cameraPreview.Render(width, height);

        if (rt.IsValid() && rt.MainTexture.IsValid())
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
                    _viewportElementId = handle.Data.ID;

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
                        canvas.SetBrushTextureTransform(
                            Transform2D.CreateTranslation(rx, ry + rh) *
                            Transform2D.CreateScale(rw, -rh));
                        canvas.RoundedRectFilled(rx, ry, rw, rh, 0, 0, rnd, rnd, Color.White);
                        canvas.ClearBrushTexture();
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
                        foreach (SceneTool overlayTool in SceneToolManager.Live)
                        {
                            _toolContext.CurrentTool = overlayTool;
                            try { overlayTool.OnDrawOverlay(_toolContext, canvas2); }
                            catch (Exception ex) { Runtime.Debug.LogError($"[SceneTool] {overlayTool.GetType().Name}.OnDrawOverlay threw: {ex.Message}"); }
                        }
                        _handles.Draw.Replay(canvas2);
                    });
                });

            // Camera input. A tool's modal operation keeps the camera still, as if the pointer were elsewhere
            bool isHovered = paper.IsParentHovered;
            _handles.PanelHovered = isHovered;
            _editorCamera.ProcessInput(
                (float)Time.UnscaledDeltaTime,
                isHovered && !_handles.Modal,
                paper.PointerPos,
                new Float2((float)_viewportAbsoluteRect.Min.X, (float)_viewportAbsoluteRect.Min.Y),
                new Float2(width, height));

            if (_editorCamera.TakeRightClick() && isHovered && !_handles.Modal)
            {
                Float2 viewportOrigin = new((float)_viewportAbsoluteRect.Min.X, (float)_viewportAbsoluteRect.Min.Y);
                Float2 pointer = _editorCamera.RightClickPointer;
                if (_handles.Shift) PlaceCursor(_handles.RayThrough(pointer));
                else SceneViewMenu.Open(_toolContext, scene, _editorCamera, pointer, pointer - viewportOrigin, new Float2(width, height));
            }

            // Scene view keyboard shortcuts (only when hovered and not right-click flying)
            if (isHovered && !_handles.KeysUsed && !_handles.Modal && !ShortcutManager.IsRebinding && !Input.GetMouseButton(1))
            {
                if (ShortcutManager.IsPressed("Scene/Delete"))
                {
                    // Shared with HierarchyPanel so the viewport enforces the same prefab
                    // structural-child protection and undo registration as the Hierarchy's Delete.
                    foreach (GameObject go in HierarchyPanel.ExcludeNestedSelections([.. Selection.GetSelected<GameObject>()]))
                        HierarchyPanel.DeleteGameObject(go);
                    Selection.Clear();
                }
                else if (ShortcutManager.IsPressed("Scene/Duplicate"))
                {
                    List<GameObject> dupes = GameObjectClipboard.Duplicate([.. Selection.GetSelected<GameObject>()]);
                    foreach (GameObject d in dupes) Undo.RegisterCreatedObjectUndo(d, "Duplicate");
                }
                else if (ShortcutManager.IsPressed("Scene/Copy"))
                {
                    GameObjectClipboard.Copy([.. Selection.GetSelected<GameObject>()]);
                }
                else if (ShortcutManager.IsPressed("Scene/Paste"))
                {
                    List<GameObject> pasted = GameObjectClipboard.Paste();
                    foreach (GameObject p in pasted) Undo.RegisterCreatedObjectUndo(p, "Paste");
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
                else if (ShortcutManager.IsPressed("Scene/ToolRect"))
                    SetGizmoMode(TransformTool.Rect);

                if (ShortcutManager.IsPressed("Scene/ToggleMaximize"))
                    EditorApplication.Instance?.Maximizer.RequestToggle(this);

                if (ShortcutManager.IsPressed("Scene/Rename") && Selection.GetSelected<GameObject>().FirstOrDefault() is { } renamed && renamed.IsValid())
                    HierarchyPanel.StartRenameGO(renamed, Selection.GetSelected<GameObject>());

                SceneViewMenu.Camera = _editorCamera;
                if (ShortcutManager.IsPressed("Scene/FrameSelected")) _editorCamera.FocusSelection();
                else if (ShortcutManager.IsPressed("Scene/ViewFront")) SceneViewMenu.ViewFront();
                else if (ShortcutManager.IsPressed("Scene/ViewBack")) SceneViewMenu.ViewBack();
                else if (ShortcutManager.IsPressed("Scene/ViewRight")) SceneViewMenu.ViewRight();
                else if (ShortcutManager.IsPressed("Scene/ViewLeft")) SceneViewMenu.ViewLeft();
                else if (ShortcutManager.IsPressed("Scene/ViewTop")) SceneViewMenu.ViewTop();
                else if (ShortcutManager.IsPressed("Scene/ViewBottom")) SceneViewMenu.ViewBottom();
                else if (ShortcutManager.IsPressed("Scene/TogglePerspective")) SceneViewMenu.TogglePerspective();
            }

            // Paper-space pointer to viewport-local via the cached viewport rect. CurrentParent here is an
            // enclosing container (toolbar row etc.), so subtracting its origin put drops off to the side.
            Float2 mouseLocal = paper.PointerPos - new Float2((float)_viewportAbsoluteRect.Min.X, (float)_viewportAbsoluteRect.Min.Y);
            var panelSize = new Float2(width, height);

            // Accept asset drops via registry-discovered handlers
            if (isHovered && DragDrop.IsDraggingType<AssetDragPayload>())
            {
                var dragPayload = (AssetDragPayload)DragDrop.Payload!;
                ISceneDropHandler? handler = SceneDrop.FindHandler(dragPayload);

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

                UpdateDropPreview(scene, dragPayload, GetDropPosition(scene, _editorCamera!, mouseLocal, panelSize));
            }
            else
            {
                ClearDropPreview();
            }

            // An OS file drop over the viewport: remember where, so the files spawn here once imported.
            if (isHovered && ExternalAssetDrop.IsResolvingDropTarget)
            {
                ExternalAssetDrop.SceneTarget = new SceneDropContext
                {
                    Scene = scene,
                    Camera = _editorCamera!,
                    MouseLocal = mouseLocal,
                    PanelSize = panelSize,
                };
            }

            if (isHovered && !DragDrop.IsDragging && DragDrop.Payload is AssetDragPayload assetDrop)
            {
                // The active tool sees the drop first, so a tool can accept assets its own way
                // (a material onto a face, say) before the generic handlers spawn an object.
                bool consumedByTool = false;
                foreach (SceneTool dropTool in SceneToolManager.Live)
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

                var dropContext = new SceneDropContext { Scene = scene, Camera = _editorCamera!, MouseLocal = mouseLocal, PanelSize = panelSize };
                if (SceneDrop.Drop(assetDrop, dropContext))
                    DragDrop.EndDrag();
            }

            // Floating transform-tools panel (top-left)
            DrawTransformTools(paper);
            DrawToolOverlays(paper, width, height);

            // Speed indicator (shows briefly when scroll changes fly speed)
            DrawSpeedIndicator(paper, font, width, height);

            // View manipulator (orientation cube) drawn as 2D overlay on top-right, hidden in 2D mode
            if (!_editorCamera!.Is2D) DrawViewManipulator(paper, font, width, height);
            else _viewCubeRadius = 0f;
            Draw2DToggle(paper, width);

            // What the selected camera sees, bottom-right
            _cameraPreview.Draw(paper, font, width, height, _editorCamera!);
        }
    }

    // How close, in pixels, the cursor must come to a gizmo line, and the least size a small or distant gizmo
    // shape counts as, so one a few pixels across can still be clicked.
    private const float GizmoLinePickPixels = 6f;
    private const float GizmoMinPickPixels = 8f;

    internal static GameObject? PickObjectAt(Scene scene, EditorCamera camera, Float2 screenPos, Float2 panelSize, bool pickGizmos = true)
    {
        Ray ray = camera.ScreenPointToRay(screenPos, panelSize);

        // Icons draw over everything, geometry and UI included, so one under the cursor wins outright.
        GizmoPick gizmo = pickGizmos && camera.ShowGizmos
            ? PickGizmo(scene.Gizmos.Hitboxes, camera.Camera, screenPos, panelSize)
            : default;
        if (gizmo.IsIcon) return gizmo.Owner;

        GameObject? bestHit = null;
        float bestDist = float.MaxValue;

        foreach (GameObject go in scene.Objects.Where(o => o.EnabledInHierarchy && o.Section is not { HiddenInSceneView: true }))
        {
            if (go.HideFlags.HasFlag(HideFlags.Hide)) continue;

            foreach (Component component in go.GetComponents())
            {
                if (component is not Runtime.Rendering.IRaycastRenderer renderer || !component.EnabledInHierarchy) continue;
                if (renderer.Raycast(ray, out float dist) && dist < bestDist) { bestDist = dist; bestHit = go; }
            }
        }

        // Other gizmos are lines and markers in the scene, so they compete with geometry on depth.
        if (gizmo.Owner.IsValid() && gizmo.Distance < bestDist)
        {
            bestHit = gizmo.Owner;
            bestDist = gizmo.Distance;
        }

        // UI elements aren't visible to renderer raycasts but should still be selectable from the
        // scene view. The scene view lays UI out in world space (ReferenceResolution + transform), so
        // pick with the same world-space override or the design-space hit test would disagree with
        // what's drawn.
        bool prevWorldSpace = GameCanvas.EditorWorldSpaceOverride;
        GameCanvas.EditorWorldSpaceOverride = true;
        try
        {
            GameObject? uiHit = UIPicker.Pick(scene, ray);
            if (uiHit.IsValid()) return uiHit;
        }
        finally
        {
            GameCanvas.EditorWorldSpaceOverride = prevWorldSpace;
        }

        return bestHit;
    }

    /// <summary>The gizmo under the cursor: the object it selects, how far it is from the camera, and whether it is an icon.</summary>
    internal readonly record struct GizmoPick(GameObject? Owner, float Distance, bool IsIcon);

    /// <summary>
    /// Tests the cursor against the hitboxes the last gizmo pass recorded. Everything is tested on screen, so a
    /// line is clicked within a few pixels however far away it is. Icons win over every other shape, then the
    /// nearest hit wins.
    /// </summary>
    internal static GizmoPick PickGizmo(IReadOnlyList<GizmoHitbox> hitboxes, Camera camera, Float2 screenPos, Float2 panelSize)
    {
        GizmoPick best = default;
        float bestDist = float.MaxValue;
        Float3 eye = camera.Transform.Position;
        Float3 right = camera.Transform.Right;
        Float3 forward = camera.Transform.Forward;

        foreach (GizmoHitbox hitbox in hitboxes)
        {
            GameObject owner = hitbox.Owner;
            if (owner.IsNotValid() || !owner.EnabledInHierarchy || owner.HideFlags.HasFlag(HideFlags.Hide)) continue;
            if (owner.Section is { HiddenInSceneView: true }) continue;

            bool isIcon = hitbox.Kind == GizmoHitboxKind.Icon;
            if (best.IsIcon && !isIcon) continue;

            float dist;
            if (hitbox.Kind == GizmoHitboxKind.Segment)
            {
                // Clip to the near plane, so an edge running past the camera stays clickable where it is visible.
                Float3 wa = hitbox.A, wb = hitbox.B;
                float near = MathF.Max(camera.NearClipPlane, 1e-4f);
                float da = Float3.Dot(wa - eye, forward), db = Float3.Dot(wb - eye, forward);
                if (da < near && db < near) continue;
                if (da < near) wa = wb + (wa - wb) * ((db - near) / (db - da));
                else if (db < near) wb = wa + (wb - wa) * ((da - near) / (da - db));

                Float3 a = camera.WorldToScreenPoint(wa, panelSize);
                Float3 b = camera.WorldToScreenPoint(wb, panelSize);
                if (a.Z <= 0f || b.Z <= 0f) continue;

                Float2 sa = new(a.X, a.Y), sb = new(b.X, b.Y);
                Float2 ab = sb - sa;
                float lengthSq = Float2.Dot(ab, ab);
                float u = lengthSq > 1e-6f ? Math.Clamp(Float2.Dot(screenPos - sa, ab) / lengthSq, 0f, 1f) : 0f;
                if (Float2.Distance(sa + ab * u, screenPos) > GizmoLinePickPixels) continue;
                dist = Float3.Distance(eye, wa + (wb - wa) * u);
            }
            else
            {
                Float3 center = camera.WorldToScreenPoint(hitbox.A, panelSize);
                if (center.Z <= 0f) continue;

                // The shape's size on screen, measured along the camera's right axis so it matches a camera facing icon.
                Float3 edge = camera.WorldToScreenPoint(hitbox.A + right * hitbox.Radius, panelSize);
                float pixels = MathF.Max(Float2.Distance(new Float2(edge.X, edge.Y), new Float2(center.X, center.Y)), GizmoMinPickPixels);
                Float2 offset = screenPos - new Float2(center.X, center.Y);

                // An icon is a square on screen, a sphere a circle.
                bool inside = isIcon
                    ? MathF.Abs(offset.X) <= pixels && MathF.Abs(offset.Y) <= pixels
                    : Float2.Dot(offset, offset) <= pixels * pixels;
                if (!inside) continue;
                dist = Float3.Distance(eye, hitbox.A);
            }

            if ((isIcon && !best.IsIcon) || dist < bestDist)
            {
                best = new GizmoPick(owner, dist, isIcon);
                bestDist = dist;
            }
        }

        return best;
    }

    /// <summary>
    /// What a click on <paramref name="go"/> selects. The first click takes the outermost prefab instance it is part
    /// of, so a prefab moves as one piece. Clicking it again while that is selected drills in to the next nested
    /// instance, and finally to the object itself. A Ctrl click (<paramref name="toggling"/>) takes the selected
    /// level instead, so it deselects what a plain click picked rather than adding the part under it.
    /// </summary>
    internal static GameObject SelectionTarget(GameObject go, bool toggling = false)
    {
        // Innermost first: the object itself, then every prefab instance root above it.
        var chain = new List<GameObject> { go };
        for (GameObject? ancestor = go.Parent; ancestor.IsValid(); ancestor = ancestor.Parent)
            if (ancestor.IsPrefabInstance && PrefabUtility.GetPrefabInstanceRoot(ancestor) == ancestor)
                chain.Add(ancestor);

        // The level just inside the deepest selected one, or the outermost when none is selected.
        for (int i = 0; i < chain.Count; i++)
            if (Selection.IsSelected(chain[i]))
                return toggling ? chain[i] : chain[Math.Max(i - 1, 0)];
        return chain[^1];
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
    /// the selection, Ctrl removes from it, plain replaces.
    /// </summary>
    private void ApplyMarquee(Scene scene, Float2 panelSize)
    {
        Float2 a = _marqueeStart, b = _handles.MousePosition;
        float minX = MathF.Min(a.X, b.X), maxX = MathF.Max(a.X, b.X);
        float minY = MathF.Min(a.Y, b.Y), maxY = MathF.Max(a.Y, b.Y);

        bool additive = _handles.Shift || _handles.Ctrl;
        if (!additive) Selection.Clear();

        // Children of one prefab resolve to the same root, which must be added or toggled once. The selected
        // level or else the outermost root: a box never drills in the way a repeated click does.
        var targets = new HashSet<GameObject>();
        foreach (GameObject go in scene.Objects.Where(o => o.EnabledInHierarchy && o.Section is not { HiddenInSceneView: true }))
        {
            if (go.HideFlags.HasFlag(HideFlags.Hide)) continue;
            if (!TryGetSelectionAnchor(go, out Float3 anchor)) continue;

            Float2? screen = _handles.WorldToScreen(anchor);
            if (screen is null) continue;
            if (screen.Value.X < minX || screen.Value.X > maxX) continue;
            if (screen.Value.Y < minY || screen.Value.Y > maxY) continue;

            targets.Add(SelectionTarget(go, toggling: true));
        }

        foreach (GameObject target in targets)
        {
            if (_handles.Ctrl) Selection.RemoveFromSelection(target);
            else Selection.AddToSelection(target);
        }
    }

    /// <summary>The point a marquee tests against: renderer bounds centre, else the transform.</summary>
    private static bool TryGetSelectionAnchor(GameObject go, out Float3 anchor)
    {
        MeshRenderer? mr = go.GetComponent<MeshRenderer>();
        if (mr.IsValid() && mr.EnabledInHierarchy && mr.Mesh.IsValid())
        {
            AABB b = mr.Mesh.bounds;
            anchor = Float4x4.TransformPoint((b.Min + b.Max) * 0.5f, go.Transform.LocalToWorldMatrix);
            return true;
        }

        SkinnedMeshRenderer? smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr.IsValid() && smr.EnabledInHierarchy && smr.SharedMesh.IsValid())
        {
            AABB b = smr.SharedMesh.bounds;
            anchor = Float4x4.TransformPoint((b.Min + b.Max) * 0.5f, go.Transform.LocalToWorldMatrix);
            return true;
        }

        // Everything else (lights, cameras, empties) selects on its origin.
        anchor = go.Transform.Position;
        return true;
    }

    private void PickObject(Scene scene, Float2 screenPos, Float2 panelSize)
    {
        GameObject? picked = PickObjectAt(scene, _editorCamera!, screenPos, panelSize);

        if (picked.IsValid())
        {
            // Shift and Ctrl both add or remove the clicked object, as in Unity
            bool toggling = Input.IsCtrlPressed || Input.IsShiftPressed;
            GameObject target = SelectionTarget(picked, toggling);
            if (toggling)
                Selection.ToggleSelection(target);
            else
                Selection.Select(target);

            // Ping so the Hierarchy scrolls to and briefly highlights the clicked GO.
            Selection.Ping(target.Identifier);
        }
        else if (!Input.IsCtrlPressed && !Input.IsShiftPressed)
        {
            Selection.Clear();
        }
    }

    public override bool SerializeState(System.Text.Json.Nodes.JsonObject state)
    {
        if (_editorCamera == null) return false;
        Float3 p = _editorCamera.RestPosition;
        state["px"] = p.X; state["py"] = p.Y; state["pz"] = p.Z;
        state["yaw"] = _editorCamera.Yaw;
        state["pitch"] = _editorCamera.Pitch;
        state["grid"] = _editorCamera.ShowGrid;
        state["2d"] = _editorCamera.Is2D;
        if (_editorCamera.Before2D is { } before)
        {
            state["b2dx"] = before.Position.X; state["b2dy"] = before.Position.Y; state["b2dz"] = before.Position.Z;
            state["b2dyaw"] = before.Yaw; state["b2dpitch"] = before.Pitch;
            state["b2dortho"] = before.Orthographic; state["b2dsize"] = before.OrthographicSize; state["b2ddist"] = before.Distance;
        }
        if (_editorCamera.IsOrthographic) state["orthoSize"] = _editorCamera.OrthographicSize;
        state["fov"] = _editorCamera.FieldOfView;
        state["near"] = _editorCamera.NearClip;
        state["far"] = _editorCamera.FarClip;
        state["speed"] = _editorCamera.MoveSpeed;
        state["smoothing"] = _editorCamera.MoveSmoothing;
        state["gizmos"] = _editorCamera.ShowGizmos;
        state["post"] = _editorCamera.PostProcessing;
        state["debugView"] = _editorCamera.DebugView.ToString();
        state["navAlwaysShow"] = NavMeshDebugDisplay.AlwaysShow;
        state["navShowDetail"] = NavMeshDebugDisplay.ShowDetail;
        state["navShowVertices"] = NavMeshDebugDisplay.ShowVertices;
        state["orientation"] = SceneTools.Orientation.ToString();
        state["pivot"] = SceneTools.Pivot.ToString();
        state["gridSnap"] = SceneTools.GridSnap;
        state["gridSize"] = SceneTools.MoveSnap;
        state["rotationSnap"] = SceneTools.RotationSnap;
        state["rotationStep"] = SceneTools.RotateSnap;
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
        _pending2D = state["2d"]?.GetValue<bool>() == true;
        _pendingOrthoSize = state["orthoSize"]?.GetValue<float>();
        _pendingBefore2D = state["b2dx"] is null ? null : new EditorCamera.View3D(
            new Float3(state["b2dx"]!.GetValue<float>(), state["b2dy"]?.GetValue<float>() ?? 0f, state["b2dz"]?.GetValue<float>() ?? 0f),
            state["b2dyaw"]?.GetValue<float>() ?? 0f, state["b2dpitch"]?.GetValue<float>() ?? 15f,
            state["b2dortho"]?.GetValue<bool>() ?? false, state["b2dsize"]?.GetValue<float>() ?? 5f, state["b2ddist"]?.GetValue<float>() ?? 10f);
        _pendingLens = (state["fov"]?.GetValue<float>(), state["near"]?.GetValue<float>(),
                        state["far"]?.GetValue<float>(), state["speed"]?.GetValue<float>(),
                        state["smoothing"]?.GetValue<float>());
        _pendingGizmos = state["gizmos"]?.GetValue<bool>();
        _pendingPost = state["post"]?.GetValue<bool>();
        _pendingDebugView = Enum.TryParse(state["debugView"]?.GetValue<string>(), out DebugViewMode debugView) ? debugView : null;

        NavMeshDebugDisplay.AlwaysShow = state["navAlwaysShow"]?.GetValue<bool>() ?? false;
        NavMeshDebugDisplay.ShowDetail = state["navShowDetail"]?.GetValue<bool>() ?? false;
        NavMeshDebugDisplay.ShowVertices = state["navShowVertices"]?.GetValue<bool>() ?? false;

        // Tool settings are editor wide; whichever Scene view restores last wins, as they all saved the same values.
        if (Enum.TryParse(state["orientation"]?.GetValue<string>(), out PivotOrientation orientation)) SceneTools.Orientation = orientation;
        if (Enum.TryParse(state["pivot"]?.GetValue<string>(), out PivotMode pivot)) SceneTools.Pivot = pivot;
        if (state["gridSnap"]?.GetValue<bool>() is bool gridSnap) SceneTools.GridSnap = gridSnap;
        if (state["gridSize"]?.GetValue<float>() is float gridSize && gridSize > 0f) SceneTools.MoveSnap = gridSize;
        if (state["rotationSnap"]?.GetValue<bool>() is bool rotationSnap) SceneTools.RotationSnap = rotationSnap;
        if (state["rotationStep"]?.GetValue<float>() is float rotationStep && rotationStep > 0f) SceneTools.RotateSnap = rotationStep;
    }

    private bool? _pendingGrid;
    private (float? Fov, float? Near, float? Far, float? Speed, float? Smoothing)? _pendingLens;
    private bool? _pendingGizmos;
    private bool? _pendingPost;
    private DebugViewMode? _pendingDebugView;

    /// <summary>
    /// Raycast into the scene to find a drop position. Falls back to the XZ plane at Y=0.
    /// </summary>
    internal static Float3 GetDropPosition(Scene scene, EditorCamera camera, Float2 screenPos, Float2 panelSize)
    {
        Float3 p = RaycastDropPosition(scene, camera, screenPos, panelSize);
        // Same toggle and Ctrl inversion as the move gizmo. Y stays on the surface hit so the drop doesn't sink into it.
        float step = SceneTools.MoveSnap;
        if (SceneTools.GridSnap == Input.IsCtrlPressed || step <= 0f) return p;
        return new Float3(MathF.Round(p.X / step) * step, p.Y, MathF.Round(p.Z / step) * step);
    }

    /// <summary>Nearest point where <paramref name="ray"/> hits a visible mesh in the scene, or null.</summary>
    internal static Float3? RaycastSurface(Scene scene, Ray ray)
    {
        float bestDist = float.MaxValue;
        Float3? best = null;

        foreach (GameObject go in scene.Objects.Where(o => o.EnabledInHierarchy && o.Section is not { HiddenInSceneView: true }))
        {
            if (go.HideFlags.HasFlag(HideFlags.Hide)) continue;

            MeshRenderer? meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer.IsValid() && meshRenderer.EnabledInHierarchy && meshRenderer.Raycast(ray, out float dist) && dist < bestDist)
            {
                bestDist = dist;
                best = ray.Origin + ray.Direction * dist;
            }
        }

        return best;
    }

    private static Float3 RaycastDropPosition(Scene scene, EditorCamera camera, Float2 screenPos, Float2 panelSize)
    {
        Ray ray = camera.ScreenPointToRay(screenPos, panelSize);
        if (RaycastSurface(scene, ray) is { } hitPos) return hitPos;

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

    /// <summary>Called only by views that are drawing; disabled gizmos do not consume the frame's shared pass.</summary>
    internal void DrawSceneGizmos(EditorApplication? editor, Scene scene)
    {
        if (!ShowGizmos) return;
        editor?.DrawSceneGizmos(scene);
        if (ShowsCursor) DrawCursor();
        DrawSelectionGizmos();

        // Faint outline of each drag preview's extent.
        foreach ((GameObject ghost, _) in _dropGhosts)
            if (ghost.IsValid() && TryGetWorldBounds(ghost, out Float3 min, out Float3 max))
                Debug.DrawWireCube((min + max) * 0.5f, (max - min) * 0.5f, new Vector.Color(1f, 1f, 1f, 0.25f));
    }

    // Drag preview: hidden, unsaved, render-only copies of what the drop would spawn, following the cursor, each
    // at its offset from the drop point. _dropGhostPayload/_dropGhostScene are remembered even when no ghost could
    // be built, so it isn't retried every frame.
    private readonly List<(GameObject Ghost, Float3 Offset)> _dropGhosts = [];
    private AssetDragPayload? _dropGhostPayload;
    private Scene? _dropGhostScene;
    private bool _dropGhostTouched;
    private static readonly List<SceneViewPanel> s_ghostOwners = [];
    private static Material? s_meshGhostMaterial;

    private void UpdateDropPreview(Scene scene, AssetDragPayload payload, Float3 dropPos)
    {
        _dropGhostTouched = true;
        if (!ReferenceEquals(payload, _dropGhostPayload) || !ReferenceEquals(scene, _dropGhostScene))
        {
            ClearDropPreview();
            _dropGhostPayload = payload;
            _dropGhostScene = scene;
            // Not in a playing world: the copies would join the running game.
            if (!scene.IsPlaying)
            {
                foreach ((AssetDragPayload item, Float3 pos) in SceneDrop.Layout(payload, Float3.Zero))
                {
                    GameObject? ghost = BuildDropGhost(item.AssetGuid);
                    if (ghost.IsNotValid()) continue;
                    scene.Add(ghost);
                    _dropGhosts.Add((ghost, pos));
                }
                if (_dropGhosts.Count > 0) s_ghostOwners.Add(this);
            }
        }
        foreach ((GameObject ghost, Float3 offset) in _dropGhosts)
            if (ghost.IsValid()) ghost.Transform.Position = dropPos + offset;
    }

    /// <summary>
    /// Once per editor frame: drop the preview of any view that didn't draw a drag hover since the last sweep
    /// (hidden behind another dock tab, say), so its copy can't linger in the scene.
    /// </summary>
    internal static void SweepDropGhosts()
    {
        for (int i = s_ghostOwners.Count - 1; i >= 0; i--)
        {
            SceneViewPanel owner = s_ghostOwners[i];
            if (owner._dropGhostTouched) owner._dropGhostTouched = false;
            else owner.ClearDropPreview();
        }
    }

    private static GameObject? BuildDropGhost(Guid guid)
    {
        GameObject? ghost = Runtime.AssetDatabase.Get(guid) switch
        {
            PrefabAsset prefab when prefab.IsValid() => GameObject.InstantiateDetached(prefab),
            Mesh mesh when mesh.IsValid() => MeshGhost(mesh),
            _ => null,
        };
        if (ghost.IsValid()) HideGhost(ghost);
        return ghost;

        static GameObject MeshGhost(Mesh mesh)
        {
            var go = new GameObject(mesh.Name);
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.Mesh = mesh;
            if (s_meshGhostMaterial.IsNotValid()) s_meshGhostMaterial = new Material(Shader.LoadDefault(DefaultShader.Lit));
            r.Material = s_meshGhostMaterial;
            return go;
        }

        // Per object, not just the root: picking and drop raycasts test each object's own flags. Everything but
        // renderers is disabled before the copy enters the scene, so lights, colliders, audio and [ExecuteAlways]
        // scripts never wake up.
        static void HideGhost(GameObject go)
        {
            go.HideFlags |= HideFlags.Hide | HideFlags.HideAndDontSave | HideFlags.NotEditable | HideFlags.NoGizmos;
            foreach (Component c in go.GetComponents<Component>())
                if (c is not IMaterialRenderer) c.Enabled = false;
            foreach (GameObject child in go.Children) HideGhost(child);
        }
    }

    private void ClearDropPreview()
    {
        foreach ((GameObject ghost, _) in _dropGhosts)
            if (ghost.IsValid()) ghost.Destroy();
        _dropGhosts.Clear();
        _dropGhostPayload = null;
        _dropGhostScene = null;
        s_ghostOwners.Remove(this);
    }

    private void DrawSelectionGizmos()
    {
        using FrameStatScope stats = EditorFrameStats.Gizmos.Scope();
        foreach (object obj in Selection.Selected)
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

    /// <summary>
    /// The world bounds of everything a GameObject tree draws, or false when it draws nothing.
    /// Shared so anything that needs to point a camera at an object agrees with what the selection
    /// outline says its extent is.
    /// </summary>
    internal static bool TryGetWorldBounds(GameObject go, out Float3 min, out Float3 max)
    {
        min = new Float3(float.MaxValue);
        max = new Float3(float.MinValue);
        bool found = false;

        CollectRendererBounds(go, ref min, ref max, ref found);
        return found;
    }

    private static void CollectRendererBounds(GameObject go, ref Float3 min, ref Float3 max, ref bool found)
    {
        // Check MeshRenderer
        MeshRenderer? mr = go.GetComponent<MeshRenderer>();
        if (mr.IsValid() && mr.Mesh.IsValid())
            ExpandBounds(mr.Mesh.bounds, go.Transform.LocalToWorldMatrix, ref min, ref max, ref found);

        // Check SkinnedMeshRenderer
        SkinnedMeshRenderer? smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr.IsValid() && smr.SharedMesh.IsValid())
            ExpandBounds(smr.SharedMesh.bounds, go.Transform.LocalToWorldMatrix, ref min, ref max, ref found);

        // Recurse into children
        foreach (GameObject child in go.Children)
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

    public override void OnClosed()
    {
        if (_wasGizmoActive) EndGizmoDrag();
        _editorCamera?.ReleaseFlight();
        ClearDropPreview();
        _cameraPreview.Dispose();
    }

    // The kind of transform the current drag turned out to be, or null before it has moved.
    private TransformTool? _dragKind;

    // Holding Ctrl inverts a snap toggle: it snaps while the toggle is off and frees the drag while it is on.
    private bool GridSnapping => SceneTools.GridSnap != _handles.Ctrl;
    private bool RotationSnapping => SceneTools.RotationSnap != _handles.Ctrl;

    // The gizmo has one snapping switch for every kind of drag, so pick the toggle that matches the drag.
    // Before a universal-gizmo drag shows its kind, either toggle snaps.
    private bool StickySnap() => (_dragKind ?? SceneTools.Transform) switch
    {
        TransformTool.Rotate => RotationSnapping,
        TransformTool.Universal => GridSnapping || RotationSnapping,
        _ => GridSnapping,
    };

    // Grid snapping of moves is absolute: the handle's pivot lands on grid points along the axes the drag moves,
    // rather than moving in whole steps from wherever it started. Kept as where the pivot started, how far the
    // drag has moved it, and how far the cursor has dragged it before snapping.
    private Float3 _moveStart;
    private Float3 _moveApplied;
    private Float3 _moveRaw;
    private bool _moveSnapped;

    // What a snapped move lands on the grid by its bounds when a tool gives it, else the pivot snaps
    private IReadOnlyList<Float3>? _snapPoints;

    /// <summary>
    /// Where a move of <paramref name="movement"/> from <paramref name="start"/> lands with grid snapping: every
    /// axis the move changed rounds to the nearest multiple of <paramref name="step"/>, and the axes it left alone
    /// stay put. The grid is the handle's own (<paramref name="space"/>), so a local handle on a rotated object
    /// snaps along its own axes; a handle turned only by right angles snaps to the world grid.
    /// </summary>
    internal static Float3 SnapToGrid(Float3 start, Float3 movement, float step, Quaternion space)
    {
        if (step <= 0f) return start + movement;
        if (IsAxisAligned(space)) return SnapMovedAxes(start + movement, movement, step);

        Quaternion inverse = Quaternion.Inverse(space);
        Float3 localStart = inverse * start;
        Float3 localMove = inverse * movement;
        Float3 snapped = SnapMovedAxes(localStart + localMove, localMove, step);
        return start + space * (snapped - localStart);
    }

    /// <summary>
    /// The move that lands moved points on the grid: on every axis the move changed, whichever side of their bounds
    /// lies nearer a grid line lands on it, and the axes it left alone stay put. With every axis, all three land. The
    /// grid is the handle's own as in <see cref="SnapToGrid"/>, so a handle turned only by right angles snaps to the
    /// world grid.
    /// </summary>
    internal static Float3 SnapBoundsToGrid(IReadOnlyList<Float3> points, Float3 movement, float step, Quaternion space, bool everyAxis = false)
    {
        if (step <= 0f || points.Count == 0) return movement;

        bool world = IsAxisAligned(space);
        Quaternion inverse = world ? Quaternion.Identity : Quaternion.Inverse(space);
        Float3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Float3 point in points)
        {
            Float3 local = inverse * point;
            min = Maths.Min(min, local);
            max = Maths.Max(max, local);
        }

        Float3 move = inverse * movement, result = move;
        Float3 far = Maths.Max(Maths.Abs(min), Maths.Abs(max));
        float size = MathF.Max(far.X, MathF.Max(far.Y, far.Z));
        float still = 2e-6f * (1f + size);
        for (int i = 0; i < 3; i++)
        {
            if (!everyAxis && MathF.Abs(move[i]) <= still) continue;
            float low = min[i] + move[i], high = max[i] + move[i];
            float toLow = MathF.Round(low / step) * step - low, toHigh = MathF.Round(high / step) * step - high;
            result[i] = move[i] + (MathF.Abs(toLow) <= MathF.Abs(toHigh) ? toLow : toHigh);
        }
        return world ? result : space * result;
    }

    private static Float3 SnapMovedAxes(Float3 p, Float3 movement, float step)
    {
        // About 16 float ulps of the coordinates' size: a drag far from the origin leaves float noise on the axes
        // it did not move, but a real move bigger than that still snaps.
        float Moved = 2e-6f * (1f +MathF.Max(MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y)), MathF.Abs(p.Z)));
        return new Float3(
            MathF.Abs(movement.X) > Moved ? MathF.Round(p.X / step) * step : p.X,
            MathF.Abs(movement.Y) > Moved ? MathF.Round(p.Y / step) * step : p.Y,
            MathF.Abs(movement.Z) > Moved ? MathF.Round(p.Z / step) * step : p.Z);
    }

    // Every axis of the rotation lies along a world axis, so its grid is the world's.
    private static bool IsAxisAligned(Quaternion rotation)
        => IsAligned(rotation * Float3.UnitX) && IsAligned(rotation * Float3.UnitY) && IsAligned(rotation * Float3.UnitZ);

    private static bool IsAligned(Float3 axis)
        => MathF.Abs(axis.X - MathF.Round(axis.X)) < 1e-4f
        && MathF.Abs(axis.Y - MathF.Round(axis.Y)) < 1e-4f
        && MathF.Abs(axis.Z - MathF.Round(axis.Z)) < 1e-4f;

    // Size of the free move dot at the handle's centre, and how far around it a press grabs it, in pixels.
    private const float FreeMoveDotPixels = 9f;
    private const float FreeMoveGrabPixels = 8f;

    // The view plane a free move drags across, fixed for the drag where it began.
    private Float3 _freeMoveOrigin, _freeMoveNormal, _freeMoveStartHit;

    /// <summary>
    /// The small solid dot at the move handle's centre, which drags the selection across the camera's view
    /// plane. Returns the drag as a gizmo result while it is held, so it moves, snaps and records undo exactly as
    /// the axis handles do.
    /// </summary>
    private GizmoResult? UpdateFreeMove(ControlID id, Float3 position, Float3 cameraForward)
    {
        // Registered after the gizmo at zero distance, as the gizmo reports its hover, so the dot wins anywhere in its grab radius.
        bool overDot = _handles.DistanceToPoint(position, FreeMoveGrabPixels) < float.MaxValue;
        _handles.AddControl(id, overDot ? 0f : float.MaxValue, _handles.DepthOf(position));
        _handles.RequestCursor(id, PaperCursor.ResizeAll);

        if (_handles.TryBeginDrag(id))
        {
            _freeMoveOrigin = position;
            _freeMoveNormal = cameraForward;
            if (!RayPlane(_handles.MouseRay, _freeMoveOrigin, _freeMoveNormal, out _freeMoveStartHit))
                _freeMoveStartHit = position;
        }

        bool active = _handles.IsActive(id);
        _handles.Draw.Dot(position, active ? new Color32(255, 214, 80, 255) : new Color32(240, 240, 240, 235),
            active ? FreeMoveDotPixels + 2f : FreeMoveDotPixels, HandleCap.Circle);

        if (!_handles.IsHot(id) || !RayPlane(_handles.MouseRay, _freeMoveOrigin, _freeMoveNormal, out Float3 hit))
            return null;

        _dragKind = TransformTool.Translate;
        Float3 total = hit - _freeMoveStartHit;
        return new GizmoResult { TranslationDelta = total - _moveRaw, TotalTranslation = total };
    }

    private static bool RayPlane(Ray ray, Float3 origin, Float3 normal, out Float3 hit)
    {
        hit = default;
        float facing = Float3.Dot(ray.Direction, normal);
        if (MathF.Abs(facing) < 1e-6f) return false;
        float t = Float3.Dot(origin - ray.Origin, normal) / facing;
        if (t < 0f) return false;
        hit = ray.Origin + ray.Direction * t;
        return true;
    }

    private void EndGizmoDrag()
    {
        _dragKind = null;
        _moveApplied = _moveRaw = Float3.Zero;
        _moveSnapped = false;
        _wasGizmoActive = false;
        _gizmoTargets = null;
        _gizmoSelection = null;
    }

    private void UpdateTransformGizmo()
    {
        using FrameStatScope stats = EditorFrameStats.Gizmos.Scope();
        _gizmoActive = false;
        if (_wasGizmoActive && !_handles.IsHot(_handles.GetControlID(TransformControl)) && !_handles.IsHot(_handles.GetControlID(FreeMoveControl)))
        {
            EndGizmoDrag();
        }
        if (_editorCamera == null) return;
        if (SceneTools.SuppressTransformGizmo) return;

        // Only show gizmo when GameObjects are selected
        IEnumerable<GameObject> selectedObjects = _gizmoSelection ?? Selection.GetSelected<GameObject>();
        IEnumerator<GameObject> selectedGOs = selectedObjects.GetEnumerator();
        if (!selectedGOs.MoveNext()) return;

        _gizmoActive = true;

        GameObject firstGO = selectedGOs.Current;
        if (firstGO.IsNotValid()) return;

        // Create gizmo if needed
        _transformGizmo ??= new Gizmo.TransformGizmo(SceneTools.GizmoMode);
        _transformGizmo.GizmoSize = 100f;

        // The handle sits on the active object in Active mode, else on the selection's centre.
        Float3 center;
        if (SceneTools.Pivot == PivotMode.Active)
        {
            center = firstGO.Transform.Position;
        }
        else if (SceneTools.Pivot == PivotMode.Cursor)
        {
            center = SceneTools.Cursor;
        }
        else
        {
            center = Float3.Zero;
            int count = 0;
            foreach (GameObject go in selectedObjects)
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
        _transformGizmo.Orientation = SceneTools.Orientation != PivotOrientation.Global
            ? Gizmo.TransformGizmo.GizmoOrientation.Local
            : Gizmo.TransformGizmo.GizmoOrientation.Global;

        // Update gizmo use absolute screen rect so coordinates match DrawForeground
        Camera cam = _editorCamera.Camera;
        GameObject camGo = cam.GameObject;

        _transformGizmo.UpdateCamera(_handles.Viewport, cam.ViewMatrix, cam.ProjectionMatrix,
            camGo.Transform.Up, camGo.Transform.Forward, camGo.Transform.Right, camGo.Transform.Position);
        // While a move snaps, the gizmo follows the cursor's unsnapped drag rather than the snapped object. The
        // gizmo works out the drag from where its handle is, so placing it on the snapped point would feed the
        // snap back into the drag, and a free move would wander off the plane it started on.
        Float3 handlePosition = _wasGizmoActive && _moveSnapped ? _moveStart + _moveRaw : center;
        _transformGizmo.SetTransform(handlePosition, rotation, scale);

        ControlID control = _handles.GetControlID(TransformControl);

        // The toolbar toggles snap their own kind of drag, and Ctrl inverts them. Moves snap to the grid here
        // rather than in the gizmo, so the gizmo hands over the raw drag for them.
        Quaternion handleSpace = SceneTools.Orientation != PivotOrientation.Global ? rotation : Quaternion.Identity;
        _transformGizmo.Snapping = StickySnap() && (_dragKind ?? SceneTools.Transform) != TransformTool.Translate;
        _transformGizmo.SnapDistance = SceneTools.MoveSnap;
        _transformGizmo.SnapAngle = SceneTools.RotateSnap;
        _transformGizmo.SnapScale = SceneTools.ScaleSnap;
        _transformGizmo.IsShiftDown = _handles.Shift;
        // The grab decision comes from arbitration, so a handle nearer the cursor wins instead of the
        // gizmo taking every press it happens to be over.
        _transformGizmo.IsMouseDown = _handles.TryBeginDrag(control);
        _transformGizmo.IsMouseUp = _handles.PrimaryUp;

        GizmoResult? result = _transformGizmo.Update(_handles.MouseRay, _handles.MousePosition, _handles.Blocked);

        // IsOver is fresh from the Update above. Guarded on Blocked because the gizmo only clears its
        // hover inside the un-blocked branch, so a blocked frame would leave IsOver latched on.
        // Origami exposes only a bool, not a per-axis screen distance, so a hover registers at zero
        // and the gizmo's own depth decides overlaps against handles stacked on top of it.
        bool gizmoHovered = _transformGizmo.IsOver && !_handles.Blocked;
        _handles.AddControl(control, gizmoHovered ? 0f : float.MaxValue, _handles.DepthOf(center));
        _handles.RequestCursor(control, PaperCursor.ResizeAll);

        // Remember what kind of drag this is from its first result, so the next frames snap by the right
        // toggle. The gizmo recomputes a drag from where it started every frame, so the frame before is fixed up.
        // The free move dot drags in the view plane, and reports like one of the gizmo's own handles.
        ControlID freeMove = _handles.GetControlID(FreeMoveControl);
        if (SceneTools.Transform == TransformTool.Translate && !SceneTools.SuppressTransformGizmo)
            result = UpdateFreeMove(freeMove, handlePosition, camGo.Transform.Forward) ?? result;

        if (result is { } kind && _dragKind == null && _handles.IsHot(control))
        {
            if (kind.RotationDelta.HasValue) _dragKind = TransformTool.Rotate;
            else if (kind.ScaleDelta.HasValue) _dragKind = TransformTool.Scale;
            else if (kind.TranslationDelta.HasValue) _dragKind = TransformTool.Translate;
        }

        // Gizmo drawing happens in the viewport's DrawForeground callback (needs canvas)

        // The drag spans every frame the control is hot. Keyed off control ownership rather than whether the
        // gizmo produced a delta this frame - a sub-gizmo returns no result at degenerate view
        // angles, which would otherwise split one drag into two undo steps.
        bool dragging = _handles.IsHot(control) || _handles.IsHot(freeMove);
        if (dragging && !_wasGizmoActive)
        {
            _gizmoHasMoved = false;
            _gizmoSelection = [.. selectedObjects];
            _gizmoTargets = [.. GameObjectClipboard.FilterToRoots(_gizmoSelection)];
            _moveStart = center;
            _moveApplied = _moveRaw = Float3.Zero;
            _snapPoints = SceneToolManager.SnapPoints(_gizmoTargets);
            _moveSnapped = false;
        }
        _wasGizmoActive = dragging;

        _handles.TryEndDrag(control);
        _handles.TryEndDrag(freeMove);

        if (result.HasValue && dragging && _gizmoTargets is { } targets)
        {
            GizmoResult r = result.Value;

            // Decide once, on the first actual transform change. Clicking a handle creates no copy.
            bool changed = (r.TranslationDelta is { } move && !move.Equals(Float3.Zero)) ||
                (r.RotationDelta is { } angle && r.RotationAxis.HasValue && angle != 0f) ||
                (r.ScaleDelta is { } scaleDelta && !scaleDelta.Equals(Float3.One));
            if (!_gizmoHasMoved && changed)
            {
                _gizmoHasMoved = true;
                if (_handles.Shift)
                {
                    List<GameObject> copies = GameObjectClipboard.Duplicate(targets);
                    if (copies.Count > 0)
                    {
                        // Keep selected descendants represented in the copied pivot selection.
                        GameObject FindCopy(GameObject go)
                        {
                            int root = Array.IndexOf(targets, go);
                            if (root >= 0) return copies[root];
                            if (go.Parent is { } parent)
                                return FindCopy(parent).Children[parent.Children.IndexOf(go)];
                            throw new InvalidOperationException("Drag selection is outside its roots.");
                        }
                        if (_gizmoSelection is { } selection)
                        {
                            _gizmoSelection = [.. selection.Select(FindCopy)];
                            Selection.Clear();
                            foreach (GameObject go in _gizmoSelection) Selection.AddToSelection(go);
                        }
                        _gizmoTargets = targets = [.. copies];
                        foreach (GameObject copy in copies) Undo.RegisterCreatedObjectUndo(copy, "Duplicate");
                    }
                }
            }

            Undo.RecordObjects(targets, "Transform");

            if (r.TranslationDelta is { } delta)
            {
                // Absolute grid snapping places the pivot from the drag's total, then moves by whatever that
                // changes since the last frame.
                Float3 step = delta;
                _moveRaw = r.TotalTranslation ?? _moveRaw + delta;
                _moveSnapped = GridSnapping;
                if (_moveSnapped)
                    step = (_snapPoints != null
                        ? _moveStart + SnapBoundsToGrid(_snapPoints, _moveRaw, SceneTools.MoveSnap, handleSpace)
                        : SnapToGrid(_moveStart, _moveRaw, SceneTools.MoveSnap, handleSpace)) - _moveStart - _moveApplied;
                _moveApplied += step;

                foreach (GameObject go in targets)
                    if (go.IsValid()) go.Transform.Position += step;
            }

            // Rotation and scale move each object's origin about the drag's pivot, unless each pivots on itself.
            bool aroundPivot = SceneTools.Pivot != PivotMode.Individual;
            Float3 pivot = _moveStart;

            if (r.RotationDelta.HasValue && r.RotationAxis.HasValue)
            {
                var rotDelta = Quaternion.AxisAngle(r.RotationAxis.Value, r.RotationDelta.Value);
                foreach (GameObject go in targets)
                {
                    if (!go.IsValid()) continue;
                    go.Transform.Rotation = rotDelta * go.Transform.Rotation;
                    if (aroundPivot) go.Transform.Position = pivot + rotDelta * (go.Transform.Position - pivot);
                }
            }

            if (r.ScaleDelta is { } scaleBy)
            {
                Quaternion toHandle = Quaternion.Inverse(handleSpace);
                foreach (GameObject go in targets)
                {
                    if (!go.IsValid()) continue;
                    go.Transform.LocalScale *= scaleBy;
                    if (aroundPivot) go.Transform.Position = pivot + handleSpace * (toHandle * (go.Transform.Position - pivot) * scaleBy);
                }
            }
        }

        if (!dragging && _wasGizmoActive)
            EndGizmoDrag();
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
            .Rounded(EditorTheme.Roundness)
            .IsNotInteractable()
            .Text($"{_editorCamera.MoveSpeed:F1}", font)
            .TextColor(Color.FromArgb(ta, EditorTheme.Ink500))
            .FontSize(18f)
            .Alignment(TextAlignment.MiddleCenter);
    }

    // The 2D mode switch, under the view cube, or in its place while in 2D
    private void Draw2DToggle(Paper paper, float width)
    {
        EditorCamera camera = _editorCamera!;
        using (paper.Row("sv_2d_toggle")
            .PositionType(PositionType.SelfDirected)
            .Position(width - 52, camera.Is2D ? 12 : 120)
            .Size(44, 26)
            .Enter())
        {
            ButtonBuilder button = Origami.Button(paper, "sv_2d_btn", "2D", () => camera.Set2D(!camera.Is2D))
                .Small().Width(UnitValue.StretchOne).Height(26).Tooltip(Loc.Get("scene.mode_2d"));
            if (camera.Is2D) button.Primary(); else button.Ghost();
            button.Show();
        }
    }

    private void DrawViewManipulator(Paper paper, Scribe.FontFile font, float width, float height)
    {
        if (_editorCamera == null) return;

        _viewManipulator ??= new Gizmo.ViewManipulatorGizmo();

        float cubeSize = 100;

        _viewManipulator.SetCamera(_editorCamera.Camera.GameObject.Transform.Forward,
            _editorCamera.Camera.GameObject.Transform.Up);
        _viewManipulator.SetFont(font);

        // Draw as overlay on top of the scene use SelfDirected + DrawForeground
        paper.Box("sv_view_manip")
            .PositionType(PositionType.SelfDirected)
            .Position(width - cubeSize - 8, 12)
            .Size(cubeSize, cubeSize)
            .OnPostLayout((handle, rect) =>
            {
                _viewCubeElementId = handle.Data.ID;
                paper.DrawForeground(ref handle, (canvas, r) =>
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

                bool axisPicked = _viewManipulator.Update(canvas, mousePos, clicked && mayClick, !mayClick, out Float3 newForward);

                // Clicking the widget itself rather than one of its axes swaps the projection, which
                // is the other thing an orientation gizmo is conventionally good for.
                if (_viewManipulator.BackgroundClicked)
                    _editorCamera.ToggleProjection();

                if (axisPicked)
                {
                    // Snap camera to face direction
                    // Calculate yaw/pitch from the new forward vector
                    float yaw = MathF.Atan2(newForward.X, newForward.Z) * Gizmo.GizmoUtils.Rad2Deg;
                    float pitch = MathF.Asin(-newForward.Y) * Gizmo.GizmoUtils.Rad2Deg;
                    _editorCamera.SetOrientation(yaw, pitch);
                }
            });
            });
    }
}
