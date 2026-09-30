# Render Graph API

Reference for `RenderPipeline<TView>`, passes, builders, `RenderContext<TView>`, handles and resource descriptions.

- [Overview](#overview)
- [Quick example](#quick-example)
- [Views](#views)
- [RenderPipeline](#renderpipelinetview)
- [Passes](#passes)
- [Builders](#builders)
- [RenderContext](#rendercontexttview)
- [Resource descriptions](#resource-descriptions)
- [Complete example](#complete-example-offscreen-pass-and-present-pass)
- [Common patterns](#common-patterns)
- [Pitfalls](#pitfalls)
- [See also](#see-also)

## Overview

You write passes. Each pass declares in `Setup` which named resources it reads and writes, and records GPU work in `Render`. A `RenderPipeline<TView>` collects the passes and one present pass. `GraphicsDevice.DispatchGraph(pipeline, views)` orders the passes from their declarations, runs them for every view, then runs the present pass. All types live in namespace `Prowl.Graphite.RenderGraph` except `GraphicsDevice`, `CommandBuffer` and friends in `Prowl.Graphite`.

Resource names are `RenderResourceID`, an interned string. A `string` converts implicitly, so `"Scene"` can be passed anywhere an ID is expected.

## Quick example

The smallest pipeline is just a present pass drawing straight to the window ([HelloTriangle](../../Samples/HelloTriangle/Program.cs#L24)):

```csharp
internal readonly struct SceneView : IRenderView
{
    public SceneView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}

internal sealed class TrianglePresentPass : IPresentPass<SceneView>
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

    public void Present(RenderContext<SceneView> context)
    {
        Framebuffer? target = context.SwapchainTarget;
        if (target == null)
            return;

        CommandBuffer cmd = context.GetCommandBuffer("Triangle");
        cmd.SetFramebuffer(target);
        cmd.ClearDepthStencil(1, 0);
        cmd.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_triangle);
        cmd.DrawIndexed();
        context.SubmitCommandBuffer(cmd);
        context.Present();
    }
}

internal sealed class TrianglePipeline : RenderPipeline<SceneView>
{
    private readonly IPresentPass<SceneView> _present;

    public TrianglePipeline(IPresentPass<SceneView> present) => _present = present;

    protected override void InitializePasses() => SetPresentPass(_present);
}
```

Then each frame: `device.DispatchGraph(pipeline, new[] { new SceneView(600, 600) });`.

## Views

### IRenderView

A view is what you render for: a camera, a shadow cascade, an editor viewport. Implement it as a struct or class carrying whatever your passes need.

| Member | Signature | Description |
|--------|-----------|-------------|
| `PixelWidth` | `uint PixelWidth { get; }` | Width used to size view-relative targets |
| `PixelHeight` | `uint PixelHeight { get; }` | Height used to size view-relative targets |
| `ViewId` | `int ViewId { get; }` | Stable identity across frames. History resources are kept per `ViewId`. |
| `Name` | `string Name => GetType().Name` | Default interface member; shown in profiler tooling. Overridable to tell cameras apart. |

## RenderPipeline<TView>

Abstract base class. Subclass it, override `InitializePasses`, add passes. Source: [RenderPipeline.cs](../../Graphite/Core/RenderGraph/RenderPipeline.cs#L10).

| Member | Signature | Description |
|--------|-----------|-------------|
| `InitializePasses` | `protected abstract void InitializePasses()` | Runs once, lazily, on first use of `Graph`. Add passes here. |
| `AddPass` | `protected void AddPass(IPass<TView> pass)` | Adds a pass. Insertion order only breaks ties between independent passes. |
| `SetPresentPass` | `protected void SetPresentPass(IPresentPass<TView> presentPass)` | Sets the required present pass. |
| `DeclareTexture` | `protected void DeclareTexture(RenderResourceID id, GraphTextureDesc desc)` | Declares a transient texture with no owning pass. Takes priority over pass declarations of the same ID. |
| `DeclareBuffer` | `protected void DeclareBuffer(RenderResourceID id, GraphBufferDesc desc)` | Same, for a buffer. |
| `Graph` | `RenderGraph<TView> Graph { get; }` | The solved graph, built on first access. |
| `PresentPass` | `IPresentPass<TView> PresentPass { get; }` | The present pass. Throws if none was set. |
| `InvalidateGraph` | `protected void InvalidateGraph()` | Disposes the graph and reruns `InitializePasses` on next use. Throws during execution. |
| `ExecuteView` | `void ExecuteView(RenderContext<TView> context)` | Runs passes then present for one view. Called by `DispatchGraph`; you rarely call it. |
| `Dispose` | `virtual void Dispose()` | Disposes passes and the present pass that implement `IDisposable`, then the graph. |

### GraphicsDevice.DispatchGraph

```csharp
public ExecutionTask DispatchGraph<T>(RenderPipeline<T> pipeline, IReadOnlyList<T> views) where T : IRenderView
```

Begins one execution, renders every view in order, completes the execution, and calls `SwapBuffers` if any view's present pass called `context.Present()`. Returns the `ExecutionTask` (see [graphics-device.md](graphics-device.md)). Non-blocking; GPU completion is signaled by the task's fence.

### RenderGraph<TView>

Read-only result of solving. Mostly useful for tooling and tests.

| Member | Signature | Description |
|--------|-----------|-------------|
| `OrderedPasses` | `IReadOnlyList<PassNode> OrderedPasses` | Passes in execution order |
| `Resources` | `IReadOnlyDictionary<RenderResourceID, GraphResource> Resources` | Every declared resource, first declaration wins |
| `PresentInputs` | `IReadOnlyList<RenderResourceID> PresentInputs` | IDs the present pass reads |
| `PresentRequestsSwapchain` | `bool` | Whether the present pass called `RequestSwapchain` |
| `Build` | `static RenderGraph<TView> Build(IReadOnlyList<IPass<TView>>, IPresentPass<TView>, IReadOnlyList<GraphResource>? central = null)` | Builds a graph by hand. Throws on a missing producer or a cycle. |

`PassNode` exposes `Pass`, `Inputs` and `Outputs`.

## Passes

### IPass<TView>

| Member | Signature | Description |
|--------|-----------|-------------|
| `Name` | `string Name { get; }` | Debug and profiler name |
| `Setup` | `void Setup(RenderContextBuilder builder)` | Declare reads and writes. Runs once when the graph is built, with no view. |
| `Render` | `void Render(RenderContext<TView> context)` | Record work. Runs every view, every dispatch. |

### IPresentPass<TView>

Runs once per view, always after every `IPass`. It decides whether and how the result reaches the window.

| Member | Signature | Description |
|--------|-----------|-------------|
| `Name` | `string Name { get; }` | Command buffer label and diagnostics |
| `Setup` | `void Setup(PresentContextBuilder builder)` | Declare inputs and whether the swapchain is needed. No outputs. |
| `Present` | `void Present(RenderContext<TView> context)` | Draw to `context.SwapchainTarget` and call `context.Present()`, or do nothing to stay offscreen. |

### RasterPass<TView>

Abstract helper for passes that render into one declared target. Source: [RasterPass.cs](../../Graphite/Core/RenderGraph/RasterPass.cs#L12). It implements `IPass` and adds target management; it does not rent or submit command buffers.

| Member | Signature | Description |
|--------|-----------|-------------|
| `SetTarget` | `protected TextureHandle SetTarget(RenderContextBuilder builder, RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)` | Declares the single output and remembers it |
| `SetTargets` | `protected TextureHandle SetTargets(..., GraphTextureDesc mrtDesc, ...)` | Same, for a desc with several color formats (MRT) |
| `BindTarget` | `protected void BindTarget(RenderContext<TView> context, CommandBuffer cmd)` | Sets the framebuffer and applies load ops; clears color to `default(Color)` and depth to 1 |
| `BindTarget` | `protected void BindTarget(RenderContext<TView> context, CommandBuffer cmd, Color clearColor, float depthClear = 1f, byte stencilClear = 0)` | Same with explicit clear values |

`BindTarget` clears only if the resource's ops say `Load == Clear`. With default ops that is true for transient targets and false for history targets. It throws if `Setup` never declared a target.

## Builders

### RenderContextBuilder

Passed to `IPass.Setup`. Every call records a read or write. Source: [RenderContextBuilder.cs](../../Graphite/Core/RenderGraph/RenderContextBuilder.cs).

| Member | Signature | Description |
|--------|-----------|-------------|
| `GetInputTexture` | `TextureHandle GetInputTexture(RenderResourceID id)` | Declares a texture this pass reads. The producer owns the description. |
| `GetOutputTexture` | `TextureHandle GetOutputTexture(RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)` | Declares a texture this pass writes. `history > 0` keeps that many prior executions readable. |
| `ImportTexture` | `TextureHandle ImportTexture(RenderResourceID id, RenderTexture existing)` | Registers an externally owned render texture. The graph never disposes it. |
| `GetInputBuffer` | `BufferHandle GetInputBuffer(RenderResourceID id)` | Declares a buffer this pass reads |
| `GetOutputBuffer` | `BufferHandle GetOutputBuffer(RenderResourceID id, GraphBufferDesc desc, int history = 0)` | Declares a buffer this pass writes |

### PresentContextBuilder

Passed to `IPresentPass.Setup`. Source: [PresentContextBuilder.cs](../../Graphite/Core/RenderGraph/PresentContextBuilder.cs).

| Member | Signature | Description |
|--------|-----------|-------------|
| `GetInputTexture` | `TextureHandle GetInputTexture(RenderResourceID id)` | Declares a texture read |
| `GetInputBuffer` | `BufferHandle GetInputBuffer(RenderResourceID id)` | Declares a buffer read |
| `RequestSwapchain` | `void RequestSwapchain()` | Makes `context.SwapchainTarget` non-null |

## RenderContext<TView>

The per-view object handed to `Render` and `Present`. A new one is created for every view of every dispatch. Source: [RenderContext.cs](../../Graphite/Core/RenderGraph/RenderContext.cs#L9).

| Member | Signature | Description |
|--------|-----------|-------------|
| `View` | `TView View { get; }` | The view being rendered |
| `Task` | `ExecutionTask Task { get; }` | The execution everything records into |
| `GetCommandBuffer` | `CommandBuffer GetCommandBuffer(string name = "")` | Rents a command buffer that is already begun |
| `SubmitCommandBuffer` | `void SubmitCommandBuffer(CommandBuffer cmd)` | Ends it and queues it for this execution |
| `GetTransferCommandBuffer` | `TransferCommandBuffer GetTransferCommandBuffer(string name = "")` | Creates a transfer command buffer (not yet begun) |
| `SubmitTransferCommandBuffer` | `void SubmitTransferCommandBuffer(TransferCommandBuffer cmd)` | Flushes pending submissions then submits the transfer without blocking |
| `GetRenderTexture` | `RenderTexture GetRenderTexture(TextureHandle handle)` | Resolves a handle to the physical target for this view |
| `GetRenderTexture` | `RenderTexture GetRenderTexture(TextureHandle handle, int framesAgo)` | Resolves by age; 0 is current, up to the declared history depth |
| `GetRenderBuffer` | `DeviceBuffer GetRenderBuffer(BufferHandle handle)` / `(handle, int framesAgo)` | Same for buffers |
| `IsHistoryValid` | `bool IsHistoryValid(TextureHandle)` / `(BufferHandle)` | True once the view's ring holds an earlier execution |
| `AllocateTransient` | `DeviceBufferRange AllocateTransient(uint sizeInBytes)` | Bump-allocated uniform range, valid until the execution's fence signals |
| `GetTransientTexture` | `Texture GetTransientTexture(in GraphTextureDesc desc)` | Scratch texture not declared in the graph; freed when the fence signals |
| `SwapchainTarget` | `Framebuffer? SwapchainTarget { get; }` | The window framebuffer; null unless the present pass requested it |
| `Present` | `void Present()` | Arms a swapchain present when the dispatch finishes |
| `RequestPresent` | `bool RequestPresent { get; }` | True after `Present()` was called |
| `Profiler` | `IProfiler? Profiler { get; }` | The device profiler, or null |
| `WantsMetadata` | `bool WantsMetadata { get; }` | True if the profiler wants metadata |
| `RecordPassMetadata` | `void RecordPassMetadata(object metadata)` | Attaches metadata to the running pass |

Command buffer lifecycle: `GetCommandBuffer` begins it and `SubmitCommandBuffer` ends and submits it; passes never call `Begin` or `End`. One rented but never submitted triggers `GraphicsDevice.OnWarning` and is discarded. See [command-buffers.md](command-buffers.md).

### Resolving handles

```csharp
public override void Render(RenderContext<SceneView> context)
{
    RenderTexture scene = context.GetRenderTexture(_sceneHandle);
    Texture color = scene.ColorTextures[0];
    uint w = scene.Desc.Width;
    uint h = scene.Desc.Height;
}
```

The first resolve in a view rents (or fetches from the history ring); later resolves of the same ID in that view return the same object.

## Resource descriptions

### GraphTextureDesc

| Member | Signature | Description |
|--------|-----------|-------------|
| `ViewSized` | `static GraphTextureDesc ViewSized(bool depth = true, float scale = 1f, params PixelFormat[] formats)` | Size is `scale` times the view; empty `formats` means `R8_G8_B8_A8_UNorm` |
| `Sized` | `static GraphTextureDesc Sized(int width, int height, bool depth = true, params PixelFormat[] formats)` | Fixed size |
| `Resolve` | `(int width, int height) Resolve(uint viewWidth, uint viewHeight)` | Concrete size, never below 1 |
| Fields | `SizeMode`, `Scale`, `Width`, `Height`, `ColorFormats`, `EnableDepth` | Plain public fields |

```csharp
GraphTextureDesc hdr = GraphTextureDesc.ViewSized(true, 1f, PixelFormat.R16_G16_B16_A16_Float);
GraphTextureDesc half = GraphTextureDesc.ViewSized(false, 0.5f);
GraphTextureDesc shadow = GraphTextureDesc.Sized(2048, 2048, true);
```

Several formats make a multiple-render-target G-buffer; they share one resource and one framebuffer.

### GraphBufferDesc

| Member | Signature | Description |
|--------|-----------|-------------|
| `Structured` | `static GraphBufferDesc Structured(uint elementCount, uint elementStride, bool readWrite = true)` | Storage buffer |
| `Uniform` | `static GraphBufferDesc Uniform(uint sizeInBytes)` | Uniform buffer |
| `Of` | `static GraphBufferDesc Of(uint sizeInBytes, BufferUsage usage, uint structureByteStride = 0)` | Explicit usage |

### Load and store ops

| Type | Members |
|------|---------|
| `LoadAction` | `Clear`, `Load`, `DontCare` |
| `StoreAction` | `Store`, `DontCare` |
| `AttachmentOps` | `Load`, `Store`; statics `Cleared`, `Loaded`, `Discard` |
| `TargetLoadStoreOps` | `Color`, `Depth`; ctor `(AttachmentOps color, AttachmentOps depth)`; `static ForLifetime(bool persistent)` |

Defaults: a `history = 0` output uses `ForLifetime(false)` (clear, store). A history output or an imported texture uses `ForLifetime(true)` (load, store). Pass `ops` to `GetOutputTexture` or `SetTarget` to override per declaring pass.

### Handles and IDs

`TextureHandle` and `BufferHandle` are readonly structs with `Id` and `IsValid`. You obtain them only from builders. `RenderResourceID` has `Intern(string)`, `ToString(id)` (null if never interned), `IsValid`, and an implicit conversion from `string`.

## Complete example: offscreen pass and present pass

Adapted from [PBRRenderer](../../Samples/PBRRenderer/Program.cs#L32), with the bloom passes removed. `Mesh` is the sample helper in `Samples/Shared` and implements `IVertexSource`. `ScenePass` draws a mesh into a texture named "Scene". `BlitPresentPass` reads "Scene" and draws it to the window.

```csharp
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

internal readonly struct SceneView : IRenderView
{
    public SceneView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}

internal sealed class ScenePass : RasterPass<SceneView>
{
    private readonly Mesh _mesh;
    private readonly GraphicsProgram _shader;
    private readonly PropertySet _properties;
    private float _angle;

    public ScenePass(Mesh mesh, GraphicsProgram shader, PropertySet properties)
    {
        _mesh = mesh;
        _shader = shader;
        _properties = properties;
    }

    public override string Name => "Scene";

    public void Advance(float dt) => _angle += dt * 0.5f;

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, "Scene", GraphTextureDesc.ViewSized());

    public override void Render(RenderContext<SceneView> context)
    {
        Float3 eye = new Float3(MathF.Sin(_angle), 0.35f, MathF.Cos(_angle)) * 3f;
        Float4x4 projection = Float4x4.CreatePerspectiveFov(1.0472f, 1.0f, 0.05f, 100f);
        Float4x4 view = Float4x4.CreateLookAt(eye, Float3.Zero, Float3.UnitY);
        _properties.SetMatrix("MatrixMVP", projection * view);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        BindTarget(context, cmd, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_mesh);
        cmd.SetProperties(_properties);
        cmd.DrawIndexed();
        context.SubmitCommandBuffer(cmd);
    }
}

internal readonly struct FullscreenSource : IVertexSource
{
    public readonly PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

    public readonly void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
        => binding = default;

    public readonly bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount)
    {
        buffer = null!;
        format = IndexFormat.UInt32;
        indexCount = 0;
        return false;
    }
}

internal sealed class BlitPresentPass : IPresentPass<SceneView>
{
    private readonly GraphicsProgram _blitShader;
    private readonly Sampler _sampler;
    private readonly PropertySet _properties = new();
    private readonly FullscreenSource _fullscreen = new();
    private TextureHandle _sceneHandle;

    public BlitPresentPass(GraphicsProgram blitShader, Sampler sampler)
    {
        _blitShader = blitShader;
        _sampler = sampler;
    }

    public string Name => "Blit";

    public void Setup(PresentContextBuilder builder)
    {
        _sceneHandle = builder.GetInputTexture("Scene");
        builder.RequestSwapchain();
    }

    public void Present(RenderContext<SceneView> context)
    {
        Framebuffer? target = context.SwapchainTarget;
        if (target == null)
            return;

        RenderTexture scene = context.GetRenderTexture(_sceneHandle);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        cmd.SetFramebuffer(target);
        _properties.SetTexture("sceneTexture", scene.ColorTextures[0], _sampler);
        cmd.SetShader(_blitShader);
        cmd.SetVertexSource(_fullscreen);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
        context.SubmitCommandBuffer(cmd);
        context.Present();
    }
}

internal sealed class ScenePipeline : RenderPipeline<SceneView>
{
    private readonly ScenePass _scene;
    private readonly BlitPresentPass _present;

    public ScenePipeline(ScenePass scene, BlitPresentPass present)
    {
        _scene = scene;
        _present = present;
    }

    public ScenePass Scene => _scene;

    protected override void InitializePasses()
    {
        AddPass(_scene);
        SetPresentPass(_present);
    }
}
```

Per frame:

```csharp
pipeline.Scene.Advance((float)dt);
device.DispatchGraph(pipeline, views);
```

What happens: at first dispatch, `Setup` runs on `ScenePass` (declares write "Scene") and on `BlitPresentPass` (declares read "Scene", requests swapchain). The graph validates that "Scene" has a producer, orders `ScenePass` first, and runs the present pass last. In `Render`, `BindTarget` calls `GetRenderTexture`, which rents a view-sized transient texture; the present pass's `GetRenderTexture` returns that same texture. The scene texture returns to the pool when the execution's fence signals.

With more passes, the sample adds `BloomDownsample` (reads "Scene", writes "BloomHalf" at 0.5 scale) and `BloomUpsample` (reads "BloomHalf", writes "BloomFull"); the present pass reads both "Scene" and "BloomFull". None of the `AddPass` calls need to be in dependency order.

## Common patterns

### Temporal history

```csharp
public override void Setup(RenderContextBuilder builder)
{
    _color = builder.GetOutputTexture("TaaColor", GraphTextureDesc.ViewSized(false, 1f, PixelFormat.R16_G16_B16_A16_Float), history: 1);
}

public override void Render(RenderContext<SceneView> context)
{
    RenderTexture current = context.GetRenderTexture(_color);
    RenderTexture previous = context.GetRenderTexture(_color, 1);
    bool usable = context.IsHistoryValid(_color);
}
```

History outputs default to load/store so the previous contents survive. `IsHistoryValid` is false on the first frame and after a resize.

### Shared resource declared on the pipeline

```csharp
protected override void InitializePasses()
{
    DeclareTexture("GBuffer", GraphTextureDesc.ViewSized(true, 1f, PixelFormat.R8_G8_B8_A8_UNorm, PixelFormat.R16_G16_B16_A16_Float));
    AddPass(_geometry);
    AddPass(_lighting);
    SetPresentPass(_present);
}
```

Passes still declare reads and writes of "GBuffer" to be ordered, but the description comes from the pipeline.

### Importing an external target

```csharp
public override void Setup(RenderContextBuilder builder)
{
    _target = builder.ImportTexture("External", _renderTexture);
}
```

Imports default to load/store, have no history, and are never disposed by the graph.

### Offscreen-only views

If the present pass returns without calling `context.Present()`, nothing is presented for that view. Useful for render-to-texture views in the same dispatch as a windowed one.

### Multiple views

`DispatchGraph(pipeline, views)` runs every view through the same solved graph in one execution. Views need distinct `ViewId`s when passes use history.

## Pitfalls

- Handles resolve only in `Render` and `Present`; `Setup` runs without a view.
- The first declaration of an ID decides size and format.
- A pass that reads an ID nobody writes and nobody declared fails at graph build with a message naming both.
- A cycle between passes throws at build time.
- `DispatchGraph` twice per frame advances history twice.
- The context begins and ends rented command buffers.
- `ViewId` must be stable for history to persist; a changed or reused `ViewId` gets a fresh ring.
- Mutating a pass list after `InitializePasses` has no effect; `InvalidateGraph` rebuilds the graph on next use.
- `SwapchainTarget` is null unless the present pass called `RequestSwapchain()` in `Setup`.

## See also

- [internals/03-render-graph.md](../internals/03-render-graph.md) - how the graph is solved and how resources live
- [command-buffers.md](command-buffers.md) - what to record in `Render`
- [property-sets.md](property-sets.md) - binding textures and uniforms
- [graphics-device.md](graphics-device.md) - `DispatchGraph`, `ExecutionTask`, `Swapchain`
- [buffers-and-textures.md](buffers-and-textures.md) - `RenderTexture` and `PixelFormat`
- [getting-started.md](getting-started.md)
