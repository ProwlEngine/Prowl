# Shader Programs

The public types for compiled shaders: `GraphicsProgram`, `ComputeProgram`, their descriptions, the fixed-function state structs, and the keyword API on `ShaderPass`.

- [Overview](#overview)
- [Quick example](#quick-example)
- [ShaderProgram](#shaderprogram)
- [GraphicsProgram](#graphicsprogram)
- [ComputeProgram](#computeprogram)
- [ShaderDescription, ComputeDescription, ShaderStageDescription](#descriptions)
- [State descriptions](#state-descriptions)
- [Keywords and variants](#keywords-and-variants)
- [Common patterns](#common-patterns)
- [Pitfalls](#pitfalls)
- [See also](#see-also)

## Overview

A program is created once and used for many draws or dispatches. You get one by filling a description and calling `device.ResourceFactory.CreateGraphicsProgram` or `CreateComputeProgram`. The description usually comes from the ShaderDef compiler, but it is a plain struct and can be built by hand.

A `GraphicsProgram` includes its blend, depth/stencil and rasterizer state. It does not include the render target format or primitive topology; those are taken from the bound framebuffer and vertex source at draw time (see [internals](../internals/05-programs-and-pipelines.md)). A different blend mode therefore means a different program.

If you use ShaderDef, you rarely create programs yourself: `ShaderPass` creates and caches them, and `cmd.SetShader(pass)` hands you the right one for the current keywords.

## Quick example

Compile a Slang file and create a program, adapted from [ShaderLoader](../../Samples/Shared/ShaderLoader.cs):

```csharp
SlangShaderCompiler compiler = new();
compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
compiler.BeginSession(FileLoader.SearchDirectories, FileLoader.Load);

Memory<byte>? loaded = FileLoader.Load("Shader.slang");
string source = Encoding.UTF8.GetString(loaded!.Value.Span);
ShaderPass pass = new() { State = new PassState(), InlineSlang = source };
ShaderDescription description = compiler.Compile(pass, [], device.BackendType);

compiler.EndSession();

description.BlendState = BlendStateDescription.SingleDisabled;
description.DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual;
description.RasterizerState = new(FaceCullMode.Back, FrontFace.Clockwise, true, false);

GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);
```

Drawing with it, from the HelloTriangle sample:

```csharp
cmd.SetFramebuffer(target);
cmd.SetShader(program);
cmd.SetVertexSource(triangle);
cmd.DrawIndexed();
```

## ShaderProgram

[ShaderProgram](../../Graphite/Core/Shader/ShaderProgram.cs#L8) is the abstract base of both program kinds and derives from `GraphicsResource` (so it has `Name`, `IsDisposed`, `Dispose()`). You cannot construct it directly; its constructor is internal.

| Member | Signature | Description |
|---|---|---|
| `ResourceLayouts` | `IReadOnlyList<ResourceLayoutDescription> ResourceLayouts { get; }` | The descriptor-set layouts the shader declares, one entry per set that has resources. Copied from the description at creation. |
| `Name` | `string Name { get; set; }` | Debug name shown in graphics debuggers and the profiler. |
| `Dispose` | `void Dispose()` | Frees the program and everything cached on it. Idempotent. |

Each `ResourceLayoutDescription` has a `Set` index and an `Elements` array. Each element has a name, a `ResourceKind` (`UniformBuffer`, `StructuredBufferReadOnly`, `StructuredBufferReadWrite`, `TextureReadOnly`, `TextureReadWrite`, `Sampler`), the stages that use it, and a binding index; uniform buffers also list their fields. See [property sets](property-sets.md) for how these are filled at draw time.

## GraphicsProgram

[GraphicsProgram](../../Graphite/Core/Shader/GraphicsProgram.cs#L9) is a vertex (plus optional geometry/tessellation) and fragment shader with its fixed-function state.

| Member | Signature | Description |
|---|---|---|
| `Stages` | `IReadOnlyList<ShaderStages> Stages { get; }` | The stage of each compiled shader, in the order the description gave them. |
| `BlendState` | `BlendStateDescription BlendState { get; }` | Blend state baked into the program. |
| `DepthStencilState` | `DepthStencilStateDescription DepthStencilState { get; }` | Depth and stencil state. |
| `RasterizerState` | `RasterizerStateDescription RasterizerState { get; }` | Cull mode, front face, depth clip, depth bias. |
| `VertexLayouts` | `IReadOnlyList<VertexLayoutDescription> VertexLayouts { get; }` | Vertex input layouts the shader expects, one per vertex buffer slot. |

Bind it with `CommandBuffer.SetShader(GraphicsProgram)` ([source](../../Graphite/Core/CommandBuffer/CommandBuffer.State.cs#L11)). Binding the instance that is already bound is a no-op. Binding a new one keeps the merged properties from `SetProperties`; the next draw resolves them against the new program's layouts, so properties never need to be reapplied after a shader switch.

## ComputeProgram

[ComputeProgram](../../Graphite/Core/Shader/ComputeProgram.cs#L5) is a single compute shader.

| Member | Signature | Description |
|---|---|---|
| `ThreadGroupSizeX` | `uint ThreadGroupSizeX { get; }` | Threads per group along X, from the description. |
| `ThreadGroupSizeY` | `uint ThreadGroupSizeY { get; }` | Along Y. |
| `ThreadGroupSizeZ` | `uint ThreadGroupSizeZ { get; }` | Along Z. |

Bind it with `CommandBuffer.SetComputeShader(ComputeProgram)` and launch with `Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)`. The thread group size comes from the description; the library does not read it from the shader.

```csharp
ComputeProgram program = device.ResourceFactory.CreateComputeProgram(
    new ComputeDescription(stage, layouts, 16, 16, 1));

cmd.SetComputeShader(program);
cmd.Dispatch(width / 16, height / 16, 1);
```

## Descriptions

### ShaderDescription

[ShaderDescription](../../Graphite/Core/Shader/ShaderDescription.cs#L8) is a mutable struct with public fields, passed to `ResourceFactory.CreateGraphicsProgram(ShaderDescription)` (or the `ref` overload).

| Member | Signature | Description |
|---|---|---|
| `Stages` | `ShaderStageDescription[] Stages` | One entry per stage, each stage at most once. |
| `BlendState` | `BlendStateDescription BlendState` | Default-initialized by the one-argument constructor. |
| `DepthStencilState` | `DepthStencilStateDescription DepthStencilState` | Same. |
| `RasterizerState` | `RasterizerStateDescription RasterizerState` | Same. |
| `VertexLayouts` | `VertexLayoutDescription[] VertexLayouts` | Vertex input layouts. |
| `ResourceLayouts` | `ResourceLayoutDescription[] ResourceLayouts` | Descriptor set layouts. |
| constructor | `ShaderDescription(params ShaderStageDescription[] stages)` | Stages only, everything else default or empty. |
| constructor | `ShaderDescription(stages, blendState, depthStencilState, rasterizerState, vertexLayouts, resourceLayouts)` | Everything at once. |

The compiler returns a description with zeroed blend, depth and rasterizer state. Those three fields are set before creating a program, either directly or through `ShaderPass.ResolveProgram`. Left default, blending and depth testing are off and depth clip is disabled (`DepthClipEnabled` is false).

### ComputeDescription

| Member | Signature | Description |
|---|---|---|
| `Stage` | `ShaderStageDescription Stage` | Must be a compute stage. |
| `ResourceLayouts` | `ResourceLayoutDescription[] ResourceLayouts` | Descriptor set layouts. |
| `ThreadGroupSizeX/Y/Z` | `uint` | Thread group size. |
| constructor | `ComputeDescription(stage, resourceLayouts, threadGroupSizeX, threadGroupSizeY, threadGroupSizeZ)` | |

### ShaderStageDescription

| Member | Signature | Description |
|---|---|---|
| `Stage` | `ShaderStages Stage` | `Vertex`, `Geometry`, `TessellationControl`, `TessellationEvaluation`, `Fragment` or `Compute`. `ShaderStages` is a flags enum. |
| `ShaderBytes` | `byte[] ShaderBytes` | SPIR-V for Vulkan. |
| `EntryPoint` | `string EntryPoint` | Entry point name. The Vulkan compiler always emits `main`. |
| `Debug` | `bool Debug` | Debug flag. |
| constructors | `(ShaderStages, byte[], string)` and `(ShaderStages, byte[], string, bool)` | |

## State descriptions

All live in [Core/Pipeline](../../Graphite/Core/Pipeline). They are structs with public fields and static presets.

| Type | Fields | Presets |
|---|---|---|
| `BlendStateDescription` | `BlendFactor` (Color), `AttachmentStates` (BlendAttachmentDescription[]), `AlphaToCoverageEnabled` | `SingleOverrideBlend`, `SingleAlphaBlend`, `SingleAdditiveBlend`, `SingleDisabled`, `Empty` |
| `BlendAttachmentDescription` | `BlendEnabled`, `ColorWriteMask`, `SourceColorFactor`, `DestinationColorFactor`, `ColorFunction`, `SourceAlphaFactor`, `DestinationAlphaFactor`, `AlphaFunction` | `OverrideBlend`, `AlphaBlend`, `AdditiveBlend`, `Disabled` |
| `DepthStencilStateDescription` | `DepthTestEnabled`, `DepthWriteEnabled`, `DepthComparison`, `StencilTestEnabled`, `StencilFront`, `StencilBack`, `StencilReadMask`, `StencilWriteMask`, `StencilReference` | `DepthOnlyLessEqual`, `DepthOnlyLessEqualRead`, `DepthOnlyGreaterEqual`, `DepthOnlyGreaterEqualRead`, `Disabled` |
| `RasterizerStateDescription` | `CullMode`, `FrontFace`, `DepthClipEnabled`, `ScissorTestEnabled`, `DepthBiasEnabled`, `DepthBiasConstantFactor`, `DepthBiasSlopeFactor`, `DepthBiasClamp` | `Default` (back-face cull, clockwise, depth clip on), `CullNone` |
| `VertexLayoutDescription` | `Location`, `Stride`, `Elements`, `InstanceStepRate` | none |

`BlendStateDescription.AttachmentStates` is indexed by color attachment. If the framebuffer has more attachments than entries, the last entry is reused for the remaining ones.

```csharp
RasterizerStateDescription raster = new(FaceCullMode.Back, FrontFace.Clockwise, true, false);
BlendStateDescription blend = BlendStateDescription.SingleAlphaBlend;
DepthStencilStateDescription depth = DepthStencilStateDescription.DepthOnlyLessEqual;
```

## Keywords and variants

When a program comes from a ShaderDef pass, keywords choose which compiled variant is active. The members below are on `ShaderPass` ([source](../../ShaderDef/Core/ShaderPass.cs)); the file format and compiler are covered in [ShaderDef](shaderdef.md) and the model in [06 - Shader compiler](../internals/06-shader-compiler.md#the-variant-and-keyword-model).

### Keyword

`Keyword` is a readonly struct: `new Keyword(string name, string value)`. It exposes `Name`, `Value`, and interned integer ids used for fast comparison. Bool axes use the values `"false"` and `"true"`. Enum axes use the enum case names.

### ShaderPass keyword members

| Member | Signature | Description |
|---|---|---|
| `SetKeyword` | `void SetKeyword(Keyword keyword)` | Select one axis value. Throws `ArgumentException` if no axis has that name. |
| `SetKeywords` | `void SetKeywords(params Keyword[] keywords)` | Set several at once, validating all names first. Re-resolves once. |
| `TrySetKeyword` | `bool TrySetKeyword(Keyword keyword)` | Returns false and does nothing for an unknown axis. |
| `TrySetKeywords` | `bool TrySetKeywords(params Keyword[] keywords)` | Returns false and changes nothing if any name is unknown. |
| `ApplyKeywords` | `int ApplyKeywords(ReadOnlySpan<Keyword> keywords)` | Sets every known name, skips the rest, returns the number applied. For per-draw composition. |
| `ResetKeywords` | `void ResetKeywords()` | Back to the first value of every axis. |
| `Axes` | `IReadOnlyList<VariantSpace> Axes { get; }` | Axis names and values, for tooling. |
| `Count` | `int Count { get; }` | Total number of variants. |
| `CompiledCount` | `int CompiledCount { get; }` | Variants compiled for the device backend. |
| `AvailableCount` | `int AvailableCount { get; }` | Variants present for any backend. |
| `AllCompiled` | `bool AllCompiled { get; }` | True when nothing more needs compiling for this backend. |
| `CompileAll` | `void CompileAll()` | Compile every variant now. Needs a compiler. |
| `ActiveVariant` | `Variant ActiveVariant { get; }` | The selected variant, compiling on demand. |

All of them throw `InvalidOperationException` until the owning `ShaderDefinition.Create` has been called.

Bind the active variant with the extension methods in [CommandBufferExtensions](../../ShaderDef/Core/CommandBufferExtensions.cs):

| Member | Signature | Description |
|---|---|---|
| `SetShader` | `SetShader(this CommandBuffer, ShaderPass pass)` | Active variant over library-default base state. |
| `SetShader` | `SetShader(this CommandBuffer, ShaderPass pass, PassState overrideState)` | Same, with `overrideState` applied over the pass's own state. |
| `SetShader` | `SetShader(this CommandBuffer, ShaderPass pass, BlendStateDescription baseBlend, DepthStencilStateDescription baseDepth, RasterizerStateDescription baseRaster)` | You supply the base state the pass's `PassState` overlays. |

The defaults are `BlendStateDescription.SingleDisabled`, `DepthStencilStateDescription.DepthOnlyLessEqual`, and back-face culling with clockwise front faces.

Switching keywords between draws, from the PBR sample's bloom passes:

```csharp
private static readonly Keyword UpsampleOff = new("Upsample", "false");
private static readonly Keyword UpsampleOn = new("Upsample", "true");

_bloomShader.SetKeyword(UpsampleOff);
cmd.SetShader(_bloomShader);
cmd.SetVertexSource(_fullscreenSource);
cmd.SetProperties(_properties);
cmd.Draw(3);
```

## Common patterns

### Load a ShaderDef pass with every variant precompiled

From [Program.cs](../../Samples/PBRRenderer/Program.cs#L341):

```csharp
SlangShaderCompiler compiler = new();
compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));
compiler.BeginSession(FileLoader.SearchDirectories, FileLoader.Load);

Memory<byte>? loaded = FileLoader.Load("Shaders/Bloom.shader");
string source = Encoding.UTF8.GetString(loaded!.Value.Span);
ShaderDefinition def = ShaderParser.Parse(source);
def.Create(device, compiler, new Variant(), CompileMode.All);

compiler.EndSession();

ShaderPass pass = def.Passes![0];
```

With `CompileMode.All` the session can end before the first draw, and no compile ever happens on the render thread.

### Get one fixed program from a pass

For a single-variant shader `ShaderPass` can be skipped at draw time and the program built once, as [ShaderDefLoader](../../Samples/PBRRenderer/ShaderDefLoader.cs) does: `pass.ActiveVariant.TryGetDescription(backend, out ShaderDescription d)` supplies the description, `pass.State.ToBlendState(base)`, `ToDepthStencilState(base)` and `ToRasterizerState(base)` overlay the state, and `CreateGraphicsProgram(d)` creates the program.

### Reuse one pass for several draws

One `ShaderPass` serves several draws: `SetKeyword` before each `SetShader`. The pass caches one `GraphicsProgram` per (variant, blend, depth, raster), so switching back and forth does not recreate anything.

## Pitfalls

- Uniform block slots are a limited process-wide pool that is returned on program disposal. `ShaderPass` never disposes the programs it caches, so they live until disposed or until the device is.
- Creating programs does not deduplicate. Two calls with the same description yield two programs, two sets of shader modules and two pipeline caches.
- A fresh `(framebuffer outputs, topology)` combination compiles a pipeline on the first draw, which causes a one-time hitch.
- The compiler output carries no fixed-function state. A hand-built program with default state draws with depth test and blending off.
- `SetShader` keeps the merged properties. Reapplying them after every shader switch is wasted work.
- `SetKeyword` mutates the pass, not the command buffer, so render graph passes that share one `ShaderPass` share its keyword state.
- Bool keyword values are lowercase `"true"` and `"false"`. `"True"` is not a value; if the name is right but the value is wrong, `SetKeyword` does not throw and selects the nearest variant by matching slots instead.

## See also

- [Internals: Programs and pipelines](../internals/05-programs-and-pipelines.md)
- [Internals: Shader compiler](../internals/06-shader-compiler.md)
- [ShaderDef file format and compiler API](shaderdef.md)
- [Command buffers](command-buffers.md)
- [Property sets](property-sets.md)
