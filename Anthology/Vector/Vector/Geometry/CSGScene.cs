// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector.Spatial;

namespace Prowl.Vector.Geometry;

/// <summary>How a brush combines with the brushes before it in the scene.</summary>
public enum CSGOperation
{
    /// <summary>Adds the brush's volume to the solid.</summary>
    Additive,
    /// <summary>Removes the brush's volume from the solid.</summary>
    Subtractive,
    /// <summary>Keeps only the volume shared with what has accumulated so far.</summary>
    Intersect
}

/// <summary>
/// An opaque handle to a group in a <see cref="CSGScene"/>. A group combines its children into one
/// solid, which then folds into its own parent - so an assembly can be subtracted as a unit.
/// </summary>
public sealed class CSGGroupHandle
{
    internal readonly CSGScene.SceneGroup Group;
    internal CSGGroupHandle(CSGScene.SceneGroup group) => Group = group;

    public bool IsValid => Group.Attached;
    public CSGOperation Operation => Group.Op;
    public int Count => Group.Children.Count;
}

/// <summary>
/// An opaque handle to a brush placed in a <see cref="CSGScene"/>. Keep it to move, re-order,
/// change, or remove the brush later. Handles become invalid once their brush is removed.
/// </summary>
public sealed class CSGBrushHandle
{
    internal CSGScene.SceneBrush Brush;
    internal CSGBrushHandle(CSGScene.SceneBrush brush) => Brush = brush;

    /// <summary>True while the brush is still part of its scene.</summary>
    public bool IsValid => Brush is { Attached: true };

    /// <summary>Stable identity, unchanged by re-ordering. Matches
    /// <see cref="CSGScene.BrushIdAttribute"/> on faces this brush produced.</summary>
    public int Id => Brush.Id;

    /// <summary>The brush's current operation.</summary>
    public CSGOperation Operation => Brush.Op;

    /// <summary>The brush's current world transform.</summary>
    public Float4x4 Transform => Brush.Transform;
}

/// <summary>
/// An incremental N-ary CSG scene built from brushes (after Sander van Rossen's Realtime-CSG
/// model). Brushes form an ordered list; later brushes override earlier ones where they overlap, so
/// the final solid is "the last brush whose volume contains a point decides whether it is solid".
///
/// Each brush only interacts with brushes it spatially overlaps, so adding, moving, re-ordering, or
/// removing one brush re-evaluates only that brush and the ones it touches; everything else keeps its
/// cached surface. Overlap queries go through a <see cref="Bvh{T}"/> broadphase so large scenes stay
/// cheap to edit.
///
/// A brush is any closed, consistently wound mesh: concave, with bent faces, or with holes through it. Convex brushes
/// are clipped by their planes, others cut fragments along the faces that reach them and decide inside or outside by
/// winding number. A brush that is not quite closed is still classified by what it nearly encloses. Vertex,
/// edge, loop, and face attributes are carried through; where two brushes define an attribute of the
/// same name, the earlier brush's schema wins.
///
/// Each face is partitioned exactly (no gaps or overlaps). By default a T-junction welding pass makes
/// the output watertight where independent cuts subdivide a shared face; disable it via
/// <see cref="WeldTJunctions"/> for raw, faster output.
/// </summary>
public sealed class CSGScene
{
    /// <summary>Face attribute naming the brush an output face came from, matching
    /// <see cref="CSGBrushHandle.Id"/>. Always written.</summary>
    public const string BrushIdAttribute = "csg:brush";

    /// <summary>Face attribute naming the source face within that brush - its index in the brush
    /// shape's <see cref="GeometryData.Faces"/> list. With <see cref="BrushIdAttribute"/> this
    /// identifies which surface a fragment belongs to.</summary>
    public const string SourceFaceAttribute = "csg:face";

    private const float DegenerateEpsilon = 0.0001f;
    private float DistanceEpsilon = 0.0001f;
    private const float NormalEpsilon = 1.0f / 65535.0f;
    // A corner within the distance tolerance of a plane counts as on it but is not moved there, so two
    // brushes can place the same corner that far apart. Welding has to reach across that.
    private const float WeldReach = 3.0f;
    private float WeldTolerance = 0.0001f * WeldReach;

    /// <summary>
    /// How far apart two points or planes may be and still count as the same, in world units.
    /// Default 0.0001. Raise it when brush corners are only accurate to more than that, for example
    /// after rotating them far from the origin, or faces meant to coincide will not be recognised.
    /// </summary>
    public float Tolerance
    {
        get => DistanceEpsilon;
        set
        {
            if (DistanceEpsilon == value) return;
            DistanceEpsilon = value;
            WeldTolerance = value * WeldReach;
            MarkEvaluationDirty();
            InvalidateOutput(reweld: true);
        }
    }

    // The tree is the model. _brushes is a flattened cache of its leaves, rebuilt whenever the tree
    // changes, because fragmentation still tests every brush against every overlapping brush
    // regardless of where each sits in the tree.
    private readonly SceneGroup _root = new() { Op = CSGOperation.Additive, Attached = true };
    private readonly List<SceneBrush> _brushes = new();
    private readonly List<AABB> _pendingDirtyRegions = new();
    private int _nextId;
    private bool _treeDirty = true;      // the flattened brush cache is stale
    private bool _evalDirty = true;      // every brush must be re-evaluated, not just dirty ones
    private bool _hasIntersect;          // an Intersect anywhere makes edits act non-locally
    private bool _flat;                  // no groups and no Intersect: the last brush containing a point decides it
    private GeometryData? _cachedOutput;

    // The assembled mesh, kept between builds and patched where brushes changed
    private GeometryData? _output;
    private bool _reassemble = true;
    private bool _hasAttributes;
    private readonly Dictionary<SnapKey, List<GeometryData.Vertex>> _vertexGrid = new();
    private readonly Dictionary<SnapKey, List<RepPoint>> _representatives = new();
    private readonly DynamicBvh<RepPoint> _representativeTree = new();
    private readonly DynamicBvh<SceneBrush> _brushTree = new();
    private bool _brushTreeStale = true;
    private bool _deferRepresentativeTree;

    private bool _weldTJunctions = true;
    private bool _keepDiscarded;
    private bool _invertOutput;

    /// <summary>
    /// Builds the inside of the solid instead of its outside: every face turns to face inward and its texture runs the
    /// other way across, so it reads unmirrored from inside. Brushes then read as rooms carved out of an endless solid.
    /// </summary>
    public bool InvertOutput
    {
        get => _invertOutput;
        set
        {
            if (_invertOutput == value) return;
            _invertOutput = value;
            InvalidateOutput();
        }
    }

    /// <summary>
    /// Whether <see cref="Build"/> also keeps the face pieces the operations remove, for <see cref="Discarded"/>. Off by
    /// default, as an editor wants them only to show or pick what is buried or carves nothing.
    /// </summary>
    public bool KeepDiscarded
    {
        get => _keepDiscarded;
        set
        {
            if (_keepDiscarded == value) return;
            _keepDiscarded = value;
            MarkEvaluationDirty();
        }
    }

    /// <summary>The face pieces the last <see cref="Build"/> removed, while <see cref="KeepDiscarded"/> is on.</summary>
    public IEnumerable<CSGDiscardedFace> Discarded()
    {
        foreach (var brush in _brushes)
            foreach (var frag in brush.Discarded)
                yield return new CSGDiscardedFace(brush.Id, frag.Source.SourceIndex, frag.Poly, frag.Source.Plane);
    }

    /// <summary>Whether <see cref="Build"/> runs a T-junction welding pass for watertight output. Default true.</summary>
    public bool WeldTJunctions
    {
        get => _weldTJunctions;
        set
        {
            if (_weldTJunctions == value) return;
            _weldTJunctions = value;
            InvalidateOutput(reweld: true);
        }
    }

    /// <summary>
    /// Supplies the texture projection for one brush surface, or returns false to leave that surface
    /// interpolating whatever UVs its source face carried.
    /// </summary>
    /// <param name="brushId">Brush the surface belongs to, matching <see cref="CSGBrushHandle.Id"/>.</param>
    /// <param name="sourceFaceIndex">Index of the face in that brush's shape.</param>
    /// <param name="facePlane">World-space plane of the surface.</param>
    /// <param name="space">Receives the texture projection when true is returned.</param>
    public delegate bool SurfaceUVProvider(int brushId, int sourceFaceIndex, in Plane facePlane, out SurfaceTexSpace space);

    /// <summary>
    /// When set, a surface's UVs are generated by planar projection rather than interpolated from its
    /// source face corners, so they stay put as CSG re-cuts the surface.
    /// </summary>
    public SurfaceUVProvider? SurfaceUV
    {
        get => _surfaceUV;
        set { _surfaceUV = value; InvalidateOutput(reweld: false); }
    }

    private SurfaceUVProvider? _surfaceUV;

    /// <summary>Loop attribute that <see cref="SurfaceUV"/> writes. Default "uv".</summary>
    public string UVAttributeName
    {
        get => _uvAttributeName;
        set
        {
            if (_uvAttributeName == value) return;
            _uvAttributeName = value;
            InvalidateOutput(reweld: false);
        }
    }

