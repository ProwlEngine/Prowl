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
| `Origami`    | Prowl.Origami     | UI Widgets/Components for paper  |
| `Echo`       | Prowl.Echo        | Serialization                    |
| `Clay`       | Prowl.Clay        | 3D Model Loading (GLTF/FBX/Obj)  |
| `Crumb`      | Prowl.Crumb       | A simple lightweight Tokenizer   |
| `Photonic`   | Prowl.Photonic    | Progressive CPU Lightmapper      |
| `Unwrapper`  | Prowl.Unwrapper   | Mesh Unwrapping for UV's 	      |
| `Wicked`     | Prowl.Wicked      | High Level Networking Library    |
| `Rosetta`    | Prowl.Rosetta     | Localization utilities           |
| `Aspect`     | Prowl.Aspect      | Tiny Aspect-Oriented Programming |
| `Drift`      | Prowl.Drift       | 2D Physics Engine                |
| `Slang`      | Prowl.Slang       | Bindings for the Sland Compiler  |

## Versioning

The whole family ships under one version, set once in [`Directory.Build.props`](Directory.Build.props).
Bump that single `<Version>` and tag the commit `vX.Y.Z`; CI builds, packs and pushes every
package at that version.

## Repository layout

Every library is a self-contained folder. Shared build and packaging rules live in the
root `Directory.Build.props`, so individual project files stay small.

History for each library is preserved under its folder, so `git log -- Scribe/` shows the
full past of that library.

## How it works

Develop Libraries independantly of this Repo, once their stable and near-finished, We merge them into here to make them official.

`scripts/Merge-Library.ps1` clones the source, runs
[`git-filter-repo`](https://github.com/newren/git-filter-repo) to rewrite every commit so the
files live under a `<Name>/` subfolder, then merges that rewritten history into Anthology with
`--allow-unrelated-histories`. The result: `git log -- Scribe/` shows the complete history of
Scribe, so commits stay sortable by package forever.

`git-filter-repo` (a single Python file) is bundled in `scripts/`, so only Python is required.

## Per-library steps

From the Anthology repo root, with a clean working tree:

```powershell
./scripts/Merge-Library.ps1 -Name Vector -Source ../Prowl.Vector
```

## After each fold-in: clean up the .csproj

The root `Directory.Build.props` now supplies the shared settings, so trim each library's
project file:

1. Remove `<Version>` so the unified version wins. **This matters** - a leftover `<Version>`
   would override the family version and could republish at an old number.
2. Remove now-redundant lines: `<Authors>`, `<PackageId>`, `<RootNamespace>`, `<TargetFrameworks>`
   (only if identical to the shared set), and the `<None Include="..\LICENSE" .../>` pack item
   (the shared LICENSE is packed centrally; leaving it causes a duplicate-file pack error).
3. Flip internal references from package to project, e.g. in Quill:
   ```xml
   <!-- was: <PackageReference Include="Prowl.Scribe" Version="1.0.2" /> -->
   <ProjectReference Include="..\..\Scribe\Scribe\Scribe.csproj" />
   ```
4. Keep: `<Title>`, `<Description>`, `<PackageTags>`, and the project's own `README` packing.

## Gotcha: duplicate project names

Most repos have a project literally named `Tests` (and `Samples`). Several `Tests.csproj` in
one solution collide on assembly name and output path. Rename each to `<Name>.Tests`
(e.g. `Scribe.Tests`) when cleaning up. `Prowl.Crumb` already uses `Prowl.Crumb.Tests`, so it
needs no rename.

## Gotcha: projects already named `Prowl.X`

The shared `PackageId` is `Prowl.$(AssemblyName)`, which assumes assemblies are named without
the prefix (`Echo` -> `Prowl.Echo`). `Prowl.Crumb` is already named with the prefix, so it must be renamed to `Crumb`.

## Verifying

```powershell
git log --oneline -- Vector/      # full Vector history, under its folder
dotnet build Anthology.slnx -c Release
```

## Still private / unfinished

Anything not ready for public release (e.g. anything still cooking) stays in its own repo and is
not folded in yet - merging publishes its entire history. Fold it in when it is ready to ship,
optionally squashed if its history should not be public.