# Tests

## Layout

- **CPU tests** (`Core/`, `FormatSizeHelpersTests`, ...) are pure value-type tests and run in parallel.
- **GPU tests** require a graphics device. They share one device per backend and must not run
  concurrently, so every GPU test class joins the `"GPU Tests"` collection
  (`[CollectionDefinition("GPU Tests", DisableParallelization = true)]` in `XunitAssemblyOptions.cs`).
  This keeps the GPU tests serialized while leaving the CPU tests parallel.
- **`CorePath/`** holds the representative GPU tests rewritten against the current
  `GraphicsProgram` / `PropertySet` / `Frame` API (render, compute, frame lifecycle,
  transient allocation, fences, disposal, profiler counters).

### Shaders

Shaders live in `Shaders/` as Slang (`.slang`) and are compiled to per-backend bytecode at
runtime by `TestShaderLoader` (SPIR-V for Vulkan, GLSL for OpenGL/ES, HLSL for D3D11). There are
no checked-in `.spv` files. Each `.slang` collapses a vertex+fragment (or compute) pair into one
module; Slang entry-point names are not preserved on Vulkan, so every stage is created with the
entry point `"main"`.

### Pending migration

Several suites are still written against the removed `Pipeline` / `ResourceLayout` /
`ResourceSet` API and are excluded from the build (see the `<Compile Remove>` group in the
`.csproj`): `RenderTests`, `ComputeTests`, `ResourceSetTests`, `PipelineTests`,
`VertexLayoutTests`, `DisposalTests`, `SwapchainTests`. Re-include and migrate them onto the
`GraphicsProgram` / `PropertySet` API as the remaining shaders are converted to Slang.

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

`CorePath/ProfilingCountingTests` assert the live profiling counters against real device work.
They require the library to be built with `PROFILE_USAGE` (the default; disabled by
`-p:DisableProfiling=true`). When profiling is compiled out, `GetProfile` returns a zeroed
snapshot and these tests skip.

## Vulkan debug callback note

The Vulkan debug callback stores validation errors and rethrows them from managed code after the
Vulkan call returns, rather than throwing directly from the `[UnmanagedCallersOnly]` native
callback (which is undefined behavior and aborts the process). This lets Vulkan tests run to
completion instead of crashing mid-suite.
