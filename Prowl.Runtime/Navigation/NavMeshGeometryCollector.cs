// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Walks a scene and gathers world-space triangles from <see cref="MeshRenderer"/> meshes and the
/// simple physics colliders that have real, walkable-surface-shaped geometry (box, mesh) on
/// GameObjects matching a layer mask. Colliders whose shape recast has no direct use for on a walkable
/// surface (sphere, capsule, cylinder, cone, wheel) are not collected in v1; add them here if a
/// concrete surface needs one.
/// <para/>
/// Also resolves <see cref="NavMeshModifier"/> (per-object area override / bake exclusion) and
/// <see cref="NavMeshModifierVolume"/> (area override by world region) for the given agent type, into
/// <see cref="NavMeshBuildInput.AreaVolumes"/> - Recast's own tiled pipeline paints areas as convex
/// volumes stamped onto the baked heightfield, not per input triangle, so that is what a bake actually
/// receives now (see <see cref="NavMeshAreaVolumeInput"/>). A per-object <see cref="NavMeshModifier.OverrideArea"/>
/// becomes a volume covering that object's own world-space bounds - an approximation for a
/// non-box-shaped mesh, but the same one <see cref="NavMeshModifierVolume"/> itself already made.
/// <see cref="NavMeshObstacle"/> is deliberately never collected here - see its own doc comment.
/// </summary>
public static class NavMeshGeometryCollector
{
    /// <summary>Sign pattern for each of a box's 8 corners, scaled by its half-extents to get the
    /// corner's local position. Used by <see cref="CollectBox"/>.</summary>
    private static readonly Float3[] s_boxCornerSigns =
    [
        new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
        new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1),
    ];

    /// <summary>Two triangles per face, indexing into <see cref="s_boxCornerSigns"/>'s corner order.
    /// Used by <see cref="CollectBox"/>.</summary>
    private static readonly int[] s_boxTriangles =
    [
        0, 1, 2, 0, 2, 3, // -Z
        5, 4, 7, 5, 7, 6, // +Z
        4, 0, 3, 4, 3, 7, // -X
        1, 5, 6, 1, 6, 2, // +X
        3, 2, 6, 3, 6, 7, // +Y
        4, 5, 1, 4, 1, 0, // -Y
    ];

    /// <summary>
    /// Collects every matching renderer/collider in <paramref name="scene"/> into one triangle soup,
    /// with area-painting volumes resolved for <paramref name="agentTypeId"/>.
    /// <paramref name="layerMask"/> is read through its public inclusion semantics
    /// (<see cref="LayerMask.HasLayer"/>) even though the mask stores exclusions internally - callers
    /// never need to know that. Anything on or under a <see cref="NavMeshAgent"/> is skipped entirely:
    /// an agent walks the navmesh rather than forming it, so baking its own collider or renderer would
    /// stamp a permanent hole or step wherever it happened to be standing at bake time.
    /// </summary>
    public static NavMeshBuildInput Collect(Scene scene, LayerMask layerMask, int agentTypeId = NavMeshAgentTypes.HumanoidId) =>
        Collect(scene, layerMask, agentTypeId, default);

    /// <summary>Overload of <see cref="Collect(Scene, LayerMask, int)"/> narrowed by
    /// <paramref name="scope"/> - which objects are eligible (<see cref="NavMeshCollectionScope.CollectObjects"/>)
    /// and which of an object's own geometry sources count (<see cref="NavMeshCollectionScope.UseGeometry"/>).
    /// A default-valued <paramref name="scope"/> reproduces <see cref="Collect(Scene, LayerMask, int)"/>'s
    /// own behavior exactly - see <see cref="NavMeshCollectionScope"/>'s own doc comment.</summary>
    public static NavMeshBuildInput Collect(Scene scene, LayerMask layerMask, int agentTypeId, NavMeshCollectionScope scope)
    {
        List<Float3> triangles = [];
        List<NavMeshAreaVolumeInput> areaVolumes = [];

        (Float3 volMin, Float3 volMax) = scope.CollectObjects == NavMeshCollectObjects.Volume
            ? ComputeVolumeWorldBounds(scope)
            : default;

        foreach (GameObject go in scene.AllObjects)
        {
            if (go.IsDisposed || !go.EnabledInHierarchy) continue;
            if (!layerMask.HasLayer(go.LayerIndex)) continue;
            if (go.GetComponentInParent<NavMeshAgent>() != null) continue;
            if (scope.CollectObjects == NavMeshCollectObjects.Children && !IsSelfOrDescendantOf(go, scope.Root)) continue;

            (bool ignore, int? overrideArea) = ResolveModifier(go, agentTypeId);
            if (ignore) continue;

            int triangleStart = triangles.Count;

            if (scope.UseGeometry != NavMeshGeometrySource.PhysicsColliders && go.TryGetComponent(out MeshRenderer renderer))
                CollectMesh(renderer.Mesh.Res, go.Transform.LocalToWorldMatrix, triangles);

            if (scope.UseGeometry != NavMeshGeometrySource.RenderMeshes)
            {
                if (go.TryGetComponent(out BoxCollider box))
                    CollectBox(box, triangles);

                if (go.TryGetComponent(out MeshCollider meshCollider))
                    CollectMesh(meshCollider.Mesh.Res, ColliderWorldMatrix(meshCollider), triangles);
            }

            if (scope.CollectObjects == NavMeshCollectObjects.Volume && !AnyVertexInside(triangles, triangleStart, volMin, volMax))
            {
                triangles.RemoveRange(triangleStart, triangles.Count - triangleStart);
                continue;
            }

            if (overrideArea != null && triangles.Count > triangleStart)
                areaVolumes.Add(BoundsVolumeFor(triangles, triangleStart, overrideArea.Value));
        }

        CollectModifierVolumes(scene, agentTypeId, areaVolumes);

        return new NavMeshBuildInput { Triangles = triangles.ToArray(), AreaVolumes = areaVolumes };
    }

    /// <summary>Overload of <see cref="Collect(Scene, LayerMask, int, NavMeshCollectionScope)"/> that
    /// reads its scope directly from <paramref name="surface"/>'s own <see cref="NavMeshSurface.CollectObjects"/>/
    /// <see cref="NavMeshSurface.UseGeometry"/>/<see cref="NavMeshSurface.Center"/>/<see cref="NavMeshSurface.Size"/> -
    /// the way a surface actually bakes itself, as opposed to the raw overloads above (kept for callers,
    /// such as most of this file's own tests, that want an unscoped collection independent of any
    /// particular surface's own settings).</summary>
    public static NavMeshBuildInput Collect(Scene scene, NavMeshSurface surface) =>
        Collect(scene, surface.LayerMask, surface.AgentTypeId, new NavMeshCollectionScope
        {
            CollectObjects = surface.CollectObjects,
            UseGeometry = surface.UseGeometry,
            Root = surface.GameObject,
            VolumeTransform = surface.Transform,
            VolumeCenter = surface.Center,
            VolumeSize = surface.Size,
        });

    /// <summary>Whether <paramref name="go"/> is <paramref name="root"/> itself or one of its
    /// descendants - <see cref="NavMeshCollectObjects.Children"/>'s own eligibility test.</summary>
    private static bool IsSelfOrDescendantOf(GameObject go, GameObject? root)
    {
        if (root == null) return false;

        GameObject? current = go;
        while (current != null)
        {
            if (current == root) return true;
            current = current.Transform.Parent?.GameObject;
        }
        return false;
    }

    /// <summary><see cref="NavMeshCollectionScope.VolumeCenter"/>/<see cref="NavMeshCollectionScope.VolumeSize"/>'s
    /// world-space AABB - the same 8-corner transform <see cref="CollectModifierVolumes"/> already uses,
    /// so a tilted volume gets the same documented (exact for yaw-only, approximate otherwise) treatment.</summary>
    private static (Float3 Min, Float3 Max) ComputeVolumeWorldBounds(NavMeshCollectionScope scope)
    {
        Transform? t = scope.VolumeTransform;
        Float4x4 world = t != null
            ? Float4x4.CreateTRS(t.Position, t.Rotation, t.LossyScale)
            : Float4x4.Identity;
        Float3 half = scope.VolumeSize * 0.5f;

        Float3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Float3 max = new(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Float3 sign = new((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
            Float3 corner = Float4x4.TransformPoint(scope.VolumeCenter + sign * half, world);
            min = new Float3(Maths.Min(min.X, corner.X), Maths.Min(min.Y, corner.Y), Maths.Min(min.Z, corner.Z));
            max = new Float3(Maths.Max(max.X, corner.X), Maths.Max(max.Y, corner.Y), Maths.Max(max.Z, corner.Z));
        }
        return (min, max);
    }

    /// <summary>Whether any of <paramref name="triangles"/>[<paramref name="startIndex"/>..] falls
    /// within the world-space AABB [<paramref name="min"/>, <paramref name="max"/>] - an object
    /// contributes to a <see cref="NavMeshCollectObjects.Volume"/> bake if any part of its geometry
    /// does, not only one whose geometry lies entirely inside.</summary>
    private static bool AnyVertexInside(List<Float3> triangles, int startIndex, Float3 min, Float3 max)
    {
        for (int i = startIndex; i < triangles.Count; i++)
        {
            Float3 v = triangles[i];
            if (v.X >= min.X && v.X <= max.X && v.Y >= min.Y && v.Y <= max.Y && v.Z >= min.Z && v.Z <= max.Z)
                return true;
        }
        return false;
    }

    /// <summary>A world-space box volume exactly covering the AABB of triangles
    /// <paramref name="triangles"/>[<paramref name="startIndex"/>..] - the per-object
    /// <see cref="NavMeshModifier.OverrideArea"/> case, where there is no user-authored volume shape to
    /// use directly.</summary>
    private static NavMeshAreaVolumeInput BoundsVolumeFor(List<Float3> triangles, int startIndex, int area)
    {
        Float3 min = triangles[startIndex];
        Float3 max = triangles[startIndex];
        for (int i = startIndex; i < triangles.Count; i++)
        {
            Float3 v = triangles[i];
            min = new Float3(Maths.Min(min.X, v.X), Maths.Min(min.Y, v.Y), Maths.Min(min.Z, v.Z));
            max = new Float3(Maths.Max(max.X, v.X), Maths.Max(max.Y, v.Y), Maths.Max(max.Z, v.Z));
        }

        Float3[] footprint = [new(min.X, 0, min.Z), new(max.X, 0, min.Z), new(max.X, 0, max.Z), new(min.X, 0, max.Z)];
        return new NavMeshAreaVolumeInput(footprint, min.Y, max.Y, area);
    }

    /// <summary>Own modifier always wins when it applies to this agent type; otherwise the nearest
    /// ancestor whose modifier applies to children (and to this agent type) does. A modifier that is
    /// disabled or scoped to a different agent type is transparent - resolution continues past it to
    /// the next ancestor up, exactly as if it were not there.</summary>
    private static (bool ignore, int? overrideArea) ResolveModifier(GameObject go, int agentTypeId)
    {
        if (go.TryGetComponent(out NavMeshModifier own) && own.Enabled && own.AppliesTo(agentTypeId))
            return (own.IgnoreFromBuild, own.OverrideArea ? own.Area : null);

        Transform? ancestor = go.Transform.Parent;
        while (ancestor != null)
        {
            GameObject ancestorGo = ancestor.GameObject;
            if (ancestorGo.TryGetComponent(out NavMeshModifier mod) && mod.Enabled
                && mod.ApplyToChildren && mod.AppliesTo(agentTypeId))
                return (mod.IgnoreFromBuild, mod.OverrideArea ? mod.Area : null);

            ancestor = ancestor.Parent;
        }

        return (false, null);
    }

    /// <summary>Appends a <see cref="NavMeshAreaVolumeInput"/> for every <see cref="NavMeshModifierVolume"/>
    /// that applies to <paramref name="agentTypeId"/> - its own oriented box, honoring rotation and
    /// scale, not just an axis-aligned approximation.</summary>
    private static void CollectModifierVolumes(Scene scene, int agentTypeId, List<NavMeshAreaVolumeInput> areaVolumes)
    {
        foreach (GameObject go in scene.AllObjects)
        {
            if (go.IsDisposed || !go.EnabledInHierarchy) continue;
            if (!go.TryGetComponent(out NavMeshModifierVolume volume) || !volume.AppliesTo(agentTypeId)) continue;

            Float4x4 world = Float4x4.CreateTRS(go.Transform.Position, go.Transform.Rotation, go.Transform.LossyScale);
            Float3 half = volume.Size * 0.5f;

            // The volume's own 8 corners in world space - min/max Y become the prism's height range,
            // and the 4 XZ corners of the corner whose Y is closest to each extreme become its
            // footprint. This is exact for a Y-axis (yaw-only) rotation, the overwhelmingly common
            // case, and a reasonable, documented approximation for one tilted around X/Z too.
            Float3 minCorner = new(float.MaxValue, float.MaxValue, float.MaxValue);
            Float3 maxCorner = new(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Float3 sign = new((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                Float3 corner = Float4x4.TransformPoint(volume.Center + sign * half, world);
                minCorner = new Float3(Maths.Min(minCorner.X, corner.X), Maths.Min(minCorner.Y, corner.Y), Maths.Min(minCorner.Z, corner.Z));
                maxCorner = new Float3(Maths.Max(maxCorner.X, corner.X), Maths.Max(maxCorner.Y, corner.Y), Maths.Max(maxCorner.Z, corner.Z));
            }

            Float3[] footprint =
            [
                new(minCorner.X, 0, minCorner.Z), new(maxCorner.X, 0, minCorner.Z),
                new(maxCorner.X, 0, maxCorner.Z), new(minCorner.X, 0, maxCorner.Z),
            ];
            areaVolumes.Add(new NavMeshAreaVolumeInput(footprint, minCorner.Y, maxCorner.Y, volume.Area));
        }
    }

    /// <summary>Appends every triangle of <paramref name="mesh"/>, transformed into world space by
    /// <paramref name="worldMatrix"/>.</summary>
    private static void CollectMesh(Mesh? mesh, Float4x4 worldMatrix, List<Float3> triangles)
    {
        if (mesh == null) return;

        Float3[] verts = mesh.Vertices;
        uint[] indices = mesh.Indices;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            triangles.Add(Float4x4.TransformPoint(verts[indices[i]], worldMatrix));
            triangles.Add(Float4x4.TransformPoint(verts[indices[i + 1]], worldMatrix));
            triangles.Add(Float4x4.TransformPoint(verts[indices[i + 2]], worldMatrix));
        }
    }

    // Collider.Center/Rotation are the shape's offset from the GameObject's own transform. Mirrors
    // Collider.GizmoMatrix (protected, so not reusable directly) so a box collider's navmesh geometry
    // lines up with the box the physics system and the gizmo both actually use.
    /// <summary>World matrix for a collider's shape, honoring its own local center/rotation offset
    /// from the GameObject's transform.</summary>
    private static Float4x4 ColliderWorldMatrix(Collider collider)
    {
        Transform t = collider.Transform;
        return Float4x4.CreateTRS(
            Float4x4.TransformPoint(collider.Center, Float4x4.CreateTRS(t.Position, t.Rotation, t.LossyScale)),
            t.Rotation * Quaternion.FromEuler(collider.Rotation),
            t.LossyScale);
    }

    /// <summary>Appends <paramref name="box"/>'s six faces as twelve triangles.</summary>
    private static void CollectBox(BoxCollider box, List<Float3> triangles)
    {
        Float3 half = box.Size * 0.5f;
        Float4x4 world = ColliderWorldMatrix(box);

        Float3[] corners = new Float3[8];
        for (int i = 0; i < 8; i++)
            corners[i] = Float4x4.TransformPoint(s_boxCornerSigns[i] * half, world);

        for (int i = 0; i < s_boxTriangles.Length; i += 3)
        {
            triangles.Add(corners[s_boxTriangles[i]]);
            triangles.Add(corners[s_boxTriangles[i + 1]]);
            triangles.Add(corners[s_boxTriangles[i + 2]]);
        }
    }
}
