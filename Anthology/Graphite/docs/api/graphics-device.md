# GraphicsDevice API

The device is the root object of Graphite: it creates resources, runs render graphs, tracks GPU completion, and owns the swapchain. This page covers `GraphicsDevice`, `GraphicsDeviceOptions`, `Swapchain`, `ExecutionTask` and `Fence`.

- [Overview](#overview)
- [Quick example](#quick-example)
- [GraphicsDeviceOptions](#graphicsdeviceoptions)
- [GraphicsDevice](#graphicsdevice-1)
- [Swapchain](#swapchain)
- [ExecutionTask and Fence](#executiontask-and-fence)
- [GraphicsDeviceFeatures](#graphicsdevicefeatures)
- [Common patterns](#common-patterns)
- [Pitfalls](#pitfalls)
- [See also](#see-also)

## Overview

A device is created once per window (or once headless), lives for the life of the application, and is disposed last. Everything else hangs off it.

Only Vulkan is implemented. `GraphicsBackend` has one value: `Vulkan`.

## Quick example

```csharp
GraphicsDeviceOptions options = new() { VulkanValidationLayers = false };

SwapchainDescription swapchain = new()
{
    Source = SwapchainSource.CreateVulkan(window.VkSurface!),
    Width = (uint)window.FramebufferSize.X,
    Height = (uint)window.FramebufferSize.Y,
    DepthFormat = PixelFormat.D24_UNorm_S8_UInt,
    SyncToVerticalBlank = true
};

GraphicsDevice device = GraphicsDevice.CreateVulkan(options, swapchain);

ExecutionTask task = device.DispatchGraph(pipeline, views);
device.WaitForExecution(task);

device.Dispose();
```

`window` is a Silk.NET `IWindow`. The sample helper [DeviceCreateUtilities](../../Samples/Shared/DeviceCreateUtilities.cs) does exactly this; see [Getting started](getting-started.md).

## GraphicsDeviceOptions

[`GraphicsDeviceOptions`](../../Graphite/Core/GraphicsDevice/GraphicsDeviceOptions.cs#L6) is a struct of public fields. It has one constructor, `GraphicsDeviceOptions(bool debug)`, and the object initializer form is the usual way to build it. Swapchain settings do not live here; they are set on the `SwapchainDescription` passed to `CreateVulkan`.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `VulkanValidationLayers` | `bool` | false | Enable Vulkan debug report and validation layers if installed |
| `PreferDepthRangeZeroToOne` | `bool` | false | Request 0..1 depth range |
| `PreferStandardClipSpaceYDirection` | `bool` | false | Request bottom-to-top clip space Y. Not the Vulkan default and not always available; check `IsClipSpaceYInverted` afterwards |
| `MaxFramesInFlight` | `uint` | 0 (3) | Execution ring size |
| `TransientBufferInitialSize` | `uint` | 0 (4 MB) | Initial size of each slot's transient bump buffer |
| `TransientBufferSoftCapBytes` | `uint` | 0 (64 MB) | Warn once past this many transient bytes in one execution |
| `TransientBufferHardCapBytes` | `uint` | 0 (256 MB) | Throw past this many transient bytes (validation on) |
| `GraphiteValidation` | `bool` | true | Graphite's own checks |
| `Profiler` | `IProfiler?` | null | See [Diagnostics](diagnostics.md) |

The caps are clamped so that soft >= initial and hard >= soft ([InitializeFrameOptions](../../Graphite/Core/GraphicsDevice/GraphicsDevice.Execution.cs#L210)).

### Creation

| Member | Signature | Description |
|--------|-----------|-------------|
| `IsBackendSupported` | `static bool IsBackendSupported(GraphicsBackend backend)` | True if the backend can run on this system |
| `CreateVulkan` | `static GraphicsDevice CreateVulkan(GraphicsDeviceOptions options, SwapchainDescription? swapchainDescription = null, VulkanDeviceOptions vkOptions = default)` | Creates the device. Pass null for a headless device |

`VulkanDeviceOptions` has `string[] InstanceExtensions` and `string[] DeviceExtensions` for extra required extensions.

`SwapchainDescription` fields: `Source`, `Width`, `Height`, `DepthFormat`, `SyncToVerticalBlank`, `ColorSrgb`. `SwapchainSource.CreateVulkan(IVkSurface)` builds the source from a Silk.NET Vulkan surface.

## GraphicsDevice

### Identity and capabilities

| Member | Signature | Description |
|--------|-----------|-------------|
| `DeviceName` | `string DeviceName { get; }` | Adapter name |
| `VendorName` | `string VendorName { get; }` | Adapter vendor |
| `ApiVersion` | `GraphicsApiVersion ApiVersion { get; }` | `Major`, `Minor`, `Subminor`, `Patch`, `IsKnown` |
| `BackendType` | `GraphicsBackend BackendType { get; }` | Always `Vulkan` |
| `IsUvOriginTopLeft` | `bool` | Texture origin convention, matters for sampling framebuffers |
| `IsDepthRangeZeroToOne` | `bool` | Depth range convention |
| `IsClipSpaceYInverted` | `bool` | Clip-space Y direction, which projection matrices depend on |
| `Features` | `GraphicsDeviceFeatures Features { get; }` | Optional feature flags, see [below](#graphicsdevicefeatures) |
| `UniformBufferMinOffsetAlignment` | `uint` | Offsets into uniform buffers must be a multiple of this |
| `StructuredBufferMinOffsetAlignment` | `uint` | Same for structured buffers |
| `GetSampleCountLimit` | `TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)` | Highest MSAA count for a format |
| `GetPixelFormatSupport` | `bool GetPixelFormatSupport(PixelFormat, TextureType, TextureUsage)` and an overload with `out PixelFormatProperties` | Whether a format/type/usage combination works, plus max width, height, depth, mips, layers |
| `GetVulkanInfo` | `bool GetVulkanInfo(out BackendInfoVulkan? info)` and `BackendInfoVulkan GetVulkanInfo()` | Raw Vulkan handles. The second throws on a non-Vulkan device |

### Resources and default objects

| Member | Signature | Description |
|--------|-----------|-------------|
| `ResourceFactory` | `ResourceFactory ResourceFactory { get; }` | Creates buffers, textures, views, samplers, framebuffers, programs, command buffers, fences, swapchains |
| `PointSampler` | `Sampler` | Point filtered, wrap, owned by the device and never disposed by callers |
| `LinearSampler` | `Sampler` | Linear filtered, wrap, owned by the device |
| `Aniso4xSampler` | `Sampler` | Throws unless `Features.SamplerAnisotropy` |
| `NullTexture2D`, `NullTextureRW2D` | `Texture` | 1x1 fallbacks substituted for unbound texture slots |
| `NullUniform`, `NullStructured`, `NullStructuredRW` | `DeviceBuffer` | Fallback buffers for unbound slots. The structured ones need `Features.StructuredBuffer` |

The "null" resources are what a draw binds when a shader asks for a slot your `PropertySet` did not fill. See [Property sets](property-sets.md) and `OnMissingProperty` in [Diagnostics](diagnostics.md).

### Running work

| Member | Signature | Description |
|--------|-----------|-------------|
| `DispatchGraph` | `ExecutionTask DispatchGraph<T>(RenderPipeline<T> pipeline, IReadOnlyList<T> views) where T : IRenderView` | Runs the pipeline for each view as one execution, completes it, then presents if any pass called `Present()`. Returns the task without waiting |
| `BeginExecution` | `ExecutionTask BeginExecution()` | Takes a ring slot, blocking on the oldest in-flight task if all are busy. Used by `DispatchGraph` |
| `CompleteExecution` | `void CompleteExecution(ExecutionTask task)` | Marks CPU recording done. Non-blocking |
| `IsExecutionComplete` | `bool IsExecutionComplete(ExecutionTask task)` | Poll GPU completion |
| `WaitForExecution` | `bool WaitForExecution(ExecutionTask task, ulong nanosecondTimeout = ulong.MaxValue)` | Block until that task finishes. False on timeout |
| `WaitForIdle` | `void WaitForIdle()` | Block until all GPU work is done and reclaim every slot |
| `SubmitAndWait` | `void SubmitAndWait(TransferCommandBuffer commandBuffer)` | Submit an ended transfer buffer and block. Outside the ring |
| `WaitForFence` / `ResetFence` | `bool WaitForFence(Fence, ulong nanosecondTimeout = ulong.MaxValue)` / `void ResetFence(Fence)` | Wait on or reset a fence you created |
| `MaxExecutingTasks` | `uint` | Ring size (`MaxFramesInFlight`, default 3) |
| `ExecutingTasks` | `uint` | Tasks currently in flight (reclaims finished ones as a side effect) |
| `ActiveExecutions` | `IReadOnlyList<ExecutionTask>` | Snapshot of in-flight tasks, oldest first |
| `LastCompletedExecutionId` | `ulong` | Highest id known finished. Updated lazily; `IsExecutionComplete` is exact |

### Swapchain access

| Member | Signature | Description |
|--------|-----------|-------------|
| `MainSwapchain` | `Swapchain MainSwapchain { get; }` | Null for a headless device |
| `SwapchainFramebuffer` | `Framebuffer? SwapchainFramebuffer { get; }` | Current main swapchain framebuffer |
| `SyncToVerticalBlank` | `bool SyncToVerticalBlank { get; set; }` | Runtime vsync toggle. Throws (with validation on) if there is no swapchain |
| `SwapBuffers` | `void SwapBuffers()` and `void SwapBuffers(Swapchain swapchain)` | Present, then acquire the next image. The GPU waits for rendering before presenting and for the image before the next frame renders; the CPU does not wait on either. `DispatchGraph` calls this for you when a pass wrote the backbuffer |
| `ResizeMainWindow` | `void ResizeMainWindow(uint width, uint height)` | Resizes the main swapchain |

### CPU access to resources

| Member | Signature | Description |
|--------|-----------|-------------|
| `Map` | `MappedResource Map(MappableResource resource, MapMode mode, uint subresource = 0)` | Map to CPU memory |
| `Map<T>` | `MappedResourceView<T> Map<T>(MappableResource, MapMode, uint subresource = 0) where T : unmanaged` | Typed indexable view |
| `Unmap` | `void Unmap(MappableResource resource, uint subresource = 0)` | Release the map |
| `UpdateBuffer` | `void UpdateBuffer(DeviceBuffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)` plus `UpdateBuffer<T>(..., in T)` and `UpdateBuffer<T>(..., ReadOnlySpan<T>)` | Immediate buffer upload |
| `UpdateTexture` | `void UpdateTexture(Texture, IntPtr source, uint sizeInBytes, uint x, uint y, uint z, uint width, uint height, uint depth, uint mipLevel, uint arrayLayer)` plus a `ReadOnlySpan<T>` overload | Immediate texture upload |

Details and examples are on the [buffers and textures](buffers-and-textures.md) page.

### Transient pools and deferred disposal

| Member | Signature | Description |
|--------|-----------|-------------|
| `RentTransientBuffer` | `DeviceBuffer RentTransientBuffer(ExecutionTask task, in BufferDescription desc)` | Pooled buffer valid for this execution, recycled after the GPU finishes it |
| `RentTransientTexture` | `Texture RentTransientTexture(ExecutionTask task, in RenderTextureDescription desc)` | First color texture of a pooled bundle. Needs at least one color format |
| `RentTransientFramebuffer` | `Framebuffer RentTransientFramebuffer(ExecutionTask task, in RenderTextureDescription desc)` | Framebuffer of a pooled bundle |
| `RentTransientRenderTexture` | `RenderTexture RentTransientRenderTexture(ExecutionTask task, in RenderTextureDescription desc)` | The full bundle |
| `DisposeWhenIdle` | `void DisposeWhenIdle(IDisposable disposable)` | Queue disposal for when the device goes idle. For objects the GPU might still be reading |

Most applications never call the `RentTransient*` methods directly; the render graph does it for you through `GraphTextureDesc`. See [Render graph](render-graph.md).

### Warnings

| Member | Signature | Description |
|--------|-----------|-------------|
| `OnWarning` | `GraphicsDeviceWarningHandler? OnWarning { get; set; }` | Defaults to writing to `Console.Error` |
| `OnMissingProperty` | `MissingPropertyHandler? OnMissingProperty { get; set; }` | Null (silent) by default |
| `Profiler`, `SetProfiler`, `GetMemoryBudget` | see [Diagnostics](diagnostics.md) | |

### Lifetime

| Member | Signature | Description |
|--------|-----------|-------------|
| `IsDisposed` | `bool` | True after `Dispose` |
| `Dispose` | `void Dispose()` | Waits for idle, frees pools and default resources, then the backend. Idempotent. Dispose child resources first |

## Swapchain

`Swapchain` (derived from `GraphicsResource`) presents images to a window surface. You rarely touch it directly because `GraphicsDevice` forwards the common operations.

| Member | Signature | Description |
|--------|-----------|-------------|
| `Framebuffer` | `Framebuffer Framebuffer { get; }` | Render target for the current back buffer |
| `Resize` | `void Resize(uint width, uint height)` | Recreate for a new size |
| `SyncToVerticalBlank` | `bool { get; set; }` | Vsync |

Use `device.ResourceFactory.CreateSwapchain(SwapchainDescription)` for additional windows, then `device.SwapBuffers(swapchain)`.

## ExecutionTask and Fence

An `ExecutionTask` is the handle for one dispatched frame of work. The device keeps a ring of them (default 3) so the CPU can record frame N+1 while the GPU draws frame N.

| Member | Signature | Description |
|--------|-----------|-------------|
| `Id` | `ulong Id { get; }` | Monotonic, starts at 1 |
| `RingSlot` | `uint RingSlot { get; }` | Slot index in the ring |
| `CompletionFence` | `Fence CompletionFence { get; }` | Signals when the GPU work is done. Owned by the device and recycled when the slot is reused, so it is valid only for the task's lifetime |
| `Device` | `GraphicsDevice Device { get; }` | Owner |

Submitting commands and allocating transient uniform memory on a task are internal operations performed by the render graph's `RenderContext`; see [Render graph](render-graph.md) and [Device and execution internals](../internals/02-device-and-execution.md).

`Fence` (derived from `GraphicsResource`) has `bool Signaled { get; }` and `void Reset()`. Create one with `ResourceFactory.CreateFence(bool signaled)` and wait with `device.WaitForFence`.

## GraphicsDeviceFeatures

Read from `device.Features` (also `ResourceFactory.Features`). Every member is a `bool`.

| Feature | Meaning |
|---------|---------|
| `ComputeShader`, `GeometryShader`, `TessellationShaders` | Shader stage support |
| `MultipleViewports` | More than one viewport |
| `SamplerLodBias`, `SamplerAnisotropy` | Sampler features |
| `DrawBaseVertex`, `DrawBaseInstance` | Non-zero base values in `DrawIndexed` |
| `DrawIndirect`, `DrawIndirectBaseInstance` | Indirect draws |
| `DepthClipDisable`, `IndependentBlend` | Pipeline state features |
| `Texture1D`, `SubsetTextureView` | Texture features |
| `StructuredBuffer`, `BufferRangeBinding` | Buffer features |
| `CommandBufferDebugMarkers` | Debug groups and markers |
| `ShaderFloat64` | Double precision in shaders |

With validation on, using an unsupported feature throws a `RenderException`.

## Common patterns

Resize handling:

```csharp
window.FramebufferResize += size => device.ResizeMainWindow((uint)size.X, (uint)size.Y);
```

Clean shutdown:

```csharp
device.WaitForIdle();
pipeline.Dispose();
mesh.Dispose();
shader.Dispose();
device.Dispose();
```

`Dispose` also waits for idle.

One-off readback or upload outside the frame loop:

```csharp
TransferCommandBuffer transfer = device.ResourceFactory.CreateTransferCommandBuffer();
transfer.Begin();
transfer.CopyBuffer(source, 0, staging, 0, source.SizeInBytes);
transfer.End();
device.SubmitAndWait(transfer);
```

Frames in flight: the default of 3 trades latency against throughput. Fewer frames lower input latency; more frames help only when `BeginExecution` blocks while the GPU is not the bottleneck.

## Pitfalls

- `DispatchGraph` does not wait. The returned task is in flight until `IsExecutionComplete` or `WaitForExecution` says otherwise.
- `BeginExecution` blocks when every ring slot is busy. A stall there means the CPU is ahead of the GPU by `MaxFramesInFlight` frames.
- Disposing a resource the GPU may still be reading is invalid. `DisposeWhenIdle` and `WaitForIdle` make it safe.
- `Dispose` on the device requires children to be disposed already. Device-owned defaults (`LinearSampler`, `NullTexture2D`, ...) are disposed by the device.
- `SyncToVerticalBlank`, `ResizeMainWindow` and `SwapBuffers()` need a main swapchain. A headless device throws.
- `LastCompletedExecutionId` advances lazily, on reclaim. It is not a precise "GPU is done" signal.
- `ValidationEnabled` is static, so the last device created decides validation for every device in the process.
- A single device does not support arbitrary multi-threaded recording.

## See also

- [Device and execution internals](../internals/02-device-and-execution.md)
- [Architecture](../internals/01-architecture.md)
- [Getting started](getting-started.md)
- [Buffers and textures](buffers-and-textures.md)
- [Render graph](render-graph.md)
- [Diagnostics](diagnostics.md)
