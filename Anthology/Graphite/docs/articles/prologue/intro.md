---
uid: intro
---

# Introduction

NeoVeldrid is a low-level graphics library for .NET. It can be used to create high-performance 2D and 3D games, simulations, tools, and other graphical applications. Unlike most other .NET graphics libraries, NeoVeldrid is designed to be portable, meaning it is not tied to any particular operating system or native graphics API. With Vulkan, Direct3D 11, OpenGL, and OpenGL ES backends, applications built with NeoVeldrid can run on all desktop platforms without modification.

NeoVeldrid is a maintained fork of [Veldrid](https://github.com/mellinoe/veldrid), with all native bindings replaced by [Silk.NET](https://github.com/dotnet/Silk.NET). The public API is fully preserved - if you know Veldrid, you know NeoVeldrid.

## Features

* Modern GPU functionality:
  * Programmable vertex, geometry, tessellation, and fragment shaders
  * Programmable compute shaders
  * Full control over the graphics pipeline: blend state, depth-stencil state, rasterizer state, shaders and shader specialization, resource access, and more
  * Modern buffer features, including vertex, index, uniform, structured, and indirect buffers, read-write shader access, and more
  * Modern [texture](xref:textures) features, including 3D textures, array textures, multisampling, texture views, multi-output [Framebuffers](xref:framebuffers), read-write shader access, a variety of standard, compressed and packed [pixel formats](xref:NeoVeldrid.PixelFormat), and more
  * Multiple [Swapchains](xref:swapchains), with support for runtime creation and destruction
* Targets modern graphics APIs, including Vulkan and Direct3D 11.
* Cross-platform: Windows, macOS, Linux.
* Portable Shaders: First-class support for SPIR-V bytecode allows you to write your shaders once for all platforms.
* High-performance: Built on a thin, low-cost abstraction that is close to current-gen graphics APIs.
* Multi-threaded: Most NeoVeldrid objects can be used from multiple threads simultaneously, allowing you to split your work as you see fit. See [Multi-threading](xref:multi-threading) for specifics.
* Allocation-free: The core rendering loop (outside of resource creation and initialization) can be used without allocating any garbage-collected memory. Avoiding allocations is important for high-performance, low-latency rendering.
* Headless: Can be used without any application window or view, to perform GPU-accelerated operations in the background.
* .NET-friendly: Unlike native graphics APIs, NeoVeldrid was designed with .NET in mind, and integrates cleanly with regular .NET code.

## Platform Support

|               | Vulkan | D3D11 | OpenGL | OpenGL ES |
| :------------ | :----: | :---: | :----: | :-------: |
| Windows       |   ✅   |  ✅   |   ✅   |    ✅     |
| Linux         |   ✅   |  --   |   ✅   |    ✅     |
| macOS         | ✅ (1) |  --   |   ❌   |    --     |

(1) Uses [MoltenVK](https://github.com/KhronosGroup/MoltenVK), which translates Vulkan API calls to Metal. Bundled automatically with NeoVeldrid. No extra setup required.

## What NeoVeldrid Is and Isn't

NeoVeldrid is a **low-level GPU abstraction**. It gives you direct control over graphics resources (buffers, textures, shaders, pipelines, command lists) through a portable API that works across multiple graphics backends.

NeoVeldrid is **not** a game engine, a rendering framework, or a shader library. There is no built-in scene graph, no PBR materials, no shadow mapping, no post-processing, and no physics. You write your own shaders in GLSL or HLSL and build your own rendering pipeline on top of NeoVeldrid's primitives.

If you've worked with Vulkan, Direct3D, or OpenGL directly, NeoVeldrid operates at a similar level of abstraction but with a cleaner, unified .NET API. If you're looking for something higher-level, consider a .NET-friendly game engine like [Stride](https://www.stride3d.net/), [MonoGame](https://monogame.net/), or [Godot with C#](https://godotengine.org/).

## Getting Started

See [Getting Started](xref:getting-started-intro) for a basic startup guide.

## API Concepts

See [API Concepts](xref:api-concepts) for an overview of the common object types.

## Samples

See the [samples directory](https://github.com/jhm-ciberman/neo-veldrid/tree/main/samples) for demo applications, including NeoDemo (a full scene with shadows, reflections, and ImGui).
