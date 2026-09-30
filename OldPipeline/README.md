# OldPipeline - Reference Snapshot of the Pre-Graphite Render Pipeline

This directory is a **read-only reference snapshot** of Prowl's GLSL forward+ PBR render pipeline, copied from
`origin/main` at commit `3a87e329` ("Fixed project version 0.0.1 not being recognized", 2026-09-29 refresh).
It is **not part of the build**. It exists so a new pipeline can be built from scratch while still being able to look
up exactly how the old one implemented any feature.

- **Documentation:** [docs/README.md](docs/README.md) - A-Z documentation of every pipeline feature, with diagrams and
  links into the source below.
- **Rebuild checklist:** [PROGRESS.md](PROGRESS.md) - every feature as a to-do item, linked to its documentation.
- **Known issues:** [docs/reference/known-issues.md](docs/reference/known-issues.md) - bugs and dead code found in the
  old pipeline, so they are not ported by accident.
- **File map:** [docs/reference/file-map.md](docs/reference/file-map.md) - every file here mapped to its doc page.

## What was captured

Everything whose code or assets feed 3D rendering. Core systems that stay in the engine (GL device layer, textures,
render textures, meshes, `Shader`/`Material` resources, runtime UI components, physics, audio) were left out except
where a pipeline-relevant file lives inside them (`Graphics.cs`, the command buffer files, `ShaderParser.cs`,
`Scene.cs`), which are included for reference.

Layout mirrors the repository tree:

| Path | Contents |
|---|---|
| `Prowl.Runtime/Rendering/**` | pipeline, light system, BVH, shadow atlas, GI binding, image effects, shader pass/keyword types |
| `Prowl.Runtime/Components/` | `Camera`, lights, mesh/skinned/line/sprite/text renderers, terrain, particles, fog volumes, wind zones, light probe groups |
| `Prowl.Runtime/*.cs` | renderable adapters (`MeshRenderable`, `SkinnedMeshRenderable`, `InstancedMeshRenderable`, `ProceduralInstancedRenderable`) |
| `Prowl.Runtime/Assets/Defaults/**` | every default shader and include, default materials, SMAA LUTs, sky dome, default textures and editor icons |
| `Prowl.Runtime/Assets/brdf_lut.brdf` | precomputed BRDF LUT |
| `Prowl.Runtime/Graphics/**` | command buffer, executor, opcodes, property apply, `Graphics` (core, for reference) |
| `Prowl.Runtime/AssetImporting/` | `ShaderParser` (core, for reference), `LightmapUVGenerator` |
| `Prowl.Runtime/Resources/Scene.cs` | fog, ambient, skybox, baked lighting and bake settings live here (core, for reference) |
| `Prowl.Editor/AssetsDatabase/Lightmapping/**` | lightmap bake service, probe tetrahedralizer, robust predicates |
| `Prowl.Editor/GUI/**` | effect inspectors, light probe tools, environment panel, preview renderer, render texture editor |
| `Prowl.Runtime.Test/**` | pipeline-related tests (BVH, shadow matrix, shadow focus, SMAA assets, render collection, camera gather) |
| `BRDFGen/` | offline BRDF LUT generator tool |

## Refreshing the snapshot

The snapshot was produced by copying the files above verbatim from `origin/main` (`git show origin/main:<path>`).
To refresh, repeat that for the same file list (see the file map) and re-verify the documentation against the diff.
