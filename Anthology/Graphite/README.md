# Prowl.Graphite

A cross-platform, low-level graphics and compute abstraction for .NET, with backends for Vulkan and Direct3D 11. Graphite powers the rendering layer of the Prowl Game Engine and can be used to build high-performance 2D and 3D games, simulations, tools, and other graphical applications.

Graphite started life as a modified and butchered version of NeoVeldrid, and by extension Veldrid, but has diverged far enough in its setup and API surface that it is now considered a separate library rather than a fork. See [API Differences](#api-differences) for the systems that intentionally break from upstream Veldrid.

## Features

- A single, unified API over Vulkan and Direct3D 11.
- macOS support via MoltenVK (Vulkan-over-Metal translation).
- A monolithic `ShaderProgram` model that bundles shader and pipeline state, with per-backend shader compilation handled internally.
- A string/id-driven `PropertySet` resource binding system that hides per-backend binding rules (Vulkan sets/bindings, D3D registers).
- A built-in frames-in-flight ring with per-frame transient (bump-allocated) GPU memory.
- Opt-in, zero-cost-when-disabled validation and profiling layers, toggled at compile time.
- Per-backend build trimming, so unused backends can be excluded entirely from the build.

## Requirements

- .NET 10 (`net10.0`).
- A GPU and driver supporting one of the target backends.
- [Silk.NET](https://github.com/dotnet/Silk.NET) 2.23.0 (pulled in transitively; provides the native bindings).
- [Prowl.Vector](https://www.nuget.org/packages/Prowl.Vector) 2.1.0 for vector and matrix math.

## Quick Start

Create a `GraphicsDevice`, record commands into a `CommandBuffer`, and present inside a frame:

```cs
GraphicsDeviceOptions options = new()
{
    Debug = false,
    SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt,
    SyncToVerticalBlank = false,
    PreferStandardClipSpaceYDirection = true
};

// Backend-specific factories: GraphicsDevice.CreateVulkan / CreateD3D11.
GraphicsDevice device = GraphicsDevice.CreateVulkan(options, swapchainDescription, vulkanOptions);

GraphicsProgram shader = /* load + create a ShaderProgram */;
Mesh triangle = /* create vertex/index buffers */;
CommandBuffer buffer = device.ResourceFactory.CreateCommandBuffer();

// Per-frame render loop:
Frame frame = device.BeginFrame();

buffer.Begin();
buffer.SetFramebuffer(device.SwapchainFramebuffer);
buffer.ClearDepthStencil(1, 0);
buffer.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
buffer.SetShader(shader);
buffer.SetVertexSource(triangle);
buffer.DrawIndexed();
buffer.End();

frame.SubmitCommands(buffer);
device.EndFrame(frame);
device.SwapBuffers();
```

The [`Samples/`](Samples) directory contains complete, runnable versions of this loop (window creation, shader loading, and mesh setup included).

## Backends

| Backend       | Windows | Linux | macOS |
|---------------|:-------:|:-----:|:-----:|
| Direct3D 11   | Yes     | -     | -     |
| Vulkan        | Yes     | Yes   | Yes (via MoltenVK) |

A device is created through the backend-specific factory methods on `GraphicsDevice`
(`CreateVulkan`, `CreateD3D11`). The `GraphicsBackend` enum enumerates the
available backends.

## Building

The solution targets `net10.0`. Build everything with:

```sh
dotnet build Prowl.Graphite.slnx
```

### Build configuration flags

Several MSBuild properties control which optional systems are compiled in. They can be set on the
command line (`-p:Flag=true`) or in `Directory.Build.props`.

| Property            | Default | Effect                                                        |
|---------------------|---------|---------------------------------------------------------------|
| `DisableValidation` | `false` | When unset, defines `VALIDATE_USAGE` and compiles in the validation layers. |
| `DisableProfiling`  | `false` | When unset, defines `PROFILE_USAGE` and compiles in the profiling layers.   |
| `ExcludeVulkan`     | `false` | Excludes the Vulkan backend (and its Silk.NET packages) from the build.     |
| `ExcludeD3D11`      | `false` | Excludes the Direct3D 11 backend from the build.              |

Backend exclusion is also surfaced as `ExcludeVulkan` / `ExcludeD3D11`
properties in the root `Directory.Build.props`, and each maps to a corresponding
`EXCLUDE_*_BACKEND` compiler symbol.

## Validation and Profiling Layers

Graphite ships two optional, compile-time-gated layers that mirror the core source tree:

- **Validation** (`VALIDATE_USAGE`, on by default): extra argument and state checks that throw
  descriptive exceptions on misuse. Validation lives under `Graphite/ValidationLayers`, mirroring
  the structure of `Graphite/Core` and `Graphite/Platform`.
- **Profiling** (`PROFILE_USAGE`, on by default): allocation and command counters collected by the
  `GraphicsDevice`. Profiling lives under `Graphite/Profiling`, mirroring the same structure.

Both layers are written so that every method carries a `[Conditional]` attribute, meaning the
compiler strips the bodies *and* the call sites entirely when the corresponding symbol is not
defined. Disable them (`-p:DisableValidation=true` / `-p:DisableProfiling=true`) for release builds
to remove all overhead.

## API Differences

These are the systems that intentionally diverge from upstream Veldrid/NeoVeldrid.

### Pipeline API

The previous Pipeline API has been gutted in favor of a monolithic `ShaderProgram` object, which
encapsulates pipeline data slightly differently. The concrete types are `GraphicsProgram` and
`ComputeProgram`, both deriving from the abstract `ShaderProgram`.

Conceptually, `ShaderProgram` and `Pipeline` are very similar in behavior, but there are a few key
differences:

`ShaderProgram` compiles per-platform shaders itself. This tradeoff was chosen because of how the
library is used in Prowl: `Shader` objects cannot be compiled separately from `Pipeline` objects,
or reused. Prowl's shader markdown syntax directly couples pipeline state with shader state, and
Prowl's only extra axis is differing compiled Variants, which need to be compiled regardless.
Decoupled shaders and pipeline states *did not benefit Prowl in any way*, so they were removed.

`PrimitiveTopology` and `OutputDescription` have been divorced from pipelines/shader programs in
favor of simplicity. In most renderers, pipelines are already cached and indexed by their output
description. The Vulkan backend is the only one that benefits from bundling the output description
with the pipeline; there, the `OutputDescription` is saved on the command buffer and used to index
an internal Vulkan pipeline cache that keys cached pipelines on the combination of `ShaderProgram`,
`PrimitiveTopology`, and `OutputDescription`. When a `ShaderProgram` is disposed, its internally
cached pipelines are disposed alongside it.

### Command Buffers

`CommandList` has been renamed to `CommandBuffer`, shamelessly mirroring Unity's API to reduce
friction when porting over.

`CommandBuffer.SetPipeline` has been replaced with `CommandBuffer.SetShader` - conceptually the
same call.

### IVertexSource

`CommandBuffer.SetVertexBuffer`/`SetIndexBuffer` has been replaced with
`CommandBuffer.SetVertexSource`. A new `IVertexSource` interface provides a resolver architecture
where a bound shader program requests buffers at a given location.

The API is designed to strike a balance between Unity's mesh-style binding system and a flexible
binding API for lower-level users:

```cs
public interface IVertexSource
{
    // Provides the draw topology this source wants. Reasoning: topology is coupled with vertex data,
    // as it directly influences index counts.
    PrimitiveTopology Topology { get; }

    // Resolves a device buffer slot. layoutSlot is the index in the created shader's vertex inputs.
    // layout is the source layout description used by the shader, for binding vertex data by name.
    // VertexBinding is a union of the resolved DeviceBuffer and the offset in the buffer to use.
    void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding);

    // Resolves an index buffer slot. Returns false if no index buffer is available.
    // Provides format and offset data.
    bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint offset);
}
```

### New resource binding API

To replace the resource binder, a new `PropertySet` API has been created. It acts as a merged
property builder that maps user-facing strings/ids to their cross-platform binding equivalent.
Creating a shader requires more reflection information up front, but the tradeoff is that
user-facing code never has to reason about complicated binding rules across platforms, such as the
differences between D3D registers and Vulkan sets/bindings.

```cs
// PropertyID is a lightweight wrapper over an interned string->int for fast dictionary indexing.
PropertyID internedId = "MainTexture";

PropertySet propertySet = new();

// SetTexture accepts a paired sampler for platforms with combined texture/sampler binding.
propertySet.SetTexture(internedId, MainTextureObject, MainTextureSampler);

// SetSampler binds a sampler independently of any texture.
propertySet.SetSampler("SecondaryTexture_SamplerObject", SecondarySampler);

// Transient uniform properties. Transient uniforms are owned by the buffered frames-in-flight
// system, and are automatically allocated and disposed.
propertySet.SetFloat("FloatProperty", 10.3f);
propertySet.SetMatrix("MatrixProperty", ObjectMatrix);

// Set an SSBO buffer.
propertySet.SetBuffer("SSBOBuffer", MySSBOBuffer);

// Set a static, read-only UBO buffer with fixed uniforms. Any SetX() call that would write into
// this UBO is ignored while 'readOnly' is true.
propertySet.SetBuffer("UBOBuffer", MyUBOBuffer, readOnly: true);

// Set a writable UBO buffer. When 'readOnly' is false, SetX() calls use this buffer as their
// backing storage, letting users control the backing UBO lifetime manually.
propertySet.SetBuffer("UBOBuffer", MyUBOBuffer, readOnly: false);
```

### Frames-in-flight system

Graphite has a built-in frames-in-flight ring rather than leaving CPU/GPU synchronization to the
caller. Work is submitted through `Frame` objects obtained from the `GraphicsDevice`, and the
device transparently throttles the CPU so that no more than `MaxFramesInFlight` frames are ever
queued ahead of the GPU.

A frame is a single unit of GPU work with a monotonic id, a ring slot, and a completion fence:

```cs
Frame frame = device.BeginFrame();   // Blocks if the oldest ring slot is still in flight.
frame.SubmitCommands(commandBuffer);
device.EndFrame(frame);              // Signals the frame's completion fence; does not block.
device.SwapBuffers();
```

Key pieces:

- `GraphicsDevice.MaxFramesInFlight` - the ring depth. Configured via
  `GraphicsDeviceOptions.MaxFramesInFlight` (defaults to `3` when left `0`).
- `BeginFrame` / `EndFrame` - open and close the active frame. `BeginFrame` blocks only when the
  ring slot it is about to reuse has not yet completed on the GPU.
- `Frame.FrameId` / `Frame.RingSlot` - a monotonic id (starting at 1; 0 is the "no frame"
  sentinel) and the `[0, MaxFramesInFlight)` slot it occupies.
- `Frame.CompletionFence` - owned and recycled by the frame system. Do not reset it or hold the
  reference past the next `BeginFrame` for the same ring slot.
- `IsFrameComplete` / `WaitForFrame` / `LastCompletedFrameId` / `FramesInFlight` - poll, block on,
  or query frame completion. These also opportunistically advance the device's notion of the last
  completed frame.

#### Transient (per-frame) memory

Each ring slot owns a bump-allocated transient buffer. `Frame.AllocateTransient(sizeInBytes)` (or
the `GraphicsDevice.AllocateTransient` convenience wrapper) hands back a `DeviceBufferRange` that
is valid for GPU use until the frame's completion fence signals, after which the memory is
recycled. This is what backs transient `PropertySet` uniforms. The allocator is governed by:

| Option                          | Default | Behavior                                                          |
|---------------------------------|---------|-------------------------------------------------------------------|
| `TransientBufferInitialSize`    | 4 MB    | Initial size of each per-slot transient buffer.                   |
| `TransientBufferSoftCapBytes`   | 64 MB   | Per-frame soft cap; exceeding it logs a one-shot warning.         |
| `TransientBufferHardCapBytes`   | 256 MB  | Per-frame hard cap; exceeding it throws a `RenderException`.      |

## Samples

Runnable samples live under [`Samples/`](Samples) and share common setup (windowing, shader and
model loading) through the `Shared` project:

- `HelloTriangle` - the minimal render loop.
- `TexturedQuad` - texture and sampler binding.
- `Cube` / `CubeGrid` - 3D transforms and instancing-style draws.

Run one with, for example:

```sh
dotnet run --project Samples/HelloTriangle
```

## Testing

Tests live under [`Tests/`](Tests) and are split into CPU tests (pure value-type tests, run in
parallel) and GPU tests (which share one device per backend and run serialized). GPU shaders are
authored in Slang (`.slang`) under `Tests/Shaders` and compiled to per-backend bytecode at runtime
(SPIR-V for Vulkan, HLSL for D3D11); there are no checked-in compiled shaders.
See [`Tests/README.md`](Tests/README.md) for the current suite layout and the in-progress
migration of older suites onto the `GraphicsProgram` / `PropertySet` / `Frame` API.

```sh
dotnet test Tests/Prowl.Graphite.Tests.csproj
```

## Credits

Thank you to mellinoe and ciberman, the creators of
[Veldrid](https://github.com/veldrid/veldrid) and
[NeoVeldrid](https://github.com/jhm-ciberman/neo-veldrid), for being unaware of what I did to your
libraries. Having a base, known-stable library has massively boosted development and shaved hours
of boilerplate off development time.

Prowl.Graphite has had radical filesystem and API changes relative to upstream Veldrid/NeoVeldrid.
As such, changes and fixes from NeoVeldrid cannot be easily merged, and will land in the commit
history with the prefix `(NeoVeldrid)` and the same commit name, but with altered file paths,
locations, and logic. If any of the original contributors would like more or different credit for
their work, or would like me to stop sourcing from their commits, please reach out.

## License

This project is part of the Prowl Game Engine and is licensed under the MIT License. See the
[LICENSE](LICENSE) file in the project root for full details. Portions are derived from Veldrid
(Copyright (c) 2017 Eric Mellino and Veldrid contributors) and NeoVeldrid (Copyright (c) 2026
Javier Mora and NeoVeldrid contributors), both MIT licensed.
</content>
</invoke>
