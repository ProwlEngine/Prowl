# NeoVeldrid

[![Tests](https://github.com/jhm-ciberman/neo-veldrid/actions/workflows/ci.yml/badge.svg)](https://github.com/jhm-ciberman/neo-veldrid/actions/workflows/ci.yml)

> [!NOTE]
> NeoVeldrid 1.0.0 will be published to NuGet in the upcoming weeks. In the meantime, you can use it as a project reference.

A maintained fork of [Veldrid](https://github.com/mellinoe/veldrid) that replaces all native graphics bindings with [Silk.NET](https://github.com/dotnet/Silk.NET) equivalents.

Veldrid is a cross-platform, graphics API-agnostic rendering and compute library for .NET. It provides a powerful, unified interface to a system's GPU. NeoVeldrid preserves Veldrid's public API surface unchanged - only the internal backend implementations are swapped. Any app built on Veldrid should work after switching to NeoVeldrid (see [Migration Guide](docs/articles/migration.md)).

![NeoDemo - Sponza Atrium](art/sponza.webp)

## Platform Support

|               | Vulkan | D3D11 | OpenGL | OpenGL ES |
| :------------ | :----: | :---: | :----: | :-------: |
| Windows       |   ✅   |  ✅   |   ✅   |    ✅     |
| Linux         |   ✅   |  --   |   ✅   |    ✅     |
| macOS         | ✅ (1) |  --   |   ❌   |    --     |

(1) Via [MoltenVK](https://github.com/KhronosGroup/MoltenVK), bundled automatically. No setup required.

All backends pass the full GPU test suite and all 9 samples have been validated visually on Windows, Linux, and macOS.

## What Changed

Veldrid depended on six different binding libraries, some unmaintained, with platform-specific native binaries to manage manually. NeoVeldrid replaces all of them with [Silk.NET](https://github.com/dotnet/Silk.NET), a single actively-maintained ecosystem that ships cross-platform native binaries via NuGet. This means NeoVeldrid can stay current with driver updates and bug fixes without maintaining custom bindings.

| Area | Veldrid | NeoVeldrid |
|------|-----------------|-------------|
| Vulkan bindings | `Vk` 1.0.25 | `Silk.NET.Vulkan` 2.23.0 |
| D3D11 bindings | `Vortice.Direct3D11` 2.4.2 | `Silk.NET.Direct3D11` 2.23.0 |
| OpenGL bindings | Custom (`Veldrid.OpenGLBindings`) | `Silk.NET.OpenGL` 2.23.0 |
| Metal backend | Custom (`Veldrid.MetalBindings`, 101 files) | Removed (use Vulkan via MoltenVK) |
| Windowing | Custom SDL2 P/Invoke (`Veldrid.SDL2`) | `Silk.NET.SDL` 2.23.0 |
| Shader cross-compilation | `Veldrid.SPIRV` NuGet + native `libveldrid-spirv` | Pure C# via `Silk.NET.SPIRV.Cross` + `Silk.NET.Shaderc` |
| ImageSharp | 1.x (broken on .NET 10) | 3.x |
| Target framework | `netstandard2.0` | `net10.0` |


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

See the [samples directory](samples/) for the full list.

## Running Tests

```bash
# Run all tests (SPIRV + GPU)
dotnet test
```

See [tests/README.md](tests/README.md) for filtering by backend, running individual tests, non GPU tests, and details on skipped tests.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions, testing guidelines, and how to submit a PR.

## Acknowledgments

NeoVeldrid exists thanks to Eric Mellino ([@mellinoe](https://github.com/mellinoe)), who designed and built the original Veldrid. The architecture, public API, and core abstractions are entirely his work. NeoVeldrid only replaces the internal binding layer - the design that makes it all possible is Eric's. His library opened the door to cross-platform graphics programming in .NET for many of us, and we're grateful for that.

## Related Projects

- [mellinoe/veldrid](https://github.com/mellinoe/veldrid) - The original Veldrid. No longer actively maintained.
- [veldrid2/veldrid2](https://github.com/veldrid2/veldrid2) - Community fork focused on bug fixes, keeping the same binding stack.
- [dotnet/Silk.NET](https://github.com/dotnet/Silk.NET) - The .NET bindings library used by this fork.