    private string _uvAttributeName = "uv";

    /// <summary>Drop the cached mesh so the next <see cref="Build"/> re-assembles. Needed after
    /// changing something that affects output without moving a brush, such as an attribute on a
    /// brush shape.</summary>
    public void InvalidateOutput() => InvalidateOutput(reweld: false);

    private void InvalidateOutput(bool reweld)
    {
        _cachedOutput = null;
        _reassemble = true;
        if (!reweld) return;
        foreach (var brush in _brushes) brush.WeldDirty = true;
    }

    /// <summary>Number of brushes in the scene, across every group.</summary>
    public int Count
    {
        get { FlattenTree(); return _brushes.Count; }
    }

    /// <summary>Number of brushes whose surface was recomputed during the most recent <see cref="Build"/>.</summary>
    public int LastRebuildCount { get; private set; }

    #region Public editing API

    /// <summary>The implicit root group. Everything added without a parent lands here.</summary>
    public CSGGroupHandle Root => new(_root);

    /// <summary>Adds a convex brush to the end of the root group.</summary>
    public CSGBrushHandle Add(GeometryData convexShape, CSGOperation operation = CSGOperation.Additive)
        => Add(convexShape, operation, Float4x4.Identity);

    /// <summary>Adds a convex brush with a world transform to the end of the root group.</summary>
    public CSGBrushHandle Add(GeometryData convexShape, CSGOperation operation, Float4x4 transform)
        => AddTo(null, convexShape, operation, transform);

    /// <summary>Adds a convex brush to the end of a group, or the root when <paramref name="parent"/>
    /// is null.</summary>
    public CSGBrushHandle AddTo(CSGGroupHandle? parent, GeometryData convexShape,
                                CSGOperation operation = CSGOperation.Additive, Float4x4 transform = default)
    {
        if (convexShape == null) throw new ArgumentNullException(nameof(convexShape));
        if (transform.Equals(default(Float4x4))) transform = Float4x4.Identity;

        var group = ResolveGroup(parent);
        var brush = new SceneBrush
        {
            Id = _nextId++,
            Shape = convexShape,
            Op = operation,
            Transform = transform,
            Attached = true,
            Parent = group
        };
        RecomputeWorld(brush);

        group.Children.Add(brush);
        MarkTreeDirty();

        brush.Dirty = true;
        _pendingDirtyRegions.Add(BrushBox(brush));
        return new CSGBrushHandle(brush);
    }

    /// <summary>Inserts a convex brush at a specific index within the root group.</summary>
    public CSGBrushHandle Insert(int index, GeometryData convexShape, CSGOperation operation, Float4x4 transform)
    {
        var handle = AddTo(null, convexShape, operation, transform);
        SetOrder(handle, index);
        return handle;
    }

    /// <summary>
    /// Creates a group. Its children combine into one solid, which then folds into
    /// <paramref name="parent"/> by <paramref name="operation"/>.
    /// </summary>
    public CSGGroupHandle AddGroup(CSGGroupHandle? parent = null, CSGOperation operation = CSGOperation.Additive)
    {
        var group = new SceneGroup { Op = operation, Attached = true, Parent = ResolveGroup(parent) };
        group.Parent!.Children.Add(group);
        MarkTreeDirty();
        return new CSGGroupHandle(group);
    }

    /// <summary>Changes how a group folds into its parent.</summary>
    public void SetOperation(CSGGroupHandle handle, CSGOperation operation)
    {
        var group = Validate(handle);
        if (group.Op == operation) return;
        group.Op = operation;
        DirtySubtree(group);
        MarkTreeDirty();
    }

    /// <summary>Removes a group and everything inside it.</summary>
    public void Remove(CSGGroupHandle handle)
    {
        var group = Validate(handle);
        if (group == _root) throw new InvalidOperationException("The root group cannot be removed.");

        DirtySubtree(group);
        group.Parent?.Children.Remove(group);
        DetachSubtree(group);
        MarkTreeDirty();
    }

    /// <summary>Moves a brush into another group, keeping its transform and operation.</summary>
    public void SetParent(CSGBrushHandle handle, CSGGroupHandle? parent)
    {
        var brush = Validate(handle);
        var group = ResolveGroup(parent);
        if (ReferenceEquals(brush.Parent, group)) return;

        brush.Parent?.Children.Remove(brush);
        group.Children.Add(brush);
        brush.Parent = group;
        MarkTreeDirty();

        // Its own surfaces and everything it touches can both change.
        brush.Dirty = true;
        _pendingDirtyRegions.Add(BrushBox(brush));
    }

    /// <summary>Moves a group into another group.</summary>
    public void SetParent(CSGGroupHandle handle, CSGGroupHandle? parent)
    {
        var group = Validate(handle);
        var target = ResolveGroup(parent);
        if (group == _root) throw new InvalidOperationException("The root group cannot be re-parented.");
        if (ReferenceEquals(group.Parent, target)) return;
        if (IsAncestorOf(group, target)) throw new InvalidOperationException("A group cannot be moved inside itself.");

        DirtySubtree(group);
        group.Parent?.Children.Remove(group);
        target.Children.Add(group);
        group.Parent = target;
        MarkTreeDirty();
        DirtySubtree(group);
    }

    /// <summary>Removes a brush from the scene. Its handle becomes invalid.</summary>
    public void Remove(CSGBrushHandle handle)
    {
        var brush = Validate(handle);
        _pendingDirtyRegions.Add(BrushBox(brush)); // dirty whatever it used to touch
        brush.Attached = false;
        brush.Parent?.Children.Remove(brush);
        MarkTreeDirty();
    }

    /// <summary>Moves/transforms a brush. Only the brush and the brushes it touches are re-evaluated.</summary>
    public void SetTransform(CSGBrushHandle handle, Float4x4 transform)
    {
        var brush = Validate(handle);
        _pendingDirtyRegions.Add(BrushBox(brush)); // brushes it currently touches
        brush.Transform = transform;
        RecomputeWorld(brush);
        _pendingDirtyRegions.Add(BrushBox(brush)); // brushes it now touches
        brush.Dirty = true;
        _cachedOutput = null;
        EscalateIfNonLocal();
    }

    /// <summary>Changes a brush's additive/subtractive operation.</summary>
    public void SetOperation(CSGBrushHandle handle, CSGOperation operation)
    {
        var brush = Validate(handle);
        if (brush.Op == operation) return;
        brush.Op = operation;
        brush.Dirty = true;
        _pendingDirtyRegions.Add(BrushBox(brush));
        MarkTreeDirty();
    }

    /// <summary>
    /// Sets whether a brush contributes its own surface to the output. A brush that does not emit
    /// still cuts and hides the brushes around it, which suits context loaded only so that the
    /// brushes of interest come out right.
    /// </summary>
    public void SetEmits(CSGBrushHandle handle, bool emits)
    {
        var brush = Validate(handle);
        if (brush.Emits == emits) return;
        brush.Emits = emits;
        brush.Dirty = true;
        _cachedOutput = null;
    }

    /// <summary>Re-orders a brush to a new index, preserving everyone else's relative order.</summary>
    public void SetOrder(CSGBrushHandle handle, int index)
    {
        var brush = Validate(handle);
        var siblings = brush.Parent!.Children;
        index = Maths.Clamp(index, 0, siblings.Count - 1);
        if (siblings.IndexOf(brush) == index) return;

        siblings.Remove(brush);
        siblings.Insert(index, brush);
        MarkTreeDirty();
        brush.Dirty = true;
        _pendingDirtyRegions.Add(BrushBox(brush));
    }

    /// <summary>
    /// Evaluates the scene into a single mesh. Only brushes flagged dirty since the last call are
    /// re-evaluated; the rest reuse their cached surface. The merged mesh is kept and only the faces of changed
    /// brushes and their neighbours are replaced, so each call hands back the same object with a new Version.
    /// </summary>
    /// <summary>
    /// Read the brush id an output face was attributed to, or -1 if the face did not come from a
    /// <see cref="CSGScene"/> build.
    /// </summary>
    public static int GetBrushId(GeometryData.Face face) => ReadInt(face, BrushIdAttribute);

    /// <summary>
    /// Read the source-face index an output face came from, or -1. Index into the owning brush's
    /// shape <see cref="GeometryData.Faces"/>.
    /// </summary>
    public static int GetSourceFaceIndex(GeometryData.Face face) => ReadInt(face, SourceFaceAttribute);

    /// <summary>Every output face produced by one brush. For selection highlighting.</summary>
    public static IEnumerable<GeometryData.Face> FacesOf(GeometryData built, CSGBrushHandle handle)
    {
        int id = handle.Id;
        foreach (var face in built.Faces)
            if (GetBrushId(face) == id) yield return face;
    }

