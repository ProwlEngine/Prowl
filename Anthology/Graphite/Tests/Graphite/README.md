# Tests

## Layout

- **`CPU/`** holds pure value-type tests (identifiers, interner, profiling value types,
  `PropertySet`, format helpers). They touch no graphics device and run in parallel.
- **`GPU/`** holds tests that require a graphics device. They share one device per backend and
  must not run concurrently, so every GPU test class joins the `"GPU Tests"` collection
  (`[CollectionDefinition("GPU Tests", DisableParallelization = true)]` in `XunitAssemblyOptions.cs`).
  This keeps the GPU tests serialized while leaving the CPU tests parallel.
- **`GPU/Baseline/`** holds the representative smoke tests against the current
  `GraphicsProgram` / `PropertySet` / `Frame` API (render, compute, frame lifecycle,
  transient allocation, fences, disposal, profiler counters).
- The remaining `GPU/` suites are the deeper feature coverage, organized by feature rather than
  mirroring the old Veldrid suites:
  - `RenderTests` - vertex attribute formats (uint / ushort / normalized ushort / half), blend
    factor, color write mask, fragment depth writes, texture binding across passes, framebuffer
    array layers.
  - `ComputeTests` - compute-fed graphics, compute-written storage textures (2D, 2D-array, 3D),
    and indirect dispatch.
  - `GraphicsDeviceTests` - device identity/features, the `BeginFrame`/`EndFrame` ring lifecycle,
    frames-in-flight throttling, transient allocation and its hard cap, fences, and
    `ShaderProgram` lifetime.
  - `PropertySetBindingTests` - end-to-end `PropertySet` binding through `CommandBuffer`:
    transient vs. read-only vs. writable uniform buffers, structured buffers, `ApplyOther`, and
    the missing-property handler.
  - `FramebufferTests` / `SwapchainTests` - offscreen framebuffers and `OutputDescription`, plus
    the main swapchain's framebuffer, presentation, resize, and sRGB creation.
  - `DisposalTests` - resource disposal and dependency lifetimes.

### Shaders

Shaders live in `Shaders/` as Slang (`.slang`) and are compiled to per-backend bytecode at
runtime by `TestShaderLoader` (SPIR-V for Vulkan, GLSL for OpenGL/ES, HLSL for D3D11). There are
no checked-in `.spv` files. Each `.slang` collapses a vertex+fragment (or compute) pair into one
module; Slang entry-point names are not preserved on Vulkan, so every stage is created with the
entry point `"main"`.

### Migration status

The old Veldrid-era suites (`PipelineTests`, `ResourceSetTests`, `VertexLayoutTests`, and the SDL
based `SwapchainTests`) have been removed. Their coverage was folded into the feature-organized
suites above: pipeline/program creation is exercised everywhere a program is built, vertex layouts
by `RenderTests`, and resource binding by `PropertySetBindingTests`. All shaders are now Slang.

## GPU backends

Backend selection is automatic based on the platform:

| Backend | Windows | Linux | macOS |
|---------|---------|-------|-------|
| D3D11 | Yes | - | - |
| Vulkan | Yes | Yes | - |
| OpenGL | Yes | Yes | Yes |
| OpenGLES | Yes | Yes | - |

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
They require the library to be built with `PROFILE_USAGE` (the default; disabled by
`-p:DisableProfiling=true`). When profiling is compiled out, `GetProfile` returns a zeroed
snapshot and these tests skip.

## Vulkan debug callback note

The Vulkan debug callback stores validation errors and rethrows them from managed code after the
Vulkan call returns, rather than throwing directly from the `[UnmanagedCallersOnly]` native
callback (which is undefined behavior and aborts the process). This lets Vulkan tests run to
completion instead of crashing mid-suite.
