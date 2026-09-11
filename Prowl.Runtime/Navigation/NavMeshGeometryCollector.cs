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

            // Terrain contributes in both modes, read from its heightmap asset either way — the
            // collider builds its heightfield from that same asset, and reading the asset is what
            // lets a bake with the editor open, where nothing has had a gameplay callback, see
            // terrain at all. Collider mode still requires the collider to be there, so terrain
            // that is scenery rather than ground is left out of a physics bake as any other
            // collider-less object is.
            foreach (TerrainComponent terrain in go.GetComponents<TerrainComponent>())
            {
                if (geometry != NavMeshCollectGeometry.RenderMeshes && go.GetComponent<TerrainCollider>().IsNotValid())
                    continue;

                CollectTerrain(terrain, voxelSize, area, results, bounds);
            }
        }
    }

    /// <summary>
    /// True when this object moves on the navmesh rather than forming it — an agent or an
    /// obstacle — or sits under one. Baking such an object would stamp a permanent hole where it
    /// happened to sit at bake time; both components block agents at runtime instead, wherever
    /// they actually are. Whole subtrees are excluded since visuals/colliders hang off children.
    /// </summary>
    private static bool BelongsToActor(GameObject go, Dictionary<GameObject, bool> cache)
    {
        if (cache.TryGetValue(go, out bool cached)) return cached;

        GameObject? parent = go.Parent;
        // Both exclude by PRESENCE, never by enabled state, so bake output can't depend on when
        // a component was last toggled — an obstacle disabled at bake time would otherwise
        // voxelize a permanent hole once it later enables and moves. The tradeoff: an obstacle
        // disabled for the whole session leaves its object out of the mesh entirely; permanent
        // geometry should not carry the component at all.
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
        CollectLinks(EnumerateLinks(objects), layers, agentTypeId, results, bounds);
    }

    /// <inheritdoc cref="CollectLinks(IEnumerable{GameObject}, LayerMask, int, List{NavMeshLinkSource}, AABB?)"/>
    /// <remarks>Takes the links themselves, which is how a whole-scene bake avoids visiting every
    /// GameObject to find the handful that carry one — see <see cref="NavMeshWorld.Links"/>.</remarks>
    public static void CollectLinks(IEnumerable<NavMeshLink> links, LayerMask layers, int agentTypeId,
        List<NavMeshLinkSource> results, AABB? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(results);

        foreach (NavMeshLink link in links)
        {
            // EnabledInHierarchy already folds in the GameObject's own state.
            if (link.IsNotValid() || !link.EnabledInHierarchy || !link.Activated || !link.AffectsAgentType(agentTypeId))
                continue;
            if (!layers.HasLayer(link.GameObject.LayerIndex)) continue;

            NavMeshLinkSource source = link.ToLinkSource();
            if (bounds is AABB filter && !source.Bounds.Intersects(filter)) continue;
            results.Add(source);
        }
    }

    private static IEnumerable<NavMeshLink> EnumerateLinks(IEnumerable<GameObject> objects)
    {
        foreach (GameObject go in objects)
        {
            if (go.IsNotValid()) continue;
            foreach (NavMeshLink link in go.GetComponents<NavMeshLink>())
                yield return link;
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
    /// physics engine uses (capsules included); mesh colliders share the mesh's vertex array and
    /// copy its indices, which <see cref="NavMeshGeometrySource"/> needs as int.
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
    /// waste (a 1k heightmap would otherwise contribute ~2M triangles). Holes are skipped, and a
    /// bounds filter clips the sampled range rather than only rejecting the terrain outright, so a
    /// partial rebuild pays for its own tiles instead of the whole heightmap.
    /// <para/>
    /// Heights come from <see cref="TerrainData"/> in terrain-local space, placed by the object's
    /// transform exactly as <see cref="TerrainCollider"/> places the physics heightfield — so a
    /// moved, rotated or scaled terrain walks where it's drawn and where it collides. Reading the
    /// asset (not the collider) is also what lets a bake with the editor open see terrain at all.
    /// </summary>
    public static void CollectTerrain(TerrainComponent terrain, float voxelSize, int area, List<NavMeshGeometrySource> results, AABB? bounds = null)
    {
        if (terrain.IsNotValid() || !terrain.EnabledInHierarchy) return;

        terrain.Data.EnsureLoaded();
        TerrainData? data = terrain.Data.Res;
        if (data.IsNotValid()) return;

        int res = data!.HeightmapResolution;
        if (res < 2) return;

        // The stored heights span the full 0..Height band, so sculpting can never leave these bounds.
        Float4x4 localToWorld = terrain.Transform.LocalToWorldMatrix;
        if (bounds is AABB filter
            && !TransformedBoundsIntersect(Float3.Zero, new Float3(data.Size, data.Height, data.Size), localToWorld, filter))
            return;

        // The sample budget is a world-space distance, so it converts to grid steps through the
        // scale the terrain is drawn at.
        float cellSize = data.Size / (res - 1);
        Float3 scale = terrain.Transform.LossyScale;
        float localVoxelSize = voxelSize / Math.Max(1e-4f, Math.Max(MathF.Abs(scale.X), MathF.Abs(scale.Z)));
        // Capped at res - 1 because a wider stride samples nothing beyond the two edges anyway,
        // and an unbounded one (cellSize of a zero-sized terrain saturates the cast) overflows
        // the round-up in SampleCeil.
        int stride = Math.Clamp((int)MathF.Floor(Math.Max(localVoxelSize, cellSize) / cellSize), 1, res - 1);

        // A partial rebuild wants a handful of tiles, and sampling the whole terrain for them is
        // the dominant cost of one. Clipping is per axis, so X and Z get their own index range.
        int x0 = 0, x1 = res - 1, z0 = 0, z1 = res - 1;
        if (bounds is AABB clip)
        {
            // Corner-transformed, not a box transform: under rotation the local box of a world
            // box is not that box with its axes swapped around.
            AABB local = terrain.Transform.InverseTransformAABB(clip);
            x0 = SampleFloor(local.Min.X, cellSize, stride, res);
            x1 = SampleCeil(local.Max.X, cellSize, stride, res);
            z0 = SampleFloor(local.Min.Z, cellSize, stride, res);
            z1 = SampleCeil(local.Max.Z, cellSize, stride, res);
            if (x0 >= x1 || z0 >= z1) return;
        }

        // Sampled grid dimensions (always include the far edge of the range).
        List<int> xSteps = [];
        for (int i = x0; i < x1; i += stride) xSteps.Add(i);
        xSteps.Add(x1);
        List<int> zSteps = [];
        for (int i = z0; i < z1; i += stride) zSteps.Add(i);
        zSteps.Add(z1);
        int nx = xSteps.Count, nz = zSteps.Count;

        var vertices = new Float3[nx * nz];
        for (int zi = 0; zi < nz; zi++)
        {
            for (int xi = 0; xi < nx; xi++)
            {
                int x = xSteps[xi], z = zSteps[zi];
                vertices[zi * nx + xi] = new Float3(x * cellSize, data.GetHeight(x, z) * data.Height, z * cellSize);
            }
        }

        List<int> indices = new(6 * (nx - 1) * (nz - 1));
        for (int zi = 0; zi < nz - 1; zi++)
        {
            for (int xi = 0; xi < nx - 1; xi++)
            {
                // A cell is a hole if any source cell under the decimated quad is a hole.
                if (AnyHole(data, xSteps[xi], zSteps[zi], xSteps[xi + 1], zSteps[zi + 1])) continue;

                int v00 = zi * nx + xi;
                int v01 = (zi + 1) * nx + xi;
                int v11 = (zi + 1) * nx + xi + 1;
                int v10 = zi * nx + xi + 1;
                // Up-facing winding (CCW viewed from +Y), matching the builder's convention.
                indices.Add(v00); indices.Add(v01); indices.Add(v11);
                indices.Add(v00); indices.Add(v11); indices.Add(v10);
            }
        }
        if (indices.Count == 0) return;

        results.Add(new NavMeshGeometrySource(vertices, [.. indices], localToWorld, area));
    }

    /// <summary>
    /// Heightmap index bracketing a terrain-local coordinate, snapped outwards onto the stride
    /// grid an unclipped collect samples. The snap is what keeps a clipped source decimating to
    /// the same surface: sampling the same span at a shifted phase gives different heights, and a
    /// rebuilt tile would then step away from a neighbour that was not rebuilt. One sample of
    /// slack covers the quads straddling the clip edge.
    /// </summary>
    private static int SampleFloor(float local, float cellSize, int stride, int res)
    {
        int i = (int)MathF.Floor(Math.Clamp(local / cellSize, 0f, res - 1f));
        i = Math.Max(0, i - 1);
        return i - i % stride;
    }

    /// <inheritdoc cref="SampleFloor"/>
    private static int SampleCeil(float local, float cellSize, int stride, int res)
    {
        int i = (int)MathF.Ceiling(Math.Clamp(local / cellSize, 0f, res - 1f));
        i = Math.Min(res - 1, i + 1);
        return Math.Min(res - 1, (i + stride - 1) / stride * stride);
    }

    private static bool AnyHole(TerrainData data, int x0, int z0, int x1, int z1)
    {
        for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
                if (data.IsCellHole(x, z))
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
