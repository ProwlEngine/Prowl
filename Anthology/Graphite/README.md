# Veldrid-Silk

A fork of [Veldrid](https://github.com/mellinoe/veldrid) that replaces all native graphics bindings with [Silk.NET](https://github.com/dotnet/Silk.NET) equivalents.

Veldrid's public API surface remains unchanged — only the internal backend implementations are swapped.

## Status

**Work in progress.** Not yet functional.

## What's Different From Upstream Veldrid

| Area | Before | After |
|------|--------|-------|
| Vulkan bindings | `Vk` 1.0.25 | `Silk.NET.Vulkan` |
| D3D11 bindings | `Vortice.Direct3D11` | `Silk.NET.Direct3D11` |
| OpenGL bindings | Custom (`Veldrid.OpenGLBindings`) | `Silk.NET.OpenGL` |
| Metal backend | Native via `Veldrid.MetalBindings` | Removed (use Vulkan via MoltenVK) |
| Windowing | SDL2 via `Veldrid.SDL2` | `Silk.NET.Windowing` |
| Target framework | netstandard2.0 | net10.0 |
