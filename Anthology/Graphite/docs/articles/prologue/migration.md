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

| Old Package | | New Package | |
|-------------|---|-------------|---|
| `Veldrid` | [![NuGet](https://img.shields.io/nuget/v/Veldrid)](https://www.nuget.org/packages/Veldrid) | `NeoVeldrid` | [![NuGet](https://img.shields.io/nuget/v/NeoVeldrid)](https://www.nuget.org/packages/NeoVeldrid) |
| `Veldrid.StartupUtilities` | [![NuGet](https://img.shields.io/nuget/v/Veldrid.StartupUtilities)](https://www.nuget.org/packages/Veldrid.StartupUtilities) | `NeoVeldrid.StartupUtilities` | [![NuGet](https://img.shields.io/nuget/v/NeoVeldrid.StartupUtilities)](https://www.nuget.org/packages/NeoVeldrid.StartupUtilities) |
| `Veldrid.ImageSharp` | [![NuGet](https://img.shields.io/nuget/v/Veldrid.ImageSharp)](https://www.nuget.org/packages/Veldrid.ImageSharp) | `NeoVeldrid.ImageSharp` | [![NuGet](https://img.shields.io/nuget/v/NeoVeldrid.ImageSharp)](https://www.nuget.org/packages/NeoVeldrid.ImageSharp) |
| `Veldrid.ImGui` | [![NuGet](https://img.shields.io/nuget/v/Veldrid.ImGui)](https://www.nuget.org/packages/Veldrid.ImGui) | `NeoVeldrid.ImGui` | [![NuGet](https://img.shields.io/nuget/v/NeoVeldrid.ImGui)](https://www.nuget.org/packages/NeoVeldrid.ImGui) |
| `Veldrid.SPIRV` | [![NuGet](https://img.shields.io/nuget/v/Veldrid.SPIRV)](https://www.nuget.org/packages/Veldrid.SPIRV) | `NeoVeldrid.SPIRV` | [![NuGet](https://img.shields.io/nuget/v/NeoVeldrid.SPIRV)](https://www.nuget.org/packages/NeoVeldrid.SPIRV) |
| `Veldrid.Utilities` | [![NuGet](https://img.shields.io/nuget/v/Veldrid.Utilities)](https://www.nuget.org/packages/Veldrid.Utilities) | `NeoVeldrid.Utilities` | [![NuGet](https://img.shields.io/nuget/v/NeoVeldrid.Utilities)](https://www.nuget.org/packages/NeoVeldrid.Utilities) |
| `Veldrid.SDL2` | [![NuGet](https://img.shields.io/nuget/v/Veldrid.SDL2)](https://www.nuget.org/packages/Veldrid.SDL2) | `NeoVeldrid.SDL2` | [![NuGet](https://img.shields.io/nuget/v/NeoVeldrid.SDL2)](https://www.nuget.org/packages/NeoVeldrid.SDL2) |
| `Veldrid.RenderDoc` | [![NuGet](https://img.shields.io/nuget/v/Veldrid.RenderDoc)](https://www.nuget.org/packages/Veldrid.RenderDoc) | `NeoVeldrid.RenderDoc` | [![NuGet](https://img.shields.io/nuget/v/NeoVeldrid.RenderDoc)](https://www.nuget.org/packages/NeoVeldrid.RenderDoc) |

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

### ImGui.NET 1.91.x

**Likelihood Of Impact: Low**

The `NeoVeldrid.ImGui` package now uses ImGui.NET 1.91.6.1 (up from 1.90.1.1). The NeoVeldrid.ImGui API itself is unchanged, but if your own code also calls ImGui.NET directly, you may need to update it. See the [ImGui.NET releases](https://github.com/ImGuiNET/ImGui.NET/releases) for breaking changes.

### ImageSharp 3.x

**Likelihood Of Impact: Low**

The `NeoVeldrid.ImageSharp` package now uses SixLabors.ImageSharp 3.x (up from 1.x). The NeoVeldrid.ImageSharp API itself is unchanged, but if your own code also calls ImageSharp directly, you may need to update it. See the [ImageSharp 3.0 announcement](https://sixlabors.com/posts/announcing-imagesharp-300/) for breaking changes.

### Mouselook With `SetMousePosition`

**Likelihood Of Impact: Medium**

`Sdl2Window.SetMousePosition` is now a plain wrapper around `SDL_WarpMouseInWindow`. Code that captures the cursor and warps it back to a fixed anchor each frame to read the user's offset is no longer reliable on Windows. Patterns affected include:

- First-person camera mouselook.
- Orbit and pan cameras with mouse drag.
- Middle-button autoscroll.
- Drag-to-rotate or drag-to-pan viewport interactions.

Switch to relative mouse mode and read per-frame deltas via `MouseDelta`.

In Veldrid:

```csharp
// Per-frame, read the cursor's offset from the anchor and reset it back to the anchor
InputSnapshot snapshot = _window.PumpEvents();
Vector2 delta = _anchor - snapshot.MousePosition;
_window.SetMousePosition(_anchor);
yaw += delta.X * sensitivity;
pitch += delta.Y * sensitivity;
```

In NeoVeldrid:

```csharp
// When capture begins, enable relative mode to lock the cursor to the window and read deltas directly
_window.CursorRelativeMode = true;

// Per-frame
InputSnapshot snapshot = _window.PumpEvents();
Vector2 delta = _window.MouseDelta;
yaw -= delta.X * sensitivity; // Note the inverted sign
pitch -= delta.Y * sensitivity;

// When capture ends, disable relative mode to restore the cursor's original position
_window.CursorRelativeMode = false;
```

While `CursorRelativeMode` is active, the cursor is hidden and confined to the window; disabling it restores the original position.

Note the `+=` becomes `-=`. `_anchor - snapshot.MousePosition` measures how far the cursor has drifted from the anchor; `MouseDelta` measures how far it has moved since the previous frame. Same magnitude, opposite sign.
