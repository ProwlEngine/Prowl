# Command Buffers API

Reference for `CommandBuffer` (draw, dispatch, state) and `TransferCommandBuffer` (copies and uploads), plus `IVertexSource`.

- [Overview](#overview)
- [Quick example](#quick-example)
- [CommandBuffer lifecycle](#commandbuffer-lifecycle)
- [State](#state)
- [Draw and dispatch](#draw-and-dispatch)
- [Transfers on a command buffer](#transfers-on-a-command-buffer)
- [Debug markers](#debug-markers)
- [IVertexSource](#ivertexsource)
- [TransferCommandBuffer](#transfercommandbuffer)
- [Common patterns](#common-patterns)
- [Pitfalls](#pitfalls)
- [See also](#see-also)

## Overview

A `CommandBuffer` records GPU work: bind a shader, a framebuffer, vertex data and properties, then draw or dispatch. In the render graph you do not create or begin them. `RenderContext.GetCommandBuffer` hands you one that is already recording, and `RenderContext.SubmitCommandBuffer` queues it for the current execution. `TransferCommandBuffer` is a separate, smaller type for one-off uploads and readback outside the frame flow.

Both derive from `CommandBufferBase`, which carries the copy and update operations.

## Quick example

From [PBRRenderer](../../Samples/PBRRenderer/Program.cs#L67):

```csharp
CommandBuffer cmd = context.GetCommandBuffer(Name);
BindTarget(context, cmd);
cmd.SetShader(_shader);
cmd.SetVertexSource(_model.Mesh);
cmd.SetProperties(_properties);
cmd.DrawIndexed();
context.SubmitCommandBuffer(cmd);
```

## CommandBuffer lifecycle

| Step | Who | Notes |
|------|-----|-------|
| Rent and begin | `RenderContext.GetCommandBuffer(name)` | Begin resets all cached state: framebuffer, shaders, vertex source, merged properties |
| Record | You | Not thread-safe. |
| Submit | `RenderContext.SubmitCommandBuffer(cmd)` | Closes any open render pass and adds it to the execution. It stays open as the execution's tail so the next pass's barriers can be appended, and is ended when the next buffer or a transfer is submitted, or the execution completes |
| Recycle | Device | When the execution's ring slot is reused |

`Begin` and `End` on `CommandBuffer` are internal. A buffer that is rented and never submitted triggers a warning through `GraphicsDevice.OnWarning` after the pass and is dropped.

Each rented buffer is tagged with its execution and the current pass, which is how the profiler attributes draws to passes.

## State

All in `CommandBuffer.State`. Source: [CommandBuffer.State.cs](../../Graphite/Core/CommandBuffer/CommandBuffer.State.cs).

| Member | Signature | Description |
|--------|-----------|-------------|
| `SetShader` | `void SetShader(GraphicsProgram program)` | Sets the graphics program. No-op if it is already current. Must match the framebuffer's outputs. |
| `SetComputeShader` | `void SetComputeShader(ComputeProgram program)` | Sets the compute program |
| `SetVertexSource` | `void SetVertexSource(IVertexSource source)` | Replaces the vertex and index source. Must not be null; an empty source means no vertex data. |
| `SetProperties` | `void SetProperties(PropertySet properties)` | Merges the set into the bound properties. No-op when the set is unchanged since its last merge into this buffer and none of its names were overridden since. |
| `ClearProperties` | `void ClearProperties()` | Empties the merged properties. No GPU work. |
| `SetFramebuffer` | `void SetFramebuffer(Framebuffer fb)` | Sets the render target and resets viewport 0 and scissor 0 to full size |
| `SetFramebuffer` | `void SetFramebuffer(RenderTexture renderTexture)` | Uses the render texture's framebuffer |
| `ClearColorTarget` | `void ClearColorTarget(uint index, Color clearColor)` | Clears one color attachment. Framebuffer must be set. |
| `ClearDepthStencil` | `void ClearDepthStencil(float depth)` / `(float depth, byte stencil)` | Clears depth (stencil defaults to 0). Needs a depth attachment. |
| `SetViewport` | `void SetViewport(Viewport viewport)` / `(ref Viewport viewport)` | Sets viewport 0 |
| `SetViewport` | `void SetViewport(uint index, Viewport viewport)` / `(uint index, ref Viewport viewport)` | Sets one viewport. Index above 0 needs multi-viewport. |
| `SetFullViewport` | `void SetFullViewport()` / `(uint index)` | Viewport covers the framebuffer |
| `SetScissorRect` | `void SetScissorRect(uint x, uint y, uint width, uint height)` | Sets scissor rectangle 0 |
| `SetScissorRect` | `void SetScissorRect(uint index, uint x, uint y, uint width, uint height)` | Sets one scissor rectangle. Index above 0 needs multi-viewport. |
| `SetFullScissorRect` | `void SetFullScissorRect()` / `(uint index)` | Scissor covers the framebuffer |

`Viewport` is `new Viewport(x, y, width, height, minDepth, maxDepth)`.

`SetShader` and `SetFramebuffer` both skip work when passed the current value, so redundant calls are cheap.

```csharp
cmd.SetFramebuffer(target.Framebuffer);
cmd.ClearColorTarget(0, new Color(0, 0, 0, 1));
cmd.ClearDepthStencil(1f, 0);
cmd.SetViewport(new Viewport(0, 0, 640, 360, 0f, 1f));
cmd.SetScissorRect(0, 0, 640, 360);
```

## Draw and dispatch

Source: [CommandBuffer.Draw.cs](../../Graphite/Core/CommandBuffer/CommandBuffer.Draw.cs).

| Member | Signature | Description |
|--------|-----------|-------------|
| `Draw` | `void Draw(uint vertexCount)` | Non-indexed, one instance, from vertex 0 |
| `Draw` | `void Draw(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)` | Non-indexed with explicit ranges |
| `DrawIndexed` | `void DrawIndexed()` | Indexed draw of the whole bound index buffer, one instance |
| `DrawIndexed` | `void DrawIndexed(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)` | Indexed with explicit ranges; `vertexOffset` is added to every index read |
| `DrawIndirect` | `void DrawIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)` | Draws read from a buffer of `IndirectDrawArguments` |
| `DrawIndexedIndirect` | `void DrawIndexedIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)` | Reads `IndirectDrawIndexedArguments` |
| `Dispatch` | `void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)` | Compute dispatch with the compute program |
| `DispatchIndirect` | `void DispatchIndirect(DeviceBuffer indirectBuffer, uint offset)` | Reads `IndirectDispatchArguments` |

Indirect buffers need the `IndirectBuffer` usage flag, offsets must be multiples of 4, and strides must be multiples of 4 and large enough for the argument struct. Indirect draws require device support; with validation enabled an unsupported device throws.

Argument structs:

| Struct | Fields |
|--------|--------|
| `IndirectDrawArguments` | `VertexCount`, `InstanceCount`, `FirstVertex`, `FirstInstance` |
| `IndirectDrawIndexedArguments` | `IndexCount`, `InstanceCount`, `FirstIndex`, `VertexOffset`, `FirstInstance` |
| `IndirectDispatchArguments` | `GroupCountX`, `GroupCountY`, `GroupCountZ` |

A full-screen triangle with no vertex buffers is `cmd.Draw(3)` with `VertexSource.None`; see [IVertexSource](#ivertexsource).

A compute pass:

```csharp
CommandBuffer cmd = context.GetCommandBuffer("Cull");
cmd.SetComputeShader(_cullProgram);
cmd.SetProperties(_cullProperties);
cmd.Dispatch((count + 63) / 64, 1, 1);
context.SubmitCommandBuffer(cmd);
```

When validation is enabled (`GraphicsDevice.ValidationEnabled`), a draw throws `RenderException` unless a graphics program, a framebuffer and a vertex source are all bound, and indexed draws also need the source to return an index buffer. A draw with no vertex data still needs a source, use `VertexSource.None`; `null` is never allowed. Dispatch does not require a framebuffer; the backend ends any active render pass before dispatching.

## Transfers on a command buffer

These members are on `CommandBufferBase`, so they are available on both `CommandBuffer` and `TransferCommandBuffer`. Source: [CommandBufferBase.cs](../../Graphite/Core/CommandBuffer/CommandBufferBase.cs).

| Member | Signature | Description |
|--------|-----------|-------------|
| `UpdateBuffer` | `void UpdateBuffer<T>(DeviceBuffer buffer, uint bufferOffsetInBytes, in T source) where T : unmanaged` | Writes one value |
| `UpdateBuffer` | `void UpdateBuffer<T>(DeviceBuffer buffer, uint bufferOffsetInBytes, ReadOnlySpan<T> source) where T : unmanaged` | Writes a span; arrays convert implicitly |
| `UpdateBuffer` | `void UpdateBuffer(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)` | Writes from a pointer. Throws if it exceeds the buffer. Zero size is a no-op. |
| `CopyBuffer` | `void CopyBuffer(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes)` | GPU copy between buffers |
| `CopyTexture` | `void CopyTexture(Texture source, Texture destination)` | Copies every mip and layer |
| `CopyTexture` | `void CopyTexture(Texture source, Texture destination, uint mipLevel, uint arrayLayer)` | Copies one subresource |
| `CopyTexture` | `void CopyTexture(Texture source, uint srcX, uint srcY, uint srcZ, uint srcMipLevel, uint srcBaseArrayLayer, Texture destination, uint dstX, uint dstY, uint dstZ, uint dstMipLevel, uint dstBaseArrayLayer, uint width, uint height, uint depth, uint layerCount)` | Copies a region |
| `GenerateMipmaps` | `void GenerateMipmaps(Texture texture)` | Builds lower mips from mip 0. Texture needs `TextureUsage.GenerateMipmaps`. |
| `ResolveTexture` | `void ResolveTexture(Texture source, Texture destination)` | `CommandBuffer` only. Resolves a multisampled texture into a single-sample one. |

```csharp
cmd.UpdateBuffer(_constants, 0, in data);
cmd.CopyBuffer(staging, 0, vertexBuffer, 0, size);
cmd.CopyTexture(src, dst);
cmd.GenerateMipmaps(texture);
```

## Debug markers

| Member | Signature | Description |
|--------|-----------|-------------|
| `PushDebugGroup` | `void PushDebugGroup(string name)` | Opens a named group in capture tools. Nestable. |
| `PopDebugGroup` | `void PopDebugGroup()` | Closes the innermost group. Every push needs a pop. |
| `InsertDebugMarker` | `void InsertDebugMarker(string name)` | Single labeled point |

Profiling helpers on the same type: `WantsMetadata` and `RecordMetadata(object)` attach caller data to the current draw when a profiler asks for it. See [diagnostics.md](diagnostics.md).

## IVertexSource

Supplies vertex buffers, an optional index buffer and the topology. You implement it on your mesh type (the sample `Mesh` does). Source: [IVertexSource.cs](../../Graphite/Core/CommandBuffer/IVertexSource.cs).

| Member | Signature | Description |
|--------|-----------|-------------|
| `Topology` | `PrimitiveTopology Topology { get; }` | Topology for the next draw; queried every draw |
| `ResolveSlot` | `void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)` | Returns the buffer and byte offset for one of the shader's vertex layout slots |
| `TryGetIndexBuffer` | `bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount)` | Returns the index buffer; false means none. Only called on indexed paths. |

`VertexBinding` is `new VertexBinding(DeviceBuffer buffer, uint offset)`. The buffer must never be null and must have vertex buffer usage; stride lives in the program's layout, not the binding. `layout` is passed in full so implementations can dispatch on the semantic of what the slot holds rather than the slot index.

`Topology` is read on every draw. `TryGetIndexBuffer` is called on every indexed draw. `ResolveSlot` is called when the bound source or program differs from the previous draw on the same command buffer; otherwise the resolved vertex buffers are reused. The comparison is by reference, so keep one long-lived instance per mesh.

Implement `IVertexSource` on a class. `SetVertexSource` takes the interface, so a struct is boxed into a new object on every call, which allocates and also defeats the vertex buffer reuse above.

Most code does not need to implement it. `VertexSource` is a ready-made class: `new VertexSource(topology).SetBuffer("POSITION0", buffer).SetIndexBuffer(indices, IndexFormat.UInt16, count)` matches buffers to layout slots by the first element name. `VertexSource.None` is the shared source for shaders that generate vertices from `SV_VertexID` and declare no vertex layout slots:

```csharp
cmd.SetVertexSource(VertexSource.None);
cmd.Draw(3);
```

## TransferCommandBuffer

For uploads and readback that do not belong to a pass. It has no draw, dispatch, framebuffer or property API. Source: [TransferCommandBuffer.cs](../../Graphite/Core/CommandBuffer/TransferCommandBuffer.cs#L18).

| Member | Signature | Description |
|--------|-----------|-------------|
| `Device` | `GraphicsDevice Device { get; }` | Owning device |
| `Begin` | `void Begin()` | Resets and starts recording. Valid on a fresh buffer or after `End`/submit. |
| `End` | `void End()` | Finishes recording |
| `UpdateTexture` | `void UpdateTexture<T>(Texture texture, ReadOnlySpan<T> source, uint x, uint y, uint z, uint width, uint height, uint depth, uint mipLevel, uint arrayLayer) where T : unmanaged` | Updates a texture region |
| `UpdateTexture` | `void UpdateTexture(Texture texture, IntPtr source, uint sizeInBytes, uint x, uint y, uint z, uint width, uint height, uint depth, uint mipLevel, uint arrayLayer)` | Same from a pointer |
| inherited | `UpdateBuffer`, `CopyBuffer`, `CopyTexture`, `GenerateMipmaps` | From `CommandBufferBase` |

Create one with `ResourceFactory.CreateTransferCommandBuffer()` and submit with `GraphicsDevice.SubmitAndWait(cmd)`, which blocks until the GPU finishes. The buffer is reusable across Begin/End/submit cycles. Inside a graph pass, `context.GetTransferCommandBuffer` and `context.SubmitTransferCommandBuffer` apply; that submit is non-blocking and first flushes everything recorded so far.

```csharp
TransferCommandBuffer upload = device.ResourceFactory.CreateTransferCommandBuffer();
upload.Begin();
upload.UpdateTexture(texture, pixels.AsSpan(), 0, 0, 0, width, height, 1, 0, 0);
upload.GenerateMipmaps(texture);
upload.End();
device.SubmitAndWait(upload);
upload.Dispose();
```

`GenerateMipmaps` requires the texture to have been created with `TextureUsage.GenerateMipmaps`.

## Common patterns

### Many draws sharing state

Set the shader once, then vary properties and vertex source. Redundant state calls are filtered:

```csharp
cmd.SetFramebuffer(target);
cmd.SetShader(_shader);
foreach (DrawItem item in items)
{
    cmd.SetProperties(item.Properties);
    cmd.SetVertexSource(item.Mesh);
    cmd.DrawIndexed();
}
```

### Multiple command buffers per pass

A pass may rent, record and submit more than one buffer. Submission order is recording order within the execution.

### Early out without renting

Preconditions are checked before `GetCommandBuffer`, as a pass should before using an optional resource, because renting without submitting is a warning.

## Pitfalls

- The context owns `Begin` and `End` of a rented `CommandBuffer`.
- `CommandBuffer` is not thread-safe, and neither is a `TransferCommandBuffer`.
- Begin clears the framebuffer, shader, vertex source and merged properties. Every rented buffer starts with nothing bound.
- `SetFramebuffer` resets viewports and scissors, so custom ones follow it.
- `ClearColorTarget` and `ClearDepthStencil` require a framebuffer to be set first, and `ClearDepthStencil` requires a depth attachment.
- `SetVertexSource` replaces the previous source completely.
- An `IVertexSource` struct is boxed on every `SetVertexSource` call. Implement it on a class and reuse the instance.
- `Begin` clears properties, so each rented buffer starts empty.
- `GraphicsDevice.SubmitAndWait` blocks the CPU until the GPU finishes.
- Indirect argument buffers must carry the `IndirectBuffer` usage flag.
- `RecordMetadata` is meaningful only when `WantsMetadata` is true.

## See also

- [internals/03-render-graph.md](../internals/03-render-graph.md) - how buffers are rented and submitted by `RenderContext`
- [internals/04-resource-binding.md](../internals/04-resource-binding.md) - what `SetProperties` does at draw time
- [render-graph.md](render-graph.md) - `RenderContext` API
- [property-sets.md](property-sets.md)
- [shader-programs.md](shader-programs.md) - `GraphicsProgram` and `ComputeProgram`
- [buffers-and-textures.md](buffers-and-textures.md) - `DeviceBuffer`, `Texture`, `Framebuffer`
