// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.OrigamiUI;
using Prowl.Editor.Docking;
using Prowl.Editor.Inspector;
using Prowl.Editor.GUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

using PropertyGrid = Prowl.Editor.GUI.PropertyGrid;
namespace Prowl.Editor.GraphTools.ShaderGraphs.Editors;

/// <summary>
/// Dock panel that hosts a <see cref="GraphEditor"/> for editing shader graphs. Wraps
/// the widget with a left-hand sidebar containing the compile toolbar, live material
/// preview, and the Properties / Lighting / Blending / Geometry foldouts.
/// </summary>
/// <remarks>
/// The widget handles everything generic (canvas rendering, node creation, wire drag,
/// undo, etc.). This panel layers shader-graph-specific UI and the auto-recompile
/// debounce on top. When the user changes the graph type (e.g. opens a visual
/// scripting graph later), a different panel is used the widget stays the same.
/// </remarks>
[EditorWindow("Tools/Shader Graph Editor")]
public class ShaderGraphEditorWindow : DockPanel
{
    private readonly GraphEditor _editor = new();
    private ShaderGraph? _graph;
    private float _lastWindowHeight;

    // ─── Sidebar state (panel-local widget doesn't know about any of this) ─────────
    private bool _sidebarOpen = true;
    private float _sidebarWidth = 210f;

    // Preview state lives on the panel so it persists across frames / foldout
    // expansions. Lazy-created the first time the sidebar draws.
    private PreviewRenderer? _preview;
    private Runtime.Resources.Material? _lastPreviewMaterial;
    private Runtime.Resources.Mesh?     _lastPreviewMesh;
    private AssetRef<Runtime.Resources.Mesh> _previewMesh;
    private Runtime.Resources.Material? _previewMaterial;

    // ─── Auto-recompile ────────────────────────────────────────
    /// <summary>Toggleable "save after idle" off means user hits Compile manually.</summary>
    private bool _autoRecompile = true;
    /// <summary>Seconds of edit inactivity before auto-save fires. Mirrors SF's 1s.</summary>
    private const float AutoRecompileDelay = 1.0f;

    public override string Title => _graph != null
        ? $"Shader Graph {_graph.Name}"
        : "Shader Graph Editor";

    public override string Icon => EditorIcons.DiagramProject;

    public ShaderGraphEditorWindow()
    {
        // Nested subgraphs of a shader graph are themselves shader graphs route
        // "Open Subgraph" back through this window type so they get the same sidebar.
        _editor.OnOpenSubgraph = OpenSubgraph;

        // Register with SaveManager so Ctrl+S saves dirty graphs
        SaveManager.OnSave += OnProjectSave;
    }

    private string? OnProjectSave()
    {
        if (_graph == null || !_editor.IsDirty) return null;
        _editor.Save();
        return Loc.Get("save.graph", new { name = _graph.Name ?? "Untitled" });
    }

    /// <summary>Open a floating shader-graph editor bound to the given graph. Routed
    /// through here by the asset editor and by subgraph-open inside the canvas.</summary>
    public static void OpenFor(ShaderGraph graph)
    {
        var panel = new ShaderGraphEditorWindow { _graph = graph };
        panel._editor.Graph = graph;
        EditorApplication.Instance?.OpenPanelInstance(panel, 1100, 720);
    }

    private static void OpenSubgraph(Graph graph)
    {
        if (graph is ShaderGraph sg) OpenFor(sg);
        else Debug.LogWarning($"Cannot open '{graph.GetType().Name}' in ShaderGraphEditorWindow no matching editor for this graph type yet.");
    }

    public override bool SerializeState(System.Text.Json.Nodes.JsonObject state)
    {
        if (_graph == null || _graph.AssetID == Guid.Empty) return false;
        state["graph"] = _graph.AssetID.ToString();
        return true;
    }

