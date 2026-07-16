# ShaderDef

ShaderDef is Graphite's shader authoring system: a ShaderLab-inspired markup format (`Shader { Pass { ... } }`) that wraps a Slang shader in declarative render state, variant axes, and material properties. For the markup syntax itself see [`Core/ShaderSpec.md`](Core/ShaderSpec.md). This document covers the library side: the object model, how variants and pass state actually work at runtime, how the Compiler project turns Slang source into `ShaderDescription`s, and how the two projects fit into Graphite as a whole.

The library is split into two assemblies:

- **`ShaderDef` (Core)** - the parsed data model (`ShaderDefinition`, `ShaderPass`, `PassState`, `ShaderProperty`), the variant runtime (`Variant`, `VariantSet<T>`, `KeywordState`, `KeywordMap`), and the `IShaderCompiler` seam. Core has no dependency on Slang; it only knows how to hold parsed data and select among already-compiled variants.
- **`ShaderDef.Compiler`** - the parser (`ShaderParser`, `ShaderTokenizer`) and the only implementation of `IShaderCompiler` (`SlangShaderCompiler`), which drives the Slang compiler and its per-backend `CompilerModule`s (`VulkanCompiler`, `MetalCompiler`, `WebGPUCompiler`, and the deprecated D3D11 path).

Core can ship in a build with no Slang dependency and no compiler at all - it just can't compile anything it doesn't already have a `Variant` for. Compiler is what an editor, asset pipeline, or dev build links in to actually turn `.shaderdef` source into shaders.

---

## Table of Contents

