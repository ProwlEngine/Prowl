# Prowl.Unwrapper UV Unwrapper

A mesh UV unwrapper built for the Prowl Game Engine. Given an arbitrary triangle mesh, Prowl.Unwrapper produces a non-overlapping UV atlas suitable for lightmaps, AO bakes, signed-distance bakes, or any other texture-space workflow that needs a clean parametrisation.

The pipeline cleans the input mesh, segments it into charts, flattens each chart, and packs the charts into the unit square. All in pure C# with no native dependencies.

## Choosing a method

`UnwrapOptions.Method` picks the algorithm.

| | `Projection` (default) | `Conformal` |
| --- | --- | --- |
| How charts form | grouped by which cube face the normal points at | Lloyd segmentation + distortion-aware merging |
| How charts flatten | dropped onto the projection plane | LinABF angle field, then an LSCM solve |
| Sponza (262k tris) | 1.5 s | 11 s |
| Atlas coverage | ~27% | ~43% |
| L2 texture stretch | 1.00 | 1.02 |

Projection is the better default for lightmaps: near-uniform texel density matters more than atlas coverage, and it finishes fast enough to run on import. Reach for `Conformal` when atlas space is tight or when long unbroken charts matter more than turnaround time.

```csharp
var result = new UnwrapMesh(positions, triangles)
    .Unwrap(new UnwrapOptions { Method = UnwrapMethod.Conformal });
```

## Performance

Tested on Sponza (262k triangles, the standard glTF sample model) on a desktop CPU:

```
  Projection                        Conformal
  ------------------------------    ------------------------------
  Cleanup + half-edge build 1.0 s   Cleanup + half-edge build  1.0 s
  Charting + projection     0.2 s   Segmentation + LSCM       10.0 s
  Atlas packing             0.3 s   Atlas packing              0.3 s
                           ------                            ------
  Total                     1.5 s   Total                     11.3 s
```

Hot paths use SIMD via `System.Numerics.Vector<double>`, the linear solver is a Jacobi-preconditioned conjugate gradient over a CSC sparse matrix, and chart processing is parallelised across logical cores via `Parallel.For`. Hash maps for half-edge lookups use a custom open-addressing `long -> int` table with SplitMix64 mixing, which is roughly 5x faster than `Dictionary<long, int>` for this workload.

## Features

- **Robust geometry preparation**
  - Vertex welding with normal-aware splitting (coincident points with opposing normals stay separate)
  - Degenerate triangle removal (zero area, collinear corners)
  - Non-manifold geometry fixer using local edge cutting (Gueziec / Taubin), so dirty real-world meshes work
  - Optional per-corner material UV input used as a seam hint

- **Projection charting** (`UnwrapMethod.Projection`)
  - Every triangle picks the cube-face direction its normal points closest to
  - Connected same-direction triangles become a chart, flattened by dropping it onto that plane
  - Optional eight cube-corner directions on top of the six face directions
  - Undersized clusters fold into the neighbour they share the most border with, guarded so nothing gets squashed edge-on

- **Conformal segmentation** (`UnwrapMethod.Conformal`)
  - Lloyd-style chart growth scored by 3D compactness and developability
  - Hard-edge detection from a configurable dihedral threshold
  - Distortion-aware merging: adjacent chart pairs are trial-flattened and accepted only if mean angular and area distortion stay below the configured thresholds
  - Time-budgeted merge pass for pathological inputs

- **Parametrisation** (conformal only)
  - Linear Angle-Based Flattening (LinABF) provides the initial angle field
  - Least Squares Conformal Maps (LSCM) produces the per-chart UVs
  - Two pinned vertices selected from the chart boundary for stable rotation
  - Distortion metrics (mean and worst-case angular + area) reported per chart

- **Packing**
  - Convex hull and oriented bounding box per chart for tight rotation
  - Skyline-style bin packing with a configurable border for texel safety
  - Repeated atlas growth until everything fits inside [0, 1] x [0, 1]

- **API**
  - Fluent `UnwrapMesh` builder
  - Per-call `UnwrapOptions` tunable knobs
  - Optional progress sink for diagnostics
  - Per-corner UV output (3 entries per triangle), so split corners across chart seams are preserved

## Usage

### Basic unwrap

```csharp
using Prowl.Unwrapper;
using Prowl.Vector;

Double3[] positions = ...;   // one per vertex
int[] triangles    = ...;    // flat index buffer, 3 per face

var result = new UnwrapMesh(positions, triangles).Unwrap();

// result.PerCornerUVs is laid out as [tri0.c0, tri0.c1, tri0.c2, tri1.c0, ...]
Double2[] uvs = result.PerCornerUVs;
```

### With normals and material UV hints

Normals improve welding (coincident points with opposing normals stay split). Material UVs act as a seam hint during segmentation.

```csharp
var result = new UnwrapMesh(positions, triangles)
    .WithNormals(normals)
    .WithMaterialUVs(existingUVs)
    .Unwrap();
```

### Tuning chart quality

Shared knobs sit on `UnwrapOptions`; per-method knobs live under `.Projection` and `.Conformal`.

```csharp
var options = new UnwrapOptions
{
    Method     = UnwrapMethod.Conformal,
    HardAngle  = 75.0,           // more aggressive crease cutting
    PackMargin = 1.0 / 512.0,
};
options.Conformal.AngleDistortionThreshold = 0.05;   // stricter than the default 0.08
options.Conformal.AreaDistortionThreshold = 0.10;
options.Projection.UseDiagonalAxes = true;           // 14 directions instead of 6

var result = new UnwrapMesh(positions, triangles).Unwrap(options);
```

### Progress reporting

```csharp
var result = new UnwrapMesh(positions, triangles)
    .WithProgress(msg => Console.WriteLine(msg))
    .Unwrap();
```

### Handling degenerate triangles

Triangles that collapse during cleanup are reported back so caller geometry can stay aligned with the input index buffer:

```csharp
if (result.DegenerateTriangleIndices is { } skipped)
{
    foreach (int i in skipped)
        Console.WriteLine($"triangle {i} was degenerate and got zero UVs");
}
```

## Limitations

- Output UVs are per-corner, not per-vertex. Callers wanting a vertex buffer must split shared vertices along seams themselves.
- Pure CPU. There is no GPU path.
- The unwrapper minimises distortion, not seam length. For artist-facing UVs you may want a different tool.
- `Projection` has no overlap validation pass. Every triangle in a chart faces its projection plane so charts cannot fold, but a connected surface that spirals back over itself without leaving one direction bucket can still self-overlap. Use `Conformal` if that shape appears in your content.

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.
