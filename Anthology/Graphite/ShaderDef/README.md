# ShaderDef

ShaderDef is Graphite's shader authoring system: a ShaderLab-inspired markup format (`Shader { Pass { ... } }`) that wraps GLSL shader code in declarative render state, variant axes, and material properties. For the markup syntax itself see [`Core/ShaderSpec.md`](Core/ShaderSpec.md). For usage examples and the full API reference see the [ShaderDef docs](../docs/api/shaderdef.md). This document covers the library side: the object model, how variants and pass state actually work at runtime, how the Compiler project turns GLSL source into `ShaderDescription`s, and how the two projects fit into Graphite as a whole.

The library is split into two assemblies:

- **`ShaderDef` (Core)** - the parser (`ShaderParser`, `ShaderTokenizer`), the parsed data model (`ShaderDefinition`, `ShaderPass`, `PassState`, `ShaderProperty`), the variant runtime (`Variant`, `VariantSpace`, `Keyword`), and the `IShaderCompiler` seam. Core has no compiler dependency, it holds parsed data and selects among already compiled variants.
- **`ShaderDef.Compiler`** - `GlslShaderCompiler`, the implementation of `IShaderCompiler`. It compiles GLSL to SPIR-V through shaderc (glslang) and reflects the result into `ShaderDescription`s. Another compiler can be plugged in behind the same seam.

