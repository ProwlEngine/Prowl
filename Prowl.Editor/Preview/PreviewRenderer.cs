using System;
using System.Linq;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary>
/// Renders 3D previews of assets (models, materials, meshes) to a RenderTexture.
/// Creates an isolated Scene with camera + light for clean rendering.
/// Supports orbit camera for interactive previews.
/// </summary>
public class PreviewRenderer : IDisposable
{
    private Scene _scene;
    private GameObject _cameraGo;
    private Camera _camera;
    private GameObject _lightGo;
    private GameObject? _subjectGo;
    private RenderTexture? _rt;
    private readonly EditorGrid _grid = new() { MaxDistance = 10f, Falloff = 2f };

    /// <summary>Whether to draw a grid plane in the preview.</summary>
    public bool ShowGrid { get; set; }

    // Orbit camera state
    private float _orbitYaw = 30f;
    private float _orbitPitch = 20f;
    private float _orbitDistance = 3f;
    private Float3 _orbitTarget = Float3.Zero;

    public RenderTexture? Result => _rt;
    public int Width { get; private set; }
    public int Height { get; private set; }

    public PreviewRenderer(int width = 256, int height = 256)
    {
        Width = width;
        Height = height;

        _scene = new Scene();
        _scene.Name = "Preview";

        // Camera
        _cameraGo = new GameObject("PreviewCamera");
        _cameraGo.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        _camera = _cameraGo.AddComponent<Camera>();
        _camera.FieldOfView = 35f;
        _camera.NearClipPlane = 0.01f;
        _camera.FarClipPlane = 100f;
        _camera.ClearFlags = CameraClearFlags.Skybox;
        _scene.Add(_cameraGo);

        // Light
        _lightGo = new GameObject("PreviewLight");
        _lightGo.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        _lightGo.Transform.LocalEulerAngles = new Float3(-45, 45, 0);
        var light = _lightGo.AddComponent<DirectionalLight>();
        light.Intensity = 6f;
        light.CastShadows = false;
        _scene.Add(_lightGo);

        _scene.Enable();

        EnsureRT();
        UpdateCameraPosition();
    }

    /// <summary>Set up the preview to show a Model.</summary>
    public void SetupForModel(Model model)
    {
        ClearSubject();
        if (model == null) return;

        _subjectGo = new GameObject("PreviewSubject");
        _subjectGo.HideFlags = HideFlags.HideAndDontSave;
        var renderer = _subjectGo.AddComponent<ModelRenderer>();
        renderer.Model = model;

        // Scale to fit in a ~1 unit cube centered at origin
        var bounds = EstimateModelBounds(model);
        float maxExtent = MathF.Max(MathF.Max(bounds.Size.X, bounds.Size.Y), bounds.Size.Z);
        if (maxExtent > 0.001f)
        {
            float scale = 1f / maxExtent;
            _subjectGo.Transform.LocalScale = new Float3(scale, scale, scale);
            _subjectGo.Transform.Position = -bounds.Center * scale;
        }

        _scene.Add(_subjectGo);

        // Frame the normalized object
        FitToSubject(AABB.FromCenterAndSize(Float3.Zero, Float3.One));
    }

    /// <summary>Set up the preview to show a Mesh with a material.</summary>
    public void SetupForMesh(Mesh mesh, Material? material = null)
    {
        ClearSubject();
        if (mesh == null) return;

        _subjectGo = new GameObject("PreviewSubject");
        _subjectGo.HideFlags = HideFlags.HideAndDontSave;
        var renderer = _subjectGo.AddComponent<MeshRenderer>();
        renderer.Mesh = mesh;
        renderer.Material = material ?? new Material(Shader.LoadDefault(DefaultShader.Standard));

        // Scale to fit in a ~1 unit cube centered at origin
        var bounds = mesh.bounds;
        float maxExtent = MathF.Max(MathF.Max(bounds.Size.X, bounds.Size.Y), bounds.Size.Z);
        if (maxExtent > 0.001f)
        {
            float scale = 1f / maxExtent;
            _subjectGo.Transform.LocalScale = new Float3(scale, scale, scale);
            _subjectGo.Transform.Position = -bounds.Center * scale;
        }

        _scene.Add(_subjectGo);

        FitToSubject(AABB.FromCenterAndSize(Float3.Zero, Float3.One));
    }

    /// <summary>Set up the preview to show a Material on a sphere.</summary>
    public void SetupForMaterial(Material material)
    {
        ClearSubject();
        if (material == null) return;

        _subjectGo = new GameObject("PreviewSubject");
        _subjectGo.HideFlags = HideFlags.HideAndDontSave;
        var renderer = _subjectGo.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(Float3.One); // TODO: Use sphere when available
        renderer.Material = material;
        _scene.Add(_subjectGo);

        FitToSubject(AABB.FromCenterAndSize(Float3.Zero, Float3.One));
    }

    /// <summary>Render the preview to the RenderTexture.</summary>
    public void Render()
    {
        if (_rt == null) return;

        _camera.UpdateRenderData();
        _scene.RenderCollect();

        if (ShowGrid)
            _grid.Draw(_scene, _cameraGo.Transform.Position);

        var pipeline = _camera.Pipeline ?? DefaultRenderPipeline.Default;
        pipeline.Render(_camera, new RenderingData());
    }

