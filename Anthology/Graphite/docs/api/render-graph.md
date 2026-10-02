# Render Graph API

Reference for `RenderPipeline<TView>`, passes, builders, `RenderContext<TView>`, handles and resource descriptions.

- [Overview](#overview)
- [Quick example](#quick-example)
- [Views](#views)
- [RenderPipeline](#renderpipelinetview)
- [Passes](#passes)
- [Builders](#builders)
- [Usage kinds and barriers](#usage-kinds-and-barriers)
- [RenderContext](#rendercontexttview)
- [Resource descriptions](#resource-descriptions)
- [Complete example](#complete-example-offscreen-pass-and-backbuffer-pass)
- [Common patterns](#common-patterns)
- [Pitfalls](#pitfalls)
- [See also](#see-also)

## Overview

You write passes. Each pass declares in `Setup` which named resources it reads and writes and how it uses them, and records GPU work in `Render`. A `RenderPipeline<TView>` collects the passes. `GraphicsDevice.DispatchGraph(pipeline, views)` orders the passes from their declarations, moves every resource into the state its pass declared, runs the passes for every view, then presents if a pass wrote the backbuffer. All types live in namespace `Prowl.Graphite.RenderGraph` except `GraphicsDevice`, `CommandBuffer` and friends in `Prowl.Graphite`.

Resource names are `RenderResourceID`, an interned string. A `string` converts implicitly, so `"Scene"` can be passed anywhere an ID is expected.

## Quick example

The smallest pipeline is one pass drawing straight into the default backbuffer ([HelloTriangle](../../Samples/HelloTriangle/Program.cs#L24)):

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

    public override void Setup(RenderContextBuilder builder) => SetBackbufferTarget(builder, TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)));

    public override void Render(RenderContext<SceneView> context)
    {
        CommandBuffer cmd = context.GetCommandBuffer("Triangle");
        BindTarget(context, cmd);
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_triangle);
        cmd.DrawIndexed();
        context.SubmitCommandBuffer(cmd);
    }
}

internal sealed class TrianglePipeline : RenderPipeline<SceneView>
{
    private readonly TrianglePass _pass;

    public TrianglePipeline(TrianglePass pass) => _pass = pass;

    protected override void InitializePasses() => AddPass(_pass);
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

Pass a pass list to the constructor, or subclass it, override `InitializePasses` and add passes. Source: [RenderPipeline.cs](../../Graphite/Core/RenderGraph/RenderPipeline.cs#L10).

| Member | Signature | Description |
|--------|-----------|-------------|
| Constructor | `RenderPipeline(IEnumerable<IPass<TView>> passes)` | Composes a fixed pass list with no subclass. Passes are re-added after `InvalidateGraph`. |
| `InitializePasses` | `protected virtual void InitializePasses()` | Runs once, lazily, on first use of `Graph`, after composed passes are added. Add passes here. |
| `AddPass` | `protected void AddPass(IPass<TView> pass)` | Adds a pass. Insertion order only breaks ties between independent passes. |
| `DeclareTexture` | `protected void DeclareTexture(RenderResourceID id, GraphTextureDesc desc)` | Declares a transient texture with no owning pass. Takes priority over pass declarations of the same ID. |
| `DeclareBuffer` | `protected void DeclareBuffer(RenderResourceID id, GraphBufferDesc desc)` | Same, for a buffer. |
| `Graph` | `RenderGraph<TView> Graph { get; }` | The solved graph, built on first access. |
| `InvalidateGraph` | `protected void InvalidateGraph()` | Disposes the graph and reruns `InitializePasses` on next use. Throws during execution. |
| `ExecuteView` | `void ExecuteView(RenderContext<TView> context)` | Runs the passes for one view. Called by `DispatchGraph`; you rarely call it. |
| `Dispose` | `virtual void Dispose()` | Disposes passes that implement `IDisposable`, then the graph. |

### GraphicsDevice.DispatchGraph

```csharp
public ExecutionTask DispatchGraph<T>(RenderPipeline<T> pipeline, IReadOnlyList<T> views) where T : IRenderView
```

Begins one execution, renders every view in order, completes the execution, and calls `SwapBuffers` if any pass wrote the backbuffer. Returns the `ExecutionTask` (see [graphics-device.md](graphics-device.md)). Non-blocking; GPU completion is signaled by the task's fence.

### RenderGraph<TView>

Read-only result of solving. Mostly useful for tooling and tests.

| Member | Signature | Description |
|--------|-----------|-------------|
| `OrderedPasses` | `IReadOnlyList<PassNode> OrderedPasses` | Passes in execution order |
| `Resources` | `IReadOnlyDictionary<RenderResourceID, GraphResource> Resources` | Every declared resource, first declaration wins |
| `WritesBackbuffer` | `bool WritesBackbuffer { get; }` | Whether any pass declared the backbuffer, so views present |
| `Build` | `static RenderGraph<TView> Build(IReadOnlyList<IPass<TView>>, IReadOnlyList<GraphResource>? central = null)` | Builds a graph by hand. Throws on a missing producer or a cycle. |

`PassNode` exposes `Pass`, `Inputs` and `Outputs`. `Build` also throws when a pass declares a texture ID as a buffer or the reverse, and when an imported texture is declared `Storage` without having `TextureUsage.Storage`.

## Passes

### IPass<TView>

| Member | Signature | Description |
|--------|-----------|-------------|
| `Name` | `string Name { get; }` | Debug and profiler name |
| `Setup` | `void Setup(RenderContextBuilder builder)` | Declare reads and writes. Runs once when the graph is built, with no view. |
| `Render` | `void Render(RenderContext<TView> context)` | Record work. Runs every view, every dispatch. |

### RasterPass<TView>

Abstract helper for passes that render into one declared target. Source: [RasterPass.cs](../../Graphite/Core/RenderGraph/RasterPass.cs#L12). It implements `IPass` and adds target management; it does not rent or submit command buffers.

| Member | Signature | Description |
|--------|-----------|-------------|
| `SetTarget` | `protected TextureHandle SetTarget(RenderContextBuilder builder, RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null)` | Declares the single output as an `Attachment` and remembers it |
| `SetBackbufferTarget` | `protected TextureHandle SetBackbufferTarget(RenderContextBuilder builder, TargetLoadStoreOps? ops = null)` | Declares the default backbuffer as the single output |
| `SetTargets` | `protected TextureHandle SetTargets(..., GraphTextureDesc mrtDesc, ...)` | Same, for a desc with several color formats (MRT) |
| `BindTarget` | `protected void BindTarget(RenderContext<TView> context, CommandBuffer cmd)` | Sets the framebuffer and applies load ops using the clear values on the declaration |

`BindTarget` clears only if the resource's ops say `Load == Clear`. With default ops that is true for transient targets and false for history targets. It throws if `Setup` never declared a target.

## Builders

### RenderContextBuilder

Passed to `IPass.Setup`. Every call records a read or write. Source: [RenderContextBuilder.cs](../../Graphite/Core/RenderGraph/RenderContextBuilder.cs).

| Member | Signature | Description |
|--------|-----------|-------------|
| `DeclareInputTexture` | `TextureHandle DeclareInputTexture(RenderResourceID id, TextureUsageKind usage = Sampled, TextureUsageKind? initial = null, TextureUsageKind? depthUsage = null)` | Declares a texture this pass reads. The producer owns the description. `usage` combines `Sampled`, `Storage` and `TransferSrc`. |
| `DeclareOutputTexture` | `TextureHandle DeclareOutputTexture(RenderResourceID id, GraphTextureDesc desc, int history = 0, TargetLoadStoreOps? ops = null, TextureUsageKind usage = Attachment, TextureUsageKind? initial = null, TextureUsageKind? depthUsage = null)` | Declares a texture this pass writes. `history > 0` keeps that many prior executions readable. `usage` must include `Attachment`, `Storage` or `TransferDst`. |
| `DeclareImportedTexture` | `TextureHandle DeclareImportedTexture(RenderResourceID id, RenderTexture existing, TextureUsageKind usage = Attachment, TextureUsageKind? initial = null, TextureUsageKind? depthUsage = null)` | Registers an externally owned render texture as an output. The graph never disposes it. |
| `DeclareBackbuffer` | `TextureHandle DeclareBackbuffer(TargetLoadStoreOps? ops = null, TextureUsageKind usage = Attachment, TextureUsageKind? initial = null)` | Declares a write to the device's main swapchain image. Clears by default; pass `Loaded` ops to draw over an earlier backbuffer pass. `usage` is `Attachment`, `TransferDst` or both. |
| `DeclareInputBuffer` | `BufferHandle DeclareInputBuffer(RenderResourceID id, BufferUsageKind usage = AnyRead)` | Declares a buffer this pass reads. `usage` may combine read kinds only. |
| `DeclareOutputBuffer` | `BufferHandle DeclareOutputBuffer(RenderResourceID id, GraphBufferDesc desc, int history = 0, BufferUsageKind usage = Storage)` | Declares a buffer this pass writes. `usage` must include `Storage` or `TransferDst`. |

A usage kind that does not fit the declaration (for example `Attachment` on an input) throws `ArgumentException` from the builder.

### The backbuffer

The backbuffer is a graph texture under a reserved ID, resolved with `GetRenderTexture` like any other. Its `RenderTexture.Framebuffer` is the swapchain framebuffer for the current image, and its color and depth textures are the swapchain's. It can only be written: declaring it as an input, or with `Sampled`, `Storage` or `TransferSrc`, throws. If any pass in a graph declares it, every view of that graph presents after the dispatch; if none does, the graph stays offscreen. Passes that read other graph textures and write the backbuffer run after the writers of those textures. Several passes may write it; independent ones run in insertion order, and every pass after the first should declare `Loaded` ops so it does not clear the earlier result. Resolving it on a device created without a main swapchain throws `InvalidOperationException`.

## Usage kinds and barriers

Every declaration names how the pass uses the resource. Before a pass renders, the graph records the barriers that move each declared resource into its start state at the end of the command buffer submitted last, so they run after earlier passes and before anything this pass submits. Inside the pass, `context.Transition` switches a texture between the kinds it declared. After the last pass of a view the graph returns every texture it touched, including the backbuffer, to its resting layout. You never write a barrier.

When nothing has been submitted yet in the execution (the first pass), or right after a transfer flush, the barriers go at the start of the first command buffer the pass rents instead. In that pass, submit the first rented command buffer before the others; submitting another one first throws `InvalidOperationException`.

### TextureUsageKind

A flags enum. A declaration may combine kinds; the pass then starts in `initial` and moves between them with `Transition`.

| Kind | Input | Output | Color | Depth | Meaning |
|------|-------|--------|-------|-------|---------|
| `Sampled` | yes | yes | yes | yes | Read through a sampled texture binding |
| `Storage` | yes | yes | yes | no | Read and written through a storage binding |
| `Attachment` | no | yes | yes | yes | Rendered into as a framebuffer attachment |
| `TransferSrc` | yes | yes | yes | yes | Source of a copy or resolve |
| `TransferDst` | no | yes | yes | yes | Destination of a copy or resolve |
| `DepthReadOnly` | yes | yes | no | yes | Depth attachment that is tested but not written, and sampleable at the same time |

- An output must include at least one write kind (`Attachment`, `Storage`, `TransferDst`). An input may only use read kinds.
- `initial` is required when `usage` has more than one kind, and must be one of them. With a single kind it defaults to that kind.
- `depthUsage` sets the state of the bundle's depth texture for the whole pass. Left null, depth follows the color kind (and is left alone for `Storage`), including through `Transition`. An explicit `depthUsage` stays fixed; `Transition` only moves color.

A graph texture declared `Storage` by any pass is created with `TextureUsage.Storage` on its color textures. When one pass declares the same ID as input and output, the output declaration applies; history slots (`framesAgo > 0`) are always in their resting layout and can be sampled.

### Transition

```csharp
public void Transition(CommandBuffer cmd, TextureHandle handle, TextureUsageKind usage)
public void Transition(TextureHandle handle, TextureUsageKind usage)
```

Records the barrier that moves the current texture of `handle` to `usage` into `cmd` and updates the graph's state for it. The overload without `cmd` records into the only command buffer the pass has rented and not submitted; it throws `InvalidOperationException` when the pass holds none or several, and the explicit overload is needed then. `usage` must be a single kind the running pass declared for that ID, and `cmd` must be a command buffer the pass rented and has not submitted. Any open render pass on `cmd` ends, and clears queued on its framebuffer are applied first. Where the texture ends up is where the next pass starts from.

Because the state follows recording order, a pass that calls `Transition` must submit its command buffers in the order it rented them; submitting out of order throws `InvalidOperationException`.

```csharp
public override void Setup(RenderContextBuilder builder)
{
    const TextureUsageKind both = TextureUsageKind.Attachment | TextureUsageKind.Sampled;
    _a = builder.DeclareOutputTexture("BlurA", desc, usage: both, initial: TextureUsageKind.Sampled);
    _b = builder.DeclareOutputTexture("BlurB", desc, usage: both, initial: TextureUsageKind.Attachment);
}

public override void Render(RenderContext<SceneView> context)
{
    TextureHandle source = _a;
    TextureHandle target = _b;
    CommandBuffer cmd = context.GetCommandBuffer(Name);
    for (int i = 0; i < _iterations; i++)
    {
        Blur(cmd, context.GetRenderTexture(source), context.GetRenderTexture(target));
        context.Transition(source, TextureUsageKind.Attachment);
        context.Transition(target, TextureUsageKind.Sampled);
        (source, target) = (target, source);
    }
    context.SubmitCommandBuffer(cmd);
}
```

### BufferUsageKind

Flags; combine them when a pass uses a buffer several ways. Read kinds: `ShaderRead`, `Uniform`, `Vertex`, `Index`, `Indirect`, `TransferSrc` (`AnyRead` is all of them). Write kinds: `Storage` (read and write), `TransferDst`. The graph emits one memory barrier per pass boundary that covers every buffer write since the previous reads.

### Resting layouts

Outside a graph pass every texture is in a layout computed from its `TextureUsage`: shader read-only if `Sampled`, general if `Storage`, otherwise its attachment layout (present for swapchain images). Textures created through the `ResourceFactory` start there, and uploads, transfer command buffers and mip generation return them there. Non-graph textures can be bound in any pass without a declaration:

- Sampling a non-graph texture needs nothing.
- A framebuffer made only of non-graph textures renders in resting mode: the render pass moves its attachments out of and back into their resting layouts.
- A non-graph `Storage | Sampled` texture bound read-write is moved to general layout for one compute dispatch. Binding it read-write in a draw throws.

### Rules

- Resolving a resource with `GetRenderTexture` or `GetRenderBuffer` that the running pass did not declare throws `InvalidOperationException`.
- A graph texture's current state decides how it can be bound. A framebuffer needs color in `Attachment` and depth in `Attachment` or `DepthReadOnly`; sampling needs `Sampled` (or `DepthReadOnly` for depth); a read-write binding needs `Storage`. A mismatch throws `RenderException`. Use `Transition` to change state inside a pass.
- A framebuffer cannot mix graph attachments with non-graph textures.
- With depth in `DepthReadOnly`, clearing depth throws, and the bound program must not write depth.
- Copies, resolves and mip generation work in any state: the command moves the texture to a transfer layout and back. Declaring `TransferSrc` / `TransferDst` avoids the extra transitions.

## RenderContext<TView>

The per-view object handed to `Render`. A new one is created for every view of every dispatch. Source: [RenderContext.cs](../../Graphite/Core/RenderGraph/RenderContext.cs#L9).

| Member | Signature | Description |
|--------|-----------|-------------|
| `View` | `TView View { get; }` | The view being rendered |
| `Task` | `ExecutionTask Task { get; }` | The execution everything records into |
| `GetCommandBuffer` | `CommandBuffer GetCommandBuffer(string name = "")` | Rents a command buffer that is already begun |
| `SubmitCommandBuffer` | `void SubmitCommandBuffer(CommandBuffer cmd)` | Queues it for this execution. The graph ends it later; do not record into it after submitting |
| `GetTransferCommandBuffer` | `TransferCommandBuffer GetTransferCommandBuffer(string name = "")` | Creates a transfer command buffer (not yet begun) |
| `SubmitTransferCommandBuffer` | `void SubmitTransferCommandBuffer(TransferCommandBuffer cmd)` | Flushes pending submissions then submits the transfer without blocking |
| `GetRenderTexture` | `RenderTexture GetRenderTexture(TextureHandle handle)` | Resolves a handle to the physical target for this view. Throws if the running pass did not declare it. |
| `Transition` | `void Transition(CommandBuffer cmd, TextureHandle handle, TextureUsageKind usage)` / `(TextureHandle handle, TextureUsageKind usage)` | Moves a declared texture to another of its declared kinds mid-pass. The second form uses the pass's only open command buffer. See [Transition](#transition). |
| `GetRenderTexture` | `RenderTexture GetRenderTexture(TextureHandle handle, int framesAgo)` | Resolves by age; 0 is current, up to the declared history depth |
| `GetRenderBuffer` | `DeviceBuffer GetRenderBuffer(BufferHandle handle)` / `(handle, int framesAgo)` | Same for buffers |
| `IsHistoryValid` | `bool IsHistoryValid(TextureHandle)` / `(BufferHandle)` | True once the view's ring holds an earlier execution |
| `AllocateTransient` | `DeviceBufferRange AllocateTransient(uint sizeInBytes)` | Bump-allocated uniform range, valid until the execution's fence signals |
| `Profiler` | `IProfiler? Profiler { get; }` | The device profiler, or null |
| `WantsMetadata` | `bool WantsMetadata { get; }` | True if the profiler wants metadata |
| `RecordPassMetadata` | `void RecordPassMetadata(object metadata)` | Attaches metadata to the running pass |

Command buffer lifecycle: `GetCommandBuffer` begins it and `SubmitCommandBuffer` queues it; passes never call `Begin` or `End`. The last submitted buffer stays open so the graph can append the next pass's barriers to it, and is ended when another buffer is submitted, a transfer is submitted, or the execution completes. One rented but never submitted triggers `GraphicsDevice.OnWarning` and is discarded. See [command-buffers.md](command-buffers.md).

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
| `ViewSized` | `static GraphTextureDesc ViewSized(bool depth = false, float scale = 1f, params PixelFormat[] formats)` | Size is `scale` times the view; empty `formats` means `R8_G8_B8_A8_UNorm`. No depth attachment unless `depth` is true |
| `Sized` | `static GraphTextureDesc Sized(int width, int height, bool depth = false, params PixelFormat[] formats)` | Fixed size. No depth attachment unless `depth` is true |
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
| `AttachmentOps` | `Load`, `Store`, `ClearColor`, `ClearDepth`, `ClearStencil`; statics `Cleared`, `Loaded`, `Discard`, `Clear(Color)`, `Clear(float depth, byte stencil)` |
| `TargetLoadStoreOps` | `Color`, `Depth`; ctor `(AttachmentOps color, AttachmentOps depth)`; `static ForLifetime(bool persistent)`; `static Clear(Color color, float depth = 1f, byte stencil = 0)` |

Defaults: a `history = 0` output uses `ForLifetime(false)` (clear, store). A history output or an imported texture uses `ForLifetime(true)` (load, store). Pass `ops` to `DeclareOutputTexture` or `SetTarget` to override per declaring pass.

### Handles and IDs

`TextureHandle` and `BufferHandle` are readonly structs with `Id` and `IsValid`. You obtain them only from builders. `RenderResourceID` has `Intern(string)`, `ToString(id)` (null if never interned), `IsValid`, and an implicit conversion from `string`.

## Complete example: offscreen pass and backbuffer pass

Adapted from [PBRRenderer](../../Samples/PBRRenderer/Program.cs#L32), with the bloom passes removed. `Mesh` is the sample helper in `Samples/Shared` and implements `IVertexSource`. `ScenePass` draws a mesh into a texture named "Scene". `BlitPass` reads "Scene" and draws it to the backbuffer.

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
        => SetTarget(builder, "Scene", GraphTextureDesc.ViewSized(depth: true), ops: TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)));

    public override void Render(RenderContext<SceneView> context)
    {
        Float3 eye = new Float3(MathF.Sin(_angle), 0.35f, MathF.Cos(_angle)) * 3f;
        Float4x4 projection = Float4x4.CreatePerspectiveFov(1.0472f, 1.0f, 0.05f, 100f);
        Float4x4 view = Float4x4.CreateLookAt(eye, Float3.Zero, Float3.UnitY);
        _properties.SetMatrix("MatrixMVP", projection * view);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        BindTarget(context, cmd);
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_mesh);
        cmd.SetProperties(_properties);
        cmd.DrawIndexed();
        context.SubmitCommandBuffer(cmd);
    }
}

internal sealed class BlitPass : RasterPass<SceneView>
{
    private readonly GraphicsProgram _blitShader;
    private readonly Sampler _sampler;
    private readonly PropertySet _properties = new();
    private TextureHandle _sceneHandle;

    public BlitPass(GraphicsProgram blitShader, Sampler sampler)
    {
        _blitShader = blitShader;
        _sampler = sampler;
    }

    public override string Name => "Blit";

    public override void Setup(RenderContextBuilder builder)
    {
        _sceneHandle = builder.DeclareInputTexture("Scene");
        SetBackbufferTarget(builder);
    }

    public override void Render(RenderContext<SceneView> context)
    {
        RenderTexture scene = context.GetRenderTexture(_sceneHandle);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        BindTarget(context, cmd);
        _properties.SetTexture("sceneTexture", scene.ColorTextures[0], _sampler);
        cmd.SetShader(_blitShader);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
        context.SubmitCommandBuffer(cmd);
    }
}

internal sealed class ScenePipeline : RenderPipeline<SceneView>
{
    private readonly ScenePass _scene;
    private readonly BlitPass _blit;

    public ScenePipeline(ScenePass scene, BlitPass blit)
    {
        _scene = scene;
        _blit = blit;
    }

    public ScenePass Scene => _scene;

    protected override void InitializePasses()
    {
        AddPass(_scene);
        AddPass(_blit);
    }
}
```

Per frame:

```csharp
pipeline.Scene.Advance((float)dt);
device.DispatchGraph(pipeline, views);
```

What happens: at first dispatch, `Setup` runs on `ScenePass` (declares write "Scene" as an `Attachment`) and on `BlitPass` (declares read "Scene" as `Sampled` and a write to the backbuffer). The graph validates that "Scene" has a producer and orders `ScenePass` first. Before `ScenePass` renders, the graph rents a view-sized transient texture for "Scene" and moves it to attachment layout; before `BlitPass` it moves "Scene" to shader read-only and the swapchain image to attachment layout; after the view it returns both to their resting layouts. `BlitPass`'s `GetRenderTexture` returns the same "Scene" texture. Because `BlitPass` wrote the backbuffer, the dispatch presents. The scene texture returns to the pool when the execution's fence signals.

With more passes, the sample adds `BloomDownsample` (reads "Scene", writes "BloomHalf" at 0.5 scale) and `BloomUpsample` (reads "BloomHalf", writes "BloomFull"); the composite pass reads both "Scene" and "BloomFull" and writes the backbuffer. None of the `AddPass` calls need to be in dependency order.

## Common patterns

### Temporal history

```csharp
public override void Setup(RenderContextBuilder builder)
{
    _color = builder.DeclareOutputTexture("TaaColor", GraphTextureDesc.ViewSized(false, 1f, PixelFormat.R16_G16_B16_A16_Float), history: 1);
}

public override void Render(RenderContext<SceneView> context)
{
    RenderTexture current = context.GetRenderTexture(_color);
    RenderTexture previous = context.GetRenderTexture(_color, 1);
    bool usable = context.IsHistoryValid(_color);
}
```

History outputs default to load/store so the previous contents survive. `IsHistoryValid` is false on the first frame and after a resize. The previous copy is always in its resting layout, so it can be sampled while the current copy is the pass's attachment.

### Sampling depth while rendering color

```csharp
public override void Setup(RenderContextBuilder builder)
    => _scene = builder.DeclareOutputTexture("Scene", desc,
        ops: new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded),
        depthUsage: TextureUsageKind.DepthReadOnly);
```

The pass binds the "Scene" framebuffer and samples `scene.DepthTexture` in the same draw, as volumetric fog or soft particles do. Its program must have depth writes disabled.

### Compute writes, fragment reads

```csharp
public override void Setup(RenderContextBuilder builder)
    => _field = builder.DeclareOutputTexture("Field", GraphTextureDesc.Sized(256, 256, false, PixelFormat.R32_G32_B32_A32_Float), usage: TextureUsageKind.Storage);
```

A later pass declares `builder.DeclareInputTexture("Field")` and samples it. The graph adds the storage-write to shader-read barrier between the two passes.

### Shared resource declared on the pipeline

```csharp
protected override void InitializePasses()
{
    DeclareTexture("GBuffer", GraphTextureDesc.ViewSized(true, 1f, PixelFormat.R8_G8_B8_A8_UNorm, PixelFormat.R16_G16_B16_A16_Float));
    AddPass(_geometry);
    AddPass(_lighting);
}
```

Passes still declare reads and writes of "GBuffer" to be ordered, but the description comes from the pipeline.

### Importing an external target

```csharp
public override void Setup(RenderContextBuilder builder)
{
    _target = builder.DeclareImportedTexture("External", _renderTexture);
}
```

Imports default to load/store, have no history, and are never disposed by the graph. The texture must be in its resting layout when the dispatch starts; the graph leaves it there when each view ends.

### Offscreen-only pipelines

A pipeline whose passes never declare the backbuffer never presents. Use one for render-to-texture work, and dispatch it separately from the windowed pipeline.

### Multiple views

`DispatchGraph(pipeline, views)` runs every view through the same solved graph in one execution. Views need distinct `ViewId`s when passes use history.

## Pitfalls

- Handles resolve only in `Render`; `Setup` runs without a view.
- The first declaration of an ID decides size and format.
- A pass that reads an ID nobody writes and nobody declared fails at graph build with a message naming both.
- A cycle between passes throws at build time.
- `DispatchGraph` twice per frame advances history twice.
- The context begins and ends rented command buffers.
- `ViewId` must be stable for history to persist; a changed or reused `ViewId` gets a fresh ring.
- Mutating a pass list after `InitializePasses` has no effect; `InvalidateGraph` rebuilds the graph on next use.
- The backbuffer is write-only and its default ops clear. A second pass writing it must declare `Loaded` ops or it erases the first.
- A texture cannot be sampled while it is the bound attachment. Declare both kinds and `Transition` between draws, or split the work into two passes.
- Ping-pong between two IDs across separate passes is a dependency cycle; use `Transition` inside one pass or give each iteration its own ID.
- Every graph resource a pass touches must be declared by that pass, including the history resource it reads with `framesAgo > 0`.
- Scratch textures are declared as outputs like any other graph texture; there is no undeclared scratch rental on the context.
- Pass barriers are appended to the previous pass's last submitted command buffer, so profilers attribute their (small) cost to it. A separate `"<pass> Barriers"` command buffer appears only when a pass with pending barriers rents no command buffer or never submits its first one.
- In the first pass of an execution, submit the first rented command buffer before the others.

## See also

- [internals/03-render-graph.md](../internals/03-render-graph.md) - how the graph is solved and how resources live
- [command-buffers.md](command-buffers.md) - what to record in `Render`
- [property-sets.md](property-sets.md) - binding textures and uniforms
- [graphics-device.md](graphics-device.md) - `DispatchGraph`, `ExecutionTask`, `Swapchain`
- [buffers-and-textures.md](buffers-and-textures.md) - `RenderTexture` and `PixelFormat`
- [getting-started.md](getting-started.md)
