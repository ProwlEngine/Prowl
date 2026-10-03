# Diagnostics API

Validation, profiling, warnings and debug tooling: the options that turn them on and the types you use to read results.

- [Overview](#overview)
- [Quick example](#quick-example)
- [Options](#options)
- [Validation](#validation)
- [Profiling (IProfiler)](#profiling-iprofiler)
- [Event payload types](#event-payload-types)
- [Memory budget](#memory-budget)
- [Warnings and missing properties](#warnings-and-missing-properties)
- [Debug markers and Vulkan validation](#debug-markers-and-vulkan-validation)
- [Common patterns](#common-patterns)
- [Pitfalls](#pitfalls)
- [See also](#see-also)

## Overview

Graphite has four diagnostic channels, all configured through [`GraphicsDeviceOptions`](../../Graphite/Core/GraphicsDevice/GraphicsDeviceOptions.cs#L6) or properties on `GraphicsDevice`:

| Channel | Turn on with | Output |
|---------|--------------|--------|
| Validation layer | `GraphiteValidation` (on by default) | `RenderException` thrown at the bad call |
| Profiler | `Profiler` option or `SetProfiler` | callbacks on your `IProfiler` |
| Device warnings | `OnWarning` (on by default, writes to `Console.Error`) | non-fatal messages |
| Driver validation | `VulkanValidationLayers = true` | Vulkan validation layer messages, errors rethrown as `RenderException` |

Internals are in [Validation and profiling internals](../internals/08-validation-and-profiling.md).

## Quick example

```csharp
using System.Collections.Generic;
using Prowl.Graphite;

sealed class DrawCounter : IProfiler
{
    public int Draws;
    public long TextureBytes;
    public double GpuMs;

    public void Allocate(AllocBin type, long bytes) { if (type == AllocBin.Texture) TextureBytes += bytes; }
    public void Free(AllocBin type, long bytes) { if (type == AllocBin.Texture) TextureBytes -= bytes; }
    public void AllocateMemory(BufferRoleBin role, long bytes) { }
    public void FreeMemory(BufferRoleBin role, long bytes) { }
    public void Record(BufferOpBin op, long bytes) { }
    public void RecordSwap(SwapBin evt, long bytes) { }
    public void RecordResourceSetBind(uint setCount) { }
    public void RecordBarrier(BarrierBin kind, uint count) { }
    public void BeginView(in ViewInfo view) { Draws = 0; }
    public void EndView(in ViewInfo view) { }
    public void BeginPass(in PassInfo pass) { }
    public void EndPass(in PassInfo pass) { }
    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }
    public void RecordSubmit(in CommandBufferInfo commandBuffer, bool isTransfer) { }
    public void RecordPipelineSwitch(in CommandBufferInfo commandBuffer, in PipelineBindInfo info) { }
    public void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info) { Draws++; }
    public void RecordDrawBuffers(in CommandBufferInfo commandBuffer, in DrawBufferInfo info) { }
    public void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info) { }
    public bool RequestMetadata => false;
    public void RecordPassMetadata(in PassInfo pass, object metadata) { }
    public void RecordDrawMetadata(in CommandBufferInfo commandBuffer, object metadata) { }
    public bool RequestGPUStatistics => true;
    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds) { GpuMs += milliseconds; }
    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }
    public bool RequestCapture => false;
    public void Capture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer) { }
}

DrawCounter counter = new();

GraphicsDeviceOptions options = new(debug: false, swapchainDepthFormat: PixelFormat.D24_UNorm_S8_UInt)
{
    GraphiteValidation = true,
    Profiler = counter
};
```

Pass `options` to `GraphicsDevice.CreateVulkan` as shown in [Getting started](getting-started.md).

## Options

| Field | Type | Default | Effect |
|-------|------|---------|--------|
| `VulkanValidationLayers` | `bool` | `false` | Enables the Vulkan debug-report extension and any installed validation layers |
| `GraphiteValidation` | `bool` | `true` | Turns the Graphite validation layer on or off |
| `Profiler` | `IProfiler?` | `null` | Initial profiler. No implementation ships with Graphite |
| `TransientBufferSoftCapBytes` | `uint` | 0 (64 MB) | Over this, `OnWarning` fires once per device |
| `TransientBufferHardCapBytes` | `uint` | 0 (256 MB) | Over this, a `RenderException` is thrown (only while validation is on) |

Related `GraphicsDevice` members:

| Member | Signature | Description |
|--------|-----------|-------------|
| `Profiler` | `IProfiler? Profiler { get; }` | The attached profiler, or null |
| `SetProfiler` | `void SetProfiler(IProfiler? profiler)` | Replaces or removes the profiler immediately. Call it between executions |
| `GetMemoryBudget` | `MemoryBudgetInfo GetMemoryBudget()` | Driver-reported VRAM budget, see [below](#memory-budget) |
| `OnWarning` | `GraphicsDeviceWarningHandler? OnWarning { get; set; }` | Non-fatal warnings |
| `OnMissingProperty` | `MissingPropertyHandler? OnMissingProperty { get; set; }` | Shader slot without a bound property |

## Validation

The validation layer is a fixed set of checks inside the library. There is no public type to call. You control it with one option:

```csharp
GraphicsDeviceOptions release = new(debug: false, swapchainDepthFormat: null)
{
    GraphiteValidation = false
};
```

What it catches, by area:

| Area | Examples |
|------|----------|
| Resource creation | zero-sized textures, `Staging` combined with other usages, uniform buffers not a multiple of 16 bytes, missing feature support, bad vertex or resource layouts |
| Mapping and uploads | mapping a buffer without `Dynamic` or `Staging`, read-mapping a non-`Staging` buffer, out-of-bounds `UpdateTexture` regions |
| Command recording | drawing without a shader, framebuffer or vertex source, clearing a missing depth target, unaligned indirect offsets |
| Submission | submitting a command buffer that was not ended |
| Copies | null arguments, mismatched texture formats or sizes, out-of-range regions |
| Lifetime | referencing a disposed resource |
| Transient memory | exceeding `TransientBufferHardCapBytes` |

All failures are `RenderException`. Validation is on unless `GraphiteValidation` is `false`.

## Profiling (IProfiler)

[`IProfiler`](../../Graphite/Profiling/Core/IProfiler.cs#L5) is attached to the device. Every member is required; unneeded ones can be empty.

| Member | Signature | When it fires |
|--------|-----------|---------------|
| `Allocate` / `Free` | `void Allocate(AllocBin type, long bytes)` | resource create and dispose (bytes are 0 for pure object counts) |
| `AllocateMemory` / `FreeMemory` | `void AllocateMemory(BufferRoleBin role, long bytes)` | per usage role of each buffer |
| `Record` | `void Record(BufferOpBin op, long bytes)` | map, unmap, update, copy |
| `RecordSwap` | `void RecordSwap(SwapBin evt, long bytes)` | present, resize, acquire |
| `RecordResourceSetBind` | `void RecordResourceSetBind(uint setCount)` | property sets bound at draw or dispatch |
| `RecordBarrier` | `void RecordBarrier(BarrierBin kind, uint count)` | layout transitions and memory barriers |
| `BeginView` / `EndView` | `void BeginView(in ViewInfo view)` | once per view in `DispatchGraph` |
| `BeginPass` / `EndPass` | `void BeginPass(in PassInfo pass)` | around each graph pass |
| `RecordPassRead` | `void RecordPassRead(in PassInfo, RenderResourceID, RenderTexture?, DeviceBuffer?)` | per pass input before, per output after |
| `RecordSubmit` | `void RecordSubmit(in CommandBufferInfo, bool isTransfer)` | command buffer submission |
| `RecordPipelineSwitch` | `void RecordPipelineSwitch(in CommandBufferInfo, in PipelineBindInfo)` | `SetShader` / `SetComputeShader` |
| `RecordDraw` | `void RecordDraw(in CommandBufferInfo, in DrawCallInfo)` | each draw |
| `RecordDrawBuffers` | `void RecordDrawBuffers(in CommandBufferInfo, in DrawBufferInfo)` | each draw, only if `RequestCapture` |
| `RecordDispatch` | `void RecordDispatch(in CommandBufferInfo, in DispatchCallInfo)` | each compute dispatch |
| `RequestMetadata` | `bool RequestMetadata { get; }` | polled before metadata objects are built |
| `RecordPassMetadata` | `void RecordPassMetadata(in PassInfo, object)` | when your pass calls `RenderContext.RecordPassMetadata` |
| `RecordDrawMetadata` | `void RecordDrawMetadata(in CommandBufferInfo, object)` | when you call `CommandBuffer.RecordMetadata` |
| `RequestGPUStatistics` | `bool RequestGPUStatistics { get; }` | enables GPU timing and pipeline statistics |
| `RecordExecutionTime` | `void RecordExecutionTime(in CommandBufferInfo, bool isTransfer, double milliseconds)` | after the submission's fence signals, a few frames late |
| `RecordGpuVertexStats` | `void RecordGpuVertexStats(in CommandBufferInfo, in GpuVertexStats)` | same, if the device supports pipeline statistics |
| `RequestCapture` | `bool RequestCapture { get; }` | enables per-draw buffer capture and per-pass output capture |
| `Capture` | `void Capture(in PassInfo, IReadOnlyList<Framebuffer>, TransferCommandBuffer)` | after each pass, with its output framebuffers |

### Bins

| Enum | Members |
|------|---------|
| `AllocBin` | `DeviceBuffer`, `Texture`, `TextureView`, `Sampler`, `Framebuffer`, `Pipeline`, `Shader`, `ResourceLayout`, `ResourceSet`, `CommandBuffer` |
| `BufferRoleBin` | `Vertex`, `Index`, `Uniform`, `StructuredReadOnly`, `StructuredReadWrite`, `Indirect`, `Dynamic`, `Staging` (overlapping for multi-usage buffers) |
| `BufferOpBin` | `Map`, `Unmap`, `Update`, `Copy` |
| `SwapBin` | `Present`, `Resize`, `Acquire` |
| `BarrierBin` | `TextureTransition`, `BufferTransition`, `MemoryBarrier` |
| `DrawKind` | `Draw`, `DrawIndexed`, `DrawIndirect`, `DrawIndexedIndirect` |

### Attaching metadata

Metadata is arbitrary `object` data you attach to a pass or to the draws since the last call. `WantsMetadata` is true when the profiler reads metadata, so the example builds metadata only then.

```csharp
public void Render(RenderContext<SceneView> context, CommandBuffer cmd)
{
    if (context.WantsMetadata)
        context.RecordPassMetadata(new { Kind = "Opaque", Objects = 128 });

    if (cmd.WantsMetadata)
        cmd.RecordMetadata("Terrain chunk 4");
}
```

## Event payload types

Defined in [ProfilerTypes.cs](../../Graphite/Profiling/Core/ProfilerTypes.cs). All are `readonly struct` unless noted.

| Type | Key members |
|------|-------------|
| `ViewInfo` | `Name`, `Index`, `PixelWidth`, `PixelHeight` |
| `PassInfo` | `Name`, `Index`, `Inputs`, `Outputs` (`ReadOnlyMemory<RenderResourceID>`) |
| `CommandBufferInfo` | `Id` (unique per rental), `Name`, `Pass` (`PassInfo?`) |
| `DrawCallInfo` | `Kind`, `VertexOrIndexCount`, `InstanceCount`, `DrawCount`, `IsIndirect`, `Topology` |
| `DispatchCallInfo` | `GroupCountX`, `GroupCountY`, `GroupCountZ`, `IsIndirect` |
| `PipelineBindInfo` | `ShaderName`, `IsCompute`, `Stages`, `Program` (`object`, cast to `GraphicsProgram` or `ComputeProgram`) |
| `DrawBufferInfo` | `VertexBuffers`, `IndexBuffer`, `BoundBuffers` (each `BufferBindingInfo`) |
| `BufferBindingInfo` | `Name`, `Buffer`, `Offset`, `SizeInBytes`, `ContentVersion`, `ReadOnly` |
| `GpuVertexStats` | `InputAssemblyVertices`, `InputAssemblyPrimitives`, `ClippingInvocations`, `ClippingPrimitives`, `FragmentShaderInvocations` |

`BufferBindingInfo.ContentVersion` comes from [`DeviceBuffer.ContentVersion`](../../Graphite/Core/DeviceBuffer/DeviceBuffer.cs). Comparing it between draws identifies unchanged data. It does not change when a compute shader writes the buffer.

Dividing `FragmentShaderInvocations` by the render target pixel count gives a rough overdraw figure.

## Memory budget

```csharp
MemoryBudgetInfo budget = device.GetMemoryBudget();
if (budget.IsSupported)
{
    double usedPercent = 100.0 * budget.UsageBytes / budget.BudgetBytes;
}
```

| Member | Type | Description |
|--------|------|-------------|
| `IsSupported` | `bool` | False when the backend or driver cannot report; other fields are 0 |
| `BudgetBytes` | `ulong` | Bytes the driver lets this process use, summed over device-local heaps |
| `UsageBytes` | `ulong` | Estimated current use, including other processes on the GPU |

This is the driver's view, not the sum of what your profiler saw.

## Warnings and missing properties

```csharp
device.OnWarning = message => logger.Warn(message);

device.OnMissingProperty = (shader, compute, name, kind, set, binding) =>
    logger.Warn($"Missing {kind} '{PropertyID.ToString(name)}' at set {set} binding {binding}");
```

| Handler | Fires when |
|---------|------------|
| `GraphicsDeviceWarningHandler(string message)` | a buffer is implicitly reallocated because it was rewritten while in flight, or the transient soft cap is crossed. Default writes to `Console.Error` |
| `MissingPropertyHandler(GraphicsProgram? shader, ComputeProgram? compute, PropertyID name, ResourceKind expectedKind, uint set, int bindingIndex)` | a draw or dispatch reflects a resource slot with no matching entry in the bound `PropertySet`s and a default is substituted. Null by default (silent) |

The missing-property handler is the quickest way to find a typo in a property name, since the shader still runs with a default resource.

## Debug markers and Vulkan validation

`CommandBuffer.PushDebugGroup(string)`, `PopDebugGroup()` and `InsertDebugMarker(string)` label regions for tools such as RenderDoc. Every push needs a pop. See [command buffers](command-buffers.md).

Setting `GraphicsDeviceOptions.VulkanValidationLayers = true` enables the Vulkan debug-report extension plus the standard or Khronos validation layer if the system has one installed. Errors are stored by the driver callback and rethrown as `RenderException` on the next submit or `WaitForIdle`, so the stack trace may point slightly after the offending call. Resource `Name` values appear in driver messages.

## Common patterns

- Development build: `VulkanValidationLayers = true`, validation on, an `OnWarning` that logs with a stack trace, and an `OnMissingProperty` that logs.
- Release build: `VulkanValidationLayers = false`, `GraphiteValidation = false`, `Profiler = null`. The checks reduce to a static bool read.
- In-game overlay: set `RequestGPUStatistics = true`, accumulate `RecordExecutionTime` per `CommandBufferInfo.Name`, and read the totals from the UI thread.
- Toggle a profiler at runtime with `device.SetProfiler(x)` between `DispatchGraph` calls.

## Pitfalls

- `GraphiteValidation` is process-wide. The last device you create decides the setting for all devices.
- GPU time arrives late. Key by `CommandBufferInfo.Id` or `Name`, not by "the current frame".
- The command buffer and framebuffers handed to `Capture` are invalid after it returns.
- `RequestCapture` and `RequestGPUStatistics` add real work while they return true.
- `SetProfiler` is not safe to call from another thread mid-frame.
- `VulkanValidationLayers = true` and `GraphiteValidation` are independent. One controls the Vulkan driver layers, the other Graphite's own checks.
- With validation off, nulls and bad arguments fail later and less clearly, and the transient hard cap is not enforced.

## See also

- [Validation and profiling internals](../internals/08-validation-and-profiling.md)
- [GraphicsDevice API](graphics-device.md) for `GraphicsDeviceOptions`, transient caps and execution
- [Getting started](getting-started.md)
- [Command buffers](command-buffers.md) for debug groups and `RecordMetadata`
- [Render graph](render-graph.md) for `RenderContext.RecordPassMetadata`
