# Prowl.Photonic

A CPU lightmap baker for the Prowl Game Engine. Imperative builder API: scene + meshes + lights in, baked HDR atlas pages out. Direct + indirect path-traced lighting, BVH4 + AVX2 SIMD acceleration, progressive accumulation.

## Features

- **Imperative builder API**
  - `BakeScene` + `BakeMesh.Builder` + `LightmapTarget` give you a `BeginScene` / `BeginMesh` / `End` flow
  - Per-instance world transform, per-target atlas placement (offset / scale into the bake UV layer)
  - Auto-atlas packer "drop a list of meshes in, get N atlas pages out"

- **Acceleration**
  - BVH4 with binary SAH + binned splits, collapsed to 4-wide nodes (SoA `Vector128` AABB packs, one SIMD ray-vs-4-AABB per inner-node visit)
  - AVX2 leaves: 8-triangle Moller-Trumbore in `Vector256` (`LoadUnsafe` over SoA edge arrays)
  - Parallel-for over rows, deterministic per-texel seeding

- **Lighting**
  - Directional, point, spot
  - Next-event estimation at every bounce hit
  - Pluggable `IAttenuation` (`InverseSquare`, `NormalizedQuadratic`, `Constant`)
  - `IncludeDirectLighting` for indirect-only bakes, or per-light `BakeDirect` for mixed lighting

- **Sparse sampling**
  - `SparseStride` traces one texel per NxN cell and interpolates the rest from the traced points, which converges dramatically faster on indirect

- **Artifact fixes**
  - Conservative SAT-based texel rasterisation with centroid bias and strict-inside-wins claim semantics
  - Samples pushed out of solid geometry, stepping past the nearest wall
  - Phong-tessellated shading position, with per-triangle fallback to flat where the smooth position is occluded
  - UV seam stitching and edge dilation

## Usage

```csharp
using var baker = new LightmapBaker();
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

## License

MIT. See `LICENSE`.
