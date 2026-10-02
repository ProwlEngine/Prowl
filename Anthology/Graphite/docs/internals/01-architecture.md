# Architecture

The 10,000-foot view of Graphite: how the solution is laid out, how the abstract Core is split from the Vulkan backend, and how one frame travels from shader compilation to a presented image.

- [Project layout](#project-layout)
- [Core abstract, Vulkan concrete](#core-abstract-vulkan-concrete)
- [Key types](#key-types)
- [End-to-end flow](#end-to-end-flow)
- [Design decisions](#design-decisions)
- [See also](#see-also)

## Overview

Graphite is a low-level graphics and compute library for .NET. Application code talks to a small set of abstract classes (`GraphicsDevice`, `ResourceFactory`, `CommandBuffer`, ...) and never sees Vulkan types. A single concrete backend (Vulkan, through Silk.NET) implements those abstractions in `internal` classes prefixed `Vk`. On top of the device sits a render graph (`RenderPipeline<TView>`) that orders passes and hands each one a `RenderContext<TView>`; beside it, the ShaderDef projects compile Slang source into the `ShaderDescription` that the factory turns into a program.

## Project layout

The solution file is [Prowl.Graphite.slnx](../../Prowl.Graphite.slnx). Everything targets net10.0.

| Folder | Project | Role |
| --- | --- | --- |
| `Graphite/` | `Prowl.Graphite` | The library. `Core/` is backend-agnostic, `Platform/Vulkan/` is the backend, `Profiling/` and `ValidationLayers/` are optional layers compiled into the same assembly. |
| `ShaderDef/Core/` | `Prowl.Graphite.ShaderDef` | Shader data model: `ShaderPass`, `PassState`, keywords, variant spaces, the `IShaderCompiler` interface. References Graphite. |
| `ShaderDef/Compiler/` | `Prowl.Graphite.ShaderDef.Compiler` | Tokenizer, parser, variant generator, and `SlangShaderCompiler` (the Slang-backed `IShaderCompiler`). |
| `Samples/` | `HelloTriangle`, `Cube`, `CubeGrid`, `TexturedQuad`, `PBRRenderer`, `Shared` | Runnable apps. `Shared` holds window/device bootstrap, shader loading and mesh helpers. |
| `Tests/` | `Graphite`, `ShaderDef`, `ShaderDef.Compiler` | Unit tests. The library exposes internals to the Graphite test project. |
| `Tools/` | `SlangQuickCompile`, `GraphiteBench` | Command-line Slang compile helper and a benchmark harness. |

Inside `Graphite/Core/`:

| Folder | Contents |
| --- | --- |
| `GraphicsDevice/` | `GraphicsDevice` (split across partial files), `ExecutionTask`, options, features, transient pools |
| `RenderGraph/` | `RenderPipeline<T>`, `RenderGraph<T>`, `RenderContext<T>`, pass and resource descriptions |
| `CommandBuffer/` | `CommandBuffer`, `TransferCommandBuffer`, `Fence`, draw/dispatch argument structs |
| `DeviceBuffer/`, `Texture/`, `RenderTexture/`, `Framebuffer/`, `Swapchain/` | Resource types and their descriptions |
| `Shader/`, `Pipeline/`, `Binding/` | Programs, pipeline state descriptions, `PropertySet` |
| `Identifiers/` | Interned ids (`PropertyID`, `RenderResourceID`, `ShaderID`, `VertexAttributeID`) |

## Core abstract, Vulkan concrete

Every GPU object is a `GraphicsResource` subclass. Core declares what it does; the backend supplies how. Backend classes are `internal`, so the only public way to get one is through the device and its factory.

The factory uses the template-method pattern. [ResourceFactory.CreateBuffer](../../Graphite/Core/ResourceFactory.cs#L116) first runs a validation hook (`CreateBuffer_CheckDescription`) and then calls the abstract `CreateBufferCore`, which `VkResourceFactory` implements by constructing a `VkBuffer`. The same shape recurs on `GraphicsDevice` (`SwapBuffers` -> `SwapBuffersCore`, `BeginExecution` -> `BeginExecutionCore`) and on `GraphicsResource` (`Dispose` -> `DisposeCore`). Public methods own validation and profiling; `*Core` methods are backend-only.

The backend is chosen at device creation: [GraphicsDevice.CreateVulkan](../../Graphite/Core/GraphicsDevice/GraphicsDevice.Factory.cs#L33) returns `new Vk.VkGraphicsDevice(...)`. Defining `ExcludeVulkan=true` in the build removes the backend and the Silk.NET packages via the `EXCLUDE_VULKAN_BACKEND` constant (see [Prowl.Graphite.csproj](../../Graphite/Prowl.Graphite.csproj)).

### Partial-class layering

Large types are split across files by concern, and the optional layers are also partials of the same type. `GraphicsDevice` for example is `GraphicsDevice.cs`, `.Factory`, `.Execution`, `.DefaultResources`, `.DeferredDisposal`, `.TransientBuffers`, `.TransientTextures`, `.Updates`, plus `GraphicsDevice.Profiling.cs` and `GraphicsDevice.Validation.cs` from the layer folders. `VkGraphicsDevice` mirrors this with `.Init`, `.Execution`, `.Submission`, `.Staging`, `.Timing`, `.DebugMarkers`, and so on.

### How the optional layers hook in

Both layers are plain partial-class files in the same assembly. There is no conditional compilation and no decorator object.

| Layer | Mechanism | Off switch |
| --- | --- | --- |
| Validation | `*_Check*` helper methods that begin with `if (!GraphicsDevice.ValidationEnabled) return;` | `GraphicsDeviceOptions.GraphiteValidation = false` (validation is on unless it is `false`) |
| Profiling | `Profiler` property, each hook starts with `if (Profiler is not { } profiler) return;` or uses `Profiler?.Record...` | `GraphicsDeviceOptions.Profiler = null` (default) |

[ValidationEnabled](../../Graphite/ValidationLayers/Core/GraphicsDevice/GraphicsDevice.Validation.cs#L8) is a static field written once in `InitializeFrameOptions`. [SetProfiler](../../Graphite/Profiling/Core/GraphicsDevice/GraphicsDevice.Profiling.cs) swaps the profiler at any time. The two layers do not depend on each other. See [08-validation-and-profiling.md](08-validation-and-profiling.md).

## Key types

| Core type | Vulkan implementation | Notes |
| --- | --- | --- |
| `GraphicsDevice` | `VkGraphicsDevice` | Owns instance, device, queues, memory manager, slots |
| `ResourceFactory` | `VkResourceFactory` | Thin: each `Create*Core` calls a `Vk*` constructor |
| `ExecutionTask` | `VkExecutionTask` | One in-flight frame: id, ring slot, fence, transient arena |
| `Fence` | `VkFence` | Wraps a `VkFence` handle |
| `DeviceBuffer` | `VkBuffer` | Sub-allocated from `VkDeviceMemoryManager` |
| `Texture` / `TextureView` / `Sampler` | `VkTexture` / `VkTextureView` / `VkSampler` | Texture rests in a layout computed from its usage; no layout is stored |
| `Framebuffer` | `VkFramebufferBase` -> `VkFramebuffer`, `VkSwapchainFramebuffer` | |
| `Swapchain` | `VkSwapchain` | |
| `CommandBuffer` | `VkCommandBuffer` | Owns a `VkDescriptorBinder` |
| `TransferCommandBuffer` | `VkTransferCommandBuffer` | Independent of the frame ring |
| `GraphicsProgram` / `ComputeProgram` | `VkGraphicsProgram` / `VkComputeProgram` | Own layouts, pipeline cache, descriptor cache |

`RenderTexture` is a sealed helper (not a `GraphicsResource`) that bundles color textures with a `Framebuffer`. `CommandBuffer` and `TransferCommandBuffer` derive from `CommandBufferBase`, not from `GraphicsResource`.

## End-to-end flow

One frame, from shader source to a presented image:

1. **Shader compile.** [ShaderParser.Parse](../../ShaderDef/Compiler/ShaderParser.cs#L103) turns source into a `ShaderDefinition`, and [ShaderDefinition.Create](../../ShaderDef/Core/ShaderDefinition.cs#L56) binds it to a device with a `SlangShaderCompiler`. Each `ShaderPass` resolves a variant into a `ShaderDescription` with reflected stages, vertex inputs and resource layouts. See [06-shader-compiler.md](06-shader-compiler.md).
2. **Device creation.** [GraphicsDevice.CreateVulkan](../../Graphite/Core/GraphicsDevice/GraphicsDevice.Factory.cs#L33) takes `GraphicsDeviceOptions` and a swapchain description built from a window surface. The Vulkan constructor is [VkGraphicsDevice](../../Graphite/Platform/Vulkan/VkGraphicsDevice/VkGraphicsDevice.cs#L29); see [07-vulkan-backend.md](07-vulkan-backend.md) and [02-device-and-execution.md](02-device-and-execution.md).
3. **Resources.** `device.ResourceFactory.CreateGraphicsProgram(description)` validates and builds a `VkGraphicsProgram`. The pipeline object (`RenderPipeline<TView>`) lazily builds its `RenderGraph<TView>` on first use of [Graph](../../Graphite/Core/RenderGraph/RenderPipeline.cs), calling `InitializePasses` once. See [05-programs-and-pipelines.md](05-programs-and-pipelines.md) and [03-render-graph.md](03-render-graph.md).
4. **Frame.** [DispatchGraph](../../Graphite/Core/RenderGraph/GraphicsDevice.DispatchRenderGraph.cs#L14) wraps one `BeginExecution`/`CompleteExecution` pair around all views. For each view it builds a `RenderContext` and calls [ExecuteView](../../Graphite/Core/RenderGraph/RenderPipeline.cs#L97), which walks the topologically sorted passes. Passes rent command buffers with [GetCommandBuffer](../../Graphite/Core/RenderGraph/RenderContext.cs#L78), record into them, and hand them back with [SubmitCommandBuffer](../../Graphite/Core/RenderGraph/RenderContext.cs#L97), which queues them on the `ExecutionTask` (nothing reaches the GPU yet). At draw time the Vulkan command buffer resolves a pipeline for the current framebuffer and topology and binds descriptor sets; see [04-resource-binding.md](04-resource-binding.md).
5. **Submit and present.** [CompleteExecution](../../Graphite/Core/GraphicsDevice/GraphicsDevice.Execution.cs#L108) makes the backend submit every queued buffer in one `vkQueueSubmit`, signaling the slot's fence. If any view's graph writes the backbuffer, `DispatchGraph` then calls [SwapBuffers](../../Graphite/Core/GraphicsDevice/GraphicsDevice.cs#L113), which presents and acquires the next image.

## Design decisions

### Why an abstract Core with `private protected` *Core methods?
Validation, profiling and argument checks live once in Core. A backend cannot forget them because the public entry point is non-virtual. `private protected abstract` also keeps the extension surface invisible outside the assembly, since `GraphicsDevice` has an `internal` constructor and only this assembly can subclass it.

### Why partial classes for layers instead of decorators?
A decorator would wrap every resource and double the object count. Partial classes let `VkTexture` call `Constructor_RecordAllocation()` directly on itself, and the check is one boolean read when disabled. The cost is that the layers cannot be excluded from a build the way the Vulkan backend can.

### Why the render graph sits on top of the device, not inside it
`DispatchGraph` is an extension of `GraphicsDevice` in a separate file that only uses the public `BeginExecution`/`CompleteExecution` API. You can drive the device without the graph (create your own `ExecutionTask` and command buffers) and the graph is just one client.

## See also

- [02-device-and-execution.md](02-device-and-execution.md), [07-vulkan-backend.md](07-vulkan-backend.md)
- [03-render-graph.md](03-render-graph.md), [04-resource-binding.md](04-resource-binding.md), [05-programs-and-pipelines.md](05-programs-and-pipelines.md), [06-shader-compiler.md](06-shader-compiler.md), [08-validation-and-profiling.md](08-validation-and-profiling.md)
- API: [getting-started](../api/getting-started.md), [graphics-device](../api/graphics-device.md), [render-graph](../api/render-graph.md)