    /// <summary>Every output face produced by one face of one brush.</summary>
    public static IEnumerable<GeometryData.Face> FacesOf(GeometryData built, CSGBrushHandle handle, int sourceFaceIndex)
    {
        int id = handle.Id;
        foreach (var face in built.Faces)
            if (GetBrushId(face) == id && GetSourceFaceIndex(face) == sourceFaceIndex) yield return face;
    }

    private static int ReadInt(GeometryData.Face face, string name)
        => face.Attributes.TryGetValue(name, out var value) && value is GeometryData.IntAttributeValue i && i.Data.Length > 0
            ? i.Data[0]
            : -1;

    public GeometryData Build()
    {
        // A changed tree, or a changed operation, can alter any brush's visibility.
        FlattenTree();
        if (_evalDirty)
            foreach (var b in _brushes) b.Dirty = true;
        _evalDirty = false;

        var bvh = _brushTree;
        if (_brushTreeStale)
        {
            int[] handles = bvh.Rebuild(_brushes, _brushes.ConvertAll(BrushBox));
            for (int i = 0; i < handles.Length; i++) _brushes[i].TreeHandle = handles[i];
            _brushTreeStale = false;
        }
        else
        {
            foreach (var brush in _brushes)
                bvh.Update(brush.TreeHandle, BrushBox(brush));
        }

        // Propagate pending dirty regions to whatever brushes now occupy them.
        if (_pendingDirtyRegions.Count > 0)
        {
            var hits = new List<SceneBrush>();
            foreach (var region in _pendingDirtyRegions)
            {
                hits.Clear();
                bvh.QueryAABB(region.Expanded(DistanceEpsilon), hits);
                foreach (var b in hits) b.Dirty = true;
            }
            _pendingDirtyRegions.Clear();
        }

        int recomputed = 0;
        var neighborBuffer = new List<SceneBrush>();
        var changed = new List<SceneBrush>();
        foreach (var brush in _brushes)
        {
            if (!brush.Dirty) continue;
            Recompute(brush, bvh, neighborBuffer);
            brush.Dirty = false;
            brush.WeldDirty = true;
            brush.OutputChanged = true;
            changed.Add(brush);
            recomputed++;
        }
        LastRebuildCount = recomputed;

        // Nothing moved, the tree is the same and nothing was invalidated by hand, so the previous
        // mesh is still the answer.
        if (recomputed == 0 && _cachedOutput != null)
            return _cachedOutput;

        // A brush's welded rings absorb points from brushes it overlaps, so a change invalidates its
        // neighbours' welds too. Anything outside those bounds keeps its cached rings.
        if (WeldTJunctions)
        {
            foreach (var brush in changed)
            {
                neighborBuffer.Clear();
                bvh.QueryAABB(BrushBox(brush).Expanded(WeldTolerance), neighborBuffer);
                foreach (var n in neighborBuffer) n.WeldDirty = true;
            }
        }

        _cachedOutput = Assemble();
        return _cachedOutput;
    }

    #endregion

    #region Bookkeeping

    private SceneBrush Validate(CSGBrushHandle handle)
    {
        if (handle == null || handle.Brush == null || !handle.Brush.Attached)
            throw new InvalidOperationException("The brush handle is not valid for this scene.");
        return handle.Brush;
    }

    private void MarkTreeDirty()
    {
        _treeDirty = true;
        _evalDirty = true;
        _cachedOutput = null;
    }

    /// <summary>
    /// Force every brush to re-evaluate. Needed when a change can alter visibility without touching
    /// geometry: an operation change, or any edit while the tree contains an
    /// <see cref="CSGOperation.Intersect"/>, which removes volume arbitrarily far away.
    /// </summary>
    private void MarkEvaluationDirty()
    {
        _evalDirty = true;
        _cachedOutput = null;
    }

    /// <summary>Escalate a local edit to a full re-evaluation when locality does not hold.</summary>
    private void EscalateIfNonLocal()
    {
        if (_hasIntersect) MarkEvaluationDirty();
    }

