// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>Which scene representation a navmesh bake voxelizes.</summary>
public enum NavMeshCollectGeometry
{
    /// <summary>Use the visible render meshes (MeshRenderer). What you see is what you walk on.</summary>
    RenderMeshes,
    /// <summary>Use the physics colliders. Cheaper and usually simpler geometry; what physics
    /// collides with is what agents walk on.</summary>
    PhysicsColliders,
}

/// <summary>
/// Gathers bake geometry from scene objects into <see cref="NavMeshGeometrySource"/> chunks.
/// Runs on the main thread (it touches Transforms, meshes, and terrain data); the resulting
/// sources are self-contained and safe to hand to a background <see cref="NavMeshBuilder"/> run.
/// </summary>
public static class NavMeshGeometryCollector
{
    /// <summary>
    /// Collect geometry from a set of GameObjects (renderers or colliders per
    /// <paramref name="geometry"/>, plus terrain either way).
    /// </summary>
    /// <param name="objects">Objects to consider; disabled ones, and anything on or under a
    /// <see cref="NavMeshAgent"/> or <see cref="NavMeshObstacle"/>, are skipped.</param>
    /// <param name="geometry">Scene representation to voxelize.</param>
    /// <param name="layers">Only objects on these layers contribute.</param>
    /// <param name="voxelSize">Bake voxel size, used to decimate terrain sampling.</param>
    /// <param name="defaultArea">Area recorded on collected sources.</param>
    /// <param name="results">Receives the collected sources.</param>
    /// <param name="bounds">Optional world-space filter: objects whose (conservatively
    /// transformed) local bounds miss it are skipped before any vertex work, so partial
    /// rebuilds don't pay whole-scene collection.</param>
    /// <param name="agentTypeId">The bake's agent type, used to decide which
    /// <see cref="NavMeshModifier"/>s apply.</param>
    public static void Collect(IEnumerable<GameObject> objects, NavMeshCollectGeometry geometry, LayerMask layers,
        float voxelSize, int defaultArea, List<NavMeshGeometrySource> results, AABB? bounds = null, int agentTypeId = 0)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(results);

        // Modifier inheritance is resolved per object with the ancestor walks memoized here,
        // so deep hierarchies stay O(objects) per collection.
        var modifierCache = new Dictionary<GameObject, NavMeshModifier?>();
        var actorCache = new Dictionary<GameObject, bool>();

