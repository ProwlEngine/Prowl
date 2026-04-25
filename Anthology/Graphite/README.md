![NeoVeldrid](https://raw.githubusercontent.com/jhm-ciberman/neo-veldrid/main/art/logo-horizontal.svg)

[![Tests](https://github.com/jhm-ciberman/neo-veldrid/actions/workflows/ci.yml/badge.svg)](https://github.com/jhm-ciberman/neo-veldrid/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/NeoVeldrid)](https://www.nuget.org/packages/NeoVeldrid)

A low-level, high-performance, cross-platform graphics library for .NET. Build 2D and 3D games, simulations, and tools with a single portable API across Vulkan, Direct3D 11, OpenGL, and OpenGL ES.

NeoVeldrid is a maintained fork of [Veldrid](https://github.com/mellinoe/veldrid), with all native bindings replaced by [Silk.NET](https://github.com/dotnet/Silk.NET). The public API is fully preserved.

**[Documentation](https://jhm-ciberman.github.io/neo-veldrid/)** | **[Getting Started](https://jhm-ciberman.github.io/neo-veldrid/articles/prologue/intro.html)** | **[API Reference](https://jhm-ciberman.github.io/neo-veldrid/api/)**

![NeoDemo - Sponza Atrium](https://raw.githubusercontent.com/jhm-ciberman/neo-veldrid/main/art/sponza.webp)

## Features

- **Multi-backend** - Write your rendering code once. It runs on Vulkan, Direct3D 11, OpenGL, and OpenGL ES without any changes.
- **Cross-platform** - Windows, Linux, and macOS from a single codebase.
- **High performance** - Thin abstraction close to the metal. Allocation-free rendering loop with no GC pressure.
- **Multi-threaded** - Most NeoVeldrid objects can be used from multiple threads simultaneously.
- **Modern GPU features** - Programmable shaders, compute, structured buffers, array textures, multisampling, multi-target framebuffers.
- **Portable shaders** - Write GLSL once, cross-compile to all backends via SPIR-V at runtime.
- **Headless** - Run without a window for background GPU-accelerated operations.

## Platform Support

|               | Vulkan | D3D11 | OpenGL | OpenGL ES |
| :------------ | :----: | :---: | :----: | :-------: |
| Windows       |   ✅   |  ✅   |   ✅   |    ✅     |
| Linux         |   ✅   |  --   |   ✅   |    ✅     |
| macOS         | ✅ (1) |  --   |   ❌   |    --     |

(1) Via [MoltenVK](https://github.com/KhronosGroup/MoltenVK), bundled automatically. No setup required.

## Quick Start

```
dotnet new console -n MyGame
cd MyGame
dotnet add package NeoVeldrid
dotnet add package NeoVeldrid.StartupUtilities
dotnet add package NeoVeldrid.SPIRV
```

See the [documentation](https://jhm-ciberman.github.io/neo-veldrid/) for tutorials, API reference, and the [samples directory](https://github.com/jhm-ciberman/neo-veldrid/tree/main/samples).

## Coming from Veldrid?

NeoVeldrid preserves full API compatibility. Update your NuGet packages, rename the namespace, and you're done. NeoVeldid is a drop-in replacement for Veldrid, with all the same features and API you already love, but with a new binding stack that is actively maintained and updated to support the latest graphics drivers and platforms. See the [Migration Guide](https://jhm-ciberman.github.io/neo-veldrid/articles/prologue/migration.html) for details.

## Contributing

See [CONTRIBUTING.md](https://github.com/jhm-ciberman/neo-veldrid/blob/main/CONTRIBUTING.md) for build instructions, testing guidelines, and how to submit a PR.

## Acknowledgments

NeoVeldrid exists thanks to Eric Mellino ([@mellinoe](https://github.com/mellinoe)), who designed and built the original Veldrid. The architecture, public API, and core abstractions are entirely his work. NeoVeldrid only replaces the internal binding layer - the design that makes it all possible is Eric's. His library opened the door to cross-platform graphics programming in .NET for many of us, and we're grateful for that.

## Related Projects

- [mellinoe/veldrid](https://github.com/mellinoe/veldrid) - The original Veldrid. No longer actively maintained.
- [veldrid2/veldrid2](https://github.com/veldrid2/veldrid2) - Community fork focused on bug fixes, keeping the same binding stack.
- [dotnet/Silk.NET](https://github.com/dotnet/Silk.NET) - The .NET bindings library used by this fork.
