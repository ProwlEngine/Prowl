using System;
using System.IO;
using System.Threading.Tasks;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Prowl.Photonic;
using Prowl.Vector;
using VoxelEngineTest; // ImGuiController namespace
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys; // disambiguate from System.Windows.Forms.Keys
using MouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;

namespace Photonic.Demo;

/// <summary>
/// Editor-style OpenTK demo: load any glTF / OBJ / FBX, position lights, configure bake settings,
/// auto-pack multiple atlases, run a Photonic bake against the whole scene.
/// </summary>
internal sealed class DemoWindow : GameWindow
{
    private static readonly string SponzaPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Sponza", "glTF", "Sponza.gltf");

    // ---- runtime infra ----------------------------------------------------------------------
    private ImGuiController? _imgui;
    private SceneRenderer? _renderer;
    private LineRenderer? _lineRenderer;
    private SurfelDebugRenderer? _surfelRenderer;

    // ---- demo scene -------------------------------------------------------------------------
    private readonly Scene _scene = new();

    // Newly-loaded models from background tasks: the UI thread drains this each frame to add to
    // the scene + upload to the GPU (single-threaded scene mutation).
    private readonly System.Collections.Concurrent.ConcurrentQueue<SceneModel> _pendingNewModels = new();
    // Re-upload queue for models whose Baked* arrays changed (e.g. UV1 strategy switched). UI
    // thread drains and pushes fresh vertex data to the existing GPU resources.
    private readonly System.Collections.Concurrent.ConcurrentQueue<SceneModel> _pendingReupload = new();
    private bool _wantInitialSponzaLoad = true;

    // ---- bake state -------------------------------------------------------------------------
    private LightmapBaker? _baker;
    private int _lastUploadedIter = -1;
    private string _status = "Ready.";
    private bool _bakeRequested = false;

    // ---- bake settings (mirror BakeOptions) -------------------------------------------------
    private int _atlasPageSize = 512;
    private float _texelsPerWorldUnit = 100f;
    private int _bounces = 2;
    private int _samplesPerIter = 1;
    private int _dilatePixels = 2;
    private float _rayBias = 1e-3f;
    private bool _useHemisphereLUT = true;
    private bool _jitterRayOrigin = true;
    private float _jitterStrength = 1.0f;
    private bool _ignoreAlbedo = false;
    private bool _includeDirectLighting = true;
    private int _pathTracerIdx = 0; // 0 PerTexel, 1 Surfel
    private float _surfelDensity = 2.0f;
    private int _surfelMaxNeighbors = 8;
    private float _surfelNormalThr = 0.85f;
    private bool _surfelNormalRejection = true;
    private float _surfelPoissonRadiusFactor = 0.6f;
    private float _surfelPoissonAlignThr = 0.85f;

    // ---- display / debug knobs --------------------------------------------------------------
    private System.Numerics.Vector3 _skyColor = new(0.02f, 0.02f, 0.03f);
    private float _exposure = 1.0f;
    private int _debugViewIdx = 0;
    private bool _wireframe = false;
    private float _bilateral = 0f;
    private bool _showSurfels = false;
    private float _surfelDrawSize = 1.0f;
    private bool _showAtlasViewer = true;
    private int _atlasViewerIdx = 0;

    // ---- import dialog state ----------------------------------------------------------------
    private bool _showImportConfig = false;
    private string _importPathBuffer = "";
    private int _importUV1Idx = 0;

    // ---- camera -----------------------------------------------------------------------------
    private Float3 _camPos = new Float3(0, 5, -12);
    private float _yaw = 0f;
    private float _pitch = -0.05f;

    public DemoWindow(GameWindowSettings gws, NativeWindowSettings nws) : base(gws, nws) { }

