# Tests

## Layout

- **`CPU/`** holds pure value-type tests (interner, `PropertySet`, format helpers, the render
  graph solver, and `RenderPipeline` pass wiring). They
  touch no graphics device and run in parallel.
- **`GPU/`** holds tests that require a graphics device. They share one device per backend and
  must not run concurrently, so every GPU test class joins the `"GPU Tests"` collection
  (`[CollectionDefinition("GPU Tests", DisableParallelization = true)]` in `XunitAssemblyOptions.cs`).
  This keeps the GPU tests serialized while leaving the CPU tests parallel.
- **`GPU/Baseline/`** holds the end-to-end smoke tests against the current
  `GraphicsProgram` / `PropertySet` / `ExecutionTask` API: one draw and one dispatch, each read
  back through the `RunTestGraph` helper.
- The remaining `GPU/` suites are the deeper feature coverage, organized by feature rather than
  mirroring the old Veldrid suites:
  - `RenderTests` - vertex attribute formats (ushort / normalized ushort / half), blend
    factor, color write mask, fragment depth writes, texture binding across passes, framebuffer
    array layers.
  - `ComputeTests` - compute-fed graphics, compute-written storage textures (2D, 3D),
    and indirect dispatch.
  - `PropertySetBindingTests` / `ExplicitWritableUniformBufferTests` - read-only and writable
    user-provided uniform buffers bound through `PropertySet`.
  - `CrossSetBindingTests` - binding spread across three descriptor sets: a structured buffer
    outside set 0, a texture/sampler pair in a third set, descriptor-set cache reuse and
    non-aliasing, sub-ranges of one buffer, `ClearProperties`, and the missing-property handler.
  - `BindingOptimizationTests` - the per-draw binding optimizations: draw-to-draw descriptor-set
    dedup, value-based transient-UBO reuse, resolve-once, and command-buffer pooling.
  - `FrameLifecycleTests` - the execution ring mechanics: monotonic ids mapping onto ring slots,
    in-flight tracking, and the `MaxExecutingTasks` backstop enforcing the ceiling.
  - `TransientAllocationTests` - the per-execution bump allocator: offset alignment, non-overlap,
    per-execution head reset, and the overflow spill path (growth rule, single and cumulative
    hard cap).
  - `TransientTexturePoolTests` - the device-level transient render-texture pool
    (`GraphicsDevice.RentGraphTransientRenderTexture`): desc-keyed reuse once an
    execution's fence signals, no reuse while a bundle is still in flight, and leak-free disposal.
  - `BufferSafetyTests` - CPU writes to a buffer that is still in flight: the upload is a queued
    copy, so work already submitted reads the old contents and later work reads the new ones.
  - `BufferResourceTests` - graph buffer resources: writer/reader resolving one transient buffer, and
    a compute pass writing a graph buffer copied back for verification.
  - `BufferTests` / `TextureTests` (+ `TextureTests.RegressionTests`) - buffer and texture
    creation, mapping, and copy behavior, plus a dedicated file for regressions guarding specific
    fixed bugs.
  - `FramebufferTests` / `SwapchainTests` - offscreen framebuffers and `OutputDescription`, plus
    swapchain presentation, resize, view-target depth, and sRGB creation.
  - `DispatchRenderGraphTests` - the high-level `GraphicsDevice.DispatchGraph` entry point: the
    pass loop running once per view against a fresh per-view context, and the view-target pass
    resolving to the swapchain or framebuffer target only when one is available.
  - `RenderContextResourceTests` - `RenderContext.GetRenderTexture`: the resolution/caching seam,
    view-size wiring for graph resources, isolation of resolved resources across views, imported
    textures, and the profiler capture path.
  - `DisposalTests` - resource disposal and dependency lifetimes.
- **`CPU/RenderGraphSolverTests`** / **`CPU/RenderPipelineTests`** (namespace
  `Prowl.Graphite.RenderGraph.Tests`, backed by `CPU/TestPasses.cs`) - pure value-type coverage of
  `RenderGraph<TView, TDrawCommand>.Build`: pass ordering from declared inputs/outputs, dependency
  cycle detection, and presentation-source selection; plus `RenderPipeline` behavior like lazy,
  `SetPasses` rebuilds.

### Shaders

Shaders live in `Shaders/` as Slang (`.slang`) and are compiled to SPIR-V at runtime by
`TestShaderLoader`. There are no checked-in `.spv` files. Each `.slang` collapses a
vertex+fragment (or compute) pair into one module; Slang entry-point names are not preserved on
Vulkan, so every stage is created with the entry point `"main"`.

### Migration status

The old Veldrid-era suites (`PipelineTests`, `ResourceSetTests`, `VertexLayoutTests`, and the SDL
based `SwapchainTests`) have been removed. Their coverage was folded into the feature-organized
suites above: pipeline/program creation is exercised everywhere a program is built, vertex layouts
by `RenderTests`, and resource binding by `PropertySetBindingTests`. All shaders are now Slang.

## GPU backends

Backend selection is automatic based on the platform. Direct3D 11 has been removed from the
library; Vulkan is the only backend under test:

| Backend | Windows | Linux | macOS |
|---------|---------|-------|-------|
| Vulkan  | Yes     | Yes   | -     |

Run all backends for the current platform:

```bash
dotnet test Tests/Prowl.Graphite.Tests.csproj
```

Run a specific backend only (tests are tagged `[Trait("Backend", "...")]`):

```bash
dotnet test Tests/Prowl.Graphite.Tests.csproj --filter "Backend=Vulkan"
```

Run a specific test across all backends:

```bash
dotnet test Tests/Prowl.Graphite.Tests.csproj --filter "Points_WithUIntColor_ProduceExpectedPixel"
```

Run only the non-GPU tests (CI or machines without graphics hardware):

```bash
dotnet test Tests/Prowl.Graphite.Tests.csproj -p:ExcludeGPU=true
```

## Profiler tests

`GPU/ProfilerEventsTests` cover the per-execution profiler seam: the execution lifecycle, events
under one execution, per-capability merging, and `GlobalProfilers` factories. The capture and
command stream seams are covered by `GPU/CaptureHookTests`, `GPU/CaptureCopyTests` and
`GPU/CommandStreamSinkTests`, and `Graphite.Debugger` by `Tests/Debugger`.

## Vulkan debug callback note

The Vulkan debug callback stores validation errors and rethrows them from managed code after the
Vulkan call returns, rather than throwing directly from the `[UnmanagedCallersOnly]` native
callback (which is undefined behavior and aborts the process). This lets Vulkan tests run to
completion instead of crashing mid-suite.
