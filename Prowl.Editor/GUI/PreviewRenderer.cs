using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Canvas = Prowl.Quill.Canvas;
using MotionAvatar = Prowl.Motion.Avatar;
using MotionSkeleton = Prowl.Motion.Skeleton;
using MotionClip = Prowl.Motion.AnimationClipBase;
using MotionPose = Prowl.Motion.Pose;

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

    // The rig of an animated subject, resolved once when it is set up.
    private AnimatorBinding? _binding;
    private MotionPose? _pose;
    private MotionAvatar? _avatar;
    private readonly List<AnimationClip> _clips = new();

    /// <summary>Whether to draw a grid plane in the preview.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Clear to transparent instead of the skybox, so only the subject has alpha.</summary>
    public bool TransparentBackground
    {
        get => _camera.ClearFlags == CameraClearFlags.SolidColor;
        set
        {
            _camera.ClearFlags = value ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            _camera.ClearColor = new Color(0f, 0f, 0f, 0f);
        }
    }

    // Orbit camera state
    private float _orbitYaw = 30f;
    private float _orbitPitch = 20f;
    private float _orbitDistance = 3f;
    private Float3 _orbitTarget = Float3.Zero;

    /// <summary> Gets the rendered RenderTexture, or null if not yet created. </summary>
    public RenderTexture? Result => _rt;
    /// <summary> Gets the width of the preview render target in pixels. </summary>
    public int Width { get; private set; }
    /// <summary> Gets the height of the preview render target in pixels. </summary>
    public int Height { get; private set; }

    /// <summary> Creates a new PreviewRenderer with the given render target dimensions. Sets up an isolated Scene with a camera and directional light. </summary>
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
        _camera.StereoTargetEye = StereoTargetEyeMask.None;
        _scene.Add(_cameraGo);

        // Light
        _lightGo = new GameObject("PreviewLight");
        _lightGo.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        _lightGo.Transform.LocalEulerAngles = new Float3(45, 225, 0);
        var light = _lightGo.AddComponent<DirectionalLight>();
        light.Intensity = 1f;
        light.CastShadows = false;
        _scene.Add(_lightGo);

        _scene.Enable();

        EnsureRT();
        UpdateCameraPosition();
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
        renderer.Material = material.IsValid() ? material : new Material(Shader.LoadDefault(DefaultShader.Standard));

        NormalizeSubjectToUnitCube(_subjectGo);

        _scene.Add(_subjectGo);
        FitToSubject(AABB.FromCenterAndSize(Float3.Zero, Float3.One));
    }

    /// <summary>The clips the subject's animator can play, empty when it has none.</summary>
    public IReadOnlyList<AnimationClip> Clips => _clips;

    /// <summary>The rig the subject is bound to, or null when it has none.</summary>
    public MotionSkeleton? Skeleton => _pose?.Skeleton;

    /// <summary>
    /// Where a bone lands in the preview image, as a fraction across and down it. False when the rig
    /// does not have that bone, or when it sits behind the camera.
    /// </summary>
    public bool TryProjectBone(int boneIndex, out Float2 normalized)
    {
        normalized = default;
        Transform? bone = _binding?.BoneTransform(boneIndex);
        if (bone == null) return false;

        // Valid because Render() ran first: the camera's matrices are written when it is set up to
        // draw, not when it moves.
        Float4x4 viewProjection = _camera.ProjectionMatrix * _camera.ViewMatrix;
        Float4 clip = viewProjection * new Float4(bone.Position, 1f);
        if (clip.W <= 1e-5f) return false;

        float x = clip.X / clip.W, y = clip.Y / clip.W;
        normalized = new Float2(x * 0.5f + 0.5f, 0.5f - y * 0.5f);
        return true;
    }

    /// <summary>True when the subject carries a rig this preview can pose.</summary>
    public bool CanAnimate => _binding != null && _pose != null;

    /// <summary>
    /// Poses the subject at a point through a clip, from 0 at its first frame to 1 at its last. Sampling
    /// rather than playing is what makes the preview scrub: any time is one call, in any order, and it
    /// works outside play mode where nothing is ticking.
    /// </summary>
    public void PoseAt(AnimationClip? clip, float normalizedTime)
    {
        if (_binding == null || _pose == null) return;

        MotionClip? runtime = clip.IsValid() ? clip!.GetClip(_avatar) : null;
        if (runtime == null)
        {
            _pose.SetToReferencePose(false);
        }
        else
        {
            runtime.GetPose(Math.Clamp(normalizedTime, 0f, 1f), _pose);
        }

        for (int b = 0; b < _pose.BoneCount; b++)
            _binding.ApplyBone(b, _pose.GetTransform(b));
        for (int c = 0; c < _pose.FloatChannelCount; c++)
            _binding.ApplyChannel(c, _pose.GetFloat(c));
    }

    /// <summary>Set up the preview to show a Prefab's serialized GameObject hierarchy.</summary>
    public void SetupForPrefab(PrefabAsset prefab)
    {
        ClearSubject();
        if (prefab == null) return;

        _subjectGo = GameObject.InstantiateDetached(prefab);
        if (_subjectGo == null) { _subjectGo = new GameObject("PreviewSubject"); return; }
        _subjectGo.Name = "PreviewSubject";
        _subjectGo.HideFlags = HideFlags.HideAndDontSave;

        // Non-visual prefabs (script-only hierarchies with no MeshRenderers) fall back to a
        // unit-sized default inside the helper, rendering as an empty preview.
        NormalizeSubjectToUnitCube(_subjectGo);

        _scene.Add(_subjectGo);
        BindRig(_subjectGo);
        FitToSubject(AABB.FromCenterAndSize(Float3.Zero, Float3.One));
    }

    // A subject with an animator can be posed, so the preview reads its rig and binds to the copy it
    // just instantiated. The animator itself is left alone: outside play mode nothing ticks it, and
    // sampling by hand is what gives the preview a scrubber.
    private void BindRig(GameObject root)
    {
        Animator? animator = FindAnimator(root);
        if (animator.IsNotValid()) return;

        Avatar? avatar = animator!.Avatar;
        if (avatar.IsNotValid() || avatar!.Skeleton == null) return;

        _avatar = avatar.Runtime;
        _pose = new MotionPose(avatar.Skeleton);
        _binding = new AnimatorBinding(animator.Transform, avatar.Skeleton);

        foreach (AnimationClip clip in animator.Clips)
            if (clip.IsValid()) _clips.Add(clip);
    }

    private static Animator? FindAnimator(GameObject go)
    {
        Animator? own = go.GetComponent<Animator>();
        if (own.IsValid()) return own;

        foreach (GameObject child in go.Children)
            if (FindAnimator(child) is { } found) return found;
        return null;
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

        _camera.UpdateRenderData(_rt);

        var pipeline = _camera.Pipeline.IsValid() ? _camera.Pipeline : DefaultRenderPipeline.Default;
        pipeline.Render(_camera, new RenderingData { DisplayGrid = ShowGrid, FallbackTarget = _rt });
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

    /// <summary> Draw the preview into a Paper element area. Resizes and renders the preview, then displays it with orbit controls via drag and scroll. </summary>
    /// <summary>
    /// Draws the preview, with anything the caller wants on top of it. The overlay is painted by this
    /// same element rather than by one laid over it, so orbiting and clicking a gizmo do not fight: a
    /// drag turns the camera and a click reaches <paramref name="onClick"/>.
    /// </summary>
    public void DrawPreview(Paper paper, string id, float width, float height,
        Action<Canvas, Rect>? overlay = null, Action<Float2>? onClick = null)
    {
        Resize((int)width, (int)height);
        Render();

        if (_rt == null || _rt.MainTexture == null) return;
        float round = Prowl.OrigamiUI.Origami.Current.Metrics.ContainerRounding;

        ElementBuilder box = paper.Box(id)
            .Size(width, height)
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 38, 38, 42))
            .Rounded(round)
            // An overlay draws in screen space, so without this a gizmo for something off to the side
            // paints over whatever is next to the preview.
            .Clip()
            .StopEventPropagation()
            .OnDragging((e) =>
            {
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
            });

        if (onClick != null) box.OnClick(0, (_, e) => onClick(new Float2((float)e.PointerPosition.X, (float)e.PointerPosition.Y)));

        box.OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
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
                canvas.RoundedRectFilled(rx, ry, rw, rh, round, round, round, round, new Color32(255, 255, 255, 255));
                canvas.ClearBrushTexture();

                overlay?.Invoke(canvas, r);
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
        if (meshRenderer != null && meshRenderer.Mesh != null && subject.Children.Count == 0)
            bounds = meshRenderer.Mesh.bounds;
        else if (skinnedRenderer != null && skinnedRenderer.SharedMesh != null && subject.Children.Count == 0)
            bounds = skinnedRenderer.SharedMesh.bounds;
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
        _binding = null;
        _pose = null;
        _avatar = null;
        _clips.Clear();

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
        // For FOV 35 deg, distance to fit a unit object ~ 1.7
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
        if (_rt.IsValid()) _rt.Dispose();
        _rt = new RenderTexture(Width, Height, true, new[] { TextureImageFormat.Color4b });
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
            var mesh = mr.Mesh;
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
            var mesh = smr.SharedMesh;
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

    /// <summary> Releases all resources held by this PreviewRenderer, including the subject, render texture, scene, camera and light. </summary>
    public void Dispose()
    {
        ClearSubject();
        if (_rt.IsValid()) _rt.Dispose();
        _rt = null;

        // Disposes every GameObject still in the scene (camera + light), not just disables it.
        _scene.Dispose();
    }
}