    protected override void OnLoad()
    {
        base.OnLoad();
        GL.ClearColor(0.05f, 0.06f, 0.08f, 1.0f);
        GL.Enable(EnableCap.DepthTest);
        _imgui = new ImGuiController(ClientSize.X, ClientSize.Y);
        _renderer = new SceneRenderer();
        _lineRenderer = new LineRenderer();
        _surfelRenderer = new SurfelDebugRenderer();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        _imgui?.WindowResized(e.Width, e.Height);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        _imgui?.PressChar((char)e.Unicode);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _imgui?.MouseScroll(e.Offset);
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);
        UpdateCamera((float)args.Time);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        // Default-scene bootstrap on the first rendered frame so the GL context + ImGui are alive.
        if (_wantInitialSponzaLoad)
        {
            _wantInitialSponzaLoad = false;
            BeginImportInBackground(SponzaPath, UV1Strategy.AutoUnwrap, isDefaultScene: true);
            // Spawn the default sun.
            _scene.AddLight(new SceneLight
            {
                Name = "Sun",
                Kind = SceneLightKind.Directional,
                Direction = new System.Numerics.Vector3(-0.5f, -1f, -0.3f),
                Color = new System.Numerics.Vector3(3f, 2.8f, 2.4f),
            });
        }

        // Drain any newly-imported models (single-threaded scene mutation on the UI thread).
        while (_pendingNewModels.TryDequeue(out var newModel))
        {
            _scene.AddModel(newModel);
            _renderer!.UploadModel(newModel);
        }
        // Drain re-upload requests (UV1 strategy changed, baked geometry mutated, etc).
        while (_pendingReupload.TryDequeue(out var sm))
        {
            _renderer!.UploadModel(sm);
        }

        // Re-upload atlas pages whenever the bake folded a new iteration in.
        if (_baker?.Job is { } job && job.IterationCount != _lastUploadedIter)
        {
            _lastUploadedIter = job.IterationCount;
            ReuploadAllAtlases();
        }

        _imgui?.Update(this, (float)args.Time);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var view = BuildView();
        float aspect = ClientSize.X / (float)Math.Max(1, ClientSize.Y);
        var proj = CreatePerspectiveLH(MathF.PI / 3f, aspect, 0.05f, 1000f);

        if (_renderer is not null)
        {
            _renderer.CurrentDebug = (SceneRenderer.DebugMode)_debugViewIdx;
            _renderer.WireframeOverlay = _wireframe;
            _renderer.BilateralStrength = _bilateral;
            _renderer.Render(_scene, view, proj, _exposure);
        }

        // Surfel debug visualisation (when surfel mode + bake active).
        if (_showSurfels && _surfelRenderer is not null && _baker?.Job?.SurfelCloud is { } cloud)
        {
            _surfelRenderer.Render(cloud, view, proj, Forward(), _surfelDrawSize);
        }

