using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// Convenience helper that bin-packs a list of mesh instances across one or more
/// <see cref="LightmapTarget"/>s. Each mesh's atlas footprint is sized from its world-space
/// surface area times the <c>texelsPerWorldUnit</c> density squared (so larger surfaces get
/// more lightmap resolution), then shelf-packed into atlases: when the current atlas runs
/// out of room, a new one is created.
/// </summary>
/// <remarks>
/// This sits on top of the imperative API: it ultimately just calls
/// <see cref="LightmapBaker.CreateTextureTarget"/> and
/// <see cref="LightmapTarget.AddBakeInstance"/> for you. If you want finer control over which
/// instance lives in which atlas, you can keep using the imperative methods directly.
/// </remarks>
public static class AutoAtlasPacker
{
    /// <summary>
    /// Pack the given meshes into N atlas pages. Returns the targets that were created (in
    /// creation order: index 0 is the first atlas, etc.), and a parallel array mapping each
    /// input mesh to the <see cref="BakeInstance"/> that was added for it.
    /// </summary>
    /// <param name="baker">The baker that should own the new targets and instances.</param>
    /// <param name="meshes">Mesh + world-transform pairs to pack. Each mesh must have a UV layer
    /// named <paramref name="bakeUVLayer"/> with coords in <c>[0, 1]^2</c> (i.e. each mesh's UV1
    /// fills its own atlas region, not all of UV space).</param>
    /// <param name="atlasWidth">Page width in pixels.</param>
    /// <param name="atlasHeight">Page height in pixels.</param>
    /// <param name="texelsPerWorldUnit">Target lightmap density. 50 = each metre of world surface
    /// gets ~50 texels along its largest dimension; bump it up for higher quality.</param>
    /// <param name="padding">Pixels of padding between every placement.</param>
    /// <param name="bakeUVLayer">Name of the UV layer to use as the bake layer.</param>
    public static AutoAtlasResult Pack(
        LightmapBaker baker,
        System.Collections.Generic.IReadOnlyList<(BakeMesh mesh, Float4x4 transform)> meshes,
        int atlasWidth,
        int atlasHeight,
        float texelsPerWorldUnit = 50f,
        int padding = 2,
        string bakeUVLayer = "UV1")
    {
        if (baker is null) throw new System.ArgumentNullException(nameof(baker));
        if (meshes is null) throw new System.ArgumentNullException(nameof(meshes));
        if (atlasWidth <= 0 || atlasHeight <= 0) throw new System.ArgumentOutOfRangeException(nameof(atlasWidth));
        if (texelsPerWorldUnit <= 0) throw new System.ArgumentOutOfRangeException(nameof(texelsPerWorldUnit));

        int n = meshes.Count;
        int maxSide = System.Math.Min(atlasWidth, atlasHeight) - 2 * padding;
        if (maxSide < 4) throw new System.ArgumentException("Atlas too small for the requested padding.", nameof(atlasWidth));

        // ---- 1) Footprint per mesh: per-triangle world-to-UV ratio drives atlas resolution. ----
        //
        // For each non-degenerate triangle, the requested texel density translates to a minimum
        // atlas side via:
        //
        //     atlas_pixels_for_tri = U_tri * sidePx^2
        //     world_area_for_tri   = W_tri
        //     pixels_per_world_unit_side^2 = U_tri * sidePx^2 / W_tri >= tpwu^2
        //     sidePx >= tpwu * sqrt(W_tri / U_tri)
        //
        // Taking the max over all triangles guarantees every triangle meets the requested density:
        // a triangle whose UV chart was compressed (small U_tri vs its W_tri) needs more atlas
        // pixels than an averaged whole-mesh formula would give it. A uniform unwrap produces the
        // same ratio across every triangle, so this collapses to the obvious "pick any triangle"
        // answer in the common case.
        var sizes = new (int w, int h)[n];
        for (int i = 0; i < n; i++)
        {
            float maxRatio = MeshMaxWorldOverUVRatio(meshes[i].mesh, meshes[i].transform, bakeUVLayer);
            float sidePx = (float)System.Math.Sqrt(maxRatio) * texelsPerWorldUnit;
            int s = (int)System.Math.Ceiling(sidePx);
            if (s < 4) s = 4;
            if (s > maxSide) s = maxSide;
            sizes[i] = (s, s);
        }

        // ---- 2) Sort by max side desc: standard shelf-pack heuristic. ------------------------
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        System.Array.Sort(order, (a, b) =>
        {
            int sa = System.Math.Max(sizes[a].w, sizes[a].h);
            int sb = System.Math.Max(sizes[b].w, sizes[b].h);
            return sb.CompareTo(sa);
        });

        // ---- 3) Walk in sorted order, place each in the first atlas with room; otherwise open
        //         a new atlas page. Each atlas tracks its current shelf cursor + shelf height.
        var placements = new (int atlas, int x, int y, int w, int h)[n];
        var atlasCursors = new System.Collections.Generic.List<(int x, int y, int rowH)>();

        for (int oi = 0; oi < n; oi++)
        {
            int i = order[oi];
            int w = sizes[i].w, h = sizes[i].h;
            bool placed = false;

            for (int a = 0; a < atlasCursors.Count && !placed; a++)
            {
                var (cx, cy, rh) = atlasCursors[a];
                // Try the current shelf first; if no horizontal room, start a new shelf.
                if (cx + w + padding > atlasWidth)
                {
                    cy += rh + padding;
                    cx = padding;
                    rh = 0;
                }
                if (cy + h + padding > atlasHeight) continue; // this atlas has no vertical room
                placements[i] = (a, cx, cy, w, h);
                cx += w + padding;
                if (h > rh) rh = h;
                atlasCursors[a] = (cx, cy, rh);
                placed = true;
            }

            if (!placed)
            {
                // No existing atlas fits: open a new one.
                int newAtlas = atlasCursors.Count;
                atlasCursors.Add((padding + w + padding, padding, h));
                placements[i] = (newAtlas, padding, padding, w, h);
            }
        }

        // ---- 4) Create the targets and add the instances. -------------------------------------
        int atlasCount = atlasCursors.Count;
        var targets = new LightmapTarget[atlasCount];
        for (int a = 0; a < atlasCount; a++)
            targets[a] = baker.CreateTextureTarget($"AutoAtlas_{a}", atlasWidth, atlasHeight);

        var bakeInstances = new BakeInstance[n];
        for (int i = 0; i < n; i++)
        {
            var p = placements[i];
            var offset = new Float2(p.x / (float)atlasWidth, p.y / (float)atlasHeight);
            var scale  = new Float2(p.w / (float)atlasWidth, p.h / (float)atlasHeight);
            bakeInstances[i] = targets[p.atlas].AddBakeInstance(
                meshes[i].mesh, meshes[i].transform, offset, scale, bakeUVLayer);
        }

        return new AutoAtlasResult(targets, bakeInstances, placements);
    }

