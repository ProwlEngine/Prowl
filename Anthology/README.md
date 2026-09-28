# Anthology

The collection of modular libraries that power the [Prowl game engine](https://github.com/ProwlEngine/Prowl).

Each library ships as its own `Prowl.*` NuGet package and can be used independently, but
they live and version together in this one repository so that a change spanning several of
them lands in a single commit, no cross-repo version juggling, no publish chain.

## Libraries

| Folder       | Package           | Purpose                          |
| ------------ | ----------------- | -------------------------------- |
| `Vector`     | Prowl.Vector      | Math and geometry primitives     |
| `Scribe`     | Prowl.Scribe      | TrueType fonts, layout, Markdown |
| `Quill`      | Prowl.Quill       | GPU 2D vector graphics           |
| `Paper`      | Prowl.Paper       | Immediate-mode UI framework      |
| `Scaffold`   | Prowl.Scaffold    | Incremental UI layout engine     |
| `Quire`      | Prowl.Quire       | Markdown parser                  |
| `Origami`    | Prowl.Origami     | UI Widgets/Components for paper  |
| `Echo`       | Prowl.Echo        | Serialization                    |
| `Clay`       | Prowl.Clay        | 3D Model Loading (GLTF/FBX/Obj)  |
| `Crumb`      | Prowl.Crumb       | A simple lightweight Tokenizer   |
| `Photonic`   | Prowl.Photonic    | Progressive CPU Lightmapper      |
| `Unwrapper`  | Prowl.Unwrapper   | Mesh Unwrapping for UV's 	      |
| `Rosetta`    | Prowl.Rosetta     | Localization utilities           |
| `Slang`      | Prowl.Slang       | Bindings for the Sland Compiler  |
| `Graphite`   | Prowl.Graphite    | Low-level GPU graphics (Vulkan / D3D11); also `.Compiler`, `.ShaderDef`, `.Variants` |
| `Recast`     | Prowl.Recast      | DotRecast fork for Navmesh generation and pathfinding |
| `Aperture`   | Prowl.Aperture    | Image loading (BMP/DDS/EXR/GIF/HDR/ICO/JPEG/PNG/PNM/PSD/RAW/TGA/TIFF/WebP) |
| `Motion`     | Prowl.Motion      | Skeletal animation, graphs, IK and humanoid retargeting |

## Versioning

The whole family ships under one version, set once in [`Directory.Build.props`](Directory.Build.props).
Bump that single `<Version>` and tag the commit `vX.Y.Z`; CI builds, packs and pushes every
package at that version.

## Repository layout

Every library is a self-contained folder. Shared build and packaging rules live in the
root `Directory.Build.props`, so individual project files stay small.

History for each library is preserved under its folder, so `git log -- Scribe/` shows the
full past of that library.
