# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - Unreleased

First release of NeoVeldrid. A maintained, drop-in replacement for [Veldrid](https://github.com/mellinoe/veldrid) with every native binding replaced by [Silk.NET](https://github.com/dotnet/Silk.NET). If you have a Veldrid project today, migrating is roughly a 5 minute find-and-replace. See the [Migration Guide](docs/articles/prologue/migration.md) for the exact steps.

### Breaking

- `Sdl2Window.SetMousePosition` no longer supports per-frame warp-cursor-back mouselook. Code using that pattern must switch to `CursorRelativeMode` + `MouseDelta`. See the [Migration Guide](docs/articles/prologue/migration.md#mouselook-with-setmouseposition).

### Changed

- Vulkan backend now binds through `Silk.NET.Vulkan` 2.23.0 (was `Vk` 1.0.25).
- D3D11 backend now binds through `Silk.NET.Direct3D11` 2.23.0 (was `Vortice.Direct3D11` 2.4.2).
- OpenGL and OpenGL ES backends now bind through `Silk.NET.OpenGL` 2.23.0 (was the custom `Veldrid.OpenGLBindings`).
- Windowing now goes through `Silk.NET.SDL` 2.23.0 instead of a hand-rolled SDL2 P/Invoke layer.
- SPIRV cross-compilation is now pure C# via `Silk.NET.SPIRV.Cross` + `Silk.NET.Shaderc`. There is no `libveldrid-spirv` native binary to build or ship anymore.
- `ImageSharp` bumped from 1.x to 3.x.
- Target framework is now `net10.0` (was `netstandard2.0`).
- Root namespace renamed from `Veldrid` to `NeoVeldrid`.

### Added

- macOS support through Vulkan + MoltenVK, Apple Silicon included. No extra setup, MoltenVK is bundled automatically.
- Linux native libraries are now bundled inside the NuGet package. No more chasing down system `libSDL2.so` or building native binaries yourself.

### Removed

- Metal backend. macOS is now covered by Vulkan via MoltenVK.
- `Sdl2Native` static class. Reach the underlying SDL API through `Sdl2Window.SdlInstance` instead.
- `Veldrid.VirtualReality` project. There was no VR hardware available for testing, so it would have rotted into dead code.
- Android and iOS sample projects. The core library still works on those platforms, but the samples are unmaintained. Contributions welcome.

### Fixed

- [D3D11] Mipmap sampling being silently wrong at non-zero mip levels, caused by a struct layout mismatch in the old Vortice bindings.
- [Vulkan] Validation mode crashing the whole process on the first validation message, because the debug callback was throwing a managed exception back into unmanaged code.
- [Vulkan] `PixelFormat.R16_G16_Float` and `PixelFormat.R32_G32_Float` working incorrectly.
- [Vulkan] `GraphicsDeviceOptions.SwapchainSrgbFormat` being silently ignored by `CreateWindowAndGraphicsDevice`, so Vulkan swapchains always came back in the linear-UNorm format regardless of what the user requested.
- [Vulkan] Fixes memory leak with `VkDescriptorPoolManager` related to unfreed dynamic buffers.
- [Vulkan] Fixes `CreateLogicalDevice` ignoring the present queue family on GPUs where it differs from the graphics family.
- [SDL2] Scroll-to-zoom now respects sub-detent deltas from precision touchpads and high-end mice. Slow scrolls no longer round to zero.
- [SDL2] Cursor position no longer desyncs when the window regains focus or the pointer re-enters the window on Windows. Modern SDL2 emits zero-delta motion events in those cases, and the previous filter discarded them too aggressively.

[Unreleased]: https://github.com/jhm-ciberman/neo-veldrid/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/jhm-ciberman/neo-veldrid/releases/tag/v1.0.0
