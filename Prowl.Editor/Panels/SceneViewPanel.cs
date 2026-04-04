using System;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Scene")]
public class SceneViewPanel : DockPanel
{
    public override string Title => "Scene";
    public override string Icon => EditorIcons.Video;

    private EditorCamera? _editorCamera;
    private const float ToolbarHeight = 28f;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        _editorCamera ??= new EditorCamera();

        using (paper.Column("sv_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font, width);
            DrawViewport(paper, font, width, height - ToolbarHeight);
        }
    }

    private void DrawToolbar(Paper paper, Prowl.Scribe.FontFile font, float width)
    {
        using (paper.Row("sv_toolbar")
            .Height(ToolbarHeight)
            .ChildLeft(4).ChildRight(4).RowBetween(4)
            .ChildTop(2).ChildBottom(2)
            .Enter())
        {
            // Grid toggle
            bool showGrid = _editorCamera?.ShowGrid ?? true;
            paper.Box("sv_grid_btn")
                .Width(24).Height(24).Rounded(4)
                .BackgroundColor(showGrid ? EditorTheme.Accent : Color.Transparent)
                .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                .Text(EditorIcons.TableCellsLarge, font).TextColor(EditorTheme.Text)
                .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => { if (_editorCamera != null) _editorCamera.ShowGrid = !_editorCamera.ShowGrid; });

            // Spacer
            paper.Box("sv_spacer");

            // Camera info
            if (_editorCamera != null)
            {
                var pos = _editorCamera.Position;
                string info = $"({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})";
                paper.Box("sv_cam_info")
                    .Width(UnitValue.Auto).Height(24)
                    .ChildLeft(4).ChildRight(4)
                    .Text(info, font).TextColor(EditorTheme.TextDim)
                    .FontSize(EditorTheme.FontSize - 4).Alignment(TextAlignment.MiddleRight);
            }
        }
    }

    private void DrawViewport(Paper paper, Prowl.Scribe.FontFile font, float width, float height)
    {
        if (_editorCamera == null || width <= 0 || height <= 0) return;

        uint rtWidth = (uint)MathF.Max(1, width);
        uint rtHeight = (uint)MathF.Max(1, height);
        _editorCamera.EnsureRenderTarget(rtWidth, rtHeight);

        var scene = Scene.Current;
        var rt = _editorCamera.RenderTarget;

        if (scene == null)
        {
            // No scene — show message and create button
            using (paper.Column("sv_no_scene")
                .Size(width, height)
                .BackgroundColor(Color.FromArgb(255, 30, 30, 35))
                .Enter())
            {
                paper.Box("sv_no_scene_spacer");

                paper.Box("sv_no_scene_text")
                    .Height(30)
                    .Text("No Scene Loaded", font)
                    .TextColor(EditorTheme.TextDisabled)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleCenter);

                using (paper.Row("sv_no_scene_btn_row")
                    .Height(30).RowBetween(8)
                    .Enter())
                {
                    paper.Box("sv_btn_spacer_l");
                    EditorGUI.Button(paper, "sv_create_scene", $"{EditorIcons.Plus}  New Scene", width: 120)
                        .OnValueChanged(_ => CreateAndLoadDefaultScene());
                    paper.Box("sv_btn_spacer_r");
                }

                paper.Box("sv_no_scene_spacer2");
            }
            return;
        }

        // Render scene
        DrawSelectionGizmos();
        _editorCamera.Render(scene);

        if (rt != null && rt.MainTexture != null)
        {
            paper.Box("sv_viewport")
                .Size(width, height)
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    float rx = (float)r.Min.X;
                    float ry = (float)r.Min.Y;
                    float rw = (float)r.Size.X;
                    float rh = (float)r.Size.Y;

                    // Draw RT with flipped Y — OpenGL RT has Y=0 at bottom
                    canvas.SetBrushTexture(rt.MainTexture);
                    // TextureTransform maps screen rect to UV: flip V by translating +1 and scaling -1 on Y
                    canvas.SetBrushTextureTransform(
                        Transform2D.CreateTranslation(rx, ry + rh) *
                        Transform2D.CreateScale(rw, -rh));
                    //canvas.RectFilled(rx, ry, rw, rh, Color.White);
                    canvas.RoundedRectFilled(rx, ry, rw, rh, 0, 0, 8f, 8f, Color.White);
                    canvas.ClearBrushTexture();
                }))
                .OnClick(0, (_, e) =>
                {
                    if (!Input.IsAltPressed)
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
                Float2.Zero,
                new Float2(width, height));
        }
    }

    private void PickObject(Scene scene, Float2 screenPos, Float2 panelSize)
    {
        if (_editorCamera == null) return;

        var ray = _editorCamera.ScreenPointToRay(screenPos, panelSize);

        GameObject? bestHit = null;
        float bestDist = float.MaxValue;

        foreach (var go in scene.ActiveObjects)
        {
            if (go.HideFlags.HasFlag(HideFlags.Hide)) continue;

            // Check MeshRenderer
            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.Raycast(ray, out float dist))
            {
                if (dist < bestDist) { bestDist = dist; bestHit = go; }
                continue;
            }

            // Check ModelRenderer
            var modelRenderer = go.GetComponent<ModelRenderer>();
            if (modelRenderer != null && modelRenderer.Raycast(ray, out dist))
            {
                if (dist < bestDist) { bestDist = dist; bestHit = go; }
            }
        }

        if (bestHit != null)
        {
            if (Input.IsCtrlPressed)
                Selection.ToggleSelection(bestHit);
            else
                Selection.Select(bestHit);
        }
        else if (!Input.IsCtrlPressed && !Input.IsShiftPressed)
        {
            Selection.Clear();
        }
    }

    /// <summary>
    /// Create a default scene with camera, light, floor, and cubes, and load it.
    /// </summary>
    public static void CreateAndLoadDefaultScene()
    {
        var scene = new Scene();
        scene.Name = "Untitled Scene";

        var defaultMat = new Material(Shader.LoadDefault(DefaultShader.Standard));

        // Main Camera — matches sample setup
        var camGo = new GameObject("Main Camera");
        camGo.Tag = "Main Camera";
        camGo.Transform.Position = new Float3(0, 5, -15);
        camGo.Transform.LocalEulerAngles = new Float3(15, 0, 0);
        var cam = camGo.AddComponent<Camera>();
        cam.Depth = -1;
        cam.HDR = true;
        scene.Add(camGo);

        // Directional Light — matches sample setup
        var lightGo = new GameObject("Directional Light");
        lightGo.Transform.LocalEulerAngles = new Float3(-45, 45, 0);
        var light = lightGo.AddComponent<DirectionalLight>();
        light.Intensity = 8f;
        scene.Add(lightGo);

        // Floor
        var floorGo = new GameObject("Floor");
        floorGo.Transform.Position = new Float3(0, -0.05f, 0);
        floorGo.Transform.LocalScale = new Float3(10, 0.1f, 10);
        var floorRenderer = floorGo.AddComponent<MeshRenderer>();
        floorRenderer.Mesh = Mesh.CreateCube(Float3.One);
        floorRenderer.Material = defaultMat;
        scene.Add(floorGo);

        // Cube 1
        var cube1 = new GameObject("Cube");
        cube1.Transform.Position = new Float3(0, 0.5f, 0);
        var cube1Renderer = cube1.AddComponent<MeshRenderer>();
        cube1Renderer.Mesh = Mesh.CreateCube(Float3.One);
        cube1Renderer.Material = defaultMat;
        scene.Add(cube1);

        // Cube 2
        var cube2 = new GameObject("Cube (1)");
        cube2.Transform.Position = new Float3(2, 0.5f, 1);
        var cube2Renderer = cube2.AddComponent<MeshRenderer>();
        cube2Renderer.Mesh = Mesh.CreateCube(Float3.One);
        cube2Renderer.Material = defaultMat;
        scene.Add(cube2);

        Scene.Load(scene);
        Runtime.Debug.Log("Created default scene.");
    }

    private void DrawSelectionGizmos()
    {
        foreach (var obj in Selection.Selected)
        {
            if (obj is not GameObject go) continue;

            Float3 pos = go.Transform.Position;
            Float3 scale = go.Transform.LossyScale;
            var col = new Prowl.Vector.Color(0.3f, 0.6f, 1f, 1f);

            var renderer = go.GetComponent<ModelRenderer>();
            if (renderer != null)
            {
                Debug.DrawWireCube(pos, scale * 0.5f, col);
            }
            else
            {
                float s = 0.3f;
                Debug.DrawLine(pos - Float3.UnitX * s, pos + Float3.UnitX * s, col);
                Debug.DrawLine(pos - Float3.UnitY * s, pos + Float3.UnitY * s, col);
                Debug.DrawLine(pos - Float3.UnitZ * s, pos + Float3.UnitZ * s, col);
            }
        }
    }
}