    public override void RestoreState(System.Text.Json.Nodes.JsonObject state)
    {
        string? guidStr = state["graph"]?.GetValue<string>();
        if (!Guid.TryParse(guidStr, out var guid)) return;
        var graph = ((new AssetRef<Graph>(guid).Res as ShaderGraph));
        if (graph == null) return;
        _graph = graph;
        _editor.Graph = graph;
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        _lastWindowHeight = height;
        var font = EditorTheme.DefaultFont;
        if (font == null || _graph == null) return;

        // Auto-recompile debounce after the user finishes editing, save the graph
        // asset. Save triggers the importer via the asset-DB file watcher, which
        // regenerates the compiled Shader sub-asset. AssetRefs holding the old shader
        // re-resolve on their next access.
        if (_editor.IsDirty && _autoRecompile
            && Time.UnscaledTotalTime - _editor.LastChangeTime >= AutoRecompileDelay)
        {
            _editor.Save();
        }

        if (_sidebarOpen)
        {
            using (paper.Row("sg_main")
                .Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                .Enter())
            {
                DrawSidebar(paper, font);
                using (paper.Box("sg_canvas_host")
                    .Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                    .Enter())
                {
                    _editor.DrawGraph(paper, width - _sidebarWidth, height);
                }
            }
        }
        else
        {
            _editor.DrawGraph(paper, width, height);
            DrawSidebarTogglePill(paper);
        }
    }

    // ─── Sidebar ──────────────────────────────────────────────────────────────────────

