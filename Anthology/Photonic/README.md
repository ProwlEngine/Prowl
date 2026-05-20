# Prowl.Photonic

A CPU lightmap baker for the Prowl Game Engine. Imperative builder API: scene + meshes + lights in, baked HDR atlas pages out. Direct + indirect path-traced lighting, BVH4 + AVX2 SIMD acceleration, progressive accumulation, and a sparse surfel-based GI option.

## Features

- **Imperative builder API**
  - `BakeScene` + `BakeMesh.Builder` + `LightmapTarget` give you a `BeginScene` / `BeginMesh` / `End` flow
  - Per-instance world transform, per-target atlas placement (offset / scale into the bake UV layer)
  - Auto-atlas packer for "drop a list of meshes in, get N atlas pages out"

- **Two path tracers**
  - **Per-texel** (default): trace from every covered atlas pixel. Predictable, sharp, the right call when you can afford the rays.
  - **Surfel**: trace from a sparse cloud of world-space sample points then interpolate to texels via a uniform spatial grid. Much cheaper at high resolutions, trades local detail for orders-of-magnitude fewer rays. Normal-aware Poisson disk seeding + Halton-distributed barycentric sampling keep the cloud evenly distributed.

- **Acceleration**
  - BVH4 with binary SAH + binned splits, collapsed to 4-wide nodes (SoA `Vector128` AABB packs, one SIMD ray-vs-4-AABB per inner-node visit)
  - AVX2 leaves: 8-triangle Moller-Trumbore in `Vector256` (`LoadUnsafe` over SoA edge arrays)
  - Parallel-for over rows / surfels, deterministic per-row seeding

- **Lighting**
  - Directional, point, spot
  - Next-event estimation at every bounce hit
  - Pluggable `IAttenuation` (`InverseSquare`, `NormalizedQuadratic`, `Constant`)
  - `IncludeDirectLighting` toggle for indirect-only bakes (sharp shadows handled at runtime by a dynamic-light shader)

- **Bake pipeline**
  - Conservative SAT-based texel rasterisation with centroid bias + strict-inside-wins claim semantics
  - Cosine-weighted Halton-distributed 16k-entry hemisphere LUT
  - Progressive temporal accumulator (`SamplesPerIteration`); atlas updates after every iteration so a viewer can preview live
  - Per-iter dilation pass that doesn't disturb the bake's own coverage state

## Usage

```csharp
using var baker = new LightmapBaker();
baker.Options.PathTracer = PathTracerKind.PerTexel;
baker.Options.Bounces = 2;
baker.Options.SamplesPerIteration = 1;
baker.Options.IncludeDirectLighting = true;

var target = baker.CreateTextureTarget("atlas0", 512, 512);
var scene  = baker.BeginScene("Sponza");

var mat = scene.CreateMaterial("floor");
mat.DiffuseColor = new Float3(0.8f, 0.8f, 0.8f);

var mesh = scene.BeginMesh("floor")
                .AddVertices(positions, normals)
                .AddUVLayer("UV0", materialUVs)
                .AddUVLayer("UV1", lightmapUVs)
                .AddMaterialGroup("floor", indices)
                .End();
target.AddBakeInstance(mesh, Float4x4.Identity);

scene.CreateDirectionalLight("sun", Float4x4.Identity, new Float3(8f, 7f, 6f));
scene.End();

var job = baker.Start();
job.RunIterations(64);   // headless: fold 64 iters then stop
baker.Cancel();

byte[] ldr = target.ReadLDR(exposure: 1f, gamma: 1f / 2.2f);
```

### Progressive preview

```csharp
var job = baker.Start();
job.OnIterationComplete += iter =>
{
    // worker thread; marshal to your UI / GL thread before touching textures
    pendingUpload = true;
};
// each render frame, if (pendingUpload) re-upload target.Pixels (ReadOnlySpan<float>).
```

### Surfel mode

```csharp
baker.Options.PathTracer = PathTracerKind.Surfel;
baker.Options.SurfelsPerSquareMeter = 2.0f;     // density; also drives kernel radius
baker.Options.SurfelMaxNeighbors = 8;
baker.Options.SurfelNormalRejection = true;     // Poisson-disk reject aligned-normal neighbours only
```

The bake-time API is identical; under the hood the integrator scatters surfels across instance triangles at the requested density, traces from those instead of from every texel, and splats results back via a per-surfel-radius falloff kernel.

## Samples

Two reference applications under `Samples/`, both bake the Sponza model (assets ship in `Samples/Assets/Sponza/glTF/`).

- **`Photonic.Demo`** -- the main demo. OpenTK + ImGui. Live tuning of every BakeOption, atlas debug views (UV1 / coverage / lightmap-only / wireframe), per-texel ray visualisation, 3D surfel debug renderer, joint-bilateral GPU denoise. Pulls in Prowl.Clay + Prowl.Unwrapper to load Sponza and auto-unwrap UV1.

- **`Photonic.Sample.Raylib`** -- minimal Raylib-cs sample. Same scene + auto-unwrap pipeline, simpler renderer, per-texel only with default options. Read this first if you're learning the public API.

Run with `dotnet run -c Release --project Samples/Photonic.Demo` or `--project Samples/Photonic.Sample.Raylib`.

## License

MIT. See `LICENSE`.
