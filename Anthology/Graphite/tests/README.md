# Tests

## SPIRV Tests (no GPU required)

Tests SPIR-V cross-compilation (HLSL, GLSL, ESSL, MSL), GLSL-to-SPIR-V compilation, shader reflection, and JSON serialization.

```bash
dotnet test tests/Veldrid.SPIRV.Tests/Veldrid.SPIRV.Tests.csproj
```

## GPU Tests (requires graphics hardware)

Tests buffers, textures, framebuffers, compute, rendering, pipelines, resource sets, and swapchains against real GPU backends. Each backend is enabled via a define constant:

```bash
# Single backend
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj -p:DefineConstants="TEST_D3D11"
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj -p:DefineConstants="TEST_VULKAN"
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj -p:DefineConstants="TEST_OPENGL"

# Multiple backends (runs each test once per backend)
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj -p:DefineConstants='"TEST_D3D11;TEST_VULKAN;TEST_OPENGL"'
```

Without any `TEST_*` define, only non-GPU unit tests run.

### Skipped tests

Some tests are skipped. These are pre-existing upstream bugs, not regressions:

- **Validation tests** - expect `VeldridException` for invalid input, but the validation code was removed from upstream in Nov 2018 (commit `33a0545`). The tests were never updated. Marked with `[Fact(Skip = "Upstream: missing input validation")]`.

- **UseBlendFactor on Vulkan** - triggers a Vulkan image layout validation error. Same error crashes upstream's process. Our fix to the debug callback turns it into a catchable exception instead. Marked with `[SkippableFact]` and `Skip.If(Vulkan)`.

- **D3D11 cubemap storage tests** - D3D11 doesn't support storage cubemaps. Runtime skip via `[SkippableFact]`.

### Vulkan debug callback note

Upstream's Vulkan debug callback throws a managed exception from an `[UnmanagedCallersOnly]` native callback, which is undefined behavior and crashes the test process. Our fix stores the error and throws from managed code after the Vulkan call returns. This allows all Vulkan tests to run to completion instead of aborting mid-suite.
