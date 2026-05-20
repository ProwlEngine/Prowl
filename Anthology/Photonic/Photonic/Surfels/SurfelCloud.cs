using Prowl.Vector;

namespace Prowl.Photonic.Surfels;

/// <summary>
/// One surfel: a tiny oriented disk sampled on a mesh surface. Holds the world position and
/// normal, plus a running accumulator for the indirect light it sees. The texel-interpolation
/// pass blends a small set of nearby surfels (filtered by normal + distance) into each lightmap
/// texel.
/// </summary>
/// <remarks>
/// Fields are exposed as mutable to let the integrator update <see cref="IndirectSum"/> /
/// <see cref="SampleCount"/> via <c>ref</c> access. External consumers must treat every field
/// as read-only: rewriting <see cref="Position"/> or <see cref="Radius"/> corrupts the
/// owning <see cref="SurfelCloud"/>'s spatial grid.
/// </remarks>
public struct Surfel
{
    /// <summary>World-space position. Set at generation; do not rewrite.</summary>
    public Float3 Position;
    /// <summary>Unit-length world-space surface normal. Set at generation; do not rewrite.</summary>
    public Float3 Normal;
    /// <summary>Material-UV at the surfel's barycentric. Set at generation; do not rewrite.</summary>
    public Float2 UV0;
    /// <summary>Index into the bake's instance list. Set at generation; do not rewrite.</summary>
    public int InstanceIndex;
    /// <summary>Index into the source mesh's <c>MaterialGroups</c>. Set at generation; do not rewrite.</summary>
    public int MaterialGroupIndex;
    /// <summary>World-space radius the influence kernel reaches; controls per-surfel falloff. Set at generation; do not rewrite.</summary>
    public float Radius;
    /// <summary>Running sum of indirect contributions across all iterations. Written by the integrator.</summary>
    public Float3 IndirectSum;
    /// <summary>Number of indirect samples folded into <see cref="IndirectSum"/>. Written by the integrator.</summary>
    public int SampleCount;
}

/// <summary>
/// Scene-wide surfel cloud + uniform 3D grid. Each surfel is registered in every cell its
/// influence sphere overlaps. Cell size is set to the cloud's max surfel radius, so a query
/// only needs to read the single cell the query point falls in: anything that could reach the
/// point is already registered there.
/// </summary>
public sealed class SurfelCloud
{
    public Surfel[] Surfels { get; }
    public AABB Bounds { get; }
    public float CellSize { get; }
    public int CellsX { get; }
    public int CellsY { get; }
    public int CellsZ { get; }
    /// <summary>Largest <see cref="Surfel.Radius"/> in the cloud. Equal to <see cref="CellSize"/>.</summary>
    public float MaxRadius { get; }
    private readonly int[] _cellStart;   // prefix-sum index into _cellSurfels per cell (length totalCells+1)
    private readonly int[] _cellSurfels; // surfel indices grouped by cell (a single surfel index may appear in multiple cells)

    internal SurfelCloud(Surfel[] surfels, AABB bounds)
    {
        Surfels = surfels;
        Bounds = bounds;

        // Cell size = max surfel radius. Guarantees that any surfel which could overlap a point
        // also overlaps the cell containing that point: so a single-cell lookup is sufficient.
        float maxR = 0f;
        for (int i = 0; i < surfels.Length; i++)
            if (surfels[i].Radius > maxR) maxR = surfels[i].Radius;
        MaxRadius = maxR;
        CellSize = System.Math.Max(0.01f, maxR);

        var size = bounds.Max - bounds.Min;
        CellsX = System.Math.Max(1, (int)System.Math.Ceiling(size.X / CellSize));
        CellsY = System.Math.Max(1, (int)System.Math.Ceiling(size.Y / CellSize));
        CellsZ = System.Math.Max(1, (int)System.Math.Ceiling(size.Z / CellSize));
        int totalCells = CellsX * CellsY * CellsZ;

        // Count registrations per cell so we can size the flat list. Each surfel's AABB
        // ([pos - R, pos + R]) is rasterised into the grid; every overlapping cell counts +1.
        var counts = new int[totalCells + 1];
        for (int i = 0; i < surfels.Length; i++)
        {
            ref var s = ref surfels[i];
            ComputeCellRange(s.Position, s.Radius,
                out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
            for (int cz = z0; cz <= z1; cz++)
            for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
                counts[((cz * CellsY + cy) * CellsX + cx) + 1]++;
        }
        for (int i = 1; i <= totalCells; i++) counts[i] += counts[i - 1];
        _cellStart = counts;

        int totalEntries = _cellStart[totalCells];
        _cellSurfels = new int[totalEntries];
        var cursor = new int[totalCells];
        System.Array.Copy(_cellStart, cursor, totalCells);
        for (int i = 0; i < surfels.Length; i++)
        {
            ref var s = ref surfels[i];
            ComputeCellRange(s.Position, s.Radius,
                out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
            for (int cz = z0; cz <= z1; cz++)
            for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
            {
                int c = (cz * CellsY + cy) * CellsX + cx;
                _cellSurfels[cursor[c]++] = i;
            }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ComputeCellRange(Float3 pos, float radius,
        out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)
    {
        x0 = (int)System.Math.Floor((pos.X - radius - Bounds.Min.X) / CellSize);
        y0 = (int)System.Math.Floor((pos.Y - radius - Bounds.Min.Y) / CellSize);
        z0 = (int)System.Math.Floor((pos.Z - radius - Bounds.Min.Z) / CellSize);
        x1 = (int)System.Math.Floor((pos.X + radius - Bounds.Min.X) / CellSize);
        y1 = (int)System.Math.Floor((pos.Y + radius - Bounds.Min.Y) / CellSize);
        z1 = (int)System.Math.Floor((pos.Z + radius - Bounds.Min.Z) / CellSize);
        if (x0 < 0) x0 = 0; if (x1 >= CellsX) x1 = CellsX - 1;
        if (y0 < 0) y0 = 0; if (y1 >= CellsY) y1 = CellsY - 1;
        if (z0 < 0) z0 = 0; if (z1 >= CellsZ) z1 = CellsZ - 1;
        if (x1 < x0) x1 = x0; if (y1 < y0) y1 = y0; if (z1 < z0) z1 = z0;
    }

    /// <summary>
    /// Writes the indices of every surfel registered in the cell containing <paramref name="position"/>
    /// into <paramref name="buffer"/>. Because surfels are pre-registered in every cell their influence
    /// sphere overlaps, this single-cell lookup catches every surfel whose <see cref="Surfel.Radius"/>
    /// could reach the query point. Returns the number written (stops silently if the buffer fills).
    /// </summary>
    public int QueryCell(Float3 position, System.Span<int> buffer)
    {
        int cx = (int)System.Math.Floor((position.X - Bounds.Min.X) / CellSize);
        int cy = (int)System.Math.Floor((position.Y - Bounds.Min.Y) / CellSize);
        int cz = (int)System.Math.Floor((position.Z - Bounds.Min.Z) / CellSize);
        if (cx < 0 || cx >= CellsX || cy < 0 || cy >= CellsY || cz < 0 || cz >= CellsZ) return 0;
        int c = (cz * CellsY + cy) * CellsX + cx;
        int start = _cellStart[c], end = _cellStart[c + 1];
        int n = end - start;
        if (n > buffer.Length) n = buffer.Length;
        for (int k = 0; k < n; k++) buffer[k] = _cellSurfels[start + k];
        return n;
    }
}
