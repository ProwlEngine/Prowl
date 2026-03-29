# NeoVeldrid

A maintained fork of [Veldrid](https://github.com/mellinoe/veldrid) that replaces all native graphics bindings with [Silk.NET](https://github.com/dotnet/Silk.NET) equivalents.

Veldrid is a cross-platform, graphics API-agnostic rendering and compute library for .NET. It provides a powerful, unified interface to a system's GPU. NeoVeldrid preserves Veldrid's public API surface unchanged - only the internal backend implementations are swapped. Any app built on Veldrid should work after switching to NeoVeldrid (see [Migration Guide](docs/articles/migration.md)).

![NeoDemo - Sponza Atrium](art/sponza.jpeg)

## Status

All backends ported, validated, and passing upstream test suite.

| Backend | Status |
|---------|--------|
| Vulkan | Ported and validated |
| Direct3D 11 | Ported and validated |
| OpenGL | Ported and validated |
| OpenGLES | Ported and validated |
| SPIRV | Ported to pure C# (82/82 tests pass) |
| Metal | Removed (use Vulkan via MoltenVK on macOS) |
| Windowing | SDL2 via `Silk.NET.SDL` (matching upstream's SDL2 approach) |

GPU test suite: 1634 passed, 0 failed, 163 skipped (platform limitations, not bugs). Identical results to upstream.

| Platform | Status |
|----------|--------|
| Windows | All backends working. All samples validated. Full test suite passing (1634 GPU + 82 SPIRV). |
| macOS | Vulkan via MoltenVK: all samples working. OpenGL 4.1: intermittent freeze (pre-existing upstream bug). |
| Linux | Vulkan: all samples working, full test suite passing. OpenGL: all samples working. |

## What Changed

| Area | Upstream Veldrid | NeoVeldrid |
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
dotnet build NeoVeldrid.slnx
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

Set `NEOVELDRID_BACKEND` to select a backend: `d3d11`, `vulkan`, `opengl`.

```bash
NEOVELDRID_BACKEND=vulkan dotnet run --project samples/GettingStarted/GettingStarted.csproj
```

On Windows, samples default to D3D11. On other platforms, they default to Vulkan. See the [samples directory](samples/) for the full list.

## Running Tests

```bash
# Run all tests (SPIRV + GPU)
dotnet test
```

See [tests/README.md](tests/README.md) for filtering by backend, running individual tests, non GPU tests, and details on skipped tests.

## Related Projects

- [mellinoe/veldrid](https://github.com/mellinoe/veldrid) - The original Veldrid. No longer actively maintained.
- [veldrid2/veldrid2](https://github.com/veldrid2/veldrid2) - Community fork focused on bug fixes, keeping the same binding stack.
- [dotnet/Silk.NET](https://github.com/dotnet/Silk.NET) - The .NET bindings library used by this fork.