        DrawUi();
        _imgui?.Render();
        SwapBuffers();
    }

    // ---- UI -----------------------------------------------------------------------------------

    private void DrawUi()
    {
        DrawMenuBar();
        DrawHierarchy();
        DrawInspector();
        DrawBakePanel();
        DrawAtlasViewer();
        DrawImportDialog();
    }

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMainMenuBar()) return;
        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Import Model..."))           { OpenImportDialog(); }
            if (ImGui.MenuItem("Load Bundled Sponza"))        { BeginImportInBackground(SponzaPath, UV1Strategy.AutoUnwrap, isDefaultScene: false); }
            ImGui.Separator();
            if (ImGui.MenuItem("Clear Scene"))                { ClearScene(); }
            if (ImGui.MenuItem("Quit"))                       { Close(); }
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Lights"))
        {
            if (ImGui.MenuItem("Add Directional Light")) AddLightOfKind(SceneLightKind.Directional);
            if (ImGui.MenuItem("Add Point Light"))       AddLightOfKind(SceneLightKind.Point);
            if (ImGui.MenuItem("Add Spot Light"))        AddLightOfKind(SceneLightKind.Spot);
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("View"))
        {
            ImGui.MenuItem("Atlas viewer", "", ref _showAtlasViewer);
            ImGui.EndMenu();
        }
        ImGui.Text($"   |   {_status}");
        ImGui.EndMainMenuBar();
    }

    private void DrawHierarchy()
    {
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(280, 400), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Hierarchy")) { ImGui.End(); return; }

        if (ImGui.TreeNodeEx("Models", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < _scene.Models.Count; i++)
            {
                var m = _scene.Models[i];
                bool selected = _scene.Selection.Kind == SceneSelectionKind.Model && _scene.Selection.Index == i;
                ImGui.PushID(i + 1);
                if (ImGui.Selectable(m.Name, selected))
                    _scene.Selection = new SceneSelection { Kind = SceneSelectionKind.Model, Index = i };
                ImGui.PopID();
            }
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx("Lights", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < _scene.Lights.Count; i++)
            {
                var l = _scene.Lights[i];
                bool selected = _scene.Selection.Kind == SceneSelectionKind.Light && _scene.Selection.Index == i;
                ImGui.PushID(i + 10000);
                if (ImGui.Selectable($"{l.Name} [{l.Kind}]", selected))
                    _scene.Selection = new SceneSelection { Kind = SceneSelectionKind.Light, Index = i };
                ImGui.PopID();
            }
            ImGui.TreePop();
        }

        ImGui.Separator();
        ImGui.BeginDisabled(_scene.Selection.Kind == SceneSelectionKind.None);
        if (ImGui.Button("Delete Selected"))
        {
            if (_scene.SelectedModel is { } sm) _renderer?.UnloadModel(sm);
            _scene.RemoveSelected();
        }
        ImGui.EndDisabled();

        ImGui.End();
    }

    private void DrawInspector()
    {
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(340, 360), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Inspector")) { ImGui.End(); return; }

        if (_scene.SelectedModel is { } sm) DrawModelInspector(sm);
        else if (_scene.SelectedLight is { } sl) DrawLightInspector(sl);
        else ImGui.TextDisabled("Nothing selected.");

        ImGui.End();
    }

    private static readonly string[] UV1ModeLabels = { "AutoUnwrap", "TrianglePack", "UseExisting" };

    private void DrawModelInspector(SceneModel sm)
    {
        ImGui.InputText("Name", ref sm.Name, 64);
        ImGui.TextDisabled($"Source: {Path.GetFileName(sm.Source.SourcePath)}");
        ImGui.TextDisabled($"Verts: {sm.Source.Positions.Length}  tris: {sm.Source.Indices.Length / 3}  submeshes: {sm.Source.SubMeshes.Length}");
        ImGui.Separator();

        ImGui.Text("Transform");
        ImGui.DragFloat3("Position", ref sm.Position, 0.05f);
        ImGui.DragFloat3("Rotation (deg)", ref sm.RotationEulerDeg, 0.5f);
        ImGui.DragFloat3("Scale", ref sm.Scale, 0.01f, 0.001f, 1000f);

        ImGui.Separator();
        ImGui.Text("Lightmap UV strategy");
        int uvIdx = (int)sm.UV1Mode;
        if (ImGui.Combo("Mode##uv", ref uvIdx, UV1ModeLabels, UV1ModeLabels.Length))
        {
            sm.UV1Mode = (UV1Strategy)uvIdx;
            BeginUV1RegenInBackground(sm); // re-unwrap with the newly chosen strategy
        }
        if (sm.UV1Mode == UV1Strategy.UseExisting && !sm.Source.HasDedicatedUV)
            ImGui.TextColored(new System.Numerics.Vector4(1, 0.7f, 0.2f, 1), "  Model has no dedicated lightmap UV; will fall back to UV0.");
    }

    private static readonly string[] LightKindLabels = { "Directional", "Point", "Spot" };

    private void DrawLightInspector(SceneLight sl)
    {
        ImGui.InputText("Name", ref sl.Name, 64);
        int kIdx = (int)sl.Kind;
        if (ImGui.Combo("Kind", ref kIdx, LightKindLabels, LightKindLabels.Length))
            sl.Kind = (SceneLightKind)kIdx;
        ImGui.Separator();

        if (sl.Kind != SceneLightKind.Directional)
            ImGui.DragFloat3("Position", ref sl.Position, 0.05f);
        if (sl.Kind != SceneLightKind.Point)
            ImGui.DragFloat3("Direction", ref sl.Direction, 0.01f, -1f, 1f);
        if (sl.Kind != SceneLightKind.Directional)
            ImGui.DragFloat("Range", ref sl.Range, 0.1f, 0.1f, 1000f);
        if (sl.Kind == SceneLightKind.Spot)
            ImGui.DragFloat("Cone angle (deg)", ref sl.ConeAngleDeg, 0.5f, 1f, 89f);

        ImGui.ColorEdit3("Color (HDR)", ref sl.Color, ImGuiColorEditFlags.HDR | ImGuiColorEditFlags.Float);
        ImGui.Checkbox("Casts shadows", ref sl.CastsShadows);
    }

    private void DrawBakePanel()
    {
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(340, 540), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Bake")) { ImGui.End(); return; }

        bool running = _baker?.Job is { } j && j.Poll();

        // Toolbar row.
        if (running)
        {
            if (ImGui.Button("Cancel")) _baker!.Cancel();
        }
        else
        {
            if (ImGui.Button("Bake")) _bakeRequested = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear Lightmaps")) ClearLightmaps();
        ImGui.SameLine();
        ImGui.TextDisabled(running
            ? $"iter {(_baker?.Job?.IterationCount ?? 0)} - {_baker?.Job?.Activity}"
            : _status);

        ImGui.Separator();
        ImGui.ColorEdit3("Sky color (HDR)", ref _skyColor, ImGuiColorEditFlags.HDR | ImGuiColorEditFlags.Float);
        ImGui.SliderFloat("Display exposure", ref _exposure, 0.1f, 8f);
        ImGui.SliderFloat("Bilateral denoise", ref _bilateral, 0f, 4f);
        ImGui.Combo("Debug view", ref _debugViewIdx,
            new[] { "Off (diffuse * lightmap)", "UV1 atlas coords", "Coverage (magenta = empty)", "Lightmap only" }, 4);
        ImGui.Checkbox("Wireframe overlay", ref _wireframe);

        ImGui.Separator();
        if (ImGui.CollapsingHeader("Atlas / packing", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.SliderInt("Atlas page size", ref _atlasPageSize, 64, 4096);
            ImGui.SliderFloat("Texels per world unit", ref _texelsPerWorldUnit, 1f, 512f);
            ImGui.TextDisabled("  Higher = more atlas resolution per metre of mesh; may open extra atlas pages.");
        }

        if (ImGui.CollapsingHeader("Path tracer", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Combo("Algorithm##pt", ref _pathTracerIdx, new[] { "Per-texel", "Surfel cloud" }, 2);
            ImGui.SliderInt("Bounces", ref _bounces, 0, 4);
            ImGui.SliderInt("Samples / iter", ref _samplesPerIter, 1, 32);
            ImGui.SliderInt("Dilate pixels", ref _dilatePixels, 0, 8);
            ImGui.SliderFloat("Ray bias", ref _rayBias, 1e-5f, 1e-2f, "%.5f");
            ImGui.Checkbox("Hemisphere LUT", ref _useHemisphereLUT);
            ImGui.Checkbox("Jitter ray origin", ref _jitterRayOrigin);
            if (_jitterRayOrigin) ImGui.SliderFloat("Jitter strength", ref _jitterStrength, 0f, 2f);
            ImGui.Checkbox("Ignore albedo", ref _ignoreAlbedo);
            ImGui.Checkbox("Include direct lighting at texel", ref _includeDirectLighting);
        }

        if (_pathTracerIdx == 1 && ImGui.CollapsingHeader("Surfel options"))
        {
            ImGui.SliderFloat("Surfels / m^2", ref _surfelDensity, 0.1f, 50f, "%.2f");
            ImGui.SliderInt("Max neighbours / texel", ref _surfelMaxNeighbors, 1, 64);
            ImGui.SliderFloat("Normal threshold", ref _surfelNormalThr, -1f, 1f, "%.3f");
            ImGui.Checkbox("Normal-aware Poisson rejection", ref _surfelNormalRejection);
            if (_surfelNormalRejection)
            {
                ImGui.SliderFloat("Poisson radius x kernel", ref _surfelPoissonRadiusFactor, 0.05f, 1.5f, "%.2f");
                ImGui.SliderFloat("Poisson align threshold", ref _surfelPoissonAlignThr, -1f, 1f, "%.3f");
            }
            ImGui.Separator();
            ImGui.Checkbox("Draw surfels (3D debug)", ref _showSurfels);
            if (_showSurfels) ImGui.SliderFloat("Surfel sphere size x", ref _surfelDrawSize, 0.1f, 5f);
        }

        ImGui.End();

        // Trigger the bake at end of frame so all state mutations land first.
        if (_bakeRequested && !running)
        {
            _bakeRequested = false;
            StartBake();
        }
    }

    private void DrawAtlasViewer()
    {
        if (!_showAtlasViewer) return;
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(360, 360), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Atlas viewer", ref _showAtlasViewer)) { ImGui.End(); return; }

        int n = _renderer?.AtlasCount ?? 0;
        if (n == 0) { ImGui.TextDisabled("No atlases. Bake the scene first."); ImGui.End(); return; }

        if (_atlasViewerIdx >= n) _atlasViewerIdx = 0;
        ImGui.SliderInt("Page", ref _atlasViewerIdx, 0, n - 1);
        int tex = _renderer!.GetAtlasTexture(_atlasViewerIdx);
        var avail = ImGui.GetContentRegionAvail();
        float side = Math.Max(64f, Math.Min(avail.X, avail.Y) - 4);
        ImGui.Image((nint)tex, new System.Numerics.Vector2(side, side), new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));

        ImGui.End();
    }

    private void DrawImportDialog()
    {
        if (!_showImportConfig) return;
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(420, 180), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Import settings", ref _showImportConfig)) { ImGui.End(); return; }

        ImGui.Text("File:");
        ImGui.TextDisabled("  " + _importPathBuffer);

        ImGui.Separator();
        ImGui.Text("Lightmap UV strategy:");
        ImGui.Combo("##importUv", ref _importUV1Idx, UV1ModeLabels, UV1ModeLabels.Length);
        ImGui.TextWrapped(_importUV1Idx switch
        {
            0 => "Run Prowl.Unwrapper. Best quality, slow on big meshes.",
            1 => "Per-triangle shelf-pack. Every triangle gets a unique region (no overlap).",
            _ => "Use the model's best-existing UV layer (prefers UV2 / UV1; falls back to UV0 with a warning).",
        });

        ImGui.Separator();
        if (ImGui.Button("Import"))
        {
            _showImportConfig = false;
            BeginImportInBackground(_importPathBuffer, (UV1Strategy)_importUV1Idx, isDefaultScene: false);
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) _showImportConfig = false;

        ImGui.End();
    }

    // ---- import & bake ----------------------------------------------------------------------

    private void OpenImportDialog()
    {
        // Native Windows file picker. Apartment state set by [STAThread] on Main.
        using var ofd = new System.Windows.Forms.OpenFileDialog
        {
            Title = "Import model",
            Filter = "Models (*.gltf;*.glb;*.obj;*.fbx)|*.gltf;*.glb;*.obj;*.fbx|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _importPathBuffer = ofd.FileName;
        _importUV1Idx = 0;
        _showImportConfig = true;
    }

    private void BeginImportInBackground(string path, UV1Strategy uv1, bool isDefaultScene)
    {
        _status = $"Importing {Path.GetFileName(path)}...";
        Task.Run(() =>
        {
            try
            {
                var loaded = ModelLoader.Load(path, s => _status = s);
                var sm = new SceneModel
                {
                    Name = isDefaultScene ? "Sponza" : loaded.DisplayName,
                    Source = loaded,
                    UV1Mode = uv1,
                };
                // Pre-seed Baked* with source data, then run the chosen UV1 strategy. UV1
                // generation can take 30s+ for an auto-unwrap on a big mesh -- doing it here on
                // the background thread keeps the UI responsive and means StartBake is instant.
                sm.BakedPositions = loaded.Positions;
                sm.BakedNormals   = loaded.Normals;
                sm.BakedUV0       = loaded.UV0;
                sm.BakedUV1       = new Float2[loaded.Positions.Length];
                sm.BakedIndices   = loaded.Indices;
                sm.BakedSubMeshes = loaded.SubMeshes;
                UV1Generator.Bake(sm, s => _status = s);

                _pendingNewModels.Enqueue(sm);
                _status = $"Imported {sm.Name} ({sm.BakedPositions.Length} verts, UV1 ready).";
            }
            catch (Exception ex)
            {
                _status = $"Import failed: {ex.Message}";
            }
        });
    }

    /// <summary>Re-run the chosen UV1 strategy on an existing model (e.g. user changed the combo).</summary>
    private void BeginUV1RegenInBackground(SceneModel sm)
    {
        // Any in-flight bake is operating against the now-stale BakedUV1 -- cancel it and clear
        // the atlas so the user has to re-bake.
        _baker?.Cancel();
        _baker = null;
        _renderer?.SetAtlasCount(0);
        foreach (var m in _scene.Models)
        {
            m.AtlasTargetIndex = -1;
            m.UVOffset = Float2.Zero;
            m.UVScale  = Float2.One;
        }
        _lastUploadedIter = -1;
        _status = $"Regenerating UV1 for {sm.Name}...";

        Task.Run(() =>
        {
            try
            {
                UV1Generator.Bake(sm, s => _status = s);
                _pendingReupload.Enqueue(sm);
                _status = $"UV1 regenerated for {sm.Name}.";
            }
            catch (Exception ex)
            {
                _status = $"UV1 regen failed: {ex.Message}";
            }
        });
    }

    private void AddLightOfKind(SceneLightKind k)
    {
        var l = new SceneLight { Name = $"{k} {_scene.Lights.Count + 1}", Kind = k };
        _scene.AddLight(l);
    }

    private void ClearScene()
    {
        foreach (var sm in _scene.Models) _renderer?.UnloadModel(sm);
        _scene.Models.Clear();
        _scene.Lights.Clear();
        _scene.Selection = SceneSelection.None;
        _baker?.Cancel();
        _baker = null;
        _renderer?.SetAtlasCount(0);
        _lastUploadedIter = -1;
        _status = "Scene cleared.";
    }

    private void ClearLightmaps()
    {
        _baker?.Cancel();
        _baker = null;
        foreach (var sm in _scene.Models)
        {
            sm.AtlasTargetIndex = -1;
            sm.UVOffset = Float2.Zero;
            sm.UVScale  = Float2.One;
        }
        _renderer?.SetAtlasCount(0);
        _lastUploadedIter = -1;
        _status = "Lightmaps cleared.";
    }

    private void StartBake()
    {
        if (_scene.Models.Count == 0) { _status = "No models to bake."; return; }

        // UV1 was generated at import time (and on every UV1Mode change); StartBake just consumes
        // the cached BakedXxx arrays. This keeps Bake responsive: the only synchronous work
        // happens below (BakeScene construction + AutoAtlasPacker.Pack), then baker.Start spawns
        // the actual integration on its own thread for progressive accumulation.

        // ---- Photonic scene ---------------------------------------------------------------------
        _baker?.Cancel();
        _baker = new LightmapBaker();
        ApplyBakeOptions();
        var photonicScene = _baker.BeginScene("DemoScene");

        // Materials + textures. We register once per LoadedModel: the same loaded model used in
        // multiple SceneModels still only emits one set of bake materials, keyed by display name +
        // material index. For this demo every SceneModel has its own copy of the model anyway, but
        // material name collisions are still possible if two imported files share material names --
        // disambiguate with a per-model prefix.
        var materialNameByModel = new System.Collections.Generic.Dictionary<SceneModel, string[]>();
        for (int i = 0; i < _scene.Models.Count; i++)
        {
            var sm = _scene.Models[i];
            string prefix = $"m{i}";
            var perMat = new string[sm.Source.Materials.Length];
            // textures first
            var loadedTexBakeRef = new BakeTexture?[sm.Source.Textures.Length];
            for (int t = 0; t < sm.Source.Textures.Length; t++)
            {
                var blob = sm.Source.Textures[t];
                if (blob is null) continue;
                loadedTexBakeRef[t] = photonicScene.CreateTextureRGBA($"{prefix}_tex_{t}", blob.Width, blob.Height, blob.RGBA, inputGamma: 2.2f);
            }
            for (int m = 0; m < sm.Source.Materials.Length; m++)
            {
                var matInfo = sm.Source.Materials[m];
                string name = $"{prefix}_mat_{m}";
                perMat[m] = name;
                var bm = photonicScene.CreateMaterial(name);
                bm.DiffuseColor = matInfo.BaseColor;
                if (matInfo.DiffuseTextureIndex >= 0 && loadedTexBakeRef[matInfo.DiffuseTextureIndex] is { } bt)
                    bm.DiffuseTexture = bt;
            }
            materialNameByModel[sm] = perMat;
        }

        // Build BakeMesh per SceneModel.
        var bakeMeshes = new (BakeMesh mesh, Float4x4 transform)[_scene.Models.Count];
        for (int i = 0; i < _scene.Models.Count; i++)
        {
            var sm = _scene.Models[i];
            var names = materialNameByModel[sm];
            var builder = photonicScene.BeginMesh($"m{i}_mesh")
                .AddVertices(sm.BakedPositions, sm.BakedNormals)
                .AddUVLayer("UV0", sm.BakedUV0)
                .AddUVLayer("UV1", sm.BakedUV1);
            foreach (var sub in sm.BakedSubMeshes)
            {
                var slIdx = new int[sub.IndexCount];
                Array.Copy(sm.BakedIndices, sub.IndexStart, slIdx, 0, sub.IndexCount);
                string matName = sub.MaterialIndex >= 0 && sub.MaterialIndex < names.Length ? names[sub.MaterialIndex] : "";
                builder.AddMaterialGroup(matName, slIdx);
            }
            bakeMeshes[i] = (builder.End(), Scene.GetModelTransform(sm));
        }

        // Lights.
        for (int i = 0; i < _scene.Lights.Count; i++)
        {
            var sl = _scene.Lights[i];
            var color = new Float3(sl.Color.X, sl.Color.Y, sl.Color.Z);
            switch (sl.Kind)
            {
                case SceneLightKind.Directional:
                {
                    var t = MakeDirectionalTransform(sl.Direction);
                    photonicScene.CreateDirectionalLight(sl.Name, t, color).CastsShadows = sl.CastsShadows;
                    break;
                }
                case SceneLightKind.Point:
                {
                    var t = Float4x4.CreateTranslation(new Float3(sl.Position.X, sl.Position.Y, sl.Position.Z));
                    photonicScene.CreatePointLight(sl.Name, t, color, sl.Range).CastsShadows = sl.CastsShadows;
                    break;
                }
                case SceneLightKind.Spot:
                {
                    var t = MakeDirectionalTransform(sl.Direction);
                    t.c3 = new Float4(sl.Position.X, sl.Position.Y, sl.Position.Z, 1f);
                    photonicScene.CreateSpotLight(sl.Name, t, color, sl.Range, sl.ConeAngleDeg * MathF.PI / 180f).CastsShadows = sl.CastsShadows;
                    break;
                }
            }
        }

        // Auto-pack everything into one or more atlas pages.
        _status = "Packing atlas...";
        var packed = AutoAtlasPacker.Pack(_baker, bakeMeshes, _atlasPageSize, _atlasPageSize,
            texelsPerWorldUnit: _texelsPerWorldUnit, padding: 2, bakeUVLayer: "UV1");

        for (int i = 0; i < _scene.Models.Count; i++)
        {
            var sm = _scene.Models[i];
            sm.AtlasTargetIndex = packed.Placements[i].AtlasIndex;
            sm.UVOffset = packed.Instances[i].UVOffset;
            sm.UVScale  = packed.Instances[i].UVScale;
        }

        _renderer?.SetAtlasCount(packed.Targets.Length);
        _lastUploadedIter = -1;

        photonicScene.End();
        _baker.Start();
        _status = $"Baking ({packed.Targets.Length} atlas pages, {_scene.Models.Count} models, {_scene.Lights.Count} lights).";
    }

    private void ApplyBakeOptions()
    {
        if (_baker is null) return;
        var o = _baker.Options;
        o.PathTracer = (PathTracerKind)_pathTracerIdx;
        o.Bounces = _bounces;
        o.SamplesPerIteration = _samplesPerIter;
        o.DilatePixels = _dilatePixels;
        o.RayBias = _rayBias;
        o.UseHemisphereLUT = _useHemisphereLUT;
        o.JitterRayOrigin = _jitterRayOrigin;
        o.JitterStrength = _jitterStrength;
        o.IgnoreAlbedo = _ignoreAlbedo;
        o.IncludeDirectLighting = _includeDirectLighting;
        o.SkyColor = new Float3(_skyColor.X, _skyColor.Y, _skyColor.Z);
        o.SurfelsPerSquareMeter = _surfelDensity;
        o.SurfelMaxNeighbors = _surfelMaxNeighbors;
        o.SurfelNormalThreshold = _surfelNormalThr;
        o.SurfelNormalRejection = _surfelNormalRejection;
        o.SurfelPoissonRadiusFactor = _surfelPoissonRadiusFactor;
        o.SurfelPoissonAlignThreshold = _surfelPoissonAlignThr;
    }

    private void ReuploadAllAtlases()
    {
        if (_renderer is null || _baker is null) return;
        for (int t = 0; t < _baker.Targets.Count; t++)
        {
            var target = _baker.Targets[t];
            // Upload the raw HDR linear irradiance straight through. The fragment shader handles
            // exposure + Reinhard + the sRGB gamma encode via FramebufferSrgb -- doing any of that
            // here would mean tonemapping twice.
            _renderer.UpdateAtlas(t, target.Width, target.Height, target.Pixels);
        }
    }

    // ---- camera + view ----------------------------------------------------------------------

    private void UpdateCamera(float dt)
    {
        // Suppress camera input when ImGui wants the mouse / keyboard.
        var io = ImGui.GetIO();
        if (io.WantCaptureKeyboard) return;

        var fwd = Forward();
        var right = Float3.Normalize(Float3.Cross(new Float3(0, 1, 0), fwd));

        float speed = KeyboardState.IsKeyDown(Keys.LeftShift) ? 20f : 6f;
        if (KeyboardState.IsKeyDown(Keys.W)) _camPos += fwd * speed * dt;
        if (KeyboardState.IsKeyDown(Keys.S)) _camPos -= fwd * speed * dt;
        if (KeyboardState.IsKeyDown(Keys.A)) _camPos -= right * speed * dt;
        if (KeyboardState.IsKeyDown(Keys.D)) _camPos += right * speed * dt;
        if (KeyboardState.IsKeyDown(Keys.Space))      _camPos += new Float3(0, speed * dt, 0);
        if (KeyboardState.IsKeyDown(Keys.LeftControl)) _camPos -= new Float3(0, speed * dt, 0);

        if (!io.WantCaptureMouse && MouseState.IsButtonDown(MouseButton.Right))
        {
            float mx = MouseState.Delta.X;
            float my = MouseState.Delta.Y;
            _yaw   += mx * 0.003f;
            _pitch -= my * 0.003f;
            _pitch = Math.Clamp(_pitch, -1.5f, 1.5f);
        }
    }

    private Float3 Forward() => Float3.Normalize(new Float3(
        (float)Math.Sin(_yaw) * (float)Math.Cos(_pitch),
        (float)Math.Sin(_pitch),
        (float)Math.Cos(_yaw) * (float)Math.Cos(_pitch)));

    private Float4x4 BuildView() => Float4x4.CreateLookAt(_camPos, _camPos + Forward(), new Float3(0, 1, 0));

    // Prowl.Vector is left-handed, so the projection has +Z forward and +w on +z.
    private static Float4x4 CreatePerspectiveLH(float fovY, float aspect, float zn, float zf)
    {
        float ys = 1f / MathF.Tan(fovY * 0.5f);
        float xs = ys / aspect;
        float zs = zf / (zf - zn);
        float wz = -zn * zs;
        var m = new Float4x4();
        m.c0 = new Float4(xs, 0, 0, 0);
        m.c1 = new Float4(0, ys, 0, 0);
        m.c2 = new Float4(0, 0, zs, 1);
        m.c3 = new Float4(0, 0, wz, 0);
        return m;
    }

    /// <summary>Build an object-to-world transform whose +Z column = the direction the light travels.</summary>
    private static Float4x4 MakeDirectionalTransform(System.Numerics.Vector3 dir)
    {
        var f = Float3.Normalize(new Float3(dir.X, dir.Y, dir.Z));
        var m = Float4x4.Identity;
        m.c2 = new Float4(f.X, f.Y, f.Z, 0);
        return m;
    }

    protected override void OnUnload()
    {
        _imgui?.Dispose();
        _lineRenderer?.Dispose();
        _surfelRenderer?.Dispose();
        _renderer?.Dispose();
        _baker?.Cancel();
        base.OnUnload();
    }
}
