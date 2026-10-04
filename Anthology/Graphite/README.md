# Prowl.Graphite

A cross-platform, low-level graphics and compute abstraction for .NET, with a Vulkan backend. Graphite powers the rendering layer of the Prowl Game Engine and can be used to build high-performance 2D and 3D games, simulations, tools, and other graphical applications.

Graphite started life as a modified and butchered version of NeoVeldrid, and by extension Veldrid, but has diverged far enough in its setup and API surface that it is now considered a separate library rather than a fork. There would be an API difference page, but there's not a lot left. However, some heritage remains regarding the texture/buffer API and some of the command buffer internals.

## Features

- A Vulkan backend, with macOS support via MoltenVK (Vulkan-over-Metal translation).
- A monolithic `ShaderProgram` model that bundles shader and pipeline state, with per-backend shader compilation handled internally.
- A string/id-driven `PropertySet` resource binding system that hides per-backend binding rules.
- A declarative render graph (`RenderPipeline`, `IPass`) that orders passes from their
  declared texture and buffer reads/writes and resolves shared, transient, and history resources
  automatically.
- A frame-less `ExecutionTask` ring for CPU/GPU synchronization, with per-execution transient
  (bump-allocated) GPU memory.
- Runtime-toggleable validation and profiling layers, controlled via `GraphicsDeviceOptions`.

## Requirements

- .NET 10 (`net10.0`).
- A GPU and driver supporting Vulkan.
- [Silk.NET](https://github.com/dotnet/Silk.NET) 2.23.0 (pulled in transitively; provides the native bindings).
- [Prowl.Vector](https://www.nuget.org/packages/Prowl.Vector) 2.1.0 for vector and matrix math.

## Quick Start

Rendering is built around a render graph: a `RenderPipeline` owns a list of `IPass`es, and a
`GraphicsDevice` dispatches that pipeline against a list of views. The simplest possible pipeline is one
pass that writes the view target, which presents the frame when the view sets its `TargetSwapchain`:

```cs
internal readonly struct SceneView : IRenderView
{
    public SceneView(uint width, uint height, Swapchain swapchain)
    {
        PixelWidth = width;
        PixelHeight = height;
        TargetSwapchain = swapchain;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
    public Swapchain TargetSwapchain { get; }
}

internal sealed class TrianglePass : RasterPass<SceneView>
{
    private readonly Mesh _triangle;
    private readonly GraphicsProgram _shader;

    public TrianglePass(Mesh triangle, GraphicsProgram shader)
    {
        _triangle = triangle;
        _shader = shader;
    }

    public override string Name => "Triangle";

    public override void Setup(RenderContextBuilder builder) => SetViewTarget(builder, TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)));

    public override void Render(RenderContext<SceneView> context, CommandBuffer cmd)
    {
        BindTarget(context, cmd);
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_triangle);
        cmd.DrawIndexed();

    }
}

internal sealed class TrianglePipeline : RenderPipeline<SceneView>
{
    private readonly TrianglePass _pass;

    public TrianglePipeline(TrianglePass pass) => _pass = pass;

    protected override void InitializePasses() => AddPass(_pass);
}
```

Creating a device and dispatching the pipeline each frame:

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
TrianglePipeline pipeline = new(new TrianglePass(triangle, shader));
SceneView[] views = { new SceneView(600, 600, device.MainSwapchain) };

// Per-frame render loop: builds an ExecutionTask internally, runs the pipeline for every view, and
// presents the swapchain of every view that wrote the view target.
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

- **Validation** (`GraphicsDeviceOptions.GraphiteValidation`, defaults to true): extra
  argument and state checks that throw descriptive exceptions on misuse. Validation lives under
  `Graphite/ValidationLayers`, mirroring the structure of `Graphite/Core` and `Graphite/Platform`,
  and every check is gated behind `GraphicsDevice.ValidationEnabled`.
- **Profiling** (`GraphicsDeviceOptions.EnableProfiling`, defaults to disabled when `null`):
  allocation and command counters collected by the `GraphicsDevice` and readable through
  `GraphicsDevice.GetProfile()`. Profiling lives under `Graphite/Profiling`, mirroring the same
  structure, and every counter is gated behind `GraphicsDevice.ProfilingEnabled`.

Both settings are read once at device creation and apply for the device's lifetime. Leave
`GraphiteValidation` on during development; disable it for release builds where the extra checks
aren't needed. `EnableProfiling` stays off unless you're actively reading `GetProfile()`.

## Samples

Runnable samples live under [`Samples/`](Samples) and share common setup (windowing, shader and
model loading) through the `Shared` project:

- `HelloTriangle` - the minimal render loop: one pass writing the view target, no offscreen passes.
- `TexturedQuad` - texture and sampler binding.
- `Cube` / `CubeGrid` - 3D transforms and instancing-style draws.
- `PBRRenderer` - a multi-pass render graph: an offscreen "Scene" pass, a two-step bloom
  (downsample/upsample), and a composite pass that writes Scene + bloom to the view target. The
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