    /// <summary>
    /// Largest per-triangle <c>world_area / uv_area</c> ratio across every triangle in the mesh
    /// under the given transform. Returns 0 for empty or fully-degenerate meshes (callers clamp).
    /// </summary>
    /// <remarks>
    /// Triangles with sub-pixel UV chart area or sub-mm world area are skipped: a single
    /// genuinely-degenerate triangle would otherwise dominate the ratio and produce an atlas the
    /// size of the whole page for one bad face. The thresholds (<c>1e-9 m^2</c> world,
    /// <c>1e-10</c> UV) sit well below anything a real shipped model produces.
    /// </remarks>
    private static float MeshMaxWorldOverUVRatio(BakeMesh mesh, Float4x4 transform, string bakeUVLayer)
    {
        if (!mesh.UVLayers.TryGetValue(bakeUVLayer, out var uv)) return 0f;
        var positions = mesh.Positions;
        float maxRatio = 0f;
        for (int g = 0; g < mesh.MaterialGroups.Count; g++)
        {
            var idx = mesh.MaterialGroups[g].Indices;
            for (int i = 0; i < idx.Length; i += 3)
            {
                var v0 = Float4x4.TransformPoint(positions[idx[i    ]], transform);
                var v1 = Float4x4.TransformPoint(positions[idx[i + 1]], transform);
                var v2 = Float4x4.TransformPoint(positions[idx[i + 2]], transform);
                float wArea = Float3.Length(Float3.Cross(v1 - v0, v2 - v0)) * 0.5f;
                if (wArea < 1e-9f) continue;

                var a = uv[idx[i    ]];
                var b = uv[idx[i + 1]];
                var c = uv[idx[i + 2]];
                float uArea = System.Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) * 0.5f;
                if (uArea < 1e-10f) continue;

                float ratio = wArea / uArea;
                if (ratio > maxRatio) maxRatio = ratio;
            }
        }
        return maxRatio;
    }
}

/// <summary>Result of an <see cref="AutoAtlasPacker.Pack"/> call.</summary>
public sealed class AutoAtlasResult
{
    /// <summary>One target per atlas page. Length is the smallest number of pages that fit the input.</summary>
    public LightmapTarget[] Targets { get; }

    /// <summary>One <see cref="BakeInstance"/> per input mesh, in the same order as the input list.</summary>
    public BakeInstance[] Instances { get; }

    /// <summary>For each input mesh: which atlas it landed in (index into <see cref="Targets"/>), its pixel offset, and its pixel footprint.</summary>
    public (int AtlasIndex, int X, int Y, int W, int H)[] Placements { get; }

    internal AutoAtlasResult(LightmapTarget[] targets, BakeInstance[] instances,
                             (int, int, int, int, int)[] placements)
    {
        Targets = targets;
        Instances = instances;
        Placements = placements;
    }
}
