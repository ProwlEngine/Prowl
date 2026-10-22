# Prowl.Graphite

A cross-platform, low-level graphics and compute abstraction for .NET, with a Vulkan backend. Graphite powers the rendering layer of the Prowl Game Engine and can be used to build high-performance 2D and 3D games, simulations, tools, and other graphical applications.

Graphite started life as a modified and butchered version of NeoVeldrid, and by extension Veldrid, but has diverged far enough in its setup and API surface that it is now considered a separate library rather than a fork. There would be an API difference page, but there's not a lot left. However, some heritage remains regarding the texture/buffer API and some of the command buffer internals.

## Features

- A Vulkan backend, with macOS support via MoltenVK (Vulkan-over-Metal translation).
- A monolithic `ShaderProgram` model that bundles shader and pipeline state, with per-backend shader compilation handled internally.
- A string/id-driven `PropertySet` resource binding system that hides per-backend binding rules.
- A declarative render graph (`RenderGraph`, `IExecutionPass`, `IInstancePass`): passes run once per
  dispatch or once per instance of their kind and are ordered by what they read and write, with
  transient, persistent and history resources owned by the graph and barriers planned from each
  pass's declared reads and writes.
- A frame-less `ExecutionTask` ring for CPU/GPU synchronization, with per-execution transient
  (bump-allocated) GPU memory.
- Runtime-toggleable validation through `GraphicsDeviceOptions`, and per-execution profilers.

## Requirements

