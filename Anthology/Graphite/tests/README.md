# Tests

## SPIRV Tests (no GPU required)

Tests SPIR-V cross-compilation (HLSL, GLSL, ESSL, MSL), GLSL-to-SPIR-V compilation, shader reflection, and JSON serialization.

```bash
dotnet test tests/Veldrid.SPIRV.Tests/Veldrid.SPIRV.Tests.csproj
```

## GPU Tests (requires graphics hardware)

Tests buffers, textures, framebuffers, compute, rendering, pipelines, resource sets, and swapchains against real GPU backends. Backend selection is automatic based on the platform:

| Backend | Windows | Linux | macOS |
|---------|---------|-------|-------|
| D3D11 | Yes | - | - |
| Vulkan | Yes | Yes | - |
| OpenGL | Yes | Yes | Yes |
| OpenGLES | Yes | Yes | - |

Run all backends for the current platform:

```bash
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj
```

Run a specific backend only:

```bash
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj --filter "Backend=D3D11"
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj --filter "Backend=Vulkan"
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj --filter "Backend=OpenGL"
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj --filter "Backend=OpenGLES"
```

Run multiple backends:

```bash
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj --filter "Backend=D3D11|Backend=Vulkan"
```

Run a specific test across all backends:

```bash
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj --filter "Map_WrongFlags_Throws"
```

Run only non-GPU tests (for CI or machines without graphics hardware):

```bash
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj -p:ExcludeGPU=true
```

### Skipped tests

Some tests are skipped at runtime via `[SkippableFact]` + `Skip.If`/`Skip.IfNot`:

- **UseBlendFactor on Vulkan** - triggers a Vulkan image layout validation error. Same error crashes upstream's process. Our fix to the debug callback turns it into a catchable exception instead.

- **D3D11 cubemap storage tests** - D3D11 doesn't support storage cubemaps.

- **OpenGLES compute and buffer-range tests** - GLES on Windows desktop doesn't support compute shaders or buffer range binding. These are platform limitations, not bugs.

### Vulkan debug callback note

Upstream's Vulkan debug callback throws a managed exception from an `[UnmanagedCallersOnly]` native callback, which is undefined behavior and crashes the test process. Our fix stores the error and throws from managed code after the Vulkan call returns. This allows all Vulkan tests to run to completion instead of aborting mid-suite.
