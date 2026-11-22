// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

[Flags]
public enum CSGModelFlags
{
    None = 0,
    DoNotRender = 1 << 0,
    NoCollider = 1 << 1,
    ConvexCollider = 1 << 2,
}

/// <summary>Root of a CSG model. Mirrors the <see cref="CSGBrush"/> and <see cref="CSGGroup"/> components beneath it into a <see cref="CSGScene"/> and writes the result into a <see cref="MeshRenderer"/> and <see cref="MeshCollider"/>.</summary>
[ExecuteAlways]
[AddComponentMenu("CSG/CSG Model")]
public sealed class CSGModel : Component
{
    public CSGModelFlags Flags;
    public Material? DefaultMaterial;

    /// <summary>Texture tiles per world unit, multiplied into every surface's own scale.</summary>
    public float DefaultTexelScale = 1f;

    /// <summary>Keep rebuilding outside the editor when brushes change. Off ships the meshes saved with the scene.</summary>
    public bool RebuildAtRuntime;

    /// <summary>Generate lightmap UVs for this model.</summary>
    public bool GenerateLightmapUVs;

    /// <summary>Unwrap automatically once the geometry stops changing.</summary>
    public bool AutoRebuildLightmapUVs;

    /// <summary>True when the lightmap UVs no longer match the geometry.</summary>
    public bool LightmapUVsStale { get; private set; }

    /// <summary>Geometry hash the current mesh's UV2 was unwrapped from, or 0 when it never was.</summary>
    [SerializeField] private long lightmapUVHash;

    // Hashes of the saved meshes as built, before any unwrap, so the first rebuild after a load can keep them
    [SerializeField] private long visibleHash;
    [SerializeField] private long collisionHash;

    // The hashes of the meshes held now. Not serialized, so an undo of the fields above cannot fool the comparison
    [SerializeIgnore] private long _visibleKey;
    [SerializeIgnore] private long _collisionKey;
    [SerializeIgnore] private Mesh? _preview;

    /// <summary>Editor only: brushes left out of what the viewport draws. They still shape the model and its saved mesh.</summary>
    internal static Func<CSGBrush, bool>? EditorHidden;


    [SerializeIgnore] private double _geometryChangedAt = double.NegativeInfinity;

    [SerializeIgnore] private readonly CSGScene _scene = new();
    [SerializeIgnore] private readonly Dictionary<CSGBrush, BrushLink> _brushes = new();
    [SerializeIgnore] private readonly Dictionary<CSGGroup, GroupLink> _groups = new();
    [SerializeIgnore] private readonly Dictionary<int, BrushLink> _byBrushId = new();

    [SerializeIgnore] private readonly HashSet<CSGBrush> _seenBrushes = new();
    [SerializeIgnore] private readonly HashSet<CSGGroup> _seenGroups = new();
    [SerializeIgnore] private readonly Dictionary<CSGGroup, int> _childCounter = new();
    [SerializeIgnore] private int _rootChildCount;

    [SerializeIgnore] private GeometryData? _geometry;
    [SerializeIgnore] private Mesh? _visible;
    [SerializeIgnore] private Mesh? _collision;
    [SerializeIgnore] private bool _dirty = true;
    [SerializeIgnore] private bool _rebuildFromScratch;
    [SerializeIgnore] private bool _built;
    [SerializeIgnore] private long _structure;

    public int VertexCount { get; private set; }
    public int TriangleCount { get; private set; }
    public int BrushCount { get; private set; }

    /// <summary>How many times meshes were generated, for tests.</summary>
    internal int BuildCount { get; private set; }

    /// <summary>The generated renderable mesh, or null when the model produces nothing.</summary>
    public Mesh? GeneratedMesh => _visible;

    /// <summary>The generated collision mesh, or null.</summary>
    public Mesh? GeneratedCollisionMesh => _collision;

    /// <summary>Materials in submesh order, parallel to <see cref="GeneratedMesh"/>'s submeshes.</summary>
    public List<Material?> GeneratedMaterials { get; } = new();

    /// <summary>Request a rebuild on the next tick. Safe to call repeatedly.</summary>
    public void SetDirty() => _dirty = true;

