---
uid: migration
---

# Migration from Veldrid

NeoVeldrid is designed as a drop-in replacement for Veldrid. The public API is identical. Migrating an existing Veldrid project takes just a few steps.

## Step 1: Replace NuGet packages

Remove the old Veldrid packages and add the NeoVeldrid equivalents:

| Old Package | New Package |
|-------------|-------------|
| `Veldrid` | `NeoVeldrid` |
| `Veldrid.StartupUtilities` | `NeoVeldrid.StartupUtilities` |
| `Veldrid.ImageSharp` | `NeoVeldrid.ImageSharp` |
| `Veldrid.ImGui` | `NeoVeldrid.ImGui` |
| `Veldrid.SPIRV` | `NeoVeldrid.SPIRV` |
| `Veldrid.Utilities` | `NeoVeldrid.Utilities` |

## Step 2: Update namespaces

Replace `using Veldrid;` with `using NeoVeldrid;` in your source files. All types, methods, and enums have the same names.

## Step 3: Remove Metal references

If your code references `GraphicsBackend.Metal`, remove those branches. On macOS, use `GraphicsBackend.Vulkan` instead (NeoVeldrid uses MoltenVK to run Vulkan on Metal).

## Step 4: Build and run

That's it. Your project should compile and run identically to before.

## Breaking Changes

These are the only differences from upstream Veldrid:

1. **Metal backend removed** - `GraphicsBackend.Metal` no longer exists. macOS uses Vulkan via MoltenVK.
2. **Target framework: net10.0** - netstandard2.0 and netcoreapp are no longer supported.
3. **ImageSharp 3.x** - If you use `Veldrid.ImageSharp`, note that it now depends on SixLabors.ImageSharp 3.x instead of 1.x.
4. **Sdl2Native removed** - If you used `Sdl2Native` for direct SDL2 P/Invoke calls, use `Sdl2Window.SdlInstance` instead to access the Silk.NET SDL API.

## What Improved

- Works on macOS (upstream Veldrid couldn't run on Apple Silicon)
- Works on Linux out of the box (native libraries bundled via NuGet)
- D3D11 mipmap sampling bug fixed (was caused by a Vortice struct layout bug)
- SPIRV cross-compilation is now pure C# (no native binary dependency)
- SDL2 mouse input lag fixed (affected mouselook at high frame rates)