    /// <summary>Flatten the tree into the brush cache and stamp traversal order. Order is only used to
    /// pick a deterministic owner for a shared surface; solidity comes from the tree itself.</summary>
    private void FlattenTree()
    {
        if (!_treeDirty) return;
        _treeDirty = false;

        _brushTreeStale = true;
        _brushes.Clear();
        _hasIntersect = false;
        bool hasGroups = false;
        Walk(_root);
        _flat = !hasGroups && !_hasIntersect;

        void Walk(SceneGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child.Op == CSGOperation.Intersect) _hasIntersect = true;

                if (child is SceneBrush brush)
                {
                    brush.EvalOrder = _brushes.Count;
                    _brushes.Add(brush);
                }
                else if (child is SceneGroup sub)
                {
                    hasGroups = true;
                    Walk(sub);
                }
            }
        }
    }

    private SceneGroup ResolveGroup(CSGGroupHandle? handle)
    {
        if (handle == null) return _root;
        return Validate(handle);
    }

    internal SceneGroup Validate(CSGGroupHandle handle)
    {
        if (handle == null || handle.Group == null || !handle.Group.Attached)
            throw new InvalidOperationException("The group handle is not valid for this scene.");
        return handle.Group;
    }

    private static bool IsAncestorOf(SceneGroup possibleAncestor, SceneGroup node)
    {
        for (SceneGroup? p = node; p != null; p = p.Parent)
            if (ReferenceEquals(p, possibleAncestor)) return true;
        return false;
    }

    /// <summary>Everything a subtree occupies has to be re-evaluated when the subtree changes.</summary>
    private void DirtySubtree(SceneNode node)
    {
        if (node is SceneBrush brush)
        {
            brush.Dirty = true;
            _pendingDirtyRegions.Add(BrushBox(brush));
            return;
        }
        if (node is SceneGroup group)
            foreach (var child in group.Children) DirtySubtree(child);
    }

    private static void DetachSubtree(SceneNode node)
    {
        node.Attached = false;
        if (node is SceneGroup group)
            foreach (var child in group.Children) DetachSubtree(child);
    }

    private static AABB BrushBox(SceneBrush brush) => brush.Bounds;

    #endregion

    #region World-space derivation

    private static void RecomputeWorld(SceneBrush brush)
    {
        brush.Faces.Clear();

        AABB brushBounds = default;
        bool firstBrush = true;

        // A mirroring transform turns the winding inside out, so it is read backwards
        bool mirrored = Float4x4.Determinant(brush.Transform) < 0f;

        for (int faceIndex = 0; faceIndex < brush.Shape.Faces.Count; faceIndex++)
        {
            var face = brush.Shape.Faces[faceIndex];
            if (face.VertCount < 3) continue;

            var verts = face.NeighborVertices();
            int n = verts.Count;
            var world = new Float3[n];
            var srcVerts = new GeometryData.Vertex[n];
            var srcLoops = new GeometryData.Loop?[n];

            for (int i = 0; i < n; i++)
            {
                srcVerts[i] = verts[i];
                srcLoops[i] = face.GetLoop(verts[i]);
                world[i] = Float4x4.TransformPoint(verts[i].Point, brush.Transform);
            }

            if (mirrored)
            {
                Array.Reverse(world);
                Array.Reverse(srcVerts);
                Array.Reverse(srcLoops);
            }

            // Plane and normal from the transformed winding (Newell's method).
            Float3 normal = Float3.Zero;
            Float3 centroid = Float3.Zero;
            for (int i = 0; i < n; i++)
            {
                Float3 cur = world[i];
                Float3 nxt = world[(i + 1) % n];
                normal.X += (cur.Y - nxt.Y) * (cur.Z + nxt.Z);
                normal.Y += (cur.Z - nxt.Z) * (cur.X + nxt.X);
                normal.Z += (cur.X - nxt.X) * (cur.Y + nxt.Y);
                centroid += cur;
            }
            if (Float3.LengthSquared(normal) < DegenerateEpsilon * DegenerateEpsilon) continue;

            var plane = Plane.FromNormalAndPoint(Float3.Normalize(normal), centroid / n);

            int before = brush.Faces.Count;
            if (IsFlat(world, plane) && IsConvex(world, plane.Normal))
                brush.Faces.Add(Piece(faceIndex, face, world, srcVerts, srcLoops, plane, plane));
            else
                AddTriangles(brush, faceIndex, face, world, srcVerts, srcLoops, plane);

            for (int i = before; i < brush.Faces.Count; i++)
            {
                if (firstBrush) { brushBounds = brush.Faces[i].Bounds; firstBrush = false; }
                else brushBounds = brushBounds.Encapsulating(brush.Faces[i].Bounds);
            }
        }

        brush.Bounds = brushBounds;
        brush.Convex = brush.Shape.IsClosed() && IsConvexSolid(brush);
    }

    private static FaceData Piece(int sourceIndex, GeometryData.Face face, Float3[] world, GeometryData.Vertex[] srcVerts,
                                  GeometryData.Loop?[] srcLoops, Plane plane, Plane surfacePlane)
    {
        Float3 min = world[0], max = world[0];
        for (int i = 1; i < world.Length; i++)
        {
            min = Maths.Min(min, world[i]);
            max = Maths.Max(max, world[i]);
        }

        return new FaceData
        {
            SourceIndex = sourceIndex,
            World = world,
            Plane = plane,
            SurfacePlane = surfacePlane,
            Source = face,
            SrcVerts = srcVerts,
            SrcLoops = srcLoops,
            Bounds = new AABB(min, max),
        };
    }

    /// <summary>
    /// Splits a concave or bent face into flat triangles, ear clipped in the face's fitted plane. Each keeps the face's
    /// index and fitted plane, so the pieces stay one surface with one texture projection.
    /// </summary>
    private static void AddTriangles(SceneBrush brush, int sourceIndex, GeometryData.Face face, Float3[] world,
                                     GeometryData.Vertex[] srcVerts, GeometryData.Loop?[] srcLoops, Plane surfacePlane)
    {
        PlaneBasis(surfacePlane.Normal, out Float3 u, out Float3 v);
        var flat = new Float2[world.Length];
        for (int i = 0; i < world.Length; i++) flat[i] = new Float2(Float3.Dot(world[i], u), Float3.Dot(world[i], v));

        List<int> triangles = Polygon2D.Triangulate(flat);
        for (int t = 0; t + 2 < triangles.Count; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            Float3[] corners = [world[a], world[b], world[c]];
            Float3 normal = Float3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
            if (Float3.LengthSquared(normal) < DegenerateEpsilon * DegenerateEpsilon) continue;

            var plane = Plane.FromNormalAndPoint(Float3.Normalize(normal), corners[0]);
            brush.Faces.Add(Piece(sourceIndex, face, corners, [srcVerts[a], srcVerts[b], srcVerts[c]],
                [srcLoops[a], srcLoops[b], srcLoops[c]], plane, surfacePlane));
        }
    }

    /// <summary>Two axes across a plane, so that with its normal they make a right handed frame.</summary>
    private static void PlaneBasis(Float3 normal, out Float3 u, out Float3 v)
    {
        Float3 helper = Maths.Abs(normal.Y) < 0.9f ? Float3.UnitY : Float3.UnitX;
        u = Float3.Normalize(Float3.Cross(helper, normal));
        v = Float3.Cross(normal, u);
    }

    private static bool IsFlat(Float3[] world, Plane plane)
    {
        foreach (var p in world)
            if (Maths.Abs(Float3.Dot(plane.Normal, p) - plane.D) > DegenerateEpsilon) return false;
        return true;
    }

    /// <summary>Whether a flat polygon turns the same way at every corner.</summary>
    private static bool IsConvex(Float3[] world, Float3 normal)
    {
        int n = world.Length;
        for (int i = 0; i < n; i++)
        {
            Float3 a = world[i], b = world[(i + 1) % n], c = world[(i + 2) % n];
            if (Float3.Dot(Float3.Cross(b - a, c - b), normal) < -DegenerateEpsilon) return false;
        }
        return true;
    }

    /// <summary>A closed brush is convex when every face is one flat convex piece and every corner lies behind every face.</summary>
    private static bool IsConvexSolid(SceneBrush brush)
    {
        int pieces = 0;
        var sources = new HashSet<int>();
        foreach (var face in brush.Faces) { pieces++; sources.Add(face.SourceIndex); }
        if (pieces != sources.Count) return false;

        foreach (var face in brush.Faces)
            foreach (var other in brush.Faces)
                foreach (var p in other.World)
                    if (Float3.Dot(face.Plane.Normal, p) - face.Plane.D > 1e-3f) return false;
        return true;
    }

    #endregion

    #region Per-brush surface computation

    private enum Relation { Interior, Aligned, Opposite }

    private readonly struct Rel
    {
        public readonly SceneBrush Brush;
        public readonly Relation Relation;
        public Rel(SceneBrush brush, Relation relation) { Brush = brush; Relation = relation; }
    }

    private sealed class Fragment
    {
        public List<Float3> Poly;
        public List<Rel> Rels;
        /// <summary>Its visibility is settled, so no further brush needs to cut it.</summary>
        public bool Final;
        public Fragment(List<Float3> poly, List<Rel> rels) { Poly = poly; Rels = rels; }
    }

    private static readonly Comparison<SceneBrush> s_latestFirst = (a, b) => b.EvalOrder.CompareTo(a.EvalOrder);
    private static readonly Comparison<SceneBrush> s_earliestFirst = (a, b) => a.EvalOrder.CompareTo(b.EvalOrder);

    private void Recompute(SceneBrush self, DynamicBvh<SceneBrush> bvh, List<SceneBrush> neighborBuffer)
    {
        self.Output.Clear();
        self.Discarded.Clear();
        if (!self.Emits) return;

        // Boxes are widened by the tolerance, or two faces a hair apart would never be tested against each other
        neighborBuffer.Clear();
        bvh.QueryAABB(BrushBox(self).Expanded(DistanceEpsilon), neighborBuffer);

        // In a flat scene a fragment inside a brush is decided by that brush and the ones after it,
        // so walking the neighbours latest first lets such a fragment stop early.
        neighborBuffer.Sort(_flat ? s_latestFirst : s_earliestFirst);

        var work = new List<Fragment>();
        var next = new List<Fragment>();

        foreach (var face in self.Faces)
        {
            work.Clear();
            work.Add(new Fragment(new List<Float3>(face.World), new List<Rel>()));

            foreach (var b in neighborBuffer)
            {
                if (b == self) continue;
                if (!face.Bounds.Expanded(DistanceEpsilon).Intersects(b.Bounds))
                    continue;

                next.Clear();
                foreach (var frag in work)
                {
                    if (frag.Final || !Reaches(frag.Poly, b.Bounds)) next.Add(frag);
                    else if (b.Convex) SplitByBrush(frag, b, face.Plane, self, next);
                    else SplitByConcave(frag, b, face.Plane, self, next);
                }

                (work, next) = (next, work);
            }

            int emitted = self.Output.Count;
            foreach (var frag in work)
            {
                if (frag.Poly.Count < 3) continue;
                if (Visible(frag.Rels, self, out bool flip))
                    self.Output.Add(new OutFrag { Poly = frag.Poly.ToArray(), Source = face, Flip = flip });
                else if (_keepDiscarded)
                    self.Discarded.Add(new OutFrag { Poly = frag.Poly.ToArray(), Source = face });
            }
            MergePieces(self.Output, emitted);
        }
    }

    /// <summary>
    /// Joins pieces of one face back together where cutting by a concave neighbour split more than it had to: two
    /// pieces facing the same way that share an edge become one, as long as the result stays convex. Every corner is
    /// kept, even where it is now in a straight line, as a neighbouring piece may still meet the face there.
    /// </summary>
    private static void MergePieces(List<OutFrag> output, int start)
    {
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int i = start; i < output.Count && !merged; i++)
            {
                for (int j = i + 1; j < output.Count && !merged; j++)
                {
                    OutFrag a = output[i], b = output[j];
                    if (a.Flip != b.Flip || !TryJoin(a.Poly, b.Poly, a.Source.Plane.Normal, out Float3[] joined)) continue;

                    output[i] = new OutFrag { Poly = joined, Source = a.Source, Flip = a.Flip };
                    output.RemoveAt(j);
                    merged = true;
                }
            }
        }
    }

    /// <summary>The convex polygon two convex polygons make when they share an edge, or false.</summary>
    private static bool TryJoin(Float3[] p, Float3[] q, Float3 normal, out Float3[] joined)
    {
        joined = Array.Empty<Float3>();
        for (int i = 0; i < p.Length; i++)
        {
            Float3 a = p[i], b = p[(i + 1) % p.Length];
            for (int k = 0; k < q.Length; k++)
            {
                if (!Same(q[k], b) || !Same(q[(k + 1) % q.Length], a)) continue;

                // Round the first polygon from the end of the shared edge back to its start, then the rest of the second
                var ring = new List<Float3>(p.Length + q.Length - 2);
                for (int n = 1; n <= p.Length; n++) ring.Add(p[(i + n) % p.Length]);
                for (int n = 2; n < q.Length; n++) ring.Add(q[(k + n) % q.Length]);

                if (!IsConvex(ring.ToArray(), normal) || DoublesBack(ring)) return false;
                joined = ring.ToArray();
                return true;
            }
        }
        return false;
    }

    private static bool Same(Float3 a, Float3 b) => Float3.DistanceSquared(a, b) < 1e-10f;

    /// <summary>Whether a ring turns straight back on itself anywhere, which a convexity test alone lets through.</summary>
    private static bool DoublesBack(List<Float3> ring)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            Float3 before = ring[(i + ring.Count - 1) % ring.Count], at = ring[i], after = ring[(i + 1) % ring.Count];
            Float3 into = at - before, outOf = after - at;
            if (Float3.Dot(into, outOf) < 0f && Float3.LengthSquared(Float3.Cross(into, outOf)) <= 1e-12f * Float3.LengthSquared(into) * Float3.LengthSquared(outOf)) return true;
        }
        return false;
    }

    // Whether every corner of a polygon lies on a plane. Measured at the corners themselves, because
    // two planes through the same points can still differ in their constants by rounding.
    private bool LiesOn(IReadOnlyList<Float3> poly, Plane plane)
    {
        foreach (var p in poly)
            if (Maths.Abs(Float3.Dot(plane.Normal, p) - plane.D) > DistanceEpsilon) return false;
        return true;
    }

    private bool Reaches(List<Float3> poly, AABB box)
    {
        Float3 min = poly[0], max = poly[0];
        for (int i = 1; i < poly.Count; i++)
        {
            min = Maths.Min(min, poly[i]);
            max = Maths.Max(max, poly[i]);
        }
        return min.X <= box.Max.X + DistanceEpsilon && max.X >= box.Min.X - DistanceEpsilon
            && min.Y <= box.Max.Y + DistanceEpsilon && max.Y >= box.Min.Y - DistanceEpsilon
            && min.Z <= box.Max.Z + DistanceEpsilon && max.Z >= box.Min.Z - DistanceEpsilon;
    }

    // Splits a face fragment by a neighbour brush into the piece inside/on the brush (tagged with the
    // relation) and the pieces outside it (untagged). A fragment the brush does not reach stays whole.
    private void SplitByBrush(Fragment frag, SceneBrush b, Plane facePlane, SceneBrush self, List<Fragment> results)
    {
        // Detect a brush face coplanar with this face (a shared surface).
        int coincident = -1;
        Relation coincRelation = Relation.Interior;
        for (int i = 0; i < b.Faces.Count; i++)
        {
            Plane bp = b.Faces[i].Plane;
            float dn = Float3.Dot(bp.Normal, facePlane.Normal);
            if (Maths.Abs(dn) < 1.0f - NormalEpsilon) continue;

            // Either face may be the small one whose plane is only roughly known, so the test runs both ways
            if (!LiesOn(frag.Poly, bp) && !LiesOn(b.Faces[i].World, facePlane)) continue;
            coincident = i;
            coincRelation = dn > 0 ? Relation.Aligned : Relation.Opposite;
            break;
        }

        int start = results.Count;
        var inside = frag.Poly;
        var back = new List<Float3>();
        var front = new List<Float3>();

        for (int i = 0; i < b.Faces.Count; i++)
        {
            if (i == coincident) continue;

            ClipByPlane(inside, b.Faces[i].Plane, back, front, out bool anyBack, out bool anyFront);

            if (!anyBack || back.Count < 3)
            {
                results.RemoveRange(start, results.Count - start);
                results.Add(frag);
                return;
            }

            if (!anyFront) continue;
            if (front.Count >= 3)
                results.Add(new Fragment(new List<Float3>(front), frag.Rels));
            inside = new List<Float3>(back);
        }

        // Two brushes sharing a surface would each emit it. The later one in traversal order owns it.
        if (coincident >= 0 && coincRelation == Relation.Aligned && b.EvalOrder > self.EvalOrder)
            return;

        var rels = new List<Rel>(frag.Rels) { new Rel(b, coincident >= 0 ? coincRelation : Relation.Interior) };
        var piece = new Fragment(inside, rels);
        if (_flat && coincident < 0)
        {
            if (!Visible(rels, self, out _)) return;
            piece.Final = true;
        }
        results.Add(piece);
    }

    /// <summary>
    /// Splits a face fragment by a neighbour that is not convex. The fragment is cut only along the neighbour's faces
    /// that touch its plane, and along the edges of neighbour faces lying in it, so every cell is wholly inside, wholly
    /// outside, or on the neighbour. Each cell is then classified on its own: lying on a neighbour face gives the
    /// shared surface relation, otherwise the neighbour's winding number at the cell's centre says whether it is inside.
    /// </summary>
    private void SplitByConcave(Fragment frag, SceneBrush b, Plane facePlane, SceneBrush self, List<Fragment> results)
    {
        AABB fragBox = PolyBounds(frag.Poly).Expanded(DistanceEpsilon);
        var cuts = new List<Plane>();
        var coplanar = new List<(FaceData Face, Relation Relation)>();

        foreach (var f in b.Faces)
        {
            if (!f.Bounds.Expanded(DistanceEpsilon).Intersects(fragBox)) continue;

            float dn = Float3.Dot(f.Plane.Normal, facePlane.Normal);
            if (Maths.Abs(dn) >= 1.0f - NormalEpsilon && (LiesOn(frag.Poly, f.Plane) || LiesOn(f.World, facePlane)))
            {
                coplanar.Add((f, dn > 0 ? Relation.Aligned : Relation.Opposite));

                // The face covers the fragment only within its edges, so the fragment is cut along them
                for (int i = 0; i < f.World.Length; i++)
                {
                    Float3 a = f.World[i], c = f.World[(i + 1) % f.World.Length];
                    Float3 n = Float3.Cross(c - a, facePlane.Normal);
                    if (Float3.LengthSquared(n) < DegenerateEpsilon * DegenerateEpsilon) continue;
                    n = Float3.Normalize(n);
                    cuts.Add(new Plane(n, Float3.Dot(n, a)));
                }
                continue;
            }

            if (Touches(f.World, facePlane)) cuts.Add(f.Plane);
        }

        var cells = new List<List<Float3>> { frag.Poly };
        var back = new List<Float3>();
        var front = new List<Float3>();
        foreach (var plane in cuts)
        {
            var split = new List<List<Float3>>(cells.Count + 2);
            foreach (var cell in cells)
            {
                ClipByPlane(cell, plane, back, front, out bool anyBack, out bool anyFront);
                if (anyBack && anyFront && back.Count >= 3 && front.Count >= 3)
                {
                    split.Add(new List<Float3>(back));
                    split.Add(new List<Float3>(front));
                }
                else split.Add(cell);
            }
            cells = split;
        }

        foreach (var cell in cells)
        {
            Float3 centre = Float3.Zero;
            foreach (var p in cell) centre += p;
            centre /= cell.Count;

            Relation? relation = null;
            foreach (var (f, rel) in coplanar)
                if (InsideConvex(f.World, f.Plane.Normal, centre)) { relation = rel; break; }
            if (relation == null && WindingNumber(b, centre) > 0.5f) relation = Relation.Interior;

            if (relation == null)
            {
                results.Add(new Fragment(cell, frag.Rels));
                continue;
            }

            // Two brushes sharing a surface would each emit it. The later one in traversal order owns it.
            if (relation == Relation.Aligned && b.EvalOrder > self.EvalOrder) continue;

            var rels = new List<Rel>(frag.Rels) { new Rel(b, relation.Value) };
            var piece = new Fragment(cell, rels);
            if (_flat && relation == Relation.Interior)
            {
                if (!Visible(rels, self, out _)) continue;
                piece.Final = true;
            }
            results.Add(piece);
        }
    }

    private static AABB PolyBounds(List<Float3> poly)
    {
        Float3 min = poly[0], max = poly[0];
        for (int i = 1; i < poly.Count; i++)
        {
            min = Maths.Min(min, poly[i]);
            max = Maths.Max(max, poly[i]);
        }
        return new AABB(min, max);
    }

    /// <summary>Whether a polygon reaches a plane: corners on both sides of it, or on it.</summary>
    private bool Touches(Float3[] poly, Plane plane)
    {
        bool below = false, above = false;
        foreach (var p in poly)
        {
            float d = Float3.Dot(plane.Normal, p) - plane.D;
            if (d <= DistanceEpsilon) below = true;
            if (d >= -DistanceEpsilon) above = true;
        }
        return below && above;
    }

    /// <summary>Whether a point in a convex polygon's plane lies inside it, edges included within the tolerance.</summary>
    private bool InsideConvex(Float3[] poly, Float3 normal, Float3 point)
    {
        for (int i = 0; i < poly.Length; i++)
        {
            Float3 a = poly[i], c = poly[(i + 1) % poly.Length];
            Float3 inward = Float3.Cross(normal, c - a);
            float length = Float3.Length(inward);
            if (length < DegenerateEpsilon) continue;
            if (Float3.Dot(inward / length, point - a) < -DistanceEpsilon) return false;
        }
        return true;
    }

    /// <summary>
    /// How many times a brush's surface wraps around a point: one inside a closed brush, zero outside, and in between
    /// for a brush that is not quite closed. The sum of the solid angles its faces fill seen from the point.
    /// </summary>
    private static float WindingNumber(SceneBrush brush, Float3 point)
    {
        float total = 0f;
        foreach (var face in brush.Faces)
        {
            Float3 a = face.World[0] - point;
            for (int i = 1; i + 1 < face.World.Length; i++)
                total += SolidAngle(a, face.World[i] - point, face.World[i + 1] - point);
        }
        return total / (4f * MathF.PI);
    }

    /// <summary>The signed solid angle a triangle fills seen from the origin, positive when it winds counter clockwise toward it.</summary>
    private static float SolidAngle(Float3 a, Float3 b, Float3 c)
    {
        float la = Float3.Length(a), lb = Float3.Length(b), lc = Float3.Length(c);
        float numerator = Float3.Dot(a, Float3.Cross(b, c));
        float denominator = la * lb * lc + Float3.Dot(a, b) * lc + Float3.Dot(a, c) * lb + Float3.Dot(b, c) * la;
        return 2f * MathF.Atan2(numerator, denominator);
    }

    /// <summary>
    /// Whether a fully classified fragment lies on the final solid's surface: true exactly when the
    /// volume behind the face and the volume in front disagree about being solid. Each side is
    /// evaluated against the whole tree.
    /// </summary>
    private bool Visible(List<Rel> rels, SceneBrush self, out bool flip)
    {
        flip = false;

        // Two brushes sharing a surface would each emit it, whichever way their faces point. The later one in traversal
        // order owns it. The earlier one always meets the later one before its fragment is settled, so it is the one
        // that can reliably step aside.
        foreach (var r in rels)
            if (r.Relation != Relation.Interior && r.Brush.EvalOrder > self.EvalOrder)
                return false;

        bool innerSolid = _flat ? FlatSolid(rels, self, inner: true) : EvaluateTree(_root, rels, self, inner: true);
        bool outerSolid = _flat ? FlatSolid(rels, self, inner: false) : EvaluateTree(_root, rels, self, inner: false);

        if (innerSolid == outerSolid)
            return false; // both sides solid or both empty: not a boundary

        flip = !innerSolid && outerSolid; // solid is in front: the face must point the other way

        return true;
    }

    /// <summary>The flat scene answer for a side of a fragment: the latest brush filling that side
    /// decides it. Relations are listed latest brush first.</summary>
    private static bool FlatSolid(List<Rel> rels, SceneBrush self, bool inner)
    {
        bool selfPending = inner;
        foreach (var r in rels)
        {
            if (selfPending && r.Brush.EvalOrder < self.EvalOrder)
                return self.Op == CSGOperation.Additive;

            bool fills = r.Relation switch
            {
                Relation.Interior => true,
                Relation.Aligned => inner,
                _ => !inner,
            };
            if (fills) return r.Brush.Op == CSGOperation.Additive;
        }
        return selfPending && self.Op == CSGOperation.Additive;
    }

    /// <summary>Fold a group's children into one solid/empty answer for a side of a fragment. Each
    /// child applies to the result accumulated from the children before it.</summary>
    private static bool EvaluateTree(SceneGroup group, List<Rel> rels, SceneBrush self, bool inner)
    {
        bool acc = false;
        foreach (var child in group.Children)
        {
            bool occupied = child is SceneBrush brush
                ? Occupies(brush, rels, self, inner)
                : EvaluateTree((SceneGroup)child, rels, self, inner);

            acc = child.Op switch
            {
                CSGOperation.Additive => acc || occupied,
                CSGOperation.Subtractive => acc && !occupied,
                CSGOperation.Intersect => acc && occupied,
                _ => acc,
            };
        }
        return acc;
    }

    /// <summary>Whether a brush fills the volume on one side of the fragment.</summary>
    private static bool Occupies(SceneBrush brush, List<Rel> rels, SceneBrush self, bool inner)
    {
        // The fragment came from self's own face, so self fills the side behind it and not in front.
        if (ReferenceEquals(brush, self)) return inner;

        foreach (var r in rels)
        {
            if (!ReferenceEquals(r.Brush, brush)) continue;
            return r.Relation switch
            {
                Relation.Interior => true,                 // strictly inside: fills both sides
                Relation.Aligned => inner,                 // coincident, same facing: fills behind
                Relation.Opposite => !inner,               // coincident, opposite facing: fills in front
                _ => false,
            };
        }

        // Not in the relation list: this brush does not reach the fragment at all.
        return false;
    }

    // Splits a convex polygon by a plane into the part behind it (back, inside half-space) and in
    // front of it (front, outside half-space). Points on the plane go to both.
    private void ClipByPlane(List<Float3> poly, Plane plane, List<Float3> back, List<Float3> front,
        out bool anyBack, out bool anyFront)
    {
        back.Clear();
        front.Clear();
        anyBack = anyFront = false;

        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            Float3 a = poly[i];
            Float3 b = poly[(i + 1) % n];
            float da = Float3.Dot(plane.Normal, a) - plane.D;
            float db = Float3.Dot(plane.Normal, b) - plane.D;
            int sa = da > DistanceEpsilon ? 1 : da < -DistanceEpsilon ? -1 : 0;
            int sb = db > DistanceEpsilon ? 1 : db < -DistanceEpsilon ? -1 : 0;

            if (sa <= 0) back.Add(a);
            if (sa >= 0) front.Add(a);
            anyBack |= sa < 0;
            anyFront |= sa > 0;

            if ((sa > 0 && sb < 0) || (sa < 0 && sb > 0))
            {
                float t = da / (da - db);
                Float3 p = a + (b - a) * t;
                back.Add(p);
                front.Add(p);
            }
        }
    }

    #endregion

    #region Assembly + T-junction welding

    internal sealed class EmitItem
    {
        public SceneBrush Brush = null!;
        public List<Float3> Ring = null!;
        public FaceData Source = null!;
        public bool Flip;
    }

    private GeometryData Assemble()
    {
        bool fresh = _output == null || _reassemble || _brushes.TrueForAll(b => b.WeldDirty);
        if (fresh) StartAssembly();

        var output = _output!;
        if (WeldTJunctions)
        {
            _deferRepresentativeTree = fresh;
            foreach (var brush in _brushes)
                if (brush.OutputChanged) Reference(brush);
            _deferRepresentativeTree = false;
            if (fresh) RebuildRepresentativeTree();
        }

        foreach (var brush in _brushes)
        {
            brush.OutputChanged = false;
            if (!brush.WeldDirty) continue;
            brush.WeldDirty = false;

            if (!fresh) RemoveEmitted(output, brush);
            Reweld(brush);
            foreach (var item in brush.Welded)
                EmitPolygon(output, _vertexGrid, item, _hasAttributes);
        }

        return output;
    }

    // Drops everything assembled so far and starts an empty mesh every brush emits into again.
    private void StartAssembly()
    {
        var result = new GeometryData();
        _hasAttributes = RegisterAttributes(result);

        // Written for every face regardless of what attributes the source shapes declare.
        result.AddFaceAttribute(BrushIdAttribute, GeometryData.AttributeBaseType.Int, 1);
        result.AddFaceAttribute(SourceFaceAttribute, GeometryData.AttributeBaseType.Int, 1);

        if (SurfaceUV != null && !result.HasLoopAttribute(UVAttributeName))
            result.AddLoopAttribute(UVAttributeName, GeometryData.AttributeBaseType.Float, 2);

        _output = result;
        _reassemble = false;
        _vertexGrid.Clear();
        _representatives.Clear();
        foreach (var brush in _brushes)
        {
            brush.Emitted.Clear();
            brush.References.Clear();
            brush.OutputChanged = true;
            brush.WeldDirty = true;
        }
    }

    // Points within the weld tolerance of each other become one point before anything else, the same way the
    // emitted vertices weld, so every ring agrees on where a corner is before T-junctions are filled in. Each brush
    // holds the representative of every corner of its output, and a representative lives while anything holds it.
    private void Reference(SceneBrush brush)
    {
        foreach (var held in brush.References) Release(held);
        brush.References.Clear();

        foreach (var frag in brush.Output)
            foreach (var p in frag.Poly)
                brush.References.Add(Representative(p));
    }

    private void RebuildRepresentativeTree()
    {
        var all = new List<RepPoint>();
        var boxes = new List<AABB>();
        foreach (var bucket in _representatives.Values)
            foreach (var rep in bucket)
            {
                all.Add(rep);
                boxes.Add(new AABB(rep.Point, rep.Point).Expanded(WeldTolerance));
            }

        int[] handles = _representativeTree.Rebuild(all, boxes);
        for (int i = 0; i < handles.Length; i++) all[i].TreeHandle = handles[i];
    }

    // Rebuilds a brush's rings after T-junction welding from its output and the representatives it holds.
    private void Reweld(SceneBrush brush)
    {
        brush.Welded.Clear();
        if (!WeldTJunctions)
        {
            foreach (var frag in brush.Output)
                brush.Welded.Add(new EmitItem { Brush = brush, Ring = new List<Float3>(frag.Poly), Source = frag.Source, Flip = frag.Flip });
            return;
        }

        var candidates = new List<RepPoint>();
        int held = 0;
        foreach (var frag in brush.Output)
        {
            var ring = new List<Float3>(frag.Poly.Length);
            for (int i = 0; i < frag.Poly.Length; i++)
            {
                Float3 snapped = brush.References[held++].Point;
                if (ring.Count == 0 || ring[^1] != snapped) ring.Add(snapped);
            }
            if (ring.Count > 1 && ring[0] == ring[^1]) ring.RemoveAt(ring.Count - 1);
            if (ring.Count < 3) continue;

            brush.Welded.Add(new EmitItem
            {
                Brush = brush,
                Ring = InsertCollinearPoints(ring, candidates),
                Source = frag.Source,
                Flip = frag.Flip
            });
        }
    }

    // Takes a brush's faces out of the mesh, with the edges and vertices nothing else uses any more.
    private void RemoveEmitted(GeometryData output, SceneBrush brush)
    {
        if (brush.Emitted.Count == 0) return;

        var edges = new List<GeometryData.Edge>();
        var verts = new List<GeometryData.Vertex>();
        foreach (var face in brush.Emitted)
        {
            if (face.Owner != output) continue;
            var loop = face.Loop;
            if (loop != null)
            {
                do
                {
                    edges.Add(loop.Edge);
                    verts.Add(loop.Vert);
                    loop = loop.Next;
                } while (loop != null && loop != face.Loop);
            }
            output.RemoveFace(face);
        }
        brush.Emitted.Clear();

        foreach (var edge in edges)
            if (edge.Owner == output && edge.Loop == null)
                output.RemoveEdge(edge);

        foreach (var vertex in verts)
        {
            if (vertex.Owner != output || vertex.Edge != null) continue;
            var home = VertexHome(vertex.Point);
            if (_vertexGrid.TryGetValue(home, out var bucket) && bucket.Remove(vertex) && bucket.Count == 0)
                _vertexGrid.Remove(home);
            output.RemoveVertex(vertex);
        }
    }

    // Inserts representatives lying on the interior of a ring's edges, removing the T-junctions that arise when
    // independent cuts subdivide a shared face.
    private List<Float3> InsertCollinearPoints(List<Float3> ring, List<RepPoint> candidates)
    {
        float tol2 = WeldTolerance * WeldTolerance;
        int n = ring.Count;
        var output = new List<Float3>(n);
        var inserts = new List<(float t, Float3 point)>();

        for (int i = 0; i < n; i++)
        {
            Float3 a = ring[i];
            Float3 b = ring[(i + 1) % n];
            output.Add(a);

            Float3 ab = b - a;
            float abLen2 = Float3.Dot(ab, ab);
            if (abLen2 < tol2) continue;

            candidates.Clear();
            _representativeTree.QueryAABB(new AABB(Maths.Min(a, b), Maths.Max(a, b)).Expanded(WeldTolerance), candidates);

            inserts.Clear();
            foreach (var rep in candidates)
            {
                Float3 c = rep.Point;
                if (Float3.LengthSquared(c - a) < tol2 || Float3.LengthSquared(c - b) < tol2) continue;
                float t = Float3.Dot(c - a, ab) / abLen2;
                if (t <= 0.0f || t >= 1.0f) continue;
                Float3 closest = a + ab * t;
                if (Float3.LengthSquared(c - closest) > tol2) continue;
                inserts.Add((t, c));
            }

            if (inserts.Count == 0) continue;
            inserts.Sort((x, y) => x.t.CompareTo(y.t));

            Float3 last = a;
            foreach (var (_, point) in inserts)
            {
                if (Float3.LengthSquared(point - last) < tol2) continue;
                output.Add(point);
                last = point;
            }
        }

        return output;
    }

    #endregion

    #region Emission + attribute carrying

    private void EmitPolygon(GeometryData result, Dictionary<SnapKey, List<GeometryData.Vertex>> weld, EmitItem item, bool hasAttributes)
    {
        var ring = item.Ring;
        int count = ring.Count;
        var verts = new List<GeometryData.Vertex>(count);
        var positions = new List<Float3>(count);

        for (int idx = 0; idx < count; idx++)
        {
            Float3 point = item.Flip != _invertOutput ? ring[count - 1 - idx] : ring[idx];
            var vertex = GetOrAddVertex(result, weld, point);
            if (verts.Count > 0 && verts[verts.Count - 1] == vertex) continue;
            verts.Add(vertex);
            positions.Add(point);
        }

        if (verts.Count > 1 && verts[0] == verts[verts.Count - 1])
        {
            verts.RemoveAt(verts.Count - 1);
            positions.RemoveAt(positions.Count - 1);
        }

        if (verts.Count < 3)
            return;

        var face = result.AddFace(verts.ToArray());
        if (face == null)
            return;
        item.Brush.Emitted.Add(face);

        face.Attributes[BrushIdAttribute] = new GeometryData.IntAttributeValue(item.Brush.Id);
        face.Attributes[SourceFaceAttribute] = new GeometryData.IntAttributeValue(item.Source.SourceIndex);

        var source = item.Source;
        var shape = item.Brush.Shape;

        // A surface with its own texture space projects rather than interpolates, so its UVs do not
        // drift when CSG re-cuts the surface into different fragments.
        bool projectUV = false;
        SurfaceTexSpace texSpace = default;
        if (SurfaceUV != null)
        {
            Plane plane = source.SurfacePlane;
            projectUV = SurfaceUV(item.Brush.Id, source.SourceIndex, in plane, out texSpace);
        }

        if (projectUV)
        {
            for (int i = 0; i < verts.Count; i++)
            {
                var loop = face.GetLoop(verts[i]);
                if (loop == null) continue;
                Float2 uv = texSpace.Project(positions[i]);
                if (_invertOutput) uv = new Float2(-uv.X, uv.Y);
                loop.Attributes[UVAttributeName] = new GeometryData.FloatAttributeValue(uv.X, uv.Y);
            }
        }

        if (!hasAttributes)
            return;


        foreach (var def in result.FaceAttributes)
        {
            if (shape.HasFaceAttribute(def.Name) && source.Source.Attributes.TryGetValue(def.Name, out var value))
                face.Attributes[def.Name] = GeometryData.AttributeValue.Copy(value);
        }

        bool anyLoop = result.LoopAttributes.Count > 0;
        bool anyVertex = result.VertexAttributes.Count > 0;
        if (anyLoop || anyVertex)
        {
            var weights = new float[source.World.Length];
            for (int i = 0; i < verts.Count; i++)
            {
                if (!Intersection.PolygonMeanValueWeights(positions[i], source.World, weights))
                    continue;

                if (anyLoop)
                {
                    var loop = face.GetLoop(verts[i]);
                    if (loop != null)
                    {
                        foreach (var def in result.LoopAttributes)
                        {
                            if (projectUV && def.Name == UVAttributeName) continue; // already projected
                            if (!shape.HasLoopAttribute(def.Name)) continue;
                            loop.Attributes[def.Name] = InterpolateLoop(def, source, weights);
                        }
                    }
                }

                if (anyVertex)
                {
                    foreach (var def in result.VertexAttributes)
                    {
                        if (!shape.HasVertexAttribute(def.Name)) continue;
                        verts[i].Attributes[def.Name] = InterpolateVertex(def, source, weights);
                    }
                }
            }
        }

        if (result.EdgeAttributes.Count > 0)
            CarryEdges(result, shape, source, verts, positions);
    }

    private void CarryEdges(GeometryData result, GeometryData shape, FaceData source,
        List<GeometryData.Vertex> verts, List<Float3> positions)
    {
        int n = verts.Count;
        int sn = source.World.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            for (int k = 0; k < sn; k++)
            {
                Float3 v0 = source.World[k];
                Float3 v1 = source.World[(k + 1) % sn];
                if (Intersection.DistanceSqPointToLineSegment(v0, v1, positions[i]) >= WeldTolerance * WeldTolerance) continue;
                if (Intersection.DistanceSqPointToLineSegment(v0, v1, positions[j]) >= WeldTolerance * WeldTolerance) continue;

                var sourceEdge = source.SrcLoops[k]?.Edge;
                if (sourceEdge == null) break;

                var edge = result.FindEdge(verts[i], verts[j]);
                if (edge == null) break;

                foreach (var def in result.EdgeAttributes)
                {
                    if (shape.HasEdgeAttribute(def.Name) && sourceEdge.Attributes.TryGetValue(def.Name, out var value))
                        edge.Attributes[def.Name] = GeometryData.AttributeValue.Copy(value);
                }
                break;
            }
        }
    }

    private bool RegisterAttributes(GeometryData result)
    {
        foreach (var brush in _brushes)
        {
            var mesh = brush.Shape;
            foreach (var def in mesh.VertexAttributes) result.AddVertexAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
            foreach (var def in mesh.EdgeAttributes) result.AddEdgeAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
            foreach (var def in mesh.LoopAttributes) result.AddLoopAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
            foreach (var def in mesh.FaceAttributes) result.AddFaceAttribute(def.Name, def.Type.BaseType, def.Type.Dimensions);
        }

        return result.VertexAttributes.Count > 0 || result.EdgeAttributes.Count > 0 ||
               result.LoopAttributes.Count > 0 || result.FaceAttributes.Count > 0;
    }

    private static GeometryData.AttributeValue InterpolateLoop(GeometryData.AttributeDefinition def, FaceData source, float[] weights)
    {
        int n = source.World.Length;
        var perCorner = new GeometryData.AttributeValue?[n];
        for (int k = 0; k < n; k++)
            if (source.SrcLoops[k] is { } loop && loop.Attributes.TryGetValue(def.Name, out var value))
                perCorner[k] = value;
        return Interpolate(def, perCorner, weights);
    }

    private static GeometryData.AttributeValue InterpolateVertex(GeometryData.AttributeDefinition def, FaceData source, float[] weights)
    {
        int n = source.World.Length;
        var perCorner = new GeometryData.AttributeValue?[n];
        for (int k = 0; k < n; k++)
            if (source.SrcVerts[k].Attributes.TryGetValue(def.Name, out var value))
                perCorner[k] = value;
        return Interpolate(def, perCorner, weights);
    }

    private static GeometryData.AttributeValue Interpolate(GeometryData.AttributeDefinition def,
        GeometryData.AttributeValue?[] perCorner, float[] weights)
    {
        int dims = def.Type.Dimensions;
        var acc = new float[dims];
        for (int k = 0; k < perCorner.Length; k++)
        {
            float w = weights[k];
            switch (perCorner[k])
            {
                case GeometryData.FloatAttributeValue fv:
                    for (int d = 0; d < dims && d < fv.Data.Length; d++) acc[d] += w * fv.Data[d];
                    break;
                case GeometryData.IntAttributeValue iv:
                    for (int d = 0; d < dims && d < iv.Data.Length; d++) acc[d] += w * iv.Data[d];
                    break;
            }
        }

        if (def.Type.BaseType == GeometryData.AttributeBaseType.Float)
            return new GeometryData.FloatAttributeValue { Data = acc };

        var rounded = new int[dims];
        for (int d = 0; d < dims; d++) rounded[d] = (int)MathF.Round(acc[d]);
        return new GeometryData.IntAttributeValue { Data = rounded };
    }

    /// <summary>
    /// The representative already standing within the weld tolerance of this point, or a new one for it, now standing
    /// for every later point that close. Held once more either way. Searched by grid as <see cref="GetOrAddVertex"/> does.
    /// </summary>
    private RepPoint Representative(Float3 point)
    {
        float cell = WeldTolerance * 4;
        int x0 = (int)MathF.Floor((point.X - WeldTolerance) / cell), x1 = (int)MathF.Floor((point.X + WeldTolerance) / cell);
        int y0 = (int)MathF.Floor((point.Y - WeldTolerance) / cell), y1 = (int)MathF.Floor((point.Y + WeldTolerance) / cell);
        int z0 = (int)MathF.Floor((point.Z - WeldTolerance) / cell), z1 = (int)MathF.Floor((point.Z + WeldTolerance) / cell);

        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!_representatives.TryGetValue(new SnapKey(x, y, z), out var bucket)) continue;
                    foreach (var candidate in bucket)
                    {
                        Float3 d = candidate.Point - point;
                        if (MathF.Abs(d.X) <= WeldTolerance && MathF.Abs(d.Y) <= WeldTolerance && MathF.Abs(d.Z) <= WeldTolerance)
                        {
                            candidate.Count++;
                            return candidate;
                        }
                    }
                }

        var rep = new RepPoint { Point = point, Home = VertexHome(point), Count = 1 };
        if (!_representatives.TryGetValue(rep.Home, out var list)) _representatives[rep.Home] = list = new List<RepPoint>(1);
        list.Add(rep);
        if (!_deferRepresentativeTree) rep.TreeHandle = _representativeTree.Add(rep, new AABB(point, point).Expanded(WeldTolerance));
        return rep;
    }

    private void Release(RepPoint rep)
    {
        if (--rep.Count > 0) return;
        if (_representatives.TryGetValue(rep.Home, out var bucket) && bucket.Remove(rep) && bucket.Count == 0)
            _representatives.Remove(rep.Home);
        _representativeTree.Remove(rep.TreeHandle);
    }

    private SnapKey VertexHome(Float3 point)
    {
        float cell = WeldTolerance * 4;
        return new SnapKey((int)MathF.Floor(point.X / cell), (int)MathF.Floor(point.Y / cell), (int)MathF.Floor(point.Z / cell));
    }

    internal sealed class RepPoint
    {
        public Float3 Point;
        public SnapKey Home;
        public int Count;
        public int TreeHandle;
    }

    // Finds the vertex already standing within the weld tolerance of a point, or adds one. Vertices are
    // kept in a grid, and every cell the tolerance reaches is searched, so two points a hair apart
    // still weld when a cell border runs between them.
    private GeometryData.Vertex GetOrAddVertex(GeometryData result,
        Dictionary<SnapKey, List<GeometryData.Vertex>> weld, Float3 point)
    {
        float cell = WeldTolerance * 4;
        int x0 = (int)MathF.Floor((point.X - WeldTolerance) / cell), x1 = (int)MathF.Floor((point.X + WeldTolerance) / cell);
        int y0 = (int)MathF.Floor((point.Y - WeldTolerance) / cell), y1 = (int)MathF.Floor((point.Y + WeldTolerance) / cell);
        int z0 = (int)MathF.Floor((point.Z - WeldTolerance) / cell), z1 = (int)MathF.Floor((point.Z + WeldTolerance) / cell);

        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!weld.TryGetValue(new SnapKey(x, y, z), out var bucket)) continue;
                    foreach (var candidate in bucket)
                    {
                        Float3 d = candidate.Point - point;
                        if (MathF.Abs(d.X) <= WeldTolerance && MathF.Abs(d.Y) <= WeldTolerance && MathF.Abs(d.Z) <= WeldTolerance)
                            return candidate;
                    }
                }

        var vertex = result.AddVertex(point);
        var home = VertexHome(point);
        if (!weld.TryGetValue(home, out var list)) weld[home] = list = new List<GeometryData.Vertex>(1);
        list.Add(vertex);
        return vertex;
    }

    internal readonly struct SnapKey : IEquatable<SnapKey>
    {
        private readonly int _x, _y, _z;
        public SnapKey(int x, int y, int z) { _x = x; _y = y; _z = z; }

        public SnapKey(Float3 point, float tolerance)
        {
            _x = (int)MathF.Round(point.X / tolerance);
            _y = (int)MathF.Round(point.Y / tolerance);
            _z = (int)MathF.Round(point.Z / tolerance);
        }
        public bool Equals(SnapKey other) => _x == other._x && _y == other._y && _z == other._z;
        public override bool Equals(object? obj) => obj is SnapKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_x, _y, _z);
    }

    #endregion

    #region State

    internal sealed class FaceData
    {
        public int SourceIndex = -1;
        public Float3[] World = Array.Empty<Float3>();
        public Plane Plane;

        /// <summary>The fitted plane of the whole source face, which a concave or bent face's pieces share for texturing.</summary>
        public Plane SurfacePlane;
        public GeometryData.Face Source = null!;
        public GeometryData.Vertex[] SrcVerts = Array.Empty<GeometryData.Vertex>();
        public GeometryData.Loop?[] SrcLoops = Array.Empty<GeometryData.Loop?>();
        public AABB Bounds;
    }

    internal sealed class OutFrag
    {
        public Float3[] Poly = Array.Empty<Float3>();
        public FaceData Source = null!;
        public bool Flip;
    }

    /// <summary><see cref="Op"/> is how this node folds into its parent group's running result.</summary>
    internal abstract class SceneNode
    {
        public CSGOperation Op;
        public SceneGroup? Parent;
        public bool Attached;
    }

    internal sealed class SceneGroup : SceneNode
    {
        public readonly List<SceneNode> Children = new();
    }

    internal sealed class SceneBrush : SceneNode
    {
        public int Id;
        /// <summary>Position in a whole-tree traversal. Picks a deterministic owner when two brushes
        /// share a surface, so it is not emitted twice.</summary>
        public int EvalOrder;
        public GeometryData Shape = null!;
        public Float4x4 Transform;
        public bool Dirty;
        public bool Emits = true;

        /// <summary>Every face one flat convex piece with all corners behind it, so neighbours can clip by its planes.</summary>
        public bool Convex = true;

        public readonly List<FaceData> Faces = new();
        public AABB Bounds;
        public readonly List<OutFrag> Output = new();
        public readonly List<OutFrag> Discarded = new();

        /// <summary>Rings after T-junction welding. Cached because welding a brush's rings only
        /// depends on points from brushes it overlaps, so an untouched brush keeps its result.</summary>
        public readonly List<EmitItem> Welded = new();
        public bool WeldDirty = true;

        /// <summary>Faces this brush put in the assembled mesh, taken out again when it is re-welded.</summary>
        public readonly List<GeometryData.Face> Emitted = new();

        /// <summary>The representative of every corner of <see cref="Output"/>, in order.</summary>
        public readonly List<RepPoint> References = new();
        public bool OutputChanged = true;

        /// <summary>Handle in the scene's brush tree.</summary>
        public int TreeHandle = -1;
    }

    #endregion
}

/// <summary>A face piece a <see cref="CSGScene"/> build removed: the brush and source face it came from, its corners and the face's plane, in scene space.</summary>
public readonly record struct CSGDiscardedFace(int BrushId, int SourceFaceIndex, Float3[] Corners, Plane Plane);
