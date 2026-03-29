# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - Upcoming

First release of NeoVeldrid. Drop-in replacement for [Veldrid](https://github.com/mellinoe/veldrid) with all native bindings replaced by Silk.NET. See the [Migration Guide](docs/articles/migration.md) for upgrade instructions.

### Changed

- Vulkan backend: `Vk` 1.0.25 replaced by `Silk.NET.Vulkan` 2.23.0
- D3D11 backend: `Vortice.Direct3D11` 2.4.2 replaced by `Silk.NET.Direct3D11` 2.23.0
- OpenGL backend: custom `Veldrid.OpenGLBindings` replaced by `Silk.NET.OpenGL` 2.23.0
- Windowing: custom SDL2 P/Invoke replaced by `Silk.NET.SDL` 2.23.0
- SPIRV: native `libveldrid-spirv` replaced by pure C# via `Silk.NET.SPIRV.Cross` + `Silk.NET.Shaderc`
- ImageSharp upgraded from 1.x to 3.x
- Target framework changed from `netstandard2.0` to `net10.0`
- Namespace renamed from `Veldrid` to `NeoVeldrid`

### Added

- macOS Vulkan support via MoltenVK
- Linux native libraries bundled via NuGet

### Removed

- Metal backend (macOS uses Vulkan via MoltenVK instead)
- `Sdl2Native` static class (use `Sdl2Window.SdlInstance` for SDL API access)
- `Veldrid.VirtualReality` project (no hardware available for testing)
- Android and iOS sample projects

### Fixed

- D3D11 mipmap sampling bug (caused by a struct layout issue in the old Vortice bindings)
- Vulkan debug callback crash (threw a managed exception from an unmanaged callback)
- Shaderc compiler recreated on every shader compilation call (now cached)

[Unreleased]: https://github.com/jhm-ciberman/neo-veldrid/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/jhm-ciberman/neo-veldrid/releases/tag/v1.0.0