- .NET 10 (`net10.0`).
- A GPU and driver supporting Vulkan.
- [Silk.NET](https://github.com/dotnet/Silk.NET) 2.23.0 (pulled in transitively; provides the native bindings).
- [Prowl.Vector](https://www.nuget.org/packages/Prowl.Vector) 2.1.0 for vector and matrix math.

## Quick Start

Rendering is built around a render graph. A `RenderGraph` is a flat list of passes. An `IExecutionPass`
runs once per dispatch. An `IInstancePass` has a `GraphKind` and runs once per `GraphInstance` of that
kind while its `IsEnabled` says so. The graph sorts every pass by the resources it reads and writes, so
order in the list only breaks ties. Kinds nest through `GraphScope`s, and each instance runs after its
parent and reads the resources its ancestors own. The simplest graph is one pass that writes the
instance target, which presents when the target is a swapchain framebuffer:

```cs
internal sealed class TrianglePass : InstanceRasterPass
{
    private readonly Mesh _triangle;
    private readonly GraphicsProgram _shader;

    public TrianglePass(Mesh triangle, GraphicsProgram shader)
    {
        _triangle = triangle;
        _shader = shader;
    }

    public static readonly GraphKind View = GraphKind.Intern("View");

    public override string Name => "Triangle";

    public override GraphKind Kind => View;

    public override void Setup(InstanceContextBuilder builder) => SetInstanceTarget(builder, TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)));

    public override void Render(InstanceContext context, CommandBuffer cmd)
    {
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_triangle);
        cmd.DrawIndexed();
    }
}
```

Creating a device and dispatching the graph each frame:

```cs
GraphicsDeviceOptions options = new()
{
    VulkanValidationLayers = false,
};

SwapchainDescription swapchainDescription = new()
{
    Source = SwapchainSource.CreateVulkan(window.VkSurface!),
    Width = (uint)window.FramebufferSize.X,
    Height = (uint)window.FramebufferSize.Y,
    DepthFormat = PixelFormat.D24_UNorm_S8_UInt,
    SyncToVerticalBlank = false
};

GraphicsDevice device = GraphicsDevice.CreateVulkan(options, swapchainDescription, vulkanOptions);

GraphicsProgram shader = /* load + create a ShaderProgram */;
Mesh triangle = /* create vertex/index buffers */;
RenderGraph graph = RenderGraph.Build([new TrianglePass(triangle, shader)], [new GraphScope(TrianglePass.View, GraphKind.Root)], "Triangle");
GraphInstances instances = graph.CreateInstances();
instances.Add(TrianglePass.View, id: 0, extent: new GraphExtent(600, 600), target: device.MainSwapchain.Framebuffer);

device.DispatchGraph(graph, instances);
```

Passes that run once per dispatch, such as skinning, are execution passes. Passes that run once per
camera, light or other unit are instance passes of a kind, with an instance per unit. An instance's key,
its kind with an id, keys its history and persistent resources. A pass that is disabled for an instance
is skipped, and a pass can pipe one of its inputs to an output with `Passthrough` so its consumers still
get a resource when it is off. Buffers and textures the caller owns are bound per instance to the import
slots a pass declares:

```cs
GraphKind camera = GraphKind.Intern("Camera");
RenderGraph graph = RenderGraph.Build([new SkinningPass(), new OpaquePass(camera), new PostPass(camera)], [new GraphScope(camera, GraphKind.Root)], "Scene");

GraphInstances instances = graph.CreateInstances();
instances.Add(camera, id: 0, extent: new GraphExtent(1280, 720), target: device.MainSwapchain.Framebuffer);
instances.Add(camera, id: 1, extent: new GraphExtent(256, 256), target: minimap);
device.DispatchGraph(graph, instances);
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

## Validation and Profiling

**Validation** (`GraphicsDeviceOptions.GraphiteValidation`, defaults to true) adds argument and
state checks that throw descriptive exceptions on misuse. It lives under `Graphite/ValidationLayers`,
mirroring `Graphite/Core` and `Graphite/Platform`, and is read once at device creation. Leave it on
during development and disable it for release builds.

**Counters** (`GraphicsDevice.Counters`) are always on: live allocations, resident buffer bytes,
barriers, swaps, buffer operations and set binds. Read them with `Counters.Snapshot()`.

**Profilers** observe exactly one execution each. Pass them to the execution directly:

```csharp
Recording recording = new(device);
device.DispatchGraph(graph, instances, recording);
recording.Wait();
```

An execution started with no profilers creates one from each `GraphicsDevice.GlobalProfilers`
factory, so a factory can collect a profiler per frame, or return null to skip a frame and sample:

```csharp
device.GlobalProfilers.Add(() => new MyFrameProfiler(results));
```

A profiler gets `BeginExecution(executionId, graphName)`, then the events of each capability it
implements (`IGraphProfiler`, `IGpuStatsProfiler`, `ICaptureProfiler`, `ICommandStreamProfiler`), then
`EndExecution()` once every GPU result is delivered. The graph name is the one given to
`RenderGraph.Build`; `BeginExecution(name, ...)` names executions without a graph.
A capability with one profiler calls it directly; several profilers sharing a capability are merged
for that capability only. Execution ids grow with start order, so per-execution results sort by id.

There is no profiling for `device.Record` or other transfers outside an execution.

`Graphite.Debugger` builds on this seam: `Recording` captures instances, passes and GPU timings,
`DeepRecording` also captures commands and resource copies, and `Replayer` replays a recorded pass
on any device.

## Samples

Runnable samples live under [`Samples/`](Samples) and share common setup (windowing, shader and
model loading) through the `Shared` project:

- `HelloTriangle` - the minimal render loop: one pass writing the instance target, no offscreen passes.
- `TexturedQuad` - texture and sampler binding.
- `Cube` / `CubeGrid` - 3D transforms and instancing-style draws.
- `PBRRenderer` - a multi-pass render graph: an offscreen "Scene" pass, a two-step bloom
  (downsample/upsample), and a composite pass that writes Scene + bloom to the instance target.

Run one with, for example:

```sh
dotnet run --project Samples/HelloTriangle
```

## Testing

Tests live under [`Tests/`](Tests) and are split into CPU tests (pure value-type tests, run in
parallel) and GPU tests (which share one device per backend and run serialized). GPU shaders are
authored in GLSL (`.glsl`) under `Tests/Graphite/Shaders` and compiled to SPIR-V for Vulkan at runtime
by the ShaderDef GLSL compiler. There are no checked-in compiled shaders.
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