Core can ship in a build with no compiler at all, it just can't compile anything it doesn't already have a `Variant` for. Compiler is what an editor, asset pipeline, or dev build links in to turn `.shader` source into shaders.

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
  - [IShaderCompiler and GlslShaderCompiler](#ishadercompiler-and-glslshadercompiler)
  - [Variant Axes](#variant-axes)
  - [Building a Variant](#building-a-variant)
  - [Reflection](#reflection)
- [Binding a Shader for Drawing](#binding-a-shader-for-drawing)
- [Where This Slots Into Graphite](#where-this-slots-into-graphite)

---

## Object Model

Parsing a `.shaderdef` file produces a `ShaderDefinition`: a name, a fallback shader name, a list of `ShaderProperty` (material-facing default values), and one or more `ShaderPass`. Each `ShaderPass` holds its `PassState` (fixed-function render state), its optional name/tags, and the raw source between `GLSLPROGRAM`/`ENDGLSL`, verbatim and uninterpreted by the parser, plus the shader name and document line used for compile errors.

This is all just data at this point - `ShaderDefinition.Create` is what turns it into something you can actually draw with.

## Binding a Shader to a Device

See [Quick example](../docs/api/shaderdef.md#quick-example) and [Common patterns](../docs/api/shaderdef.md#common-patterns) in the ShaderDef docs.

`Create` walks every pass and calls `ShaderPass.Bind`, which:

1. Asks the compiler for the pass's variant axes (`IShaderCompiler.GetAxes`) - empty if no compiler is attached.
2. Expands the axes into every keyword combination (`VariantCombos.Generate`), an odometer over each axis's possible values.
3. Builds the lookup from keywords to the mixed radix variant key, key 0 being every axis at its first value.
4. If `CompileMode.All` was requested, compiles every combination immediately; otherwise nothing is compiled until something asks for the active variant.

A `ShaderDefinition` can also be re-created from a previously captured `ShaderSnapshot` (see below) with `Create(device, snapshot, compiler)`, which restores whichever variants were already compiled without needing to re-run variant discovery - a compiler is only needed if you want missing or wrong-backend variants to be able to compile on demand.

Passing `compiler: null` still produces a fully-formed pass - it just has zero variant axes and can never compile a new variant. This is the shape you get from `Create(device, snapshot)` with no compiler: a shader that plays back whatever was baked ahead of time, with no path to grow beyond it.

## Variants and Keywords

A **keyword** (`Keyword`) is an interned name/value pair - for example `Lighting=Realtime`. A **variant axis** (`VariantSpace`) is the set of values a single keyword name can take (`Lighting` might have `Realtime`, `Baked`, `Mixed`). A **variant** (`Variant`) is one fixed combination of keywords across every axis, plus whatever per-backend `ShaderDescription`s have actually been compiled for it - a variant can exist with zero, one, or several backends compiled, and it holds no reference to the compiler that produced it, so it is safe to serialize.

Axes are not declared in the ShaderDef markup itself - they come from `#pragma variant` lines in the program source, read by the compiler (see [Variant Axes](#variant-axes) below). ShaderDef's job at the Core layer is purely to enumerate combinations of whatever axes it's told about and pick the right one at runtime; it has no opinion on where axes come from.

### How a Pass Resolves Its Active Variant

A pass holds no keyword state. `ShaderPass.GetKey(ReadOnlySpan<Keyword>)` turns keywords into a mixed-radix variant index (unknown names skipped, unknown values throw), and `cmd.SetShader(pass, keywords)` or `cmd.SetShader(pass, key)` binds that variant, so passes sharing a `ShaderPass` never overwrite each other. `Axes` lists the pass's `VariantSpace`s for UI, and `GetVariant(key)` returns the variant.

See [Keywords and variants](../docs/api/shader-programs.md#keywords-and-variants) for usage.

`ShaderPass.Resolve` decides what to return for a key:

- If the variant is already compiled for the device's backend, return it as-is.
- Otherwise, if a compiler is attached, compile it now on the calling thread, cache the result, and return it. A failure is recorded per variant and not retried until the source or the compiler's `SessionVersion` changes, and `CompileFailed` fires once.
- Otherwise, or after a failure, return the fallback variant given to `Create` if it is compiled for this backend, else throw.

`ShaderPass.CompileAll` walks every combination eagerly rather than waiting for `Resolve` to hit them one at a time; `CompiledCount`/`AvailableCount`/`AllAvailable`/`AllCompiled` let you inspect how much of the space is actually filled in without forcing compilation.

## Pass State

`PassState` is a bag of nullable fields covering rasterizer, depth, stencil, blend, and color-mask state - one field per concept in the ShaderDef render-state commands (`Cull`, `ZTest`, `Blend`, `Stencil { ... }`, etc). Every field is nullable because a `.shaderdef` file only specifies what it cares about; anything left unset should fall through to a caller-supplied base state rather than some arbitrary default baked into the parser.

Three methods (`ToBlendState`, `ToDepthStencilState`, `ToRasterizerState`) collapse a `PassState` onto a base `BlendStateDescription`/`DepthStencilStateDescription`/`RasterizerStateDescription`, using `??` so an unset field just inherits from the base. This is why `CommandBufferExtensions.SetShader` takes base state descriptions as parameters - the pass state is always applied as an overlay, never as a complete pipeline description on its own.

`PassState.Apply(other)` merges two `PassState`s the same way (self wins over `other` on a per-field basis) and is how the parser combines the several individual commands it parses in a pass body (each render-state command like `Cull Back` produces its own tiny `PassState` with just that one field set) into the pass's single final `PassState`.

## Snapshots (Serializing Compiled Shaders)

`ShaderDefinition.Snapshot()` walks every pass and captures a `PassSnapshot` (the pass's variant axes plus whichever `Variant`s are currently populated - possibly a subset of the full combinatorial space, "spotty" capture is expected). This is the mechanism for baking compiled shaders ahead of time: compile what you need (or `CompileAll`), snapshot it, serialize the snapshot, and later reconstruct a `ShaderDefinition` from the same parsed source plus the snapshot via `Create(device, snapshot, compiler)` with no compiler required unless you want the ability to fill in gaps later.

## The Compiler

### Parsing

`ShaderParser` is a static, purely functional parser over `ShaderTokenizer`'s token stream. It turns ShaderDef markup text into `ShaderDefinition`/`ShaderPass`/`PassState`/`ShaderProperty` values. Render-state and stencil commands are each parsed into a small standalone `PassState` and folded together with `PassState.Apply` (see `FromSeveral`); an unrecognized identifier where a command was expected stops the loop and is reported as `Unknown command`. The `GLSLPROGRAM`/`ENDGLSL` block is captured as a raw substring - the parser never looks inside it.

### IShaderCompiler and GlslShaderCompiler

`IShaderCompiler` is the seam Core depends on and Compiler implements:

See [IShaderCompiler](../docs/api/shaderdef.md#ishadercompiler) for the interface members.

`GlslShaderCompiler` is thread safe, one instance can serve every shader. It compiles with Prism, Prowl's managed GLSL compiler. `GlslCompilerOptions` sets the host include resolver, optimization, the SPIR-V cache directory and global defines. `CompileProgram` returns a `GlslProgram` with the description, the compute thread group size and the warnings. `Invalidate` bumps `SessionVersion` after included files change.

### Variant Axes

Axes come from pragmas in the program or anything it includes, found by scanning the source and every include whether or not the include is active:

- `#pragma variant NAME` is a toggle with values `false`/`true`. `NAME` is defined when on.
- `#pragma variant NAME A B C` is an enum. `NAME_A` (and so on) is defined for the chosen value.
- `#pragma variant global ...` gives the axis `VariantScope.Global`, set by the renderer or a view, never by a material.
- `#pragma global TYPE NAME...` declares globals a material pass reads, like `#pragma global float _Wetness`. Their order is their specialization constant ids.

### Building a Variant

For a given `(pass, combo)`:

1. The program block is split into its `Shared`, `Vertex`, `Fragment` or `Compute` sections. Each stage is the shared section plus its own, behind `#version 460` and `#line` markers that point back at the `.shader` document.
2. Each stage is preprocessed with the variant defines and `GRAPHITE_VERTEX`, `GRAPHITE_FRAGMENT` or `GRAPHITE_COMPUTE`. Includes resolve through a callback: `Graphite/...` from the compiler's embedded library, anything else through the host. `#pragma once` is handled in the callback.
3. A pass with a `MaterialAbi`, set by the renderer on the passes of its material shaders, has its loose uniforms replaced: each property becomes a macro reading the material record, a texture property also gets `NAME_Texture`, `NAME_Sampler` and `NAME_ST`, each global a macro reading its offset from a specialization constant, and any other loose uniform an error where it is read. A stage reading any of them starts with the ABI's declarations it lacks. Resources without a set go to the ABI's user set. A material texture is a constructed `sampler2D`, so a function taking one takes `NAME_Texture` and `NAME_Sampler` instead.
4. The front end (`GlslFrontEnd`) scans the top level declarations of every stage and lays out the program: loose uniforms of all stages go into one std140 block named `$Global` with one layout, descriptor bindings are assigned by name across stages, varyings get locations by name, vertex inputs and fragment outputs get locations in declaration order. Explicit layout qualifiers win. `gl_VertexID` and `gl_InstanceID` map to their Vulkan forms.
5. Each rewritten stage compiles to SPIR-V 1.5 for Vulkan 1.2.

Errors are `ShaderCompileException`s whose message lists every diagnostic as `file:line: error: text`, with document lines for the pass and the include's own lines for includes.

### Reflection

`SpirvReflector` reads the SPIR-V of every stage: the entry point's interface list gives the resources each stage really uses, descriptor types and block offsets come from the SPIR-V types and decorations, and names come from the front end (optimized SPIR-V has none), keyed by the bindings and locations it assigned. Uniform blocks list every member as a `UniformBlockField`, structs flattened with dotted names and arrays carrying their length and stride. A `ShaderDescription` deliberately has no fixed-function state (blend/depth/rasterizer) attached - that's `PassState`'s job, applied later when the pass is actually resolved for drawing.

## Binding a Shader for Drawing

`CommandBufferExtensions.SetShader(commandBuffer, pass, ...)` is the usual entry point at draw time:

See [Quick example](../docs/api/shaderdef.md#quick-example) for usage.

This calls `ShaderPass.ResolveProgram`, which resolves the variant for the key (compiling on demand if needed and possible), overlays the pass's `PassState` onto the given base blend/depth/rasterizer descriptions, and creates (or reuses, via a per-pass `_programCache` keyed by variant index) a `GraphicsProgram` from the device's `ResourceFactory`. The overload with no base-state arguments uses library defaults (`BlendStateDescription.SingleDisabled`, `DepthStencilStateDescription.DepthOnlyLessEqual`, back-face culling with clockwise front faces) - the same defaults `PassState`'s own null-coalescing falls back to when a `.shaderdef` file leaves a field unset.

## Graphite Integration

- **Core has no compiler dependency.** A build that only ever plays back baked `ShaderSnapshot`s can link Core alone and never touch the Compiler project or the shaderc native library.
- **The Compiler project is the only thing that knows what a pass's program source means.** Variant discovery, the GLSL front end and reflection all live there, Core only ever deals in already-resolved `VariantSpace`s and `Variant`s.
- **A `ShaderPass` is deliberately backend-agnostic until `Bind`.** The GLSL compiler targets Vulkan only and rejects other backends.
- **Render state overlay (`PassState`) is independent of shader compilation.** A `.shaderdef` file's `Cull`/`ZTest`/`Blend`/`Stencil` commands never touch the compiler at all - they're pure data merged onto whatever base pipeline state the caller (typically a render pipeline) supplies.
- **Samples and tests both go through the same `ShaderParser.Parse`, `GlslShaderCompiler`, `ShaderDefinition.Create` -> `CommandBufferExtensions.SetShader` pipeline** (see `Samples/Shared/ShaderLoader.cs`) - there is no separate "asset pipeline" shortcut, loading a shader at runtime and loading one in an offline tool are the same three calls.