        foreach (GameObject go in objects)
        {
            if (go.IsNotValid() || !go.EnabledInHierarchy) continue;
            if (!layers.HasLayer(go.LayerIndex)) continue;
            if (BelongsToActor(go, actorCache)) continue;

            // Known cost center: this runs a GetComponent per in-scope object BEFORE the
            // per-component bounds rejection, so bounds-filtered rebuilds over large scenes
            // pay it for objects that contribute nothing. If it ever shows in a profile,
            // resolve lazily on the first collectible component that survives the bounds test.
            NavMeshModifier? modifier = ResolveModifier(go, agentTypeId, modifierCache);
            if (modifier != null && modifier.IgnoreFromBuild) continue;
            int area = modifier != null && modifier.OverrideArea ? modifier.Area : defaultArea;

            if (geometry == NavMeshCollectGeometry.RenderMeshes)
            {
                foreach (MeshRenderer renderer in go.GetComponents<MeshRenderer>())
                    CollectMeshRenderer(renderer, area, results, bounds);
            }
            else
            {
                foreach (Collider collider in go.GetComponents<Collider>())
                    CollectCollider(collider, area, results, bounds);
            }

            // Terrain contributes in both modes: its render surface and heightfield collider
            // are the same heightmap.
            foreach (TerrainCollider terrain in go.GetComponents<TerrainCollider>())
                CollectTerrain(terrain, voxelSize, area, results, bounds);
        }
    }

    /// <summary>
    /// True when this object moves on the navmesh rather than forming it — an agent or an
    /// obstacle — or sits under one. Baking such an object's collider or renderer stamps a hole
    /// into the mesh wherever it happened to be at bake time, and that hole never moves again;
    /// both components block agents at runtime instead, wherever the object actually is. Whole
    /// subtrees are excluded because visuals and colliders usually hang off child objects.
    /// Memoized like the modifier walk.
    /// </summary>
    private static bool BelongsToActor(GameObject go, Dictionary<GameObject, bool> cache)
    {
        if (cache.TryGetValue(go, out bool cached)) return cached;

        GameObject? parent = go.Parent;
        // Both exclude by PRESENCE, never by enabled state, so bake output can't depend on when
        // a component was last toggled: an obstacle disabled at bake time would otherwise
        // voxelize a hole that stays put forever once it is enabled and moves — the same frozen
        // hole this rule exists to prevent, just harder to spot. The cost of the stricter rule
        // is that an obstacle disabled for the whole session leaves its object out of the mesh;
        // an object meant to be permanent geometry should not carry the component at all.
        bool result = go.GetComponent<NavMeshAgent>().IsValid()
            || go.GetComponent<NavMeshObstacle>().IsValid()
            || (parent.IsValid() && BelongsToActor(parent!, cache));

        cache[go] = result;
        return result;
    }

    /// <summary>
    /// The modifier governing an object's bake contribution: its own (an object's modifier
    /// always wins, whether or not it applies to children), else the nearest ancestor whose
    /// modifier has <see cref="NavMeshModifier.ApplyToChildren"/> on. Modifiers that are
    /// disabled or don't affect this bake's agent type are transparent — the walk continues
    /// past them rather than shielding higher ancestors.
    /// </summary>
    private static NavMeshModifier? ResolveModifier(GameObject go, int agentTypeId,
        Dictionary<GameObject, NavMeshModifier?> cache)
    {
        NavMeshModifier? own = ValidModifier(go, agentTypeId);
        if (own != null) return own;
        GameObject? parent = go.Parent;
        return parent.IsValid() ? InheritableModifier(parent!, agentTypeId, cache) : null;
    }

    /// <summary>The modifier <paramref name="go"/> passes down to its children (memoized).</summary>
    private static NavMeshModifier? InheritableModifier(GameObject go, int agentTypeId,
        Dictionary<GameObject, NavMeshModifier?> cache)
    {
        if (cache.TryGetValue(go, out NavMeshModifier? cached)) return cached;

        NavMeshModifier? own = ValidModifier(go, agentTypeId);
        NavMeshModifier? result;
        if (own != null && own.ApplyToChildren)
        {
            result = own;
        }
        else
        {
            GameObject? parent = go.Parent;
            result = parent.IsValid() ? InheritableModifier(parent!, agentTypeId, cache) : null;
        }

        cache[go] = result;
        return result;
    }

    private static NavMeshModifier? ValidModifier(GameObject go, int agentTypeId)
    {
        var modifier = go.GetComponent<NavMeshModifier>();
        return modifier.IsValid() && modifier!.EnabledInHierarchy && modifier.AffectsAgentType(agentTypeId)
            ? modifier : null;
    }

    /// <summary>
    /// Gather enabled <see cref="NavMeshModifierVolume"/>s into self-contained
    /// <see cref="NavMeshAreaVolume"/>s (main thread — touches Transforms). Same layer and
    /// bounds filtering as geometry collection; volumes whose AABB misses
    /// <paramref name="bounds"/> are skipped, which is how partial rebuilds only pay for
    /// volumes near the changed region.
    /// </summary>
    public static void CollectModifierVolumes(IEnumerable<GameObject> objects, LayerMask layers, int agentTypeId,
        List<NavMeshAreaVolume> results, AABB? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(results);

        foreach (GameObject go in objects)
        {
            if (go.IsNotValid() || !go.EnabledInHierarchy) continue;
            if (!layers.HasLayer(go.LayerIndex)) continue;

            foreach (NavMeshModifierVolume volume in go.GetComponents<NavMeshModifierVolume>())
            {
                if (volume.IsNotValid() || !volume.EnabledInHierarchy || !volume.AffectsAgentType(agentTypeId))
                    continue;

                NavMeshAreaVolume areaVolume = volume.ComputeAreaVolume();
                if (areaVolume.Footprint.Length < 3) continue; // degenerate projection
                if (bounds is AABB filter && !areaVolume.Bounds.Intersects(filter)) continue;
                results.Add(areaVolume);
            }
        }
    }

    /// <summary>
    /// Gather enabled, activated <see cref="NavMeshLink"/>s into self-contained
    /// <see cref="NavMeshLinkSource"/>s (main thread — touches Transforms). Same layer and
    /// bounds filtering as geometry collection.
    /// </summary>
    public static void CollectLinks(IEnumerable<GameObject> objects, LayerMask layers, int agentTypeId,
        List<NavMeshLinkSource> results, AABB? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(results);

        foreach (GameObject go in objects)
        {
            if (go.IsNotValid() || !go.EnabledInHierarchy) continue;
            if (!layers.HasLayer(go.LayerIndex)) continue;

            foreach (NavMeshLink link in go.GetComponents<NavMeshLink>())
            {
                if (link.IsNotValid() || !link.EnabledInHierarchy || !link.Activated || !link.AffectsAgentType(agentTypeId))
                    continue;

                NavMeshLinkSource source = link.ToLinkSource();
                if (bounds is AABB filter && !source.Bounds.Intersects(filter)) continue;
                results.Add(source);
            }
        }
    }

    /// <summary>Conservative overlap test: transform the 8 corners of a local AABB and test
    /// the world AABB against the filter. O(1) per source instead of per-vertex. Corners are
    /// walked inline rather than via <c>AABB.TransformBy</c>, which allocates a corner array —
    /// this runs per object on every bounds-filtered collection.</summary>
    private static bool TransformedBoundsIntersect(Float3 localMin, Float3 localMax, in Float4x4 transform, in AABB filter)
    {
        var min = new Float3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Float3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Float3(
                (i & 1) == 0 ? localMin.X : localMax.X,
                (i & 2) == 0 ? localMin.Y : localMax.Y,
                (i & 4) == 0 ? localMin.Z : localMax.Z);
            Float3 world = Float4x4.TransformPoint(corner, transform);
            min = Maths.Min(min, world);
            max = Maths.Max(max, world);
        }

        return new AABB(min, max).Intersects(filter);
    }

    /// <summary>Collect one renderer's mesh, if available.</summary>
    public static void CollectMeshRenderer(MeshRenderer renderer, int area, List<NavMeshGeometrySource> results, AABB? bounds = null)
    {
        if (renderer.IsNotValid() || !renderer.EnabledInHierarchy) return;

        Mesh? mesh = renderer.Mesh.Res;
        if (mesh.IsNotValid()) return;

        if (bounds is AABB filter
            && !TransformedBoundsIntersect(mesh!.bounds.Min, mesh.bounds.Max, renderer.Transform.LocalToWorldMatrix, filter))
            return;

        Float3[] vertices = mesh!.Vertices;
        uint[] indices = mesh.Indices;
        if (vertices == null || indices == null || indices.Length < 3) return;

        results.Add(new NavMeshGeometrySource(vertices, ToIntIndices(indices), renderer.Transform.LocalToWorldMatrix, area));
    }

    /// <summary>
    /// Collect one collider as triangles. Primitive colliders tessellate to the same shape the
    /// physics engine uses (capsules included); mesh colliders reuse the shared physics bake so
    /// the triangle extraction cost is paid once per mesh, not per bake.
    /// </summary>
    public static void CollectCollider(Collider collider, int area, List<NavMeshGeometrySource> results, AABB? bounds = null)
    {
        if (collider.IsNotValid() || !collider.EnabledInHierarchy) return;

        if (bounds is AABB filter)
        {
            // Conservative local bounds per collider type, tested O(1) before any tessellation
            // or vertex extraction. Mesh colliders use the mesh's own (possibly off-center)
            // bounds; primitives are origin-centered by construction.
            Float3 localMin, localMax;
            if (collider is MeshCollider mc)
            {
                Mesh? mcMesh = mc.Mesh.Res;
                if (mcMesh.IsNotValid()) return;
                localMin = mcMesh!.bounds.Min;
                localMax = mcMesh.bounds.Max;
            }
            else
            {
                Float3 halfExtents = collider switch
                {
                    BoxCollider box => box.Size * 0.5f,
                    SphereCollider sphere => new Float3(sphere.Radius, sphere.Radius, sphere.Radius),
                    CapsuleCollider capsule => new Float3(capsule.Radius, capsule.Height * 0.5f + capsule.Radius, capsule.Radius),
                    CylinderCollider cylinder => new Float3(cylinder.Radius, cylinder.Height * 0.5f, cylinder.Radius),
                    ConeCollider cone => new Float3(cone.Radius, cone.Height * 0.5f, cone.Radius),
                    _ => new Float3(float.MaxValue, float.MaxValue, float.MaxValue), // unknown: never reject
                };
                localMin = -halfExtents;
                localMax = halfExtents;
            }
            if (!TransformedBoundsIntersect(localMin, localMax, ColliderWorldMatrix(collider), filter))
                return;
        }

        if (collider is MeshCollider meshCollider)
        {
            Mesh? sharedMesh = meshCollider.Mesh.Res;
            if (sharedMesh.IsNotValid()) return;
            Float3[] vertices = sharedMesh.Vertices;
            uint[] indices = sharedMesh.Indices;
            if (vertices == null || indices == null || indices.Length < 3) return;
            results.Add(new NavMeshGeometrySource(vertices, ToIntIndices(indices), ColliderWorldMatrix(collider), area));
            return;
        }

        // Primitive tessellation: same sizing conventions as each collider's Jitter shape and
        // gizmo (origin-centered, GizmoMatrix places it).
        Mesh? primitive = collider switch
        {
            BoxCollider box => Mesh.CreateCube(box.Size),
            SphereCollider sphere => Mesh.CreateSphere(Math.Max(sphere.Radius, 0.01f), 12, 12),
            // Collider capsule height is the cylindrical segment; CreateCapsule takes total height.
            CapsuleCollider capsule => Mesh.CreateCapsule(Math.Max(capsule.Radius, 0.01f), capsule.Height + 2f * capsule.Radius, 12, 4),
            CylinderCollider cylinder => Mesh.CreateCylinder(Math.Max(cylinder.Radius, 0.01f), cylinder.Height, 12),
            ConeCollider cone => Mesh.CreateCone(Math.Max(cone.Radius, 0.01f), cone.Height, 12),
            _ => null,
        };
        if (primitive == null) return;

        try
        {
            results.Add(new NavMeshGeometrySource(primitive.Vertices, ToIntIndices(primitive.Indices), ColliderWorldMatrix(collider), area));
        }
        finally
        {
            primitive.Dispose();
        }
    }

    /// <summary>
    /// Collect a terrain as a decimated height grid. Samples are spaced no finer than the bake
    /// voxel size — Recast re-voxelizes at that resolution anyway, so finer triangles are pure
    /// waste (a 1k heightmap would otherwise contribute ~2M triangles). Holes are skipped.
    /// </summary>
    public static void CollectTerrain(TerrainCollider terrain, float voxelSize, int area, List<NavMeshGeometrySource> results, AABB? bounds = null)
    {
        if (terrain.IsNotValid() || !terrain.EnabledInHierarchy) return;

        int res = terrain.Width;
        if (res < 2) return;

        var terrainComponent = terrain.GetComponent<TerrainComponent>();
        if (terrainComponent.IsNotValid()) return;
        TerrainData? data = terrainComponent.Data.Res;
        if (data.IsNotValid()) return;

        if (bounds is AABB filter)
        {
            // Terrain is axis-aligned (see the origin note below): position + size is its AABB.
            Float3 tMin = terrain.Transform.Position;
            var tMax = new Float3(tMin.X + data!.Size, tMin.Y + data.Height, tMin.Z + data.Size);
            if (tMin.X > filter.Max.X || tMax.X < filter.Min.X
                || tMin.Z > filter.Max.Z || tMax.Z < filter.Min.Z)
                return;
        }

        float cellSize = data.Size / (res - 1);
        int stride = Math.Max(1, (int)MathF.Floor(Math.Max(voxelSize, cellSize) / cellSize));

        // Sampled grid dimensions (always include the far edge).
        List<int> steps = [];
        for (int i = 0; i < res - 1; i += stride) steps.Add(i);
        steps.Add(res - 1);
        int n = steps.Count;

        // Terrain is axis-aligned by engine convention: the physics heightfield proxy also
        // registers with position only (see TerrainCollider), so rotation/scale on a terrain
        // object is ignored consistently across physics and navigation.
        Float3 origin = terrain.Transform.Position;
        var vertices = new Float3[n * n];
        for (int zi = 0; zi < n; zi++)
        {
            for (int xi = 0; xi < n; xi++)
            {
                int x = steps[xi], z = steps[zi];
                terrain.TryGetHeight(x, z, out float height); // world-space Y (includes terrain position)
                vertices[zi * n + xi] = new Float3(
                    origin.X + x * cellSize,
                    height,
                    origin.Z + z * cellSize);
            }
        }

        List<int> indices = new(6 * (n - 1) * (n - 1));
        for (int zi = 0; zi < n - 1; zi++)
        {
            for (int xi = 0; xi < n - 1; xi++)
            {
                // A cell is a hole if any source cell under the decimated quad is a hole.
                if (AnyHole(terrain, steps[xi], steps[zi], steps[xi + 1], steps[zi + 1])) continue;

                int v00 = zi * n + xi;
                int v01 = (zi + 1) * n + xi;
                int v11 = (zi + 1) * n + xi + 1;
                int v10 = zi * n + xi + 1;
                // Up-facing winding (CCW viewed from +Y), matching the builder's convention.
                indices.Add(v00); indices.Add(v01); indices.Add(v11);
                indices.Add(v00); indices.Add(v11); indices.Add(v10);
            }
        }
        if (indices.Count == 0) return;

        // Vertices are already world-space (heights include the terrain's Y).
        results.Add(new NavMeshGeometrySource(vertices, [.. indices], Float4x4.Identity, area));
    }

    private static bool AnyHole(TerrainCollider terrain, int x0, int z0, int x1, int z1)
    {
        for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
                if (terrain.IsCellHole(x, z))
                    return true;
        return false;
    }

    private static int[] ToIntIndices(uint[] indices)
    {
        int[] result = new int[indices.Length];
        for (int i = 0; i < indices.Length; i++)
            result[i] = (int)indices[i];
        return result;
    }

    /// <summary>World matrix for a collider's shape: the collider's Center/Rotation offsets
    /// composed with the GameObject's world TRS (same composition as the collider gizmo).</summary>
    private static Float4x4 ColliderWorldMatrix(Collider collider)
    {
        Float4x4 worldTRS = Float4x4.CreateTRS(collider.Transform.Position, collider.Transform.Rotation, collider.Transform.LossyScale);
        return Float4x4.CreateTRS(
            Float4x4.TransformPoint(collider.Center, worldTRS),
            collider.Transform.Rotation * Quaternion.FromEuler(collider.Rotation),
            collider.Transform.LossyScale);
    }
}