    /// <summary>Resize the preview render target.</summary>
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (Width == width && Height == height) return;
        Width = width;
        Height = height;
        EnsureRT();
    }

    /// <summary>
    /// Process orbit input. Call each frame when the preview is hovered.
    /// </summary>
    public void ProcessOrbitInput(bool isHovered)
    {
        if (!isHovered) return;

        // Left mouse drag = orbit
        if (Input.GetMouseButton(0))
        {
            Float2 delta = Input.MouseDelta;
            _orbitYaw += delta.X * 0.5f;
            _orbitPitch -= delta.Y * 0.5f;
            _orbitPitch = MathF.Max(-89f, MathF.Min(89f, _orbitPitch));
            UpdateCameraPosition();
        }

        // Scroll = zoom
        float scroll = Input.MouseWheelDelta;
        if (scroll != 0)
        {
            _orbitDistance *= 1f - scroll * 0.1f;
            _orbitDistance = MathF.Max(0.5f, MathF.Min(50f, _orbitDistance));
            UpdateCameraPosition();
        }
    }

    /// <summary>
    /// Draw the preview into a Paper element area. Returns true if hovered.
    /// </summary>
    public bool DrawPreview(Paper paper, string id, float width, float height)
    {
        Resize((int)width, (int)height);
        Render();

        if (_rt == null || _rt.MainTexture == null) return false;

        bool hovered = false;
        paper.Box(id)
            .Size(width, height)
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 38, 38, 42))
            .Rounded(4)
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
            {
                float rx = (float)r.Min.X;
                float ry = (float)r.Min.Y;
                float rw = (float)r.Size.X;
                float rh = (float)r.Size.Y;

                // Flip Y — OpenGL RT has Y=0 at bottom
                canvas.SetBrushTexture(_rt.MainTexture);
                canvas.SetBrushTextureTransform(
                    Prowl.Vector.Spatial.Transform2D.CreateTranslation(rx, ry + rh) *
                    Prowl.Vector.Spatial.Transform2D.CreateScale(rw, -rh));
                canvas.RoundedRectFilled(rx, ry, rw, rh, 4, 4, 4, 4, new Prowl.Vector.Color32(255, 255, 255, 255));
                canvas.ClearBrushTexture();
            }));

        hovered = paper.IsParentHovered;
        return hovered;
    }

    private void ClearSubject()
    {
        if (_subjectGo != null)
        {
            _scene.Remove(_subjectGo);
            _subjectGo.Dispose();
            _subjectGo = null;
        }
    }

    private void FitToSubject(AABB bounds)
    {
        _orbitTarget = bounds.Center;
        float maxDim = MathF.Max(MathF.Max(bounds.Size.X, bounds.Size.Y), bounds.Size.Z);
        // For FOV 35°, distance to fit a unit object ≈ 1.7
        _orbitDistance = MathF.Max(0.5f, maxDim * 1.7f);
        UpdateCameraPosition();
    }

    private void UpdateCameraPosition()
    {
        float yawRad = _orbitYaw * MathF.PI / 180f;
        float pitchRad = _orbitPitch * MathF.PI / 180f;

        Float3 offset = new Float3(
            MathF.Cos(pitchRad) * MathF.Sin(yawRad),
            MathF.Sin(pitchRad),
            MathF.Cos(pitchRad) * MathF.Cos(yawRad)
        ) * _orbitDistance;

        Float3 camPos = _orbitTarget + offset;
        _cameraGo.Transform.Position = camPos;

        Float3 dir = Float3.Normalize(_orbitTarget - camPos);
        if (Float3.LengthSquared(dir) > 0.0001f)
            _cameraGo.Transform.Rotation = Quaternion.LookRotation(dir, Float3.UnitY);
    }

    private void EnsureRT()
    {
        _rt?.Dispose();
        _rt = new RenderTexture(Width, Height, true, new[] { TextureImageFormat.Color4b });
        _camera.Target = _rt;
    }

    private static AABB EstimateModelBounds(Model model)
    {
        if (model.Meshes.Count == 0)
            return AABB.FromCenterAndSize(Float3.Zero, Float3.One);

        Float3 min = new Float3(float.MaxValue);
        Float3 max = new Float3(float.MinValue);

        foreach (var modelMesh in model.Meshes)
        {
            if (modelMesh.Mesh == null) continue;
            var b = modelMesh.Mesh.bounds;
            min = new Float3(MathF.Min(min.X, b.Min.X), MathF.Min(min.Y, b.Min.Y), MathF.Min(min.Z, b.Min.Z));
            max = new Float3(MathF.Max(max.X, b.Max.X), MathF.Max(max.Y, b.Max.Y), MathF.Max(max.Z, b.Max.Z));
        }

        if (min.X > max.X) return AABB.FromCenterAndSize(Float3.Zero, Float3.One);
        return new AABB(min, max);
    }

    public void Dispose()
    {
        ClearSubject();
        _rt?.Dispose();
        _rt = null;
        _scene.Disable();
    }
}