    /// <summary>Picks against the generated surfaces and resolves the hit back to the brush and face that produced it.</summary>
    /// <param name="worldRay">Ray in world space.</param>
    /// <param name="accept">Brushes it returns false for are seen through, as hidden ones are.</param>
    public bool Raycast(Ray worldRay, out CSGBrush brush, out int planeIndex, out Float3 worldPoint, Func<CSGBrush, bool>? accept = null)
        => Raycast(worldRay, out brush, out planeIndex, out worldPoint, out _, accept);

    /// <summary>As the other overload, also giving the way the hit face shows, which is against its plane on a subtractive brush.</summary>
    public bool Raycast(Ray worldRay, out CSGBrush brush, out int planeIndex, out Float3 worldPoint, out Float3 worldNormal, Func<CSGBrush, bool>? accept = null)
    {
        brush = null!;
        planeIndex = -1;
        worldPoint = Float3.Zero;
        worldNormal = Float3.UnitY;

        Float4x4 toLocal = Transform.WorldToLocalMatrix;
        Float3 origin = Float4x4.TransformPoint(worldRay.Origin, toLocal);
        Float3 direction = Float4x4.TransformNormal(worldRay.Direction, toLocal);

        List<CSGPickHit> hits = CSGPicking.RaycastAll(_geometry, origin, direction);
        if (_scene.KeepDiscarded)
        {
            foreach (CSGDiscardedFace face in _scene.Discarded())
                if (CSGPicking.RaycastPolygon(face.Corners, face.Plane.Normal, origin, direction, out float distance, out Float3 at, out Float3 facing))
                    hits.Add(new CSGPickHit(face.BrushId, face.SourceFaceIndex, at, facing, distance));
            hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        foreach (CSGPickHit hit in hits)
        {
            if (!_byBrushId.TryGetValue(hit.BrushId, out BrushLink? link) || link.Brush.IsNotValid()) continue;
            if (accept != null && !accept(link.Brush)) continue;

            brush = link.Brush;

            // Maps the hit face to its plane through the same map the surface data uses
            planeIndex = hit.SourceFaceIndex >= 0 && hit.SourceFaceIndex < link.FaceToPlane.Length
                ? link.FaceToPlane[hit.SourceFaceIndex]
                : -1;

            worldPoint = Float4x4.TransformPoint(hit.Point, Transform.LocalToWorldMatrix);

            // Normals go through the inverse transpose, so a scaled model keeps them square to the face
            Float3 n = hit.Normal;
            worldNormal = Float3.NormalizeSafe(new Float3(Float3.Dot(toLocal.c0.XYZ, n), Float3.Dot(toLocal.c1.XYZ, n), Float3.Dot(toLocal.c2.XYZ, n)), Float3.UnitY);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Editor only: also keep the face pieces the operations remove, so a brush that is buried or carves nothing can be
    /// shown with <see cref="DiscardedFaces"/> and picked by <see cref="Raycast(Ray, out CSGBrush, out int, out Float3, out Float3, Func{CSGBrush, bool}?)"/>.
    /// </summary>
    public bool KeepDiscarded
    {
        get => _scene.KeepDiscarded;
        set
        {
            if (_scene.KeepDiscarded == value) return;
            _scene.KeepDiscarded = value;
            SetDirty();
        }
    }

    /// <summary>The face pieces the operations removed, in world space, while <see cref="KeepDiscarded"/> is on.</summary>
    public List<CSGModelFace> DiscardedFaces()
    {
        var result = new List<CSGModelFace>();
        Float4x4 toWorld = Transform.LocalToWorldMatrix;
        foreach (CSGDiscardedFace face in _scene.Discarded())
        {
            if (!_byBrushId.TryGetValue(face.BrushId, out BrushLink? link) || link.Brush.IsNotValid()) continue;
            if (face.SourceFaceIndex < 0 || face.SourceFaceIndex >= link.FaceToPlane.Length || link.FaceToPlane[face.SourceFaceIndex] < 0) continue;

            var corners = new Float3[face.Corners.Length];
            for (int i = 0; i < corners.Length; i++) corners[i] = Float4x4.TransformPoint(face.Corners[i], toWorld);
            result.Add(new CSGModelFace(link.Brush, link.FaceToPlane[face.SourceFaceIndex], NormalOf(corners), corners));
        }
        return result;
    }

    /// <summary>Every face the model generated, in world space, with the brush plane it came from and the way it really faces.</summary>
    public List<CSGModelFace> Faces()
    {
        var result = new List<CSGModelFace>();
        if (_geometry == null) return result;

        Float4x4 toWorld = Transform.LocalToWorldMatrix;
        foreach (GeometryData.Face face in _geometry.Faces)
        {
            if (!_byBrushId.TryGetValue(CSGScene.GetBrushId(face), out BrushLink? link) || link.Brush.IsNotValid()) continue;

            int source = CSGScene.GetSourceFaceIndex(face);
            if (source < 0 || source >= link.FaceToPlane.Length || link.FaceToPlane[source] < 0) continue;

            var ring = face.NeighborVertices();
            if (ring.Count < 3) continue;

            var corners = new Float3[ring.Count];
            for (int i = 0; i < ring.Count; i++) corners[i] = Float4x4.TransformPoint(ring[i].Point, toWorld);

            result.Add(new CSGModelFace(link.Brush, link.FaceToPlane[source], NormalOf(corners), corners));
        }
        return result;
    }

    /// <summary>The facing of a polygon from its winding, which survives any scale the model carries.</summary>
    private static Float3 NormalOf(Float3[] corners)
    {
        Float3 sum = Float3.Zero;
        for (int i = 0; i < corners.Length; i++)
        {
            Float3 a = corners[i], b = corners[(i + 1) % corners.Length];
            sum += new Float3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        return Float3.NormalizeSafe(sum, Float3.UnitY);
    }

    public override void OnValidate() => SetDirty();

    public override void OnEnable() => SetDirty();

    public override void Update()
    {
        // Outside edit mode the meshes saved with the scene are used as they are, unless asked to rebuild
        bool live = RebuildAtRuntime || (Application.IsEditor && !Application.IsPlaying);
        if (!live)
        {
            if (_built) return;
            AdoptSerialisedMeshes();
            if (_visible.IsValid() || _collision.IsValid()) { _built = true; return; }
        }

        if (!_dirty && !AnyComponentChanged())
        {
            TickLightmapUVs();
            return;
        }

        Rebuild(force: _dirty);
    }

    /// <summary>Rebuilds now rather than on the next tick.</summary>
    public void Rebuild() => Rebuild(force: true);

    private void Rebuild(bool force)
    {
        _dirty = false;
        _built = true;
        _scene.SurfaceUV ??= ProjectSurfaceUV;

        AdoptSerialisedMeshes();

        SyncTree();

        // A group cannot be reordered within its parent, so a moved group rebuilds the tree.
        if (_rebuildFromScratch)
        {
            ResetScene();
            SyncTree();
        }

        GeometryData geometry = _scene.Build();
        BrushCount = _scene.Count;

        // The scene hands back its cached output when nothing moved, so the meshes are already right
        if (!force && ReferenceEquals(geometry, _geometry)) return;
        _geometry = geometry;

        BuildCount++;
        CSGMeshes meshes = CSGMeshBuilder.Build(geometry, ResolveSurface, GameObject.Name);
        ApplyMeshes(meshes);
        TrackLightmapUVs();
    }

    /// <summary>The visible mesh without hidden brushes, or null when nothing in this model is hidden.</summary>
    private Mesh? BuildPreview(GeometryData geometry)
    {
        if (EditorHidden == null) return null;

        bool any = false;
        foreach (CSGBrush brush in _brushes.Keys)
            if (brush.IsValid() && EditorHidden(brush)) { any = true; break; }
        if (!any) return null;

        CSGMeshes preview = CSGMeshBuilder.Build(geometry, (brushId, face) =>
        {
            CSGSurfaceInfo info = ResolveSurface(brushId, face);
            if (_byBrushId.TryGetValue(brushId, out BrushLink? link) && link.Brush.IsValid() && EditorHidden(link.Brush)) info.Render = false;
            info.Collide = false;
            return info;
        }, GameObject.Name);
        _previewMaterials.Clear();
        _previewMaterials.AddRange(preview.Materials);
        return preview.Visible;
    }

    [SerializeIgnore] private readonly List<Material?> _previewMaterials = new();

    /// <summary>Editor only: builds what the viewport draws again after <see cref="EditorHidden"/> changed. The model itself is not rebuilt.</summary>
    internal void RefreshHidden()
    {
        if (_geometry == null) return;
        Mesh? preview = BuildPreview(_geometry);
        if (preview.IsNotValid() && _preview.IsNotValid()) return;
        _preview = preview;
        ApplyRenderer();
    }


    /// <summary>Records when the geometry last changed so an automatic unwrap can wait for it to settle.</summary>
    private void TrackLightmapUVs()
    {
        if (!GenerateLightmapUVs || _visible.IsNotValid())
        {
            LightmapUVsStale = false;
            return;
        }

        LightmapUVsStale = lightmapUVHash == 0 || lightmapUVHash != _visibleKey;

        if (!LightmapUVsStale) return;

        _geometryChangedAt = Time.UnscaledTotalTime;
    }

    /// <summary>Unwraps now, whatever the auto rebuild setting says.</summary>
    public void RebuildLightmapUVs()
    {
        if (_visible.IsNotValid()) return;

        CSGLightmapUVs.Generate(_visible);
        lightmapUVHash = _visibleKey;
        LightmapUVsStale = false;
    }

    /// <summary>Unwraps once the geometry has been still long enough, if asked to.</summary>
    private void TickLightmapUVs()
    {
        if (!LightmapUVsStale || !AutoRebuildLightmapUVs) return;
        if (Time.UnscaledTotalTime - _geometryChangedAt < CSGLightmapUVs.QuietPeriodSeconds) return;

        RebuildLightmapUVs();
    }

    /// <summary>Polls brush transforms in model space, everything else bumps a version on the component that changed.</summary>
    private bool AnyComponentChanged()
    {
        Float4x4 toModel = Transform.WorldToLocalMatrix;
        foreach ((CSGBrush brush, BrushLink link) in _brushes)
        {
            if (brush.IsNotValid()) return true;
            if (brush.Version != link.Version) return true;
            if (brush.EnabledInHierarchy != link.Enabled) return true;
            Float4x4 world = brush.Transform.LocalToWorldMatrix;
            if (!world.Equals(link.WorldMatrix) && !(toModel * world).Equals(link.LocalMatrix)) return true;
        }

        foreach ((CSGGroup group, GroupLink link) in _groups)
        {
            if (group.IsNotValid()) return true;
            if (group.Version != link.Version) return true;
            if (group.EnabledInHierarchy != link.Enabled) return true;
        }

        return Structure(GameObject) != _structure;
    }

    /// <summary>
    /// A hash of the hierarchy in the order <see cref="Walk"/> visits it, folders and pass through groups included, and of
    /// which objects it would sync. Any reorder, reparent, added or removed brush changes it.
    /// </summary>
    private static long Structure(GameObject parentObject)
    {
        long hash = 17;
        foreach (GameObject child in parentObject.Children)
        {
            if (!child.EnabledInHierarchy || child.GetComponent<CSGModel>().IsValid()) continue;

            CSGGroup group = child.GetComponent<CSGGroup>();
            CSGBrush brush = child.GetComponent<CSGBrush>();
            int synced = (group.IsValid() && group.EnabledInHierarchy && !group.PassThrough ? 1 : 0)
                       | (brush.IsValid() && brush.EnabledInHierarchy && brush.IsShapeValid ? 2 : 0);

            hash = hash * 31 + child.InstanceID;
            hash = hash * 31 + synced;
            hash = hash * 31 + Structure(child);
        }
        return hash;
    }

    private void SyncTree()
    {
        _seenBrushes.Clear();
        _seenGroups.Clear();
        _childCounter.Clear();
        _rootChildCount = 0;

        Walk(GameObject, null);
        PruneRemoved();
        _structure = Structure(GameObject);
    }

    /// <summary>Depth first, so parents exist before children and hierarchy order becomes CSG group order.</summary>
    private void Walk(GameObject parentObject, CSGGroup? parentGroup)
    {
        foreach (GameObject child in parentObject.Children)
        {
            if (!child.EnabledInHierarchy) continue;

            // A nested model owns its own subtree.
            if (child.GetComponent<CSGModel>().IsValid()) continue;

            CSGGroup? childGroup = parentGroup;

            CSGGroup group = child.GetComponent<CSGGroup>();
            if (group.IsValid() && group.EnabledInHierarchy && !group.PassThrough)
            {
                SyncGroup(group, parentGroup);
                childGroup = group;
            }

            CSGBrush brush = child.GetComponent<CSGBrush>();
            if (brush.IsValid() && brush.EnabledInHierarchy)
                SyncBrush(brush, childGroup);

            Walk(child, childGroup);
        }
    }

    private void SyncGroup(CSGGroup group, CSGGroup? parent)
    {
        _seenGroups.Add(group);
        int index = NextChildIndex(parent);

        if (!_groups.TryGetValue(group, out GroupLink? link))
        {
            link = new GroupLink { Handle = _scene.AddGroup(HandleOf(parent), group.Operation) };
            _groups[group] = link;
        }
        else
        {
            if (link.Parent != parent)
                _scene.SetParent(link.Handle, HandleOf(parent));

            if (link.Handle.Operation != group.Operation)
                _scene.SetOperation(link.Handle, group.Operation);
        }

        // Groups cannot be reordered within their parent, so a moved group forces a clean rebuild.
        if (link.Index >= 0 && link.Index != index) _rebuildFromScratch = true;

        link.Parent = parent;
        link.Index = index;
        link.Version = group.Version;
        link.Enabled = group.EnabledInHierarchy;
    }

    private void SyncBrush(CSGBrush brush, CSGGroup? parent)
    {
        _seenBrushes.Add(brush);
        int index = NextChildIndex(parent);

        GeometryData? shape = brush.Shape;
        if (shape == null)
        {
            // The planes stopped enclosing a solid, so drop it.
            if (_brushes.TryGetValue(brush, out BrushLink? broken)) RemoveBrush(brush, broken);
            return;
        }

        Float4x4 world = brush.Transform.LocalToWorldMatrix;

        // Column vector convention: brush local to world, then world to model space.
        Float4x4 local = Transform.WorldToLocalMatrix * world;

        if (!_brushes.TryGetValue(brush, out BrushLink? link))
        {
            link = new BrushLink();
            _brushes[brush] = link;
            AddBrushToScene(brush, link, shape, parent, local);
        }
        else if (link.Version != brush.Version && !ReferenceEquals(link.Shape, shape))
        {
            // A changed plane set cannot be swapped in place, so the brush is readded at the same position.
            _byBrushId.Remove(link.Handle!.Id);
            _scene.Remove(link.Handle);
            AddBrushToScene(brush, link, shape, parent, local);
        }
        else
        {
            // Same shape with a version bump means surface data changed, so drop the cached assembly that holds the UVs.
            if (link.Version != brush.Version)
                _scene.InvalidateOutput();

            if (link.Parent != parent)
                _scene.SetParent(link.Handle!, HandleOf(parent));

            if (link.Handle!.Operation != brush.Operation)
                _scene.SetOperation(link.Handle, brush.Operation);

            if (!link.LocalMatrix.Equals(local))
                _scene.SetTransform(link.Handle, local);
        }

        if (link.Index != index) _scene.SetOrder(link.Handle!, index);

        link.Parent = parent;
        link.Index = index;
        link.Version = brush.Version;
        link.Enabled = brush.EnabledInHierarchy;
        link.WorldMatrix = world;
        link.LocalMatrix = local;
    }

    private void AddBrushToScene(CSGBrush brush, BrushLink link, GeometryData shape, CSGGroup? parent, Float4x4 local)
    {
        link.Handle = _scene.AddTo(HandleOf(parent), shape, brush.Operation, local);
        link.Shape = shape;
        link.FaceToPlane = brush.FaceToPlane;
        link.Brush = brush;
        link.Index = -1;
        _byBrushId[link.Handle.Id] = link;
    }

    private void PruneRemoved()
    {
        foreach (CSGBrush brush in new List<CSGBrush>(_brushes.Keys))
        {
            if (_seenBrushes.Contains(brush)) continue;
            RemoveBrush(brush, _brushes[brush]);
        }

        foreach (CSGGroup group in new List<CSGGroup>(_groups.Keys))
        {
            if (_seenGroups.Contains(group)) continue;
            _scene.Remove(_groups[group].Handle);
            _groups.Remove(group);
        }
    }

    private void RemoveBrush(CSGBrush brush, BrushLink link)
    {
        if (link.Handle != null)
        {
            _byBrushId.Remove(link.Handle.Id);
            _scene.Remove(link.Handle);
        }
        _brushes.Remove(brush);
    }

    private void ResetScene()
    {
        _rebuildFromScratch = false;

        foreach (BrushLink link in _brushes.Values)
            if (link.Handle != null) _scene.Remove(link.Handle);

        foreach (GroupLink link in _groups.Values)
            _scene.Remove(link.Handle);

        _brushes.Clear();
        _groups.Clear();
        _byBrushId.Clear();
    }

    private CSGGroupHandle? HandleOf(CSGGroup? group)
        => group.IsValid() && _groups.TryGetValue(group!, out GroupLink? link) ? link.Handle : null;

    private int NextChildIndex(CSGGroup? parent)
    {
        if (parent.IsNotValid()) return _rootChildCount++;

        _childCounter.TryGetValue(parent!, out int next);
        _childCounter[parent!] = next + 1;
        return next;
    }

    /// <summary>Resolves what a generated face looks like. Model, brush and surface flags can each turn a face off.</summary>
    private CSGSurfaceInfo ResolveSurface(int brushId, int sourceFaceIndex)
    {
        var info = new CSGSurfaceInfo
        {
            Material = DefaultMaterial,
            Render = !Flags.HasFlag(CSGModelFlags.DoNotRender),
            Collide = !Flags.HasFlag(CSGModelFlags.NoCollider),
        };

        if (!TryResolve(brushId, sourceFaceIndex, out CSGBrush brush, out SurfaceData surface))
            return info;

        info.Render &= !brush.Flags.HasFlag(CSGBrushFlags.NoRender)
                    && !surface.Flags.HasFlag(CSGSurfaceFlags.NoRender);

        info.Collide &= !brush.Flags.HasFlag(CSGBrushFlags.NoCollision)
                     && !surface.Flags.HasFlag(CSGSurfaceFlags.NoCollision);

        info.SmoothingGroup = surface.SmoothingGroup;
        if (surface.Material.IsValid()) info.Material = surface.Material;

        return info;
    }

    /// <summary>Generates a face's UVs by planar projection, in brush space unless <see cref="CSGSurfaceFlags.WorldSpaceTexture"/> is set.</summary>
    private bool ProjectSurfaceUV(int brushId, int sourceFaceIndex, in Plane facePlane, out SurfaceTexSpace space)
    {
        space = default;
        if (!TryResolve(brushId, sourceFaceIndex, out CSGBrush brush, out SurfaceData surface, out int plane))
            return false;

        float texel = DefaultTexelScale > 0f ? DefaultTexelScale : 1f;
        Float2 scale = new(
            (surface.Scale.X == 0f ? 1f : surface.Scale.X) / texel,
            (surface.Scale.Y == 0f ? 1f : surface.Scale.Y) / texel);

        if (surface.Flags.HasFlag(CSGSurfaceFlags.WorldSpaceTexture) || plane < 0)
        {
            space = SurfaceTexSpace.FromFace(facePlane.Normal, surface.Offset, scale, surface.Rotation);
            return true;
        }

        // Built from the brush local normal so the basis turns with the brush, then expressed in model space.
        SurfaceTexSpace local = SurfaceTexSpace.FromFace(
            brush.Planes[plane].Normal, surface.Offset, scale, surface.Rotation);

        space = CSGSurfaceGeometry.Rebase(local, ModelToBrush(brush));
        return true;
    }

    /// <summary>Model space into the brush's own space, the inverse of what the scene was given.</summary>
    private Float4x4 ModelToBrush(CSGBrush brush)
        => (Transform.WorldToLocalMatrix * brush.Transform.LocalToWorldMatrix).Invert();

    private bool TryResolve(int brushId, int sourceFaceIndex, out CSGBrush brush, out SurfaceData surface)
        => TryResolve(brushId, sourceFaceIndex, out brush, out surface, out _);

    private bool TryResolve(int brushId, int sourceFaceIndex, out CSGBrush brush,
                            out SurfaceData surface, out int planeIndex)
    {
        brush = null!;
        surface = SurfaceData.Default;
        planeIndex = -1;

        if (!_byBrushId.TryGetValue(brushId, out BrushLink? link) || link.Brush.IsNotValid())
            return false;

        brush = link.Brush;
        if (sourceFaceIndex < 0 || sourceFaceIndex >= link.FaceToPlane.Length) return true;

        int plane = link.FaceToPlane[sourceFaceIndex];
        SurfaceData[] surfaces = brush.Surfaces;
        if (plane >= 0 && plane < surfaces.Length) { surface = surfaces[plane]; planeIndex = plane; }

        return true;
    }

    /// <summary>Swaps in the freshly built meshes, keeping any that are identical so a loaded mesh keeps its lightmap UVs.</summary>
    private void ApplyMeshes(CSGMeshes meshes)
    {
        Mesh? previous = _visible;
        _visible = KeepOrReplace(_visible, meshes.Visible, ref _visibleKey);

        // A new mesh carries no unwrap, even when its geometry matches the one that was unwrapped
        if (!ReferenceEquals(previous, _visible)) lightmapUVHash = 0;
        _collision = KeepOrReplace(_collision, meshes.Collision, ref _collisionKey);
        visibleHash = _visibleKey;
        collisionHash = _collisionKey;
        _preview = BuildPreview(_geometry!);

        GeneratedMaterials.Clear();
        GeneratedMaterials.AddRange(meshes.Materials);

        VertexCount = _visible.IsValid() ? _visible.Vertices.Length : 0;
        TriangleCount = _visible.IsValid() ? _visible.Indices.Length / 3 : 0;

        ApplyRenderer();
        ApplyCollider();
    }

    /// <summary>
    /// Keeps <paramref name="existing"/> when the rebuild produced the same geometry. A replaced mesh is dropped, never
    /// destroyed, since a duplicate or an undo record can still hold it and an unused runtime mesh frees itself.
    /// </summary>
    private static Mesh? KeepOrReplace(Mesh? existing, Mesh? rebuilt, ref long hash)
    {
        long rebuiltHash = CSGLightmapUVs.ContentHash(rebuilt);
        if (existing.IsValid() && rebuilt.IsValid() && rebuiltHash == hash) return existing;

        hash = rebuiltHash;
        return rebuilt;
    }

    /// <summary>Adopts the generated meshes the renderer and collider held after a scene load. Asset meshes are not ours.</summary>
    private void AdoptSerialisedMeshes()
    {
        if (_visible.IsNotValid())
        {
            MeshRenderer renderer = GameObject.GetComponent<MeshRenderer>();
            if (renderer.IsValid() && IsGenerated(renderer.Mesh))
            {
                _visible = renderer.Mesh;
                _visibleKey = visibleHash;
            }
        }

        if (_collision.IsNotValid())
        {
            MeshCollider collider = GameObject.GetComponent<MeshCollider>();
            if (collider.IsValid() && IsGenerated(collider.Mesh))
            {
                _collision = collider.Mesh;
                _collisionKey = collisionHash;
            }
        }
    }

    private static bool IsGenerated(Mesh? mesh) => mesh.IsValid() && !mesh!.IsFromDatabase;

    private void ApplyRenderer()
    {
        MeshRenderer renderer = GameObject.GetComponent<MeshRenderer>();
        if (renderer.IsNotValid())
        {
            if (_visible.IsNotValid()) return;
            renderer = GameObject.AddComponent<MeshRenderer>();
        }

        renderer.Mesh = _visible;
        renderer.Materials = GeneratedMaterials!;

        // Hidden brushes only leave what is drawn, so saves, copies, undo and bakes keep every brush
        renderer.SetDrawOverride(_preview, _preview.IsValid() ? _previewMaterials! : null);
    }

    private void ApplyCollider()
    {
        MeshCollider collider = GameObject.GetComponent<MeshCollider>();
        if (collider.IsNotValid())
        {
            if (_collision.IsNotValid()) return;
            collider = GameObject.AddComponent<MeshCollider>();
        }

        collider.Convex = Flags.HasFlag(CSGModelFlags.ConvexCollider);
        collider.Mesh = _collision;
    }

    private sealed class BrushLink
    {
        public CSGBrushHandle? Handle;
        public CSGBrush Brush = null!;
        public GeometryData? Shape;
        public int[] FaceToPlane = [];
        public CSGGroup? Parent;
        public int Index = -1;
        public int Version = -1;
        public bool Enabled = true;
        public Float4x4 WorldMatrix;
        public Float4x4 LocalMatrix;
    }

    private sealed class GroupLink
    {
        public CSGGroupHandle Handle = null!;
        public CSGGroup? Parent;
        public int Index = -1;
        public int Version = -1;
        public bool Enabled = true;
    }
}

/// <summary>A face of a model's generated surface: the brush and plane it came from, the way it faces and its corners, in world space.</summary>
public readonly record struct CSGModelFace(CSGBrush Brush, int PlaneIndex, Float3 Normal, Float3[] Corners);
