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
    private ulong _renderedFrame = ulong.MaxValue;
    private GameObject _lightGo;
    private GameObject? _subjectGo;
    private RenderTexture? _rt;

    // The rig of an animated subject, resolved once when it is set up.
    private AnimatorBinding? _binding;
    private MotionPose? _pose;
    private MotionAvatar? _avatar;
    private readonly List<AnimationClip> _clips = [];

    /// <summary>Whether to draw a grid plane in the preview.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>
    /// Draws as an overlay onto a target cleared to transparent instead of over the skybox, so only the subject has
    /// alpha. Graphite views clear opaque, so the target is zeroed before each render. Colour comes out premultiplied.
    /// </summary>
    public bool TransparentBackground
    {
        get => _camera.Background == CameraBackground.None;
        set => _camera.Background = value ? CameraBackground.None : CameraBackground.Skybox;
    }

    private byte[]? _transparentPixels;

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

        _scene = new Scene(SceneMode.Preview);
        _scene.Name = "Preview";

        // Camera
        _cameraGo = new GameObject("PreviewCamera");
        _cameraGo.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        _camera = _cameraGo.AddComponent<Camera>();
        _camera.FieldOfView = 35f;
        _camera.NearClipPlane = 0.01f;
        _camera.FarClipPlane = 100f;
        _camera.Background = CameraBackground.Skybox;
        _camera.StereoTargetEye = StereoTargetEyeMask.None;
        _camera.Profile = VolumeProfile.CreateCameraDefault("Preview Profile");
        _camera.Profile.Get<AutoExposure>()!.Enabled.Set(false);
        _scene.Add(_cameraGo);

        // Light
        _lightGo = new GameObject("PreviewLight");
        _lightGo.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        _lightGo.Transform.LocalEulerAngles = new Float3(45, 225, 0);
        _lightGo.AddComponent<DirectionalLight>().CastShadows = false;
        _scene.Add(_lightGo);

        _scene.Activate();

        EnsureRT();
        UpdateCameraPosition();
    }

    /// <summary>Set up the preview to show a Mesh with a material.</summary>
    public void SetupForMesh(Mesh mesh, Material? material = null)
    {
        ClearSubject();
        if (mesh.IsNotValid()) return;

        _subjectGo = new GameObject("PreviewSubject") { HideFlags = HideFlags.HideAndDontSave };
        MeshRenderer renderer = _subjectGo.AddComponent<MeshRenderer>();
        renderer.Mesh = mesh;
        renderer.Material = material.IsValid() ? material : new Material(Shader.LoadDefault(DefaultShader.Lit));

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

    /// <summary>True when the subject carries an animator with clips or a rig this preview can pose.</summary>
    public bool CanAnimate => (_binding != null && _pose != null) || (_subjectGo.IsValid() && _clips.Count > 0);

    /// <summary>
    /// Poses the subject at a point through a clip, from 0 at its first frame to 1 at its last. Sampling
    /// rather than playing is what makes the preview scrub: any time is one call, in any order, and it
    /// works outside play mode where nothing is ticking.
    /// </summary>
    public void PoseAt(AnimationClip? clip, float normalizedTime)
    {
        float clampedNorm = Math.Clamp(normalizedTime, 0f, 1f);

        if (_binding != null && _pose != null)
        {
            MotionClip? runtime = clip.IsValid() ? clip!.GetClip(_avatar) : null;
            if (runtime == null)
            {
                _pose.SetToReferencePose(false);
            }
            else
            {
                runtime.GetPose(clampedNorm, _pose);
            }

            for (int b = 0; b < _pose.BoneCount; b++)
                _binding.ApplyBone(b, _pose.GetTransform(b));
            for (int c = 0; c < _pose.FloatChannelCount; c++)
                _binding.ApplyChannel(c, _pose.GetFloat(c));
        }

        if (_subjectGo.IsValid() && clip.IsValid() && clip!.HasPropertyTracks)
        {
            var propBinding = new AnimationClipBinding(_subjectGo!, clip);
            propBinding.Sample(clampedNorm * clip.Duration);
        }
    }

    /// <summary>Set up the preview to show a Prefab's serialized GameObject hierarchy.</summary>
    public void SetupForPrefab(PrefabAsset prefab)
    {
        ClearSubject();
        if (prefab.IsNotValid()) return;

        _subjectGo = GameObject.InstantiateDetached(prefab);
        if (_subjectGo.IsNotValid()) { _subjectGo = new GameObject("PreviewSubject"); return; }
        _subjectGo.Name = "PreviewSubject";
        _subjectGo.HideFlags = HideFlags.HideAndDontSave;

        // Bind rig first so that _avatar, _pose and _binding are available.
        BindRig(_subjectGo);

        // Non-visual prefabs (script-only hierarchies with no MeshRenderers) use the skeleton if possible,
        // but if not fall back to a unit-sized default inside the helper, rendering as an empty preview.
        NormalizeSubjectToUnitCube(_subjectGo);

        _scene.Add(_subjectGo);
        FitToSubject(AABB.FromCenterAndSize(Float3.Zero, Float3.One));
    }

    // A subject with an animator can be posed, so the preview reads its rig and binds to the copy it
    // just instantiated. The animator itself is left alone: outside play mode nothing ticks it, and
    // sampling by hand is what gives the preview a scrubber.
    private void BindRig(GameObject root)
    {
        Animator? animator = FindAnimator(root);
        if (animator.IsNotValid()) return;

        foreach (AnimationClip clip in animator!.Clips)
            if (clip.IsValid()) _clips.Add(clip);

        Avatar? avatar = animator.Avatar;
        if (avatar.IsNotValid() || avatar!.Skeleton == null) return;

        _avatar = avatar.Runtime;
        _pose = new MotionPose(avatar.Skeleton);
        _binding = new AnimatorBinding(animator.Transform, avatar.Skeleton);
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
        if (material.IsNotValid()) return;

        _subjectGo = new GameObject("PreviewSubject");
        _subjectGo.HideFlags = HideFlags.HideAndDontSave;
        MeshRenderer renderer = _subjectGo.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateSphere(0.5f, 32, 32);

        if (material.Shader.IsNotValid())
            material.Shader = Shader.LoadDefault(DefaultShader.Lit);
        renderer.Material = material;

        _scene.Add(_subjectGo);

        FitToSubject(AABB.FromCenterAndSize(Float3.Zero, Float3.One));
        _orbitDistance *= 1.25f; // Zoom out a bit more for materials
        UpdateCameraPosition();
    }

    /// <summary>Render the preview to the RenderTexture.</summary>
    public void Render()
    {
        if (_rt.IsNotValid()) return;

        _camera.UpdateRenderData(_rt);

        if (TransparentBackground)
        {
            int bytes = _rt.MainTexture.GetSize();
            if (_transparentPixels?.Length != bytes) _transparentPixels = new byte[bytes];
            _rt.MainTexture.SetData(_transparentPixels, 0, 0, (uint)Width, (uint)Height);
        }

        _camera.Render(new CameraRenderOptions
        {
            FallbackTarget = _rt,
            Kind = Prowl.Graphite.Rendering.ViewKind.Preview,
            Features = ShowGrid ? GridFeature.List : null,
        });
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
        // Inspectors showing the same asset share this preview, and its camera may only render once a frame
        if (_renderedFrame != Graphics.FrameIndex)
        {
            _renderedFrame = Graphics.FrameIndex;
            Resize((int)width, (int)height);
            Render();
        }

        if (_rt.IsNotValid() || _rt.MainTexture.IsNotValid()) return;
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
    private void NormalizeSubjectToUnitCube(GameObject subject)
    {
        // Reset the root so computed world-space bounds are relative to a clean frame.
        // Translating/scaling the root afterwards then produces the expected centering.
        subject.Transform.Position = Float3.Zero;
        subject.Transform.Rotation = Quaternion.Identity;
        subject.Transform.LocalScale = Float3.One;

        AABB bounds;
        MeshRenderer? meshRenderer = subject.GetComponent<MeshRenderer>();
        SkinnedMeshRenderer? skinnedRenderer = subject.GetComponent<SkinnedMeshRenderer>();

        // Fast path for single-MeshRenderer subjects (SetupForMesh case): use the mesh's own
        // bounds directly, since there is no child hierarchy to walk and world == local at this
        // point thanks to the identity reset above.
        if (meshRenderer.IsValid() && meshRenderer.Mesh.IsValid() && subject.Children.Count == 0)
            bounds = meshRenderer.Mesh.bounds;
        else if (skinnedRenderer.IsValid() && skinnedRenderer.SharedMesh.IsValid() && subject.Children.Count == 0)
            bounds = skinnedRenderer.SharedMesh.bounds;
        else
        {
            // Walk the hierarchy and get the min/max of all mesh renderers.
            bounds = ComputeHierarchyBounds(subject);
            if (bounds == default)
            {
                // No mesh renderers were found. Create a mesh renderer for the avatar skeleton, if one exists.
                AddPreviewMeshes(subject);
                bounds = ComputeHierarchyBounds(subject);

                // Fallback
                if (bounds == default)
                    bounds = AABB.FromCenterAndSize(Float3.Zero, Float3.One);
            }
        }

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

        if (!ReferenceEquals(_subjectGo, null))
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
        _rt = new RenderTexture(Width, Height, true, [TextureFormat.RGBA8]);
    }

    /// <summary>
    /// Compute bounds by walking the GO hierarchy for all MeshRenderer and SkinnedMeshRenderer components.
    /// </summary>
    private static AABB ComputeHierarchyBounds(GameObject root)
    {
        var min = new Float3(float.MaxValue);
        var max = new Float3(float.MinValue);
        bool found = false;

        CollectBoundsRecursive(root, ref min, ref max, ref found);

        if (!found)
            return default;

        return new AABB(min, max);
    }

    private static void CollectBoundsRecursive(GameObject go, ref Float3 min, ref Float3 max, ref bool found)
    {
        // Check MeshRenderer
        MeshRenderer? mr = go.GetComponent<MeshRenderer>();
        if (mr.IsValid())
        {
            Mesh? mesh = mr.Mesh;
            if (mesh.IsValid())
            {
                AABB worldBounds = mesh.bounds.TransformBy(go.Transform.LocalToWorldMatrix);
                min = new Float3(MathF.Min(min.X, worldBounds.Min.X), MathF.Min(min.Y, worldBounds.Min.Y), MathF.Min(min.Z, worldBounds.Min.Z));
                max = new Float3(MathF.Max(max.X, worldBounds.Max.X), MathF.Max(max.Y, worldBounds.Max.Y), MathF.Max(max.Z, worldBounds.Max.Z));
                found = true;
            }
        }

        // Check SkinnedMeshRenderer
        SkinnedMeshRenderer? smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr.IsValid())
        {
            Mesh? mesh = smr.SharedMesh;
            if (mesh.IsValid())
            {
                AABB worldBounds = mesh.bounds.TransformBy(go.Transform.LocalToWorldMatrix);
                min = new Float3(MathF.Min(min.X, worldBounds.Min.X), MathF.Min(min.Y, worldBounds.Min.Y), MathF.Min(min.Z, worldBounds.Min.Z));
                max = new Float3(MathF.Max(max.X, worldBounds.Max.X), MathF.Max(max.Y, worldBounds.Max.Y), MathF.Max(max.Z, worldBounds.Max.Z));
                found = true;
            }
        }

        foreach (GameObject child in go.Children)
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


    #region Skeleton preview

    /// <summary>
    /// Gives every descendant of <paramref name="root"/> a visible joint and bone stick, so a mesh-less
    /// clip (e.g. BVH) can be previewed. The root itself is skipped: it is the animator's own object
    /// and not a bone.
    /// </summary>
    public static void AddPreviewMeshes(GameObject root)
    {
        // Gather every descendant, plus the lengths of the bone-to-bone offsets for sizing.
        var bones = new List<GameObject>();
        var lengths = new List<float>();
        var stack = new Stack<GameObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            GameObject current = stack.Pop();
            bool isRoot = current == root;
            foreach (GameObject child in current.Children)
            {
                bones.Add(child);
                stack.Push(child);

                // A top-level bone's offset is where the skeleton sits in the world, not a bone length.
                if (!isRoot)
                {
                    Float3 p = child.Transform.LocalPosition;
                    float len = MathF.Sqrt(Float3.Dot(p, p));
                    if (len > 1e-6f) lengths.Add(len);
                }
            }
        }
        if (bones.Count == 0) return;

        // BVH units are arbitrary, so size the joints from the median bone length.
        lengths.Sort();
        float median = lengths.Count > 0 ? lengths[lengths.Count / 2] : 1f;

        // One material shared by every bone.
        var material = new Material(Shader.LoadDefault(DefaultShader.Lit));

        foreach (GameObject bone in bones)
        {
            // Child offsets are in this bone's local space, so they are the stick tails.
            var tails = new List<Float3>();
            foreach (GameObject child in bone.Children)
                tails.Add(child.Transform.LocalPosition);

            if (tails.Count == 0)
            {
                // Leaf: no child to point at, so continue in the direction the bone was reached from.
                // That direction is in the parent's space, which equals this bone's local space only
                // while its rotation is identity (true for BVH nodes).
                Float3 p = bone.Transform.LocalPosition;
                float len = MathF.Sqrt(Float3.Dot(p, p));
                tails.Add(len > 1e-6f ? p * (median * 0.5f / len) : Float3.UnitY * (median * 0.5f));
            }

            MeshRenderer renderer = bone.AddComponent<MeshRenderer>();
            renderer.Mesh = CreateBone([.. tails], jointRadius: median * 0.12f);
            renderer.Material = material;
        }
    }

    /// <summary>Builds the visual for a joint and it's children, in bone-local space.</summary>
    private static Mesh CreateBone(Float3[] tails, float jointRadius, float widthRatio = 0.12f)
    {
        List<Float3> vertices = [], normals = [];
        List<Float4> tangents = [];
        List<Float2> uvs = [];
        List<uint> indices = [];

        // Joint sphere.
        Mesh sphere = Mesh.CreateSphere(jointRadius, 6, 8);
        uint offset = (uint)vertices.Count;
        vertices.AddRange(sphere.Vertices);
        normals.AddRange(sphere.Normals);
        tangents.AddRange(sphere.Tangents);
        uvs.AddRange(sphere.UV);
        foreach (uint i in sphere.Indices)
            indices.Add(i + offset);

        foreach (Float3 tail in tails)
            AddStick(tail, widthRatio, vertices, normals, tangents, uvs, indices);

        Mesh mesh = new()
        {
            Vertices = [.. vertices],
            Normals = [.. normals],
            Tangents = [.. tangents],
            UV = [.. uvs],
            Indices = [.. indices],
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Builds the visual for the "stick" part of a bone's ball and stick.</summary>
    private static void AddStick(Float3 tail, float widthRatio,
        List<Float3> vertices, List<Float3> normals, List<Float4> tangents, List<Float2> uvs, List<uint> indices)
    {
        float length = MathF.Sqrt(Float3.Dot(tail, tail));
        if (length < 1e-6f) return;
        Float3 dir = tail * (1f / length);

        // Two axes perpendicular to the bone direction.
        Float3 helper = MathF.Abs(dir.Y) < 0.99f ? Float3.UnitY : Float3.UnitX;
        Float3 side = Float3.Cross(helper, dir);
        side *= 1f / MathF.Sqrt(Float3.Dot(side, side));
        Float3 up = Float3.Cross(dir, side);

        float w = length * widthRatio;
        Float3 head = Float3.Zero;
        Float3 mid = dir * (length * 0.15f);   // widest point, near the head like Blender's bones
        Float3[] ring = [mid + side * w, mid + up * w, mid - side * w, mid - up * w];

        for (int i = 0; i < 4; i++)
        {
            Float3 r0 = ring[i], r1 = ring[(i + 1) % 4];
            AddTri(head, r0, r1, mid, vertices, normals, tangents, uvs, indices);
            AddTri(tail, r1, r0, mid, vertices, normals, tangents, uvs, indices);
        }
    }

    /// <summary>Builds a flat-shaded triangle. 'inside' is any point inside the (convex) shape, used to orient the winding.</summary>
    private static void AddTri(Float3 a, Float3 b, Float3 c, Float3 inside,
        List<Float3> vertices, List<Float3> normals, List<Float4> tangents, List<Float2> uvs, List<uint> indices)
    {
        Float3 n = Float3.Cross(b - a, c - a);
        Float3 centroid = (a + b + c) * (1f / 3f);
        if (Float3.Dot(n, centroid - inside) > 0f)
        {
            // n points outward, so swap to make it point inward.
            (b, c) = (c, b);
            n = Float3.Cross(b - a, c - a);
        }
        Float3 outward = n * (-1f / MathF.Sqrt(Float3.Dot(n, n)));
        Float3 edge = b - a;
        edge *= 1f / MathF.Sqrt(Float3.Dot(edge, edge));

        uint start = (uint)vertices.Count;
        foreach (Float3 p in new[] { a, b, c })
        {
            vertices.Add(p);
            normals.Add(outward);
            tangents.Add(new Float4(edge.X, edge.Y, edge.Z, 1f));
            uvs.Add(Float2.Zero);
        }
        indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
    }

    #endregion
}
