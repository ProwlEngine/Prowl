# Prowl.Graphite

A cross-platform, low-level graphics and compute abstraction for .NET, with a Vulkan backend. Graphite powers the rendering layer of the Prowl Game Engine and can be used to build high-performance 2D and 3D games, simulations, tools, and other graphical applications.

Graphite started life as a modified and butchered version of NeoVeldrid, and by extension Veldrid, but has diverged far enough in its setup and API surface that it is now considered a separate library rather than a fork. See [API Differences](#api-differences) for the systems that intentionally break from upstream Veldrid.

## Features

- A Vulkan backend, with macOS support via MoltenVK (Vulkan-over-Metal translation).
- A monolithic `ShaderProgram` model that bundles shader and pipeline state, with per-backend shader compilation handled internally.
- A string/id-driven `PropertySet` resource binding system that hides per-backend binding rules.
- A declarative render graph (`RenderPipeline`, `IPass`, `IPresentPass`) that orders passes from their
  declared texture reads/writes and resolves shared render targets automatically.
- A frame-less `ExecutionTask` ring for CPU/GPU synchronization, with per-execution transient
  (bump-allocated) GPU memory.
- Runtime-toggleable validation and profiling layers, controlled via `GraphicsDeviceOptions`.

## Requirements

- .NET 10 (`net10.0`).
- A GPU and driver supporting Vulkan.
- [Silk.NET](https://github.com/dotnet/Silk.NET) 2.23.0 (pulled in transitively; provides the native bindings).
- [Prowl.Vector](https://www.nuget.org/packages/Prowl.Vector) 2.1.0 for vector and matrix math.

## Quick Start

Rendering is built around a render graph: a `RenderPipeline` owns a list of `IPass`es plus one required
`IPresentPass`, and a `GraphicsDevice` dispatches that pipeline against a list of views. The simplest
possible pipeline has no offscreen passes at all, and draws straight into the swapchain from its present pass:

```cs
internal readonly struct SceneView : IRenderView
{
    public SceneView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
}

internal sealed class TrianglePresentPass : IPresentPass<SceneView, int>
{
    private readonly Mesh _triangle;
    private readonly GraphicsProgram _shader;

    public TrianglePresentPass(Mesh triangle, GraphicsProgram shader)
    {
        _triangle = triangle;
        _shader = shader;
    }

    public string Name => "Present";

    public void Setup(PresentContextBuilder builder) => builder.RequestSwapchain();

    public void Present(RenderContext<SceneView, int> context)
    {
        Framebuffer? target = context.SwapchainTarget;
        if (target == null)
            return;

        CommandBuffer cmd = context.GetCommandBuffer("Triangle");
        cmd.Begin();
        cmd.SetFramebuffer(target);
        cmd.ClearDepthStencil(1, 0);
        cmd.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_triangle);
        cmd.DrawIndexed();
        cmd.End();

        context.SubmitCommandBuffer(cmd);
        context.Present();
    }
}

internal sealed class TrianglePipeline : RenderPipeline<SceneView, int>
{
    private readonly IPresentPass<SceneView, int> _present;

    public TrianglePipeline(IPresentPass<SceneView, int> present) => _present = present;

    protected override void InitializePasses() => SetPresentPass(_present);
}
```

Creating a device and dispatching the pipeline each frame:

```cs
GraphicsDeviceOptions options = new()
{
    Debug = false,
    SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt,
    SyncToVerticalBlank = false,
    PreferStandardClipSpaceYDirection = true
};

GraphicsDevice device = GraphicsDevice.CreateVulkan(options, swapchainDescription, vulkanOptions);

GraphicsProgram shader = /* load + create a ShaderProgram */;
Mesh triangle = /* create vertex/index buffers */;
TrianglePipeline pipeline = new(new TrianglePresentPass(triangle, shader));
SceneView[] views = { new SceneView(600, 600) };

// Per-frame render loop: builds an ExecutionTask internally, runs the pipeline for every view, and
// swaps buffers if any view's present pass requested it.
device.DispatchGraph(pipeline, views);
```

The [`Samples/`](Samples) directory contains complete, runnable versions of this and larger graphs
(window creation, shader loading, and mesh setup included).

## Backends

| Backend       | Windows | Linux | macOS |
|---------------|:-------:|:-----:|:-----:|
| Vulkan        | Yes     | Yes   | Yes (via MoltenVK) |

A device is created through the backend-specific factory methods on `GraphicsDevice`
(`CreateVulkan`). The `GraphicsBackend` enum enumerates the available backends.

## Building

The solution targets `net10.0`. Build everything with:

```sh
dotnet build Prowl.Graphite.slnx
```

### Build configuration flags

One MSBuild property controls backend trimming. It can be set on the command line
(`-p:ExcludeVulkan=true`) or in `Directory.Build.props`.

| Property        | Default | Effect                                                                                                      |
|-----------------|---------|--------------------------------------------------------------------------------------------------------------------|
| `ExcludeVulkan` | `false` | Excludes the Vulkan backend (and its Silk.NET packages) from the build, defining `EXCLUDE_VULKAN_BACKEND`. |

## Validation and Profiling Layers

Graphite ships two optional layers that mirror the core source tree, toggled at runtime through
`GraphicsDeviceOptions` rather than at compile time:

- **Validation** (`GraphicsDeviceOptions.EnableValidation`, defaults to enabled when `null`): extra
  argument and state checks that throw descriptive exceptions on misuse. Validation lives under
  `Graphite/ValidationLayers`, mirroring the structure of `Graphite/Core` and `Graphite/Platform`,
  and every check is gated behind `GraphicsDevice.ValidationEnabled`.
- **Profiling** (`GraphicsDeviceOptions.EnableProfiling`, defaults to disabled when `null`):
  allocation and command counters collected by the `GraphicsDevice` and readable through
  `GraphicsDevice.GetProfile()`. Profiling lives under `Graphite/Profiling`, mirroring the same
  structure, and every counter is gated behind `GraphicsDevice.ProfilingEnabled`.

Both settings are read once at device creation and apply for the device's lifetime. Leave
`EnableValidation` on during development; disable it for release builds where the extra checks
aren't needed. `EnableProfiling` stays off unless you're actively reading `GetProfile()`.

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

// Transient uniform properties. Transient uniforms are owned by the execution ring's transient
// allocator, and are automatically allocated and disposed.
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

### Execution ring

Veldrid leaves CPU/GPU synchronization entirely to the caller: `GraphicsDevice.SubmitCommands`
takes an optional `Fence` you own, and any double/triple buffering (which command list or resource
generation is safe to reuse) is the caller's responsibility to track by hand.

Graphite builds a frames-in-flight ring into the device instead, addressed through `ExecutionTask`.
`GraphicsDevice.BeginExecution()` grabs a free ring slot (blocking on the oldest in-flight task if
all slots are busy) and hands back an `ExecutionTask`; `CompleteExecution` closes it out
non-blockingly, and the ring's own fence tells you when it's safe to reuse that slot again. In
practice you rarely call these directly - `GraphicsDevice.DispatchGraph` does it for you around a
`RenderPipeline` run:

```cs
ExecutionTask task = device.BeginExecution();  // Blocks if the oldest ring slot is still in flight.
// ... rent command buffers via a RenderContext, submit them ...
device.CompleteExecution(task);                // Signals the task's completion fence; does not block.
device.SwapBuffers();
```

Key pieces:

- `GraphicsDevice.MaxExecutingTasks` - the ring depth. Configured via
  `GraphicsDeviceOptions.MaxFramesInFlight` (defaults to `3` when left `0`).
- `BeginExecution` / `CompleteExecution` - open and close an execution. `BeginExecution` blocks only
  when the ring slot it is about to reuse has not yet completed on the GPU.
- `ExecutionTask.Id` / `ExecutionTask.RingSlot` - a monotonic id (starting at 1; 0 is the "none"
  sentinel) and the `[0, MaxExecutingTasks)` slot it occupies.
- `ExecutionTask.CompletionFence` - owned and recycled by the ring. Do not reset it or hold the
  reference past the next `BeginExecution` for the same ring slot.
- `IsExecutionComplete` / `WaitForExecution` / `LastCompletedExecutionId` / `ExecutingTasks` /
  `ActiveExecutions` - poll, block on, or query execution completion. These also opportunistically
  advance the device's notion of the last completed execution.

#### Transient (per-execution) memory

Each ring slot owns a bump-allocated transient buffer. `RenderContext.AllocateTransient(sizeInBytes)`
hands back a `DeviceBufferRange` that is valid for GPU use until the execution's completion fence
signals, after which the memory is recycled. This is what backs transient `PropertySet` uniforms.
The allocator is governed by:

| Option                          | Default | Behavior                                                          |
|---------------------------------|---------|-------------------------------------------------------------------|
| `TransientBufferInitialSize`    | 4 MB    | Initial size of each per-slot transient buffer.                   |
| `TransientBufferSoftCapBytes`   | 64 MB   | Per-execution soft cap; exceeding it logs a one-shot warning.     |
| `TransientBufferHardCapBytes`   | 256 MB  | Per-execution hard cap; exceeding it throws a `RenderException`.  |

### Render graph and pass lifetime

Rendering is no longer "record commands into a `CommandBuffer` yourself each frame" - it's a
declarative graph of passes over a `RenderPipeline<TView, TDrawCommand>`:

- **`IPass<TView, TDrawCommand>`** - a single offscreen pass. `Setup(RenderContextBuilder)` runs once
  (lazily, on first use) and declares the graph textures the pass reads and writes via
  `GetInputTexture`/`GetOutputTexture`; a pass may nominate one of its outputs as its `SetMainOutput`.
  `Render(RenderContext<TView, TDrawCommand>)` runs every dispatch and records the pass's actual work.
- **`IPresentPass<TView, TDrawCommand>`** - the one required, terminal pass. `Setup(PresentContextBuilder)`
  declares the textures it reads from the graph and whether it needs the window's swapchain this run
  (`RequestSwapchain()`). `Present(...)` runs after every other pass; grab `context.SwapchainTarget`,
  draw into it, and call `context.Present()` to arm the present. Do nothing to stay offscreen.
- **`RenderPipeline<TView, TDrawCommand>`** - subclass and override `InitializePasses()` to call
  `AddPass` for each `IPass` and `SetPresentPass` once. The pipeline lazily solves the declared passes
  into a `RenderGraph` the first time it runs: passes are topologically sorted so readers run after
  their writers, and the resource nominated by the last pass to call `SetMainOutput` becomes the
  graph's presentation source. A dependency cycle throws.
- **`GraphTextureDesc`** - describes a graph texture: view-relative (`GraphTextureDesc.ViewSized`,
  scaled off `IRenderView.PixelWidth`/`PixelHeight`) or fixed-size (`GraphTextureDesc.Sized`), plus
  color formats and whether it has a depth attachment. Passes sharing a resource ID share one
  physical target; the first pass to declare an ID wins its description.
- **`TextureHandle`** - the opaque handle a pass gets back from `RenderContextBuilder`/`PresentContextBuilder`
  during setup; resolve it to a real `RenderTexture` during rendering via `context.GetRenderTexture(handle)`.
- **`IRenderView`** - the minimal size contract (`PixelWidth`/`PixelHeight`) a pipeline's view type must
  implement so view-relative textures can be sized; add richer per-view data (matrices, frustum) on
  top in your own type.
- **`IDrawCommandProvider<TDrawCommand>`** / **`IRenderable`** / **`RenderQuery`** - the optional seam between
  scene and framework. `Provider.Initialize(view)` runs once per view; passes then pull slices of
  draw commands on demand via `context.GetDrawCommands(query)`, where `RenderQuery` carries a
  `SortMode` and an optional `FrustumOverride` (e.g. for a shadow pass culling from a light instead
  of the camera).
- **`IPassProfiler`** - optional per-dispatch hooks (`BeginSample`/`EndSample`, `RecordDrawCall`, and
  a `RequestCapture`/`Capture` pair the pipeline calls between passes to copy pass outputs to an
  intermediate texture for inspection).

Dispatch a pipeline against a list of views with `GraphicsDevice.DispatchGraph`:

```cs
device.DispatchGraph(pipeline, views, profiler: null);
```

This opens one `ExecutionTask`, runs `RenderPipeline.ExecuteView` (ordered passes, then the present
pass) for every view, completes the execution, and calls `SwapBuffers()` if any view's present pass
requested a present.

## Samples

Runnable samples live under [`Samples/`](Samples) and share common setup (windowing, shader and
model loading) through the `Shared` project:

- `HelloTriangle` - the minimal render loop: one present pass, no offscreen passes.
- `TexturedQuad` - texture and sampler binding.
- `Cube` / `CubeGrid` - 3D transforms and instancing-style draws.
- `PBRRenderer` - a multi-pass render graph: an offscreen "Scene" pass, a two-step bloom
  (downsample/upsample), and a present pass that composites Scene + bloom to the swapchain. The
  graph orders the four passes from their declared texture reads/writes.

Run one with, for example:

```sh
dotnet run --project Samples/HelloTriangle
```

## Testing

Tests live under [`Tests/`](Tests) and are split into CPU tests (pure value-type tests, run in
parallel) and GPU tests (which share one device per backend and run serialized). GPU shaders are
authored in Slang (`.slang`) under `Tests/Shaders` and compiled to SPIR-V for Vulkan at runtime;
there are no checked-in compiled shaders.
See [`Tests/README.md`](Tests/README.md) for the current suite layout and the in-progress
migration of older suites onto the `GraphicsProgram` / `PropertySet` / `ExecutionTask` API.

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
