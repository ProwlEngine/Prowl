using System;
using System.Linq;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI;

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
        light.Intensity = 1f;
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

        // Instantiate the model's GO hierarchy for preview
        _subjectGo = model.Instantiate();
        if (_subjectGo == null) { _subjectGo = new GameObject("PreviewSubject"); return; }
        _subjectGo.Name = "PreviewSubject";
        _subjectGo.HideFlags = HideFlags.HideAndDontSave;

        NormalizeSubjectToUnitCube(_subjectGo);

        _scene.Add(_subjectGo);
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

        NormalizeSubjectToUnitCube(_subjectGo);

        _scene.Add(_subjectGo);
        FitToSubject(AABB.FromCenterAndSize(Float3.Zero, Float3.One));
    }

    /// <summary>Set up the preview to show a Prefab's serialized GameObject hierarchy.</summary>
    public void SetupForPrefab(PrefabAsset prefab)
    {
        ClearSubject();
        if (prefab == null) return;

        _subjectGo = prefab.Instantiate();
        if (_subjectGo == null) { _subjectGo = new GameObject("PreviewSubject"); return; }
        _subjectGo.Name = "PreviewSubject";
        _subjectGo.HideFlags = HideFlags.HideAndDontSave;

        // Non-visual prefabs (script-only hierarchies with no MeshRenderers) fall back to a
        // unit-sized default inside the helper, rendering as an empty preview.
        NormalizeSubjectToUnitCube(_subjectGo);

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
        renderer.Mesh = Mesh.CreateSphere(0.5f, 32, 32);

        if (material.Shader == null || !material.Shader.IsValid())
            material.Shader = Shader.LoadDefault(DefaultShader.Standard);
        renderer.Material = material;

        _scene.Add(_subjectGo);

        FitToSubject(AABB.FromCenterAndSize(Float3.Zero, Float3.One));
        _orbitDistance *= 1.25f; // Zoom out a bit more for materials
        UpdateCameraPosition();
    }

    /// <summary>Render the preview to the RenderTexture.</summary>
    public void Render()
    {
        if (_rt == null) return;

        _camera.UpdateRenderData();

        var pipeline = _camera.Pipeline ?? DefaultRenderPipeline.Default;
        pipeline.Render(_camera, new RenderingData { DisplayGrid = ShowGrid });
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
    /// Draw the preview into a Paper element area. Returns true if hovered.
    /// </summary>
    public void DrawPreview(Paper paper, string id, float width, float height)
    {
        Resize((int)width, (int)height);
        Render();

        if (_rt == null || _rt.MainTexture == null) return;

        paper.Box(id)
            .Size(width, height)
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 38, 38, 42))
            .Rounded(4)
            .StopEventPropagation()
            .OnDragging((e) => {
                Float2 delta = e.Delta;
                _orbitYaw += delta.X * 0.5f;
                _orbitPitch += delta.Y * 0.5f;
                _orbitPitch = MathF.Max(-89f, MathF.Min(89f, _orbitPitch));
                UpdateCameraPosition();
            })
            .OnScroll((e) =>
            {
                _orbitDistance *= 1f - e.Delta * 0.1f;
                _orbitDistance = MathF.Max(0.5f, MathF.Min(50f, _orbitDistance));
                UpdateCameraPosition();
            })
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
            {
                float rx = (float)r.Min.X;
                float ry = (float)r.Min.Y;
                float rw = (float)r.Size.X;
                float rh = (float)r.Size.Y;

                // Flip Y OpenGL RT has Y=0 at bottom
                canvas.SetBrushTexture(_rt.MainTexture);
                canvas.SetBrushTextureTransform(
                    Prowl.Vector.Spatial.Transform2D.CreateTranslation(rx, ry + rh) *
                    Prowl.Vector.Spatial.Transform2D.CreateScale(rw, -rh));
                canvas.RoundedRectFilled(rx, ry, rw, rh, 4, 4, 4, 4, new Color32(255, 255, 255, 255));
                canvas.ClearBrushTexture();
            }));
    }

    /// <summary>
    /// Resets the root transform of <paramref name="subject"/>, measures its world-space mesh
    /// bounds (children included), then scales + translates the root so the aggregate bounds
    /// fit inside a unit cube centered at the origin. Robust to hierarchies whose root has a
    /// saved non-identity transform (e.g. prefabs) the reset ensures the bounds we measure
    /// are in the same frame we then apply the normalization into.
    /// </summary>
    private static void NormalizeSubjectToUnitCube(GameObject subject)
    {
        // Reset the root so computed world-space bounds are relative to a clean frame.
        // Translating/scaling the root afterwards then produces the expected centering.
        subject.Transform.Position = Float3.Zero;
        subject.Transform.Rotation = Quaternion.Identity;
        subject.Transform.LocalScale = Float3.One;

        AABB bounds;
        var meshRenderer = subject.GetComponent<MeshRenderer>();
        var skinnedRenderer = subject.GetComponent<SkinnedMeshRenderer>();

        // Fast path for single-MeshRenderer subjects (SetupForMesh case): use the mesh's own
        // bounds directly, since there is no child hierarchy to walk and world == local at this
        // point thanks to the identity reset above.
        if (meshRenderer != null && meshRenderer.Mesh.Res != null && subject.Children.Count == 0)
            bounds = meshRenderer.Mesh.Res.bounds;
        else if (skinnedRenderer != null && skinnedRenderer.SharedMesh.Res != null && subject.Children.Count == 0)
            bounds = skinnedRenderer.SharedMesh.Res.bounds;
        else
            bounds = ComputeHierarchyBounds(subject);

        float maxExtent = MathF.Max(MathF.Max(bounds.Size.X, bounds.Size.Y), bounds.Size.Z);
        if (maxExtent <= 0.001f) return; // no visuals leave at identity

        float scale = 1f / maxExtent;
        subject.Transform.LocalScale = new Float3(scale, scale, scale);
        subject.Transform.Position = -bounds.Center * scale;
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

    /// <summary>
    /// Compute bounds by walking the GO hierarchy for all MeshRenderer and SkinnedMeshRenderer components.
    /// </summary>
    private static AABB ComputeHierarchyBounds(GameObject root)
    {
        Float3 min = new Float3(float.MaxValue);
        Float3 max = new Float3(float.MinValue);
        bool found = false;

        CollectBoundsRecursive(root, ref min, ref max, ref found);

        if (!found)
            return AABB.FromCenterAndSize(Float3.Zero, Float3.One);

        return new AABB(min, max);
    }

    private static void CollectBoundsRecursive(GameObject go, ref Float3 min, ref Float3 max, ref bool found)
    {
        // Check MeshRenderer
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var mesh = mr.Mesh.Res;
            if (mesh != null)
            {
                var worldBounds = mesh.bounds.TransformBy(go.Transform.LocalToWorldMatrix);
                min = new Float3(MathF.Min(min.X, worldBounds.Min.X), MathF.Min(min.Y, worldBounds.Min.Y), MathF.Min(min.Z, worldBounds.Min.Z));
                max = new Float3(MathF.Max(max.X, worldBounds.Max.X), MathF.Max(max.Y, worldBounds.Max.Y), MathF.Max(max.Z, worldBounds.Max.Z));
                found = true;
            }
        }

        // Check SkinnedMeshRenderer
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr != null)
        {
            var mesh = smr.SharedMesh.Res;
            if (mesh != null)
            {
                var worldBounds = mesh.bounds.TransformBy(go.Transform.LocalToWorldMatrix);
                min = new Float3(MathF.Min(min.X, worldBounds.Min.X), MathF.Min(min.Y, worldBounds.Min.Y), MathF.Min(min.Z, worldBounds.Min.Z));
                max = new Float3(MathF.Max(max.X, worldBounds.Max.X), MathF.Max(max.Y, worldBounds.Max.Y), MathF.Max(max.Z, worldBounds.Max.Z));
                found = true;
            }
        }

        foreach (var child in go.Children)
            CollectBoundsRecursive(child, ref min, ref max, ref found);
    }

    public void Dispose()
    {
        ClearSubject();
        _rt?.Dispose();
        _rt = null;
        _scene.Disable();
    }
}
