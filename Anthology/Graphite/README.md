# Veldrid-Silk

A fork of [Veldrid](https://github.com/mellinoe/veldrid) that replaces all native graphics bindings with [Silk.NET](https://github.com/dotnet/Silk.NET) equivalents.

Veldrid is a cross-platform, graphics API-agnostic rendering and compute library for .NET. It provides a powerful, unified interface to a system's GPU. Veldrid's public API surface remains unchanged in this fork: only the internal backend implementations are swapped. Any app built on Veldrid should work identically after switching to veldrid-silk.

![NeoDemo - Sponza Atrium](art/sponza.jpeg)

## Status

**Work in progress.** Vulkan, D3D11, OpenGL, and windowing backends are ported and validated.

| Backend | Status |
|---------|--------|
| Vulkan | Ported and validated |
| Direct3D 11 | Ported and validated |
| OpenGL | Ported and validated |
| Metal | Removed (use Vulkan via MoltenVK on macOS) |
| Windowing | Ported (Silk.NET.Windowing replaces SDL2) |

All [samples](samples/) have been validated on Vulkan, D3D11, and OpenGL.

## What's Different From Upstream Veldrid

| Area | Upstream | veldrid-silk |
|------|----------|-------------|
| Vulkan bindings | `Vk` 1.0.25 | `Silk.NET.Vulkan` 2.23.0 |
| D3D11 bindings | `Vortice.Direct3D11` 2.4.2 | `Silk.NET.Direct3D11` 2.23.0 |
| OpenGL bindings | Custom (`Veldrid.OpenGLBindings`) | `Silk.NET.OpenGL` 2.23.0 |
| Metal backend | Native via `Veldrid.MetalBindings` | Removed (use Vulkan via MoltenVK) |
| Windowing | SDL2 via `Veldrid.SDL2` | `Silk.NET.Windowing` 2.23.0 |
| ImageSharp | 1.x | 3.x (1.x is broken on .NET 10) |
| Target framework | `netstandard2.0` | `net10.0` |

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

On Windows, samples default to D3D11. On other platforms, they default to Vulkan. See the [samples directory](samples/) for the full list.

## Related Projects

- [mellinoe/veldrid](https://github.com/mellinoe/veldrid) - The original Veldrid. No longer actively maintained.
- [veldrid2/veldrid2](https://github.com/veldrid2/veldrid2) - Community fork focused on bug fixes, keeping the same binding stack.
- [dotnet/Silk.NET](https://github.com/dotnet/Silk.NET) - The .NET bindings library used by this fork.
