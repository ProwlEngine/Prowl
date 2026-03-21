# Veldrid-Silk

A fork of [Veldrid](https://github.com/mellinoe/veldrid) that replaces all native graphics bindings with [Silk.NET](https://github.com/dotnet/Silk.NET) equivalents.

Veldrid is a cross-platform, graphics API-agnostic rendering and compute library for .NET. It provides a powerful, unified interface to a system's GPU. Veldrid's public API surface remains unchanged in this fork: only the internal backend implementations are swapped. Any app built on Veldrid should work identically after switching to veldrid-silk.

![NeoDemo - Sponza Atrium](art/sponza.jpeg)

## Status

All backends ported, validated, and passing upstream test suite.

| Backend | Status |
|---------|--------|
| Vulkan | Ported and validated (484/499 tests pass, 15 skipped upstream bugs) |
| Direct3D 11 | Ported and validated (484/499 tests pass, 15 skipped upstream bugs) |
| OpenGL | Ported and validated (484/499 tests pass, 15 skipped upstream bugs) |
| SPIRV | Ported to pure C# (82/82 tests pass) |
| Metal | Removed (use Vulkan via MoltenVK on macOS) |
| Windowing | SDL2 via `Silk.NET.SDL` (matching upstream's SDL2 approach) |

| Platform | Status |
|----------|--------|
| Windows | All backends working. All samples validated. Full test suite passing. |
| macOS | Vulkan via MoltenVK + OpenGL 4.1. Basic samples work. Known issues with window resize/close and compute shaders (GL 4.1 limitation). |
| Linux | Untested. Vulkan and OpenGL expected to work. DXVK (D3D11 via Vulkan) implemented but untested. |

## What Changed

| Area | Upstream Veldrid | veldrid-silk |
|------|-----------------|-------------|
| Vulkan bindings | `Vk` 1.0.25 | `Silk.NET.Vulkan` 2.23.0 |
| D3D11 bindings | `Vortice.Direct3D11` 2.4.2 | `Silk.NET.Direct3D11` 2.23.0 |
| OpenGL bindings | Custom (`Veldrid.OpenGLBindings`) | `Silk.NET.OpenGL` 2.23.0 |
| Metal backend | Custom (`Veldrid.MetalBindings`, 101 files) | Removed (use Vulkan via MoltenVK) |
| Windowing | Custom SDL2 P/Invoke (`Veldrid.SDL2`) | `Silk.NET.SDL` 2.23.0 |
| Shader cross-compilation | `Veldrid.SPIRV` NuGet + native `libveldrid-spirv` | Pure C# via `Silk.NET.SPIRV.Cross` + `Silk.NET.Shaderc` |
| ImageSharp | 1.x | 3.x (1.x broken on .NET 10) |
| Target framework | `netstandard2.0` | `net10.0` |

Six different binding libraries replaced by a single ecosystem. Zero hand-written P/Invoke. Zero custom native binaries.

## Building

```bash
dotnet build VeldridSilk.slnx
```

Requires .NET 10 SDK.

## Running Samples

```bash
# Colored quad
dotnet run --project samples/GettingStarted/GettingStarted.csproj

# Textured 3D cube
dotnet run --project samples/TexturedCube/Desktop/TexturedCube.Desktop.csproj

# Full scene with shadows and ImGui
dotnet run --project samples/NeoDemo/NeoDemo.csproj
```

Set `VELDRID_BACKEND` to select a backend: `d3d11`, `vulkan`, `opengl`.

```bash
VELDRID_BACKEND=vulkan dotnet run --project samples/GettingStarted/GettingStarted.csproj
```

On Windows, samples default to D3D11. On other platforms, they default to Vulkan. See the [samples directory](samples/) for the full list.

## Running Tests

```bash
# SPIRV cross-compilation tests (no GPU required)
dotnet test tests/Veldrid.SPIRV.Tests/Veldrid.SPIRV.Tests.csproj

# GPU tests (requires graphics hardware)
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj -p:DefineConstants="TEST_D3D11"
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj -p:DefineConstants="TEST_VULKAN"
dotnet test tests/Veldrid.Tests/Veldrid.Tests.csproj -p:DefineConstants="TEST_OPENGL"
```

## Related Projects

- [mellinoe/veldrid](https://github.com/mellinoe/veldrid) - The original Veldrid. No longer actively maintained.
- [veldrid2/veldrid2](https://github.com/veldrid2/veldrid2) - Community fork focused on bug fixes, keeping the same binding stack.
- [dotnet/Silk.NET](https://github.com/dotnet/Silk.NET) - The .NET bindings library used by this fork.
