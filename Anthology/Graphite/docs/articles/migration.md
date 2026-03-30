---
uid: migration
---

# Migration from Veldrid

NeoVeldrid is designed as a drop-in replacement for Veldrid. The public API is identical. Migrating an existing Veldrid project takes just a few steps.

## What Changed

NeoVeldrid replaces all of Veldrid's native binding libraries with [Silk.NET](https://github.com/dotnet/Silk.NET) equivalents:

| Area | Veldrid | NeoVeldrid |
|------|---------|------------|
| Vulkan | Vk 1.0.25 | Silk.NET.Vulkan |
| D3D11 | Vortice.Direct3D11 | Silk.NET.Direct3D11 |
| OpenGL | Custom bindings | Silk.NET.OpenGL |
| Metal | Custom bindings (101 files) | Removed (use Vulkan via MoltenVK) |
| Windowing | Custom SDL2 P/Invoke | Silk.NET.SDL |
| SPIRV | Native C++ shim | Pure C# via Silk.NET |
| Target | netstandard2.0 | net10.0 |

## Step 1: Replace NuGet Packages

Remove the old Veldrid packages and add the NeoVeldrid equivalents:

| Old Package | Version | New Package | Version |
|-------------|---------|-------------|---------|
| `Veldrid` | 4.9.0 | `NeoVeldrid` | 1.0.0 |
| `Veldrid.StartupUtilities` | 4.9.0 | `NeoVeldrid.StartupUtilities` | 1.0.0 |
| `Veldrid.ImageSharp` | 4.9.0 | `NeoVeldrid.ImageSharp` | 1.0.0 |
| `Veldrid.ImGui` | 4.9.0 | `NeoVeldrid.ImGui` | 1.0.0 |
| `Veldrid.SPIRV` | 1.0.15 | `NeoVeldrid.SPIRV` | 1.0.0 |
| `Veldrid.Utilities` | 4.9.0 | `NeoVeldrid.Utilities` | 1.0.0 |

## Step 2: Update Namespaces

Replace all `using Veldrid;` with `using NeoVeldrid;` in your source files. A simple find-and-replace of `Veldrid` to `NeoVeldrid` across your codebase handles this. All types, methods, and enums have the same names.

```csharp
// Veldrid
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Veldrid.SPIRV;

// NeoVeldrid
using NeoVeldrid;
using NeoVeldrid.Sdl2;
using NeoVeldrid.StartupUtilities;
using NeoVeldrid.SPIRV;
```

This also renames a few types that carried the `Veldrid` prefix:

| Old Name | New Name |
|----------|----------|
| `Veldrid.VeldridException` | `NeoVeldrid.NeoVeldridException` |
| `Veldrid.StartupUtilities.VeldridStartup` | `NeoVeldrid.StartupUtilities.NeoVeldridStartup` |

These are all caught by the same find-and-replace.

## Step 3: Update Target Framework

NeoVeldrid targets `net10.0`. Please update your project's target framework to .NET 10 or later.

```xml
<!-- Veldrid supported netstandard2.0 -->
<TargetFramework>netstandard2.0</TargetFramework>

<!-- NeoVeldrid requires net10.0 -->
<TargetFramework>net10.0</TargetFramework>
```

## Step 4: Build and Fix

Build your project. Most projects will compile without any further changes. If you hit errors, check the breaking changes below.

## Breaking Changes

### Metal Backend Removed

**Likelihood Of Impact: Medium**

`GraphicsBackend.Metal` has been removed. If your code referenced it (e.g. in a backend selection switch), remove those branches. On macOS, use `GraphicsBackend.Vulkan` instead. NeoVeldrid uses MoltenVK to run Vulkan on Metal automatically, you don't need to do any additional setup.

### Sdl2Native Removed

**Likelihood Of Impact: Low**

The `Sdl2Native` static class (custom P/Invoke bindings) has been replaced by Silk.NET.SDL. If you called `Sdl2Native.SDL_*` methods directly, use `Sdl2Window.SdlInstance` instead to access the SDL API. The `Sdl2Window` class name and public API are unchanged.

```csharp
// Veldrid
Sdl2Native.SDL_SetWindowTitle(window.SdlWindowHandle, title);

// NeoVeldrid
window.SdlInstance.SetWindowTitle(window.SdlWindowHandle, title);
```

### ImageSharp 3.x

**Likelihood Of Impact: Low**

The `NeoVeldrid.ImageSharp` package now uses SixLabors.ImageSharp 3.x (up from 1.x). The NeoVeldrid.ImageSharp API itself is unchanged, but if your own code also calls ImageSharp directly, you may need to update it. See the [ImageSharp 3.0 announcement](https://sixlabors.com/posts/announcing-imagesharp-300/) for breaking changes.