    private void DrawSidebar(Paper paper, Scribe.FontFile font)
    {
        var sg = _graph!;
        using (paper.Column("sg_sidebar")
            .Width(_sidebarWidth).Height(UnitValue.Stretch())
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 32, 34, 40))
            .BorderColor(System.Drawing.Color.FromArgb(255, 50, 52, 60))
            .BorderWidth(1)
            .Padding(8, 8, 6, 8).ColBetween(4)
            .Clip()
            .Enter())
        {
            DrawSidebarToolbar(paper, sg);
            DrawSidebarPreviewRow(paper, sg);
            DrawSidebarPreview(paper, sg);

            // ScrollView wants a fixed float height compute what's left after the
            // toolbar (26) + preview row (22) + preview square (sidebar - 20) + sidebar
            // vertical padding (14) + inter-row gaps (~12). Floor at 80 so the panel
            // stays usable even on very short windows.
            float fixedConsumed = 26f + 22f + (_sidebarWidth - 20f) + 14f + 12f;
            float scrollH = MathF.Max(80f, _lastWindowHeight - fixedConsumed);
            Origami.ScrollView(paper, "sg_foldout_scroll", _sidebarWidth - 16, scrollH).Body(() =>
            {
                Origami.Foldout(paper, "sg_fold_props", "Properties").Body(() => DrawPropertiesFoldout(paper, sg));
                Origami.Foldout(paper, "sg_fold_light", "Lighting").Body(() => DrawLightingFoldout(paper, sg));
                Origami.Foldout(paper, "sg_fold_blend", "Blending").Body(() => DrawBlendingFoldout(paper, sg));
                Origami.Foldout(paper, "sg_fold_geo",   "Geometry").Body(() => DrawGeometryFoldout(paper, sg));
            });
        }
    }

    /// <summary>Top strip of the sidebar Compile / Auto / Recenter / dirty indicator.</summary>
    private void DrawSidebarToolbar(Paper paper, ShaderGraph sg)
    {
        using (paper.Row("sg_tb_row1").Height(26).RowBetween(4).Enter())
        {
            Origami.Button(paper, "sg_tb_compile", $"{EditorIcons.WandMagicSparkles} Compile", () => _editor.Save()).Width(90).Show();
            Origami.Switch(paper, "sg_tb_auto", _autoRecompile, v => _autoRecompile = v)
                .Primary().LabelRight("Auto").Show();
            // Recenter lives on F / Space no need for a button. Frees toolbar room
            // for the status indicator to stretch.

            string status;
            if (_editor.IsDirty && _autoRecompile)
            {
                float remaining = MathF.Max(0f, AutoRecompileDelay - (float)(Time.UnscaledTotalTime - _editor.LastChangeTime));
                status = $"● {remaining:0.0}s";
            }
            else if (_editor.IsDirty) status = "● dirty";
            else status = "✓";
            paper.Box("sg_tb_status").Width(UnitValue.Stretch()).Height(26)
                .Text(status, EditorTheme.DefaultFont!)
                .TextColor(_editor.IsDirty ? EditorTheme.Purple400 : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleRight);
            Origami.Button(paper, "sg_tb_hide", EditorIcons.CircleXmark, () => _sidebarOpen = false).Width(24).Show();
        }
    }

    /// <summary>Second row: mesh picker + preview renderer feature toggles.</summary>
    private void DrawSidebarPreviewRow(Paper paper, ShaderGraph sg)
    {
        using (paper.Row("sg_tb_row2").Height(22).RowBetween(4).Enter())
        {
            // Mesh AssetRef picker PropertyGrid handles drag/drop + selector modal.
            // Empty label = AssetRefPropertyEditor skips the label box so the field
            // fills the full row width.
            using (paper.Box("sg_mesh_field").Width(UnitValue.Stretch()).Height(22).Enter())
            {
                PropertyGrid.DrawField(paper, "sg_mesh", "",
                    typeof(AssetRef<Runtime.Resources.Mesh>),
                    _previewMesh,
                    newVal =>
                    {
                        if (newVal is AssetRef<Runtime.Resources.Mesh> r) _previewMesh = r;
                        else if (newVal is Runtime.Resources.Mesh m) _previewMesh = new AssetRef<Runtime.Resources.Mesh>(m);
                        _lastPreviewMesh = null;
                    }, 0);
            }
            Origami.Switch(paper, "sg_pv_grid", _preview?.ShowGrid ?? false,
                    v => { if (_preview != null) _preview.ShowGrid = v; })
                .Primary().LabelRight("Grid").Show();
        }
    }

    /// <summary>The preview RenderTexture itself. Rebinds subject only when the
    /// compiled material or chosen mesh swaps property edits flow live through
    /// the persistent Material without a rebuild.</summary>
    private void DrawSidebarPreview(Paper paper, ShaderGraph sg)
    {
        _preview ??= new PreviewRenderer(180, 180);

        if (_previewMesh.IsExplicitNull)
        {
            var sphereGuid = Prowl.Runtime.BuiltInAssets.GuidForMesh(Prowl.Runtime.Resources.DefaultModel.Sphere);
            _previewMesh = new AssetRef<Runtime.Resources.Mesh>(sphereGuid);
        }

        var material = ResolvePreviewMaterial(sg);
        var mesh = _previewMesh.Res;

        bool needsRebuild = !ReferenceEquals(_lastPreviewMaterial, material)
                          || !ReferenceEquals(_lastPreviewMesh, mesh);
        if (needsRebuild && material != null && mesh != null)
        {
            _preview.SetupForMesh(mesh, material);
            _lastPreviewMaterial = material;
            _lastPreviewMesh = mesh;
        }

        float w = _sidebarWidth - 20;
        _preview.DrawPreview(paper, "sg_preview", w, w);
    }

    /// <summary>Resolve the compiled Shader sub-asset created by ShaderGraphImporter
    /// and wrap it in a persistent Material so override edits apply live. Returns null
    /// when the graph hasn't been compiled yet.</summary>
    private Runtime.Resources.Material? ResolvePreviewMaterial(ShaderGraph sg)
    {
        if (sg.AssetID == Guid.Empty) return null;
        var entry = EditorAssetDatabase.Instance?.GetEntry(sg.AssetID);
        if (entry?.SubAssets == null || entry.SubAssets.Length == 0) return null;

        // Match by type rather than name survives any future rename of the
        // "CompiledShader" subasset slot. Use AssetDatabase.Get directly so the loader
        // kicks in when the sub-asset hasn't been cached yet.
        Runtime.Resources.Shader? shader = null;
        foreach (var sub in entry.SubAssets)
        {
            if (typeof(Runtime.Resources.Shader).IsAssignableFrom(sub.Type))
            {
                shader = (Prowl.Runtime.AssetDatabase.Get(sub.Guid) as Runtime.Resources.Shader);
                if (shader != null) break;
            }
        }
        if (shader == null) return null;

        if (_previewMaterial == null || _previewMaterial.Shader != shader)
        {
            _previewMaterial = new Runtime.Resources.Material();
            _previewMaterial.Shader = shader;
        }
        return _previewMaterial;
    }

    // ─── Foldouts ─────────────────────────────────────────────────────────────────────

    private void DrawPropertiesFoldout(Paper paper, ShaderGraph sg)
    {
        var material = ResolvePreviewMaterial(sg);
        if (material?.Shader == null)
        {
            paper.Box("sg_props_none").Height(20)
                .Text("(compile the graph to see properties)", EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleLeft);
            return;
        }
        // No rebuild callback Material is persistent, MeshRenderer holds the same
        // ref, PropertyState changes upload to GPU uniforms on the next render.
        foreach (var p in material.Shader.Properties)
        {
            MaterialPropertyDrawer.DrawPropertyRow(paper, $"sg_prop_{p.Name}", material, p);
        }
    }

    private void DrawLightingFoldout(Paper paper, ShaderGraph sg)
    {
        var master = FindMasterNode(sg);
        if (master != null)
        {
            var current = master.Lighting;
            InspectorRow.Draw(paper, "sg_lighting_mode", "Mode", () =>
                Origami.EnumDropdown(paper, "sg_lighting_mode_v", current, v =>
                {
                    if (v == current) return;
                    var cap = master; var before = current;
                    Undo.RegisterAction("Change Lighting Mode",
                        undo: () => { cap.Lighting = before; RebuildMasterPorts(cap); TouchSettings(); },
                        redo: () => { cap.Lighting = v;      RebuildMasterPorts(cap); TouchSettings(); });
                    cap.Lighting = v;
                    RebuildMasterPorts(cap);
                    TouchSettings();
                }).Show());
        }
        Origami.Checkbox(paper, "sg_recv_ambient", sg.RenderSettings.ReceivesAmbient,
                v => { var s = sg.RenderSettings; s.ReceivesAmbient = v; MutateSettings(s, "Receives Ambient"); })
            .LabelRight("Receives Ambient").Show();
        Origami.Checkbox(paper, "sg_recv_shadows", sg.RenderSettings.ReceivesShadows,
                v => { var s = sg.RenderSettings; s.ReceivesShadows = v; MutateSettings(s, "Receives Shadows"); })
            .LabelRight("Receives Shadows").Show();
        Origami.Checkbox(paper, "sg_cast_shadows", sg.RenderSettings.CastsShadows,
                v => { var s = sg.RenderSettings; s.CastsShadows = v; MutateSettings(s, "Casts Shadows"); })
            .LabelRight("Casts Shadows").Show();
    }

    private void DrawBlendingFoldout(Paper paper, ShaderGraph sg)
    {
        InspectorRow.Draw(paper, "sg_blend", "Blend Mode", () =>
            Origami.EnumDropdown(paper, "sg_blend_v", sg.RenderSettings.Blend,
                v => { var s = sg.RenderSettings; s.Blend = v; MutateSettings(s, "Blend"); }).Show());

        // Custom mode unlocks the raw Src/Dst/Op pickers matching the parser's
        // { Src X; Dst Y; Mode Z; } block exactly. Hidden for presets.
        if (sg.RenderSettings.Blend == ShaderBlendMode.Custom)
        {
            InspectorRow.Draw(paper, "sg_blend_src", "Src Factor", () =>
                Origami.EnumDropdown(paper, "sg_blend_src_v", sg.RenderSettings.BlendSrc,
                    v => { var s = sg.RenderSettings; s.BlendSrc = v; MutateSettings(s, "Src Factor"); }).Show());
            InspectorRow.Draw(paper, "sg_blend_dst", "Dst Factor", () =>
                Origami.EnumDropdown(paper, "sg_blend_dst_v", sg.RenderSettings.BlendDst,
                    v => { var s = sg.RenderSettings; s.BlendDst = v; MutateSettings(s, "Dst Factor"); }).Show());
            InspectorRow.Draw(paper, "sg_blend_op", "Blend Op", () =>
                Origami.EnumDropdown(paper, "sg_blend_op_v", sg.RenderSettings.BlendOp,
                    v => { var s = sg.RenderSettings; s.BlendOp = v; MutateSettings(s, "Blend Op"); }).Show());
        }

        InspectorRow.Draw(paper, "sg_queue", "Queue", () =>
            Origami.EnumDropdown(paper, "sg_queue_v", sg.RenderSettings.Queue,
                v => { var s = sg.RenderSettings; s.Queue = v; MutateSettings(s, "Queue"); }).Show());

        using (paper.Row("sg_presets").Height(22).RowBetween(4).Enter())
        {
            Origami.Button(paper, "sg_preset_opaque", "Opaque", () => ApplyPreset(ShaderGraphRenderSettings.OpaqueDefaults(), "Opaque Preset")).Width(70).Show();
            Origami.Button(paper, "sg_preset_transp", "Transparent", () => ApplyPreset(ShaderGraphRenderSettings.TransparentDefaults(), "Transparent Preset")).Width(90).Show();
            Origami.Button(paper, "sg_preset_add", "Additive", () => ApplyPreset(ShaderGraphRenderSettings.AdditiveDefaults(), "Additive Preset")).Width(70).Show();
        }
    }

    private void DrawGeometryFoldout(Paper paper, ShaderGraph sg)
    {
        InspectorRow.Draw(paper, "sg_cull", "Cull", () =>
            Origami.EnumDropdown(paper, "sg_cull_v", sg.RenderSettings.Cull,
                v => { var s = sg.RenderSettings; s.Cull = v; MutateSettings(s, "Cull"); }).Show());
        InspectorRow.Draw(paper, "sg_winding", "Winding", () =>
            Origami.EnumDropdown(paper, "sg_winding_v", sg.RenderSettings.Winding,
                v => { var s = sg.RenderSettings; s.Winding = v; MutateSettings(s, "Winding"); }).Show());
        Origami.Checkbox(paper, "sg_zwrite", sg.RenderSettings.ZWrite,
                v => { var s = sg.RenderSettings; s.ZWrite = v; MutateSettings(s, "Z Write"); })
            .LabelRight("Z Write").Show();
        InspectorRow.Draw(paper, "sg_ztest", "Z Test", () =>
            Origami.EnumDropdown(paper, "sg_ztest_v", sg.RenderSettings.ZTest,
                v => { var s = sg.RenderSettings; s.ZTest = v; MutateSettings(s, "Z Test"); }).Show());
    }

    /// <summary>Floating pill shown when the sidebar is hidden, so the user can
    /// re-open it without digging through a menu.</summary>
    private void DrawSidebarTogglePill(Paper paper)
    {
        paper.Box("sg_pill_backdrop")
            .PositionType(PositionType.SelfDirected)
            .Position(8, 36).Size(32, 28)
            .BackgroundColor(System.Drawing.Color.FromArgb(220, 38, 40, 48))
            .Hovered.BackgroundColor(System.Drawing.Color.FromArgb(255, 60, 64, 78)).End()
            .BorderColor(System.Drawing.Color.FromArgb(255, 80, 84, 96))
            .BorderWidth(1).Rounded(5)
            .OnClick(_ => _sidebarOpen = true)
            .Text(EditorIcons.Sliders, EditorTheme.DefaultFont!)
            .TextColor(EditorTheme.Ink500)
            .Alignment(TextAlignment.MiddleCenter);
    }

    // ─── Mutation helpers ─────────────────────────────────────────────────────────────

    /// <summary>Apply a full settings struct to the graph with undo/redo. No-op when
    /// nothing changed (dropdowns fire same-value re-selects).</summary>
    private void MutateSettings(ShaderGraphRenderSettings after, string label)
    {
        if (_graph == null) return;
        var before = _graph.RenderSettings;
        if (before.Equals(after)) return;
        var sg = _graph;
        sg.RenderSettings = after;
        Undo.RegisterAction(label,
            undo: () => { sg.RenderSettings = before; TouchSettings(); },
            redo: () => { sg.RenderSettings = after;  TouchSettings(); });
        TouchSettings();
    }

    private void ApplyPreset(ShaderGraphRenderSettings preset, string label)
        => MutateSettings(preset, label);

    /// <summary>Mark the widget dirty after a sidebar-driven mutation. The widget's
    /// own mutations fire this automatically via RegisterMutation, but settings edits
    /// live on the panel and bypass that path.</summary>
    private void TouchSettings()
    {
        // IsDirty is set via GraphMutated subscription indirectly when the widget
        // mutates. For panel-local edits we need to manually tickle the widget's
        // dirty/timestamp state done by calling Save via a dummy RegisterMutation
        // path would be overkill, so we just trigger a synthetic mutation record.
        // The cheapest path: call a widget method that sets the flags. The widget
        // doesn't expose one directly, so we simulate by calling CycleWireStyle-like
        // pattern: manually mirror dirty + timestamp via the widget's public API.
        // Here we rely on an internal no-op mutation, which isn't available. So
        // instead: directly call Save via the Compile button except we want
        // auto-recompile to handle it. Simplest: store a local _settingsDirty timer
        // and fold it into OnGUI's debounce. But that duplicates state.
        // Cleanest expose a MarkDirty() on GraphEditor. Added below.
        _editor.MarkDirty();
    }

    // ─── Master-node helpers ──────────────────────────────────────────────────────────

    // Returns the Surface master when present. Shader-type-specific UI (like the
    // lighting-mode dropdown) only applies to surface graphs; other types show a
    // different sidebar in slice 6.
    private static SurfaceMasterNode? FindMasterNode(ShaderGraph sg)
    {
        foreach (var n in sg.Nodes)
            if (n is SurfaceMasterNode m) return m;
        return null;
    }

    /// <summary>Force a node's port list to rebuild called after mutating public
    /// fields that influence which ports are visible (lighting mode toggles hide
    /// PBR inputs). Reaches into Node._defined via reflection rather than polluting
    /// the Node surface with an editor-only invalidate API.</summary>
    private static void RebuildMasterPorts(Node node)
    {
        var f = typeof(Node).GetField("_defined",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        f?.SetValue(node, false);
        node.EnsureDefined();
    }
}
