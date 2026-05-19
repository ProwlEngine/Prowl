# Prowl.Unwrapper UV Unwrapper

A mesh UV unwrapper built for the Prowl Game Engine. Given an arbitrary triangle mesh, Prowl.Unwrapper produces a non-overlapping UV atlas suitable for lightmaps, AO bakes, signed-distance bakes, or any other texture-space workflow that needs a clean parametrisation.

The pipeline cleans the input mesh, segments it into roughly developable charts, flattens each chart with LSCM, and packs the charts into the unit square. All in pure C# with no native dependencies.

## Performance

Tested on Sponza (262k triangles, the standard glTF sample model) on a desktop CPU:

```
  Cleanup + half-edge build:   1.2 s
  Chart segmentation:          7.4 s
  LinABF + LSCM solve:         9.1 s
  Atlas packing:               5.1 s
                              ------
  Total:                      22.8 s
```

Hot paths use SIMD via `System.Numerics.Vector<double>`, the linear solver is a Jacobi-preconditioned conjugate gradient over a CSC sparse matrix, and chart processing is parallelised across logical cores via `Parallel.For`. Hash maps for half-edge lookups use a custom open-addressing `long -> int` table with SplitMix64 mixing, which is roughly 5x faster than `Dictionary<long, int>` for this workload.

## Features

- **Robust geometry preparation**
  - Vertex welding with normal-aware splitting (coincident points with opposing normals stay separate)
  - Degenerate triangle removal (zero area, collinear corners)
  - Non-manifold geometry fixer using local edge cutting (Gueziec / Taubin), so dirty real-world meshes work
  - Optional per-corner material UV input used as a seam hint

- **Segmentation**
  - Lloyd-style chart growth scored by 3D compactness and developability
  - Hard-edge detection from a configurable dihedral threshold
  - Distortion-aware merging: adjacent chart pairs are trial-flattened and accepted only if mean angular and area distortion stay below the configured thresholds
  - Time-budgeted merge pass for pathological inputs

- **Parametrisation**
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

```csharp
var options = new UnwrapOptions
{
    AngleDistortionThreshold = 0.05,   // stricter than default 0.08
    AreaDistortionThreshold  = 0.10,
    HardAngle                = 75.0,   // more aggressive crease cutting
    PackMargin               = 1.0 / 512.0,
};

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

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.
