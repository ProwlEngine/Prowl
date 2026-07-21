# Tests

## Layout

- **`CPU/`** holds pure value-type tests (identifiers, interner, profiling value types,
  `PropertySet`, format helpers, the render graph solver, and `RenderPipeline` pass wiring). They
  touch no graphics device and run in parallel.
- **`GPU/`** holds tests that require a graphics device. They share one device per backend and
  must not run concurrently, so every GPU test class joins the `"GPU Tests"` collection
  (`[CollectionDefinition("GPU Tests", DisableParallelization = true)]` in `XunitAssemblyOptions.cs`).
  This keeps the GPU tests serialized while leaving the CPU tests parallel.
- **`GPU/Baseline/`** holds the representative smoke tests against the current
  `GraphicsProgram` / `PropertySet` / `ExecutionTask` API (render, compute, execution-ring
  lifecycle, transient allocation, fences, disposal, profiler counters) - the built-in ring that
  replaces Veldrid's caller-managed `Fence`/`SubmitCommands` model. Tests exercise
  `BeginExecution`/`CompleteExecution` and the `RunTestGraph` helper.
- The remaining `GPU/` suites are the deeper feature coverage, organized by feature rather than
  mirroring the old Veldrid suites:
  - `RenderTests` - vertex attribute formats (uint / ushort / normalized ushort / half), blend
    factor, color write mask, fragment depth writes, texture binding across passes, framebuffer
    array layers.
  - `ComputeTests` - compute-fed graphics, compute-written storage textures (2D, 2D-array, 3D),
    and indirect dispatch.
  - `GraphicsDeviceTests` - device identity/features, the `BeginExecution`/`CompleteExecution`
    ring lifecycle, `MaxExecutingTasks` throttling, transient allocation and its hard cap, fences,
    and `ShaderProgram` lifetime.
  - `PropertySetBindingTests` - end-to-end `PropertySet` binding through `CommandBuffer`:
    transient vs. read-only vs. writable uniform buffers, structured buffers, `ApplyOther`, and
    the missing-property handler.
  - `CrossSetBindingTests` - binding spread across three descriptor sets: a structured buffer
    outside set 0, a texture/sampler pair in a third set, descriptor-set cache reuse and
    non-aliasing, sub-ranges of one buffer, and `ClearProperties`.
  - `MultiParameterBlockBindingTests` - regression coverage for a binding-point aliasing bug
    where two buffers with the same local parameter-set binding could resolve to the same
    shader-wide binding by accident.
  - `BindingOptimizationTests` - the per-draw binding optimizations: draw-to-draw descriptor-set
    dedup, value-based transient-UBO reuse, resolve-once, and command-buffer pooling.
  - `FrameLifecycleTests` - the execution ring mechanics: ring slot cycling and repeating, the
    completion fence recycled per slot, in-flight tracking, and the `MaxExecutingTasks` backstop
    enforcing the ceiling.
  - `TransientAllocationTests` - the per-execution bump allocator: offset alignment, non-overlap,
    per-execution head reset, and the overflow spill path (growth rule, cumulative hard cap, the
    one-shot soft-cap warning).
  - `TransientTexturePoolTests` - the device-level transient render-texture pool
    (`GraphicsDevice.RentTransientTexture`/`RentTransientFramebuffer`): desc-keyed reuse once an
    execution's fence signals, no reuse while a bundle is still in flight, and leak-free disposal.
  - `BufferSafetyTests` - the implicit-reallocation ("orphaning") path: writing a buffer that is
    still in flight retires its native resource behind a stable managed identity, and the retired
    resource is freed once the ring cycles. Also covers the `TransientWrites` opt-out and the
    repeat-reallocation warning.
  - `BufferResourceTests` - graph buffer resources: writer/reader resolving one transient buffer, and
    a compute pass writing a graph buffer copied back for verification.
  - `BufferTests` / `TextureTests` (+ `TextureTests.RegressionTests`) - buffer and texture
    creation, mapping, and copy behavior, plus a dedicated file for regressions guarding specific
    fixed bugs.
  - `FramebufferTests` / `SwapchainTests` - offscreen framebuffers and `OutputDescription`, plus
    the main swapchain's framebuffer, presentation, resize, and sRGB creation.
  - `DispatchRenderGraphTests` - the high-level `GraphicsDevice.DispatchGraph` entry point: one
    execution per dispatch, the pass loop running once per view against a fresh per-view context,
    the returned task completing, transient acquisition surviving many dispatches, and the present
    pass arming the swap only when a swapchain target is available.
  - `RenderContextResourceTests` - `RenderContext.GetRenderTexture`: the resolution/caching seam,
    view-size wiring for graph resources, isolation of resolved resources across views and across
    dispatches, and the profiler capture path.
  - `DisposalTests` - resource disposal and dependency lifetimes.
- **`CPU/RenderGraphSolverTests`** / **`CPU/RenderPipelineTests`** (namespace
  `Prowl.Graphite.RenderGraph.Tests`, backed by `CPU/TestPasses.cs`) - pure value-type coverage of
  `RenderGraph<TView, TDrawCommand>.Build`: pass ordering from declared inputs/outputs, dependency
  cycle detection, and presentation-source selection; plus `RenderPipeline` behavior like lazy,
  once-only `InitializePasses`.

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

`GPU/Baseline/ProfilingCountingTests` assert the live profiling counters against real device work.
Profiling is now a runtime toggle (`GraphicsDeviceOptions.EnableProfiling`) rather than a
compile-time flag, so each test probes the running device directly (creates a throwaway buffer and
checks whether `GetProfile()` moved) and skips itself if profiling isn't enabled on that device,
instead of relying on a build-time define.

## Vulkan debug callback note

The Vulkan debug callback stores validation errors and rethrows them from managed code after the
Vulkan call returns, rather than throwing directly from the `[UnmanagedCallersOnly]` native
callback (which is undefined behavior and aborts the process). This lets Vulkan tests run to
completion instead of crashing mid-suite.
