# Buffers and Textures API

GPU memory resources: `DeviceBuffer`, `Texture`, `TextureView`, `Sampler`, `RenderTexture`, `Framebuffer`, `PixelFormat`, and how to get data in and out of them.

- [Overview](#overview)
- [Quick example](#quick-example)
- [DeviceBuffer](#devicebuffer)
- [Texture](#texture)
- [TextureView](#textureview)
- [Sampler](#sampler)
- [RenderTexture](#rendertexture)
- [Framebuffer](#framebuffer)
- [PixelFormat](#pixelformat)
- [Mapping and uploads](#mapping-and-uploads)
- [Common patterns](#common-patterns)
- [Pitfalls](#pitfalls)
- [See also](#see-also)

## Overview

Every resource is created through [`device.ResourceFactory`](../../Graphite/Core/ResourceFactory.cs#L6) from a description struct, and (except `RenderTexture`) derives from `GraphicsResource`, which gives you a debug `Name`, `IsDisposed`, and an idempotent `Dispose()`. Descriptions are plain structs with public fields, so you can build them with object initializers or the helper constructors and static factories.

`BindableResource` means it can be set on a [`PropertySet`](property-sets.md). `MappableResource` means `device.Map` accepts it.

## Quick example

Load pixels into a sampled texture and bind it, the way the [ImageLoader sample](../../Samples/Shared/ImageLoader.cs#L14) does:

```csharp
TextureDescription desc = TextureDescription.Texture2D(
    width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled);

Texture texture = device.ResourceFactory.CreateTexture(desc);
device.UpdateTexture<byte>(texture, rgbaBytes, 0, 0, 0, width, height, 1, 0, 0);

Sampler sampler = device.ResourceFactory.CreateSampler(SamplerDescription.Linear);

PropertySet properties = new();
properties.SetTexture("MainTexture", texture, sampler);
```

`rgbaBytes` is a `byte[]` of `width * height * 4` bytes.

## DeviceBuffer

A fixed-size block of GPU memory. It cannot be resized.

| Member | Signature | Description |
|--------|-----------|-------------|
| `SizeInBytes` | `uint SizeInBytes { get; }` | Size fixed at creation |
| `Usage` | `BufferUsage Usage { get; }` | Allowed uses |
| `ContentVersion` | `uint ContentVersion { get; }` | Increments on each CPU write or GPU copy into the buffer. Not updated by compute shader writes |
| `Name`, `IsDisposed`, `Dispose()` | from `GraphicsResource` | |

Creation: `ResourceFactory.CreateBuffer(BufferDescription)` (or `ref`).

`BufferDescription` fields:

| Field | Type | Description |
|-------|------|-------------|
| `SizeInBytes` | `uint` | Total size. Must be a multiple of 16 for `UniformBuffer` (validation) |
| `Usage` | `BufferUsage` | Flags below |
| `StructureByteStride` | `uint` | Element size for structured buffers, otherwise 0 |
| `UseTypedHlslBinding` | `bool` | Structured buffers only: typed instead of raw byte-address binding |
| `TransientWrites` | `bool` | Skip write-hazard tracking. Cheap per-frame updates; requires that the GPU is not reading the same bytes |

Constructors: `(uint sizeInBytes, BufferUsage usage)`, `(..., uint structureByteStride)`, `(..., uint structureByteStride, bool useTypedHlslBinding)`.

`BufferUsage` flags:

| Flag | Meaning |
|------|---------|
| `VertexBuffer`, `IndexBuffer` | Geometry |
| `UniformBuffer` | Constant data |
| `StructuredBufferReadOnly`, `StructuredBufferReadWrite` | Storage buffers. Needs `Features.StructuredBuffer`, a non-zero `StructureByteStride`, and cannot be combined with `UniformBuffer` |
| `IndirectBuffer` | Indirect draw and dispatch arguments |
| `Dynamic` | CPU-writable through `Map` |
| `Staging` | CPU-readable and writable. Must be the only flag |

`DeviceBufferRange` (`Buffer`, `Offset`, `SizeInBytes`, `IsFullRange`) is a bindable slice of a buffer, used with `PropertySet.SetBuffer` when a shader should see only part of one.

```csharp
BufferDescription desc = new((uint)(vertices.Length * sizeof(float)), BufferUsage.VertexBuffer);
DeviceBuffer vertexBuffer = device.ResourceFactory.CreateBuffer(desc);
device.UpdateBuffer<float>(vertexBuffer, 0, vertices);
```

### In-flight writes

If you write to a buffer that the GPU is still using (through `UpdateBuffer`, or `Map` with `Write` or `ReadWrite`), Graphite allocates fresh backing memory for it behind the scenes (called orphaning, [DeviceBuffer.EnsureWritable](../../Graphite/Core/DeviceBuffer/DeviceBuffer.cs#L63)) so the GPU still sees the old data. If the same buffer is orphaned again within 10 executions, `OnWarning` fires. Per-frame rewrites belong in a transient buffer or a per-execution buffer from the render graph.

## Texture

Image data of 1D, 2D or 3D type, with optional mips, array layers, cubemap layout and multisampling.

| Member | Signature | Description |
|--------|-----------|-------------|
| `Format` | `PixelFormat` | Texel format |
| `Width`, `Height`, `Depth` | `uint` | Size in texels |
| `MipLevels`, `ArrayLayers` | `uint` | Subresource counts |
| `Usage` | `TextureUsage` | Creation flags |
| `Type` | `TextureType` | `Texture2D`, `Texture1D`, `Texture3D` |
| `SampleCount` | `TextureSampleCount` | `Count1` through `Count32` |
| `CalculateSubresource` | `uint CalculateSubresource(uint mipLevel, uint arrayLayer)` | Index for `Map`: `arrayLayer * MipLevels + mipLevel` |

Creation: `ResourceFactory.CreateTexture(TextureDescription)`. There is also `CreateTexture(ulong nativeTexture, ref TextureDescription)` to wrap an existing native Vulkan image.

`TextureDescription` helpers:

| Factory | Signature |
|---------|-----------|
| 1D | `Texture1D(uint width, uint mipLevels, uint arrayLayers, PixelFormat format, TextureUsage usage)` |
| 2D | `Texture2D(uint width, uint height, uint mipLevels, uint arrayLayers, PixelFormat format, TextureUsage usage)` and an overload with `TextureSampleCount` |
| 3D | `Texture3D(uint width, uint height, uint depth, uint mipLevels, PixelFormat format, TextureUsage usage)` |
| Full | `new TextureDescription(width, height, depth, mipLevels, arrayLayers, format, usage, type[, sampleCount])` |

`TextureUsage` flags:

| Flag | Meaning |
|------|---------|
| `Sampled` | Read in shaders through a view |
| `Storage` | Read and write in shaders |
| `RenderTarget` | Color attachment of a framebuffer |
| `DepthStencil` | Depth attachment of a framebuffer |
| `Cubemap` | 2D cubemap (6 layers per cube) |
| `Staging` | CPU-mappable transfer texture. Must be the only flag |
| `GenerateMipmaps` | Allows `GenerateMipmaps` on a command buffer. Not with `DepthStencil` |

A texture needs `Sampled` or `Storage` for a view to be created over it (validation rejects anything else). A texture must carry the flags for every way it is used.

```csharp
TextureDescription desc = TextureDescription.Texture2D(
    1024, 1024, 11, 1, PixelFormat.R8_G8_B8_A8_UNorm_SRgb,
    TextureUsage.Sampled | TextureUsage.GenerateMipmaps);
Texture albedo = device.ResourceFactory.CreateTexture(desc);
```

`GetPixelFormatSupport` reports whether a combination is valid:

```csharp
bool ok = device.GetPixelFormatSupport(
    PixelFormat.R16_G16_B16_A16_Float, TextureType.Texture2D,
    TextureUsage.RenderTarget | TextureUsage.Sampled);
```

## TextureView

A window onto a texture: a mip range, a layer range, and optionally a reinterpreted format. The texture itself is bindable in a `PropertySet` and uses a full view automatically (created lazily and cached on the texture), so you only need an explicit view to expose a subset.

| Member | Signature | Description |
|--------|-----------|-------------|
| `Target` | `Texture` | Viewed texture |
| `BaseMipLevel`, `MipLevels` | `uint` | Visible mip range |
| `BaseArrayLayer`, `ArrayLayers` | `uint` | Visible layer range |
| `Format` | `PixelFormat` | View format (defaults to the texture's) |

Creation:

- `ResourceFactory.CreateTextureView(Texture target)` for a full view.
- `ResourceFactory.CreateTextureView(TextureViewDescription)`, where `new TextureViewDescription(Texture target, uint? baseMipLevel = null, uint? mipLevels = null, uint? baseArrayLayer = null, uint? arrayLayers = null, PixelFormat? format = null)` defaults unset fields from the target.

```csharp
TextureView mip2 = device.ResourceFactory.CreateTextureView(
    new TextureViewDescription(albedo, baseMipLevel: 2, mipLevels: 1));
```

Partial views need `Features.SubsetTextureView`. The format override must be view-compatible with the real format (same size class).

## Sampler

How a shader filters and addresses a texture. `Sampler` has no members beyond `GraphicsResource`.

`SamplerDescription` fields:

| Field | Type | Description |
|-------|------|-------------|
| `AddressModeU`, `AddressModeV`, `AddressModeW` | `SamplerAddressMode` | `Wrap`, `Mirror`, `Clamp`, `Border` |
| `Filter` | `SamplerFilter` | `MinPoint_MagPoint_MipPoint` ... `MinLinear_MagLinear_MipLinear`, or `Anisotropic` |
| `ComparisonKind` | `ComparisonKind?` | Set for shadow (comparison) samplers |
| `MaximumAnisotropy` | `uint` | Used with `Anisotropic`. Needs `Features.SamplerAnisotropy` |
| `MinimumLod`, `MaximumLod` | `uint` | LOD clamp |
| `LodBias` | `int` | Needs `Features.SamplerLodBias` when non-zero |
| `BorderColor` | `SamplerBorderColor` | `TransparentBlack`, `OpaqueBlack`, `OpaqueWhite` |

Presets: `SamplerDescription.Point`, `SamplerDescription.Linear` and `SamplerDescription.Aniso4x`, all with `Wrap` addressing. The device also owns ready-made `PointSampler`, `LinearSampler` and (if supported) `Aniso4xSampler`; they are never disposed by callers.

```csharp
SamplerDescription clamped = SamplerDescription.Linear;
clamped.AddressModeU = SamplerAddressMode.Clamp;
clamped.AddressModeV = SamplerAddressMode.Clamp;
Sampler sampler = device.ResourceFactory.CreateSampler(clamped);
```

## RenderTexture

A bundle of color textures, an optional depth texture and the framebuffer that renders into them. It is a `sealed class` with `IDisposable`, not a `GraphicsResource`.

| Member | Signature | Description |
|--------|-----------|-------------|
| `Desc` | `RenderTextureDescription` | The description it was made from |
| `ColorTextures` | `Texture[]` | One per color format. Usage `RenderTarget | Sampled`, plus `Storage` when `Desc.Storage` is set |
| `DepthTexture` | `Texture?` | Usage `DepthStencil | Sampled`, or null |
| `Framebuffer` | `Framebuffer` | Framebuffer over these attachments |
| `Name` | `string { set; }` | Names the framebuffer and each attachment |
| `ResolveDepthFormat` | `static PixelFormat ResolveDepthFormat(GraphicsDevice device)` | `D24_UNorm_S8_UInt` if usable as sampled depth, else `D32_Float_S8_UInt`. Cached process-wide |
| `Dispose` | `void Dispose()` | Disposes framebuffer and textures |

`RenderTextureDescription` is a readonly struct: `Width`, `Height`, `ColorFormats` (empty for depth-only), `Depth`, `SampleCount`, `Storage`. Constructors take either a `PixelFormat[]` or a single `PixelFormat`, plus `bool depth`, an optional sample count and an optional `storage` flag. Equal descriptions compare equal, which is what the transient pool keys on. The render graph sets `Storage` for a graph texture that some pass declares with `TextureUsageKind.Storage`.

Two ways to get one:

| Way | Ownership |
|-----|-----------|
| `device.ResourceFactory.CreateRenderTexture(in RenderTextureDescription)` | Owned and disposed by the caller |
| `device.RentTransientRenderTexture(task, in desc)` | Pooled. Valid for that execution and recycled after the GPU finishes it. Never disposed by the caller |

A render texture needs at least one color format or a depth attachment; otherwise it throws `RenderException`.

```csharp
RenderTextureDescription desc = new(1280, 720, PixelFormat.R16_G16_B16_A16_Float, depth: true);
RenderTexture scene = device.ResourceFactory.CreateRenderTexture(desc);
scene.Name = "Scene";

cmd.SetFramebuffer(scene);
properties.SetTexture("SceneColor", scene, device.LinearSampler);
```

`CommandBuffer.SetFramebuffer` has a `RenderTexture` overload, and `PropertySet.SetTexture` accepts one (binding its first color texture). In a render graph you normally do not create these yourself; see [Render graph](render-graph.md).

## Framebuffer

A set of render targets (up to one depth plus any number of colors) that commands draw into. Derives from `GraphicsResource`.

| Member | Signature | Description |
|--------|-----------|-------------|
| `DepthTarget` | `FramebufferAttachment?` | Depth attachment, if any |
| `ColorTargets` | `IReadOnlyList<FramebufferAttachment>` | Color attachments |
| `OutputDescription` | `OutputDescription` | Formats and sample count; used to pick the right pipeline variant |
| `Width`, `Height` | `uint` | Dimensions of the first target at its mip level |

`FramebufferAttachment` has `Target` (`Texture`), `ArrayLayer`, `MipLevel`.

Creation: `ResourceFactory.CreateFramebuffer(FramebufferDescription)`. `new FramebufferDescription(Texture? depthTarget, params Texture[] colorTargets)` attaches mip 0 layer 0 of each. For a specific layer or mip, use `new FramebufferDescription(FramebufferAttachmentDescription? depth, FramebufferAttachmentDescription[] colors)` with `new FramebufferAttachmentDescription(Texture target, uint arrayLayer, uint mipLevel)`.

```csharp
FramebufferDescription fbDesc = new(depthTexture, colorTexture);
Framebuffer framebuffer = device.ResourceFactory.CreateFramebuffer(fbDesc);
```

Color targets need `TextureUsage.RenderTarget`, the depth target needs `TextureUsage.DepthStencil`, and a layer or mip past the texture throws at construction (validation). The swapchain's framebuffer comes from `device.SwapchainFramebuffer` and is owned by the swapchain.

## PixelFormat

[`PixelFormat`](../../Graphite/Core/PixelFormat.cs#L6) names encode channel order and width, then the numeric type:

| Suffix | Meaning |
|--------|---------|
| `UNorm` / `SNorm` | Unsigned or signed normalized (0..1 or -1..1) |
| `UInt` / `SInt` | Integer |
| `Float` | Floating point |
| `SRgb` | sRGB-encoded color (hardware converts on sample and write) |

Families: 8, 16 and 32-bit per channel `R`, `R_G`, `R_G_B_A` formats; `B8_G8_R8_A8` (the usual swapchain order); packed `R10_G10_B10_A2` and `R11_G11_B10_Float`; depth `D24_UNorm_S8_UInt` and `D32_Float_S8_UInt`; and block-compressed `BC1` through `BC7` (plus sRGB variants) and `ETC2`.

Helpers:

| Helper | Signature | Description |
|--------|-----------|-------------|
| `GetSizeInBytes` | `uint GetSizeInBytes(this PixelFormat)` | Bytes per texel. Invalid on compressed formats |
| `GetSizeInBytes` | `uint GetSizeInBytes(this VertexElementFormat)` | Bytes per vertex element |
| `device.GetPixelFormatSupport` | see [Texture](#texture) | Support and limits for a combination |
| `device.GetSampleCountLimit` | `TextureSampleCount GetSampleCountLimit(PixelFormat, bool depthFormat)` | Max MSAA |

`_SRgb` formats suit color data authored in sRGB (albedo). Normal maps, masks and HDR buffers use linear formats.

## Mapping and uploads

There are three ways to put data into a resource and one to read it back.

| Method | Use when | Notes |
|--------|----------|-------|
| `device.UpdateBuffer` / `UpdateTexture` | Initial data or occasional updates | Immediate, simplest. Profiled as `BufferOpBin.Update` |
| `device.Map` on a `Dynamic` buffer | Frequent CPU writes | Map with `MapMode.Write`, write, `Unmap` |
| `TransferCommandBuffer` + `SubmitAndWait` | Upload or readback with explicit copies | Also the way to read GPU data back |
| `device.Map` on a `Staging` resource | CPU read or write of a staging copy | The only way to read |

### Map rules

[`device.Map(resource, mode, subresource)`](../../Graphite/Core/GraphicsDevice/GraphicsDevice.cs#L170) returns a `MappedResource`.

| Resource | Requirement |
|----------|-------------|
| `DeviceBuffer` | `Dynamic` or `Staging` usage. `subresource` must be 0. `MapMode.Read` and `ReadWrite` require `Staging` |
| `Texture` | `Staging` usage, and `subresource < ArrayLayers * MipLevels` |

`MappedResource` fields: `Resource`, `Mode`, `Data` (`IntPtr`), `SizeInBytes`, `Subresource`, `RowPitch`, `DepthPitch`. `RowPitch` and `DepthPitch` matter for textures, since rows may be padded.

`device.Map<T>(resource, mode, subresource)` (`T : unmanaged`) returns a `MappedResourceView<T>` with `Count` and indexers: `view[i]`, `view[x, y]` (uses `RowPitch`), `view[x, y, z]` (uses `DepthPitch`). Each indexer returns `ref T`.

Mapping with `Write` or `ReadWrite` on a `DeviceBuffer` first triggers the in-flight check and possible orphaning described [above](#in-flight-writes). Every `Map` pairs with an `Unmap`.

### Writing a dynamic buffer

```csharp
BufferDescription desc = new(256, BufferUsage.UniformBuffer | BufferUsage.Dynamic);
DeviceBuffer uniforms = device.ResourceFactory.CreateBuffer(desc);

MappedResourceView<float> view = device.Map<float>(uniforms, MapMode.Write);
view[0] = 1.0f;
view[1] = 0.5f;
device.Unmap(uniforms);
```

### Reading back a texture

```csharp
TextureDescription stagingDesc = TextureDescription.Texture2D(
    target.Width, target.Height, 1, 1, target.Format, TextureUsage.Staging);
Texture staging = device.ResourceFactory.CreateTexture(stagingDesc);

TransferCommandBuffer transfer = device.ResourceFactory.CreateTransferCommandBuffer();
transfer.Begin();
transfer.CopyTexture(target, staging);
transfer.End();
device.SubmitAndWait(transfer);

MappedResourceView<byte> pixels = device.Map<byte>(staging, MapMode.Read);
byte first = pixels[0];
device.Unmap(staging);

staging.Dispose();
transfer.Dispose();
```

### Uploading a texture region

`UpdateTexture(Texture texture, IntPtr source, uint sizeInBytes, uint x, uint y, uint z, uint width, uint height, uint depth, uint mipLevel, uint arrayLayer)` has a `ReadOnlySpan<T>` overload that computes `sizeInBytes`. `sizeInBytes` must equal the region size exactly, and for block-compressed formats the region must be aligned to the block size.

## Common patterns

- Static mesh data: `VertexBuffer` or `IndexBuffer` usage, filled once with `UpdateBuffer`.
- Per-frame constants: `PropertySet.SetFloat*` / `SetMatrix` take per-execution transient memory, and a rented transient buffer is the alternative. A buffer rewritten every frame triggers the orphan warning.
- Compute output read by the CPU: write to a `StructuredBufferReadWrite` buffer, `CopyBuffer` to a `Staging` buffer on a `TransferCommandBuffer`, `SubmitAndWait`, then `Map` for read.
- Mipmaps: create with `Sampled | GenerateMipmaps` and the full mip count, upload mip 0, then call `GenerateMipmaps(texture)` on a command buffer.
- `resource.Name` shows in RenderDoc and in Vulkan validation messages.

## Pitfalls

- `Staging` cannot be combined with other usage flags on either buffers or textures, and unstaged resources cannot be mapped.
- Uniform buffer sizes must be a multiple of 16 bytes.
- Forgetting `TextureUsage.Sampled` (or `RenderTarget`, `DepthStencil`) fails at creation or at view creation, not at draw time.
- `Map` for `Read` on a GPU-written resource shows stale data until a `SubmitAndWait` (or a completed execution) has finished the copy into the staging resource.
- Re-writing the same buffer each frame triggers the orphan warning.
- `DeviceBuffer.ContentVersion` does not see compute writes.
- `PixelFormat.GetSizeInBytes` on a compressed format is invalid (it asserts in debug builds). Block-compressed region sizes derive from the block layout; `UpdateTexture` validates the size and alignment.
- Rented `RenderTexture`s, the swapchain framebuffer, and the device's default samplers and null resources are never disposed by the caller.
- A texture is disposed only after the GPU is done with it; `device.WaitForIdle()` and `device.DisposeWhenIdle(resource)` provide that.

## See also

- [GraphicsDevice API](graphics-device.md) for `Map`, `UpdateBuffer`, transient rentals and feature queries
- [Command buffers](command-buffers.md) for `SetFramebuffer`, copies and `GenerateMipmaps`
- [Property sets](property-sets.md) for binding textures, samplers and buffers
- [Render graph](render-graph.md) for graph-managed textures and history
- [Device and execution internals](../internals/02-device-and-execution.md)
- [Validation and profiling internals](../internals/08-validation-and-profiling.md) for what the creation and map checks reject