- [Object Model](#object-model)
- [Binding a Shader to a Device](#binding-a-shader-to-a-device)
- [Variants and Keywords](#variants-and-keywords)
  - [How a Pass Resolves Its Active Variant](#how-a-pass-resolves-its-active-variant)
  - [VariantSet\<T\>](#variantsett)
- [Pass State](#pass-state)
- [Snapshots (Serializing Compiled Shaders)](#snapshots-serializing-compiled-shaders)
- [The Compiler](#the-compiler)
  - [Parsing](#parsing)
  - [IShaderCompiler and SlangShaderCompiler](#ishadercompiler-and-slangshadercompiler)
  - [Variant Axis Discovery](#variant-axis-discovery)
  - [Building a Variant](#building-a-variant)
  - [CompilerModule and Per-Backend Reflection](#compilermodule-and-per-backend-reflection)
- [Binding a Shader for Drawing](#binding-a-shader-for-drawing)
- [Where This Slots Into Graphite](#where-this-slots-into-graphite)

---

## Object Model

Parsing a `.shaderdef` file produces a `ShaderDefinition`: a name, a fallback shader name, a list of `ShaderProperty` (material-facing default values), and one or more `ShaderPass`. Each `ShaderPass` holds its `PassState` (fixed-function render state), its optional name/tags, and the raw Slang source between `SLANGPROGRAM`/`ENDSLANG`, verbatim and uninterpreted by the parser.

This is all just data at this point - `ShaderDefinition.Create` is what turns it into something you can actually draw with.

## Binding a Shader to a Device

```csharp
ShaderDefinition def = ShaderParser.Parse(source);
def.Create(device, compiler, CompileMode.OnDemand);
```

`Create` walks every pass and calls `ShaderPass.Bind`, which:

1. Asks the compiler for the pass's variant axes (`IShaderCompiler.GetAxes`) - empty if no compiler is attached.
2. Expands the axes into every keyword combination (`VariantCombos.Generate`), an odometer over each axis's possible values.
3. Builds a `KeywordMap` over all of those combinations for fast lookup, and sets the pass's active keyword state to the first combination.
4. If `CompileMode.All` was requested, compiles every combination immediately; otherwise nothing is compiled until something asks for the active variant.

A `ShaderDefinition` can also be re-created from a previously captured `ShaderSnapshot` (see below) with `Create(device, snapshot, compiler)`, which restores whichever variants were already compiled without needing to re-run variant discovery - a compiler is only needed if you want missing or wrong-backend variants to be able to compile on demand.

Passing `compiler: null` still produces a fully-formed pass - it just has zero variant axes and can never compile a new variant. This is the shape you get from `Create(device, snapshot)` with no compiler: a shader that plays back whatever was baked ahead of time, with no path to grow beyond it.

## Variants and Keywords

A **keyword** (`Keyword`) is an interned name/value pair - for example `Lighting=Realtime`. A **variant axis** (`VariantSpace`) is the set of values a single keyword name can take (`Lighting` might have `Realtime`, `Baked`, `Mixed`). A **variant** (`Variant`) is one fixed combination of keywords across every axis, plus whatever per-backend `ShaderDescription`s have actually been compiled for it - a variant can exist with zero, one, or several backends compiled, and it holds no reference to the compiler that produced it, so it is safe to serialize.

Axes are not declared in the ShaderDef markup itself - they come from the Slang source, reflected by the compiler (see [Variant Axis Discovery](#variant-axis-discovery) below). ShaderDef's job at the Core layer is purely to enumerate combinations of whatever axes it's told about and pick the right one at runtime; it has no opinion on where axes come from.

### How a Pass Resolves Its Active Variant

Each `ShaderPass` keeps a single `KeywordState` representing "what's currently selected" and an index into its `_variants` array for the currently active combination. Calling `SetKeyword`/`SetKeywords` (or the `Try*` variants, which don't throw on an unrecognized keyword name) updates that state and re-resolves:

```csharp
pass.SetKeyword(new Keyword("Lighting", "Baked"));
GraphicsProgram program = pass.ActiveVariant...
```

Resolution goes through `KeywordMap.FindNearest`: it first looks for an exact hash match for the current keyword state (`Find`), and if the exact combination was never compiled, it falls back to whichever known variant shares the most keyword slots (`MatchScore`) - there is always at least one variant in the map, so this never fails, it just degrades to the closest thing available. This means a pass can have only a handful of its full combinatorial variant space actually compiled and still resolve to *something* reasonable for combinations you haven't asked for yet.

Once an index is resolved, `ShaderPass.Resolve` decides what to actually return:

- If the variant is already compiled for the device's backend, return it as-is.
- Otherwise, if a compiler is attached, compile it now (`Compile`), cache the result in `_variants`, and return it.
- Otherwise, if a variant exists but for the wrong backend, return it anyway (the caller will fail later trying to use it - see `ResolveProgram` below) - a pass with no compiler can't materialize a variant it doesn't already have.

`ShaderPass.CompileAll` walks every combination eagerly rather than waiting for `Resolve` to hit them one at a time; `CompiledCount`/`AvailableCount`/`AllAvailable`/`AllCompiled` let you inspect how much of the space is actually filled in without forcing compilation.

## Pass State

`PassState` is a bag of nullable fields covering rasterizer, depth, stencil, blend, and color-mask state - one field per concept in the ShaderDef render-state commands (`Cull`, `ZTest`, `Blend`, `Stencil { ... }`, etc). Every field is nullable because a `.shaderdef` file only specifies what it cares about; anything left unset should fall through to a caller-supplied base state rather than some arbitrary default baked into the parser.

Three methods (`ToBlendState`, `ToDepthStencilState`, `ToRasterizerState`) collapse a `PassState` onto a base `BlendStateDescription`/`DepthStencilStateDescription`/`RasterizerStateDescription`, using `??` so an unset field just inherits from the base. This is why `CommandBufferExtensions.SetShader` takes base state descriptions as parameters - the pass state is always applied as an overlay, never as a complete pipeline description on its own.

`PassState.Apply(other)` merges two `PassState`s the same way (self wins over `other` on a per-field basis) and is how the parser combines the several individual commands it parses in a pass body (each render-state command like `Cull Back` produces its own tiny `PassState` with just that one field set) into the pass's single final `PassState`.

## Snapshots (Serializing Compiled Shaders)

`ShaderDefinition.Snapshot()` walks every pass and captures a `PassSnapshot` (the pass's variant axes plus whichever `Variant`s are currently populated - possibly a subset of the full combinatorial space, "spotty" capture is expected). This is the mechanism for baking compiled shaders ahead of time: compile what you need (or `CompileAll`), snapshot it, serialize the snapshot, and later reconstruct a `ShaderDefinition` from the same parsed source plus the snapshot via `Create(device, snapshot, compiler)` with no compiler required unless you want the ability to fill in gaps later.

## The Compiler

### Parsing

`ShaderParser` is a static, purely functional parser over `ShaderTokenizer`'s token stream. It has no dependency on Slang - it just turns ShaderDef markup text into `ShaderDefinition`/`ShaderPass`/`PassState`/`ShaderProperty` values. Render-state and stencil commands are each parsed into a small standalone `PassState` and folded together with `PassState.Apply` (see `FromSeveral`); an unrecognized identifier where a command was expected stops the loop and is reported as `Unknown command`. The `SLANGPROGRAM`/`ENDSLANG` block is captured as a raw substring - the parser never looks inside it.

### IShaderCompiler and SlangShaderCompiler

`IShaderCompiler` is the seam Core depends on and Compiler implements:

```csharp
IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass);
ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend);
```

`SlangShaderCompiler` is the only implementation. A compiler instance owns a Slang `Session` (`BeginSession`/`EndSession`) and one or more registered `CompilerModule`s, one per target backend (Vulkan, Metal, WebGPU; D3D11 is present but explicitly unsupported - see the OpenGL/legacy-backend note in the repo's Graphite guidance, D3D11 is in the same boat: get it to compile, nothing more). Reuse a single compiler instance across many shaders in the same session to keep its loaded-module cache warm.

### Variant Axis Discovery

Axes aren't declared in ShaderDef markup - they're reflected out of the pass's own Slang source by `VariantReflection.CollectVariantSpaces`. The convention: an `extern` field tagged `[VariantAxis]` in the shader source (or in a module it transitively pulls in) becomes an axis; its declared type must be `bool` (axis values `"false"`/`"true"`) or an enum (axis values are the enum's case names). Anything else throws - only bool and enum axes are supported.

`SlangShaderCompiler.Prepare` (private, cached per-`ShaderPass`) loads the pass's inline Slang as its own module, finds its entrypoints, and runs variant reflection to get both the axis list and the set of modules that need to be linked in for every variant (a module only gets linked in if it actually declares an extern matching a discovered axis - unrelated modules loaded earlier in the same session are filtered out).

### Building a Variant

For a given `(pass, combo, backend)`, `Compile`:

1. Reuses the cached `Prepared` composite (entrypoints + axis-declaring modules) from `Prepare`.
2. Synthesizes a tiny Slang module on the fly (`VariantGenerator.BuildSpecializationModule`) that exports one `static const` per axis, set to that combo's value (`export public static const Lighting Lighting = Lighting.Realtime;` for an enum axis, or a bare `true`/`false` for a bool axis). This is what actually resolves the `extern` declarations the shader source references.
3. Also links in a `UVOrigin` specialization module (`UVOriginTopLeftModule`/`UVOriginBottomLeftModule`) so shader source can read a backend-appropriate `IsUVOriginTopLeft` constant without the pass needing to care - Vulkan is treated as top-left, everything else bottom-left (`IsBackendTopLeft`).
4. Composes and links all of the above into one `ComponentType`, then hands it to the `CompilerModule` registered for the requested backend.

Each distinct combo gets its own uniquely-named specialization module (`__Variant_<id>_<id>...`) specifically because the Slang session caches loaded modules by name - reusing a name across combos would silently reuse the first combo's constants for every later one.

### CompilerModule and Per-Backend Reflection

`CompilerModule` is the per-backend seam: it owns the Slang `TargetDescription` (profile + output format) and turns a linked `ComponentType` into a `ShaderDescription` (`CompileForTarget`). Each implementation (`VulkanCompiler`, `MetalCompiler`, `WebGPUCompiler`, `DXCompiler`) is responsible for reflecting Slang's parameter layout into Graphite's `ResourceLayoutDescription`s in whatever shape that backend actually expects - for example `DXCompiler.Reflect` walks parameter blocks and register classes to fold Slang's HLSL register/space layout into Graphite's binding model, differing between shader-model 5.0 (FXC/D3D11, no register spaces) and 5.1+ (native register spaces). A `ShaderDescription` coming out of `CompileForTarget` deliberately has no fixed-function state (blend/depth/rasterizer) attached - that's `PassState`'s job, applied later when the pass is actually resolved for drawing.

## Binding a Shader for Drawing

`CommandBufferExtensions.SetShader(commandBuffer, pass, ...)` is the usual entry point at draw time:

```csharp
commandBuffer.SetShader(pass);
```

This calls `ShaderPass.ResolveProgram`, which resolves the active variant (compiling on demand if needed and possible), overlays the pass's `PassState` onto the given base blend/depth/rasterizer descriptions, and creates (or reuses, via a per-pass `_programCache` keyed by active variant index) a `GraphicsProgram` from the device's `ResourceFactory`. The overload with no base-state arguments uses library defaults (`BlendStateDescription.SingleDisabled`, `DepthStencilStateDescription.DepthOnlyLessEqual`, back-face culling with clockwise front faces) - the same defaults `PassState`'s own null-coalescing falls back to when a `.shaderdef` file leaves a field unset.

## Graphite Integration

- **Core has no Slang dependency.** A build that only ever plays back baked `ShaderSnapshot`s can link Core alone and never touch the Compiler project or the Slang native library.
- **The Compiler project is the only thing that knows what a `.shaderdef` file's Slang source means.** Parsing, variant discovery, and per-backend reflection all live there; Core only ever deals in already-resolved `VariantSpace`s and `Variant`s.
- **A `ShaderPass` is deliberately backend-agnostic until `Bind`.** The same parsed `ShaderDefinition` can be created against a Vulkan, Metal, or WebGPU device; the compiler's registered `CompilerModule`s are what actually differ per backend.
- **Render state overlay (`PassState`) is independent of shader compilation.** A `.shaderdef` file's `Cull`/`ZTest`/`Blend`/`Stencil` commands never touch Slang or the compiler at all - they're pure data merged onto whatever base pipeline state the caller (typically a render pipeline) supplies.
- **Samples and tests both go through the same `ShaderParser.Parse` -> `SlangShaderCompiler` -> `ShaderDefinition.Create` -> `CommandBufferExtensions.SetShader` pipeline** (see `Samples/PBRRenderer/ShaderDefLoader.cs` and `Samples/Shared/ShaderLoader.cs`) - there is no separate "asset pipeline" shortcut, loading a shader at runtime and loading one in an offline tool are the same three calls.
