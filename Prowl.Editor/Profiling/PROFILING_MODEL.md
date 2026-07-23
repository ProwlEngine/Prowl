# Profiler data model

What this system tracks, at what depth, and why. Read this alongside `ProfiledFrame.cs` (the data
tree), `Models.cs` (leaf/value types), and the four collectors (`TimingCollector`,
`CountersCollector`, `PassGraphCollector`, `DrawHierarchyCollector`) that write into it.

This doc covers two things: how to drive `EditorProfiler` from code (init, pause, capture,
read back), and the shape of the data you get back. If you only want the API, read
[Quickstart](#quickstart) and [API reference](#api-reference). If you want to know exactly what's
recorded and when, skip to [The two-tier model](#the-two-tier-model) onward.

## Quickstart

`EditorProfiler` is a single object you create once, attach to a `GraphicsDevice`, and call
`BeginFrame`/`EndFrame` around each frame. Everything else - views, passes, counters, timing - is
recorded automatically by Graphite calling back into it through `IProfiler`; you never call those
methods yourself.

### Initialize

```csharp
var profiler = new EditorProfiler();
profiler.Attach(device); // calls device.SetProfiler(this)
```

`Attach` must happen at a frame boundary (same rule as `GraphicsDevice.SetProfiler`) - never
mid-pass or mid-submit.

### Drive it every frame

```csharp
profiler.BeginFrame();

profiler.BeginView("Scene");
// ... render graph dispatch happens here; Graphite calls BeginPass/RecordDraw/etc.
//     on the profiler as it executes ...
profiler.EndView();

profiler.EndFrame();
```

`BeginFrame`/`EndFrame` are the only two calls you must bracket every frame with. `BeginView`/
`EndView` are optional per-view scopes (call once per named view you want CPU timing and object
counts attributed to, e.g. `"Game"`, `"Scene"`). Everything below view level (`BeginPass`,
`RecordDraw`, `RecordPipelineSwitch`, ...) is Graphite calling into the profiler through
`IProfiler` as it walks the render graph - you don't call those.

### Stop / pause

```csharp
profiler.Pause();  // BeginFrame/EndFrame/BeginView/EndView and all IProfiler.* calls become no-ops
// ... later ...
profiler.Resume();
```

There is no separate "stop" - `Pause()` freezes recording in place (the ring keeps whatever it last
held; `Latest`/`History` still read the last-sealed frames) and `Resume()` picks back up. To
fully tear down, `Detach()` un-registers from the device:

```csharp
profiler.Detach(); // calls device.SetProfiler(null)
```

### Requesting a capture and getting a Snapshot

A "capture" arms the *next* frame to record the deep, per-draw-call tier (see
[The two-tier model](#the-two-tier-model)) and produce a `Snapshot` with real GPU resource bytes.
Wire the handlers once, then arm whenever you want one:

```csharp
profiler.CaptureHandler = profiler.SnapshotCapturer.HandleCapture;
profiler.CaptureFinalizeHandler = profiler.SnapshotCapturer.Finalize;
profiler.SnapshotCaptured += snapshot =>
{
    // snapshot.Frame has HasCaptureDepth == true; snapshot.Resources has readback bytes
    SnapshotSerializer.Save(snapshot, path);
};

// somewhere on user action ("Capture Frame" button, a hotkey, ...):
profiler.RequestCaptureNextFrame();
```

The snapshot doesn't arrive synchronously - it shows up on `SnapshotCaptured` at the end of the
*armed* frame's `EndFrame()`, once the frame's GPU work has been submitted and read back. Don't
assume `RequestCaptureNextFrame()` followed by `Latest` on the same call stack gives you capture
data; wait for the event (or check `profiler.Latest?.HasCaptureDepth` on a later frame).

### Reading back data

```csharp
ProfiledFrame? latest = profiler.Latest;
ProfiledFrame? tenFramesAgo = profiler.FrameAgo(10);
IReadOnlyList<ProfiledFrame> allHistory = profiler.History; // up to 240 frames, oldest first

double frameMs = latest?.FrameMilliseconds ?? 0.0;
foreach (ProfiledView view in latest?.Views ?? Array.Empty<ProfiledView>())
{
    Console.WriteLine($"{view.Name}: cpu={view.CpuMilliseconds}ms gpu={view.GpuMilliseconds}ms " +
                       $"objects={view.RenderedObjects}/{view.TotalObjects} draws={view.DrawCallCount}");
}

// A single named counter's value across the whole retained history:
IReadOnlyList<double> liveMeshes = profiler.CounterHistory("Live/Mesh");
IReadOnlyList<string> allCounterNames = profiler.CounterNames;
```

### Turning on GPU execution timing

GPU timing is off by default (Graphite still records `RecordExecutionTime` callbacks internally,
but `IProfiler.RequestExecutionTiming` gates whether it's worth Graphite's while to report them).
Flip it on/off any time:

```csharp
profiler.RequestExecutionTiming = true;
```

When on, `ProfiledFrame.GpuRoot`, `ProfiledPass.GpuMilliseconds`, and
`ProfiledCommandBuffer.GpuMilliseconds` start getting populated (with the usual multi-frame lag -
see [GPU timing always lags](#gpu-timing-always-lags)). When off, `GpuRoot` stays `null` and all
GPU millisecond fields read `0`.

## API reference

Everything a caller (editor UI, tooling) is meant to touch on `EditorProfiler`. Anything not
listed here (the explicit `IProfiler.*` implementations) is Graphite's side of the contract, not
yours to call.

| Member | What it does |
|---|---|
| `Attach(GraphicsDevice)` / `Detach()` | Registers/unregisters as the device's `IProfiler` |
| `BeginFrame()` / `EndFrame()` | Frame boundary - required every frame |
| `BeginView(string)` / `EndView()` | Optional named-view scope for CPU timing + object counts |
| `Pause()` / `Resume()` / `IsPaused` | Freeze/thaw recording; all calls no-op while paused |
| `RequestCaptureNextFrame()` | Arms the next frame for deep capture + `Snapshot` production |
| `RequestExecutionTiming` (get/set) | Toggles GPU execution-time reporting |
| `CaptureHandler` (set) | Delegate invoked mid-frame per pass with texture outputs while armed - normally `SnapshotCapturer.HandleCapture` |
| `CaptureFinalizeHandler` (set) | Delegate invoked once at end of an armed frame to assemble the `Snapshot` - normally `SnapshotCapturer.Finalize` |
| `SnapshotCaptured` (event) | Fires with the finished `Snapshot` once `CaptureFinalizeHandler` returns non-null |
| `Latest` | `ProfiledFrame?` for the most recently sealed frame |
| `FrameAgo(int n)` | `ProfiledFrame?` for `n` frames before latest (`FrameAgo(0) == Latest`) |
| `History` | All currently-retained frames, oldest to newest (up to 240) |
| `CounterNames` | Every counter name in the fixed registry, in registry order |
| `CounterHistory(string name)` | That counter's value across all retained history, one entry per frame |
| `DrawHierarchy` | The `DrawHierarchyCollector` instance - exposed for `SnapshotCapturer` wiring, not typically read directly |
| `SnapshotCapturer` | The `SnapshotCapturer` instance - source of the two capture delegates above |

## The two-tier model

Every frame, unconditionally, the profiler records a mid-depth tree: `ProfiledFrame` ->
`ProfiledView` -> `ProfiledPass` -> `ProfiledCommandBuffer`. This is cheap (bounded by pass/view/
command-buffer counts, not draw counts) and always on - it's what `EditorProfiler.History`,
`Latest`, and `FrameAgo(n)` return.

One level deeper - `ProfiledPipelineSwitch` -> `ProfiledCallingObject` -> `ProfiledDrawCall` - only
gets built on a frame where a **capture was armed** (`EditorProfiler.RequestCaptureNextFrame()`).
This is the expensive part: potentially thousands of nodes, one chain per draw call. It is gated by
`ProfiledFrame.HasCaptureDepth`, which callers can check before assuming that layer is populated.

An armed frame also gets one extra thing no other frame gets: an independent `Clone()` of itself
handed off to become a `Snapshot`, plus - only for a `Snapshot` - actual GPU resource **byte
readback** (`SnapshotResource`/`SnapshotResourceVersion`), which never exists on a live
`ProfiledFrame` at all, armed or not.

So there are really three tiers, not two:

| Tier | Built | Lifetime |
|---|---|---|
| Frame/View/Pass/CommandBuffer | every frame | lives in the 240-frame ring, reused/reset in place |
| PipelineSwitch/CallingObject/DrawCall | only when a capture is armed | lives in that one ring slot; **also** copied into the Snapshot clone |
| SnapshotResource (actual pixel/buffer bytes) | only for a captured frame, and only after `SnapshotCapturer.Finalize` runs | exists **only** inside the `Snapshot` object - never touches the live ring at all |

## Running / always-on data

Built by `TimingCollector`, `CountersCollector`, and `PassGraphCollector` every single frame,
whether or not a capture is armed. Cost is bounded by the shape of the render graph (views, passes,
command buffers), not by what's drawn.

### Frame level (`ProfiledFrame`)

| Field | Source | Notes |
|---|---|---|
| `FrameIndex` | `EditorProfiler.BeginFrame` | Monotonic, matches the engine's own frame counter |
| `FrameMilliseconds`, `Fps` | `EditorProfiler` wall clock (`Stopwatch`) around `BeginFrame`/`EndFrame` | CPU-side frame time, not GPU |
| `HasCaptureDepth` | `EditorProfiler.BeginFrame` | True only if a capture was armed for this frame |
| `CpuRoot` | `TimingCollector` | Root of the CPU flame tree - see [Timing](#timing-cpu-and-gpu) |
| `GpuRoot` | `TimingCollector` | Root of the GPU flame tree; null if no GPU timing arrived this frame |
| `Counters` | `CountersCollector` | Flat array of named counter values - see [Counters](#counters) |
| `Submits` | `CountersCollector` (`IProfiler.RecordSubmit`) | One `SubmitRecord` per submit call this frame: kind, name, command buffer count |

### View level (`ProfiledView`, keyed by view name e.g. `"Game"`/`"Scene"`)

| Field | Source | Notes |
|---|---|---|
| `CpuMilliseconds` | `TimingCollector` (`BeginView`/`EndView` scope) | Inclusive CPU time for the whole view |
| `GpuMilliseconds` | **derived** | Sum of this view's passes' `GpuMilliseconds` - not stored separately |
| `RegisteredObjects` | `DrawHierarchyCollector` (`Renderable` events) | Count of objects the culler registered this view |
| `CulledObjects` | `DrawHierarchyCollector` | Count of objects culled this view |
| `TotalObjects` | `DrawHierarchyCollector` | Count of every `Renderable` event this view (registered + not) |
| `RenderedObjects` | **derived** | `TotalObjects - CulledObjects` - not its own counter |
| `DrawCallCount` | `DrawHierarchyCollector` | Sum of `DrawCallCount` across all `Renderable` events |
| `Passes` | `PassGraphCollector` | Ordered list of passes touched this view this frame |
| `Edges` | `PassGraphCollector` | Producer -> consumer edges between passes, detected from resource read/write overlap |

Object counts are always-on regardless of capture: they come from the `Renderable` marker stream,
not from the deep draw-call tree.

### Pass level (`ProfiledPass`, keyed by render-graph pass index)

| Field | Source | Notes |
|---|---|---|
| `Index`, `Name` | Render graph `PassInfo` | Stable identity across frames (same pipeline structure) |
| `CpuMilliseconds` | `TimingCollector` (`BeginPass`/`EndPass` scope) | Inclusive CPU time for the pass |
| `CpuSamples` | `TimingCollector` | Nested `BeginSample`/`EndSample` scopes recorded inside this pass, as a `TimeSample` tree |
| `GpuMilliseconds` | **derived** | Sum of this pass's command buffers' `GpuMilliseconds` |
| `Inputs`, `Outputs` | `PassGraphCollector` (`RecordPassRead`) | `ResourceRef` list: numeric id, resolved name, `Texture`/`Buffer`/`Unknown` kind. `Resource` (a `SnapshotResourceID`) is only valid when a capture is armed - otherwise it's `SnapshotResourceID.Invalid` |
| `CommandBuffers` | `PassGraphCollector` (`OnCommandBufferSeen`) | Ordered list, identity-by-id **within this frame only** (see [Command buffer ids](#command-buffer-ids-are-not-stable-across-frames)) |

### CommandBuffer level (`ProfiledCommandBuffer`)

| Field | Source | Notes |
|---|---|---|
| `Id`, `Name` | Graphite `CommandBufferInfo` | Id is a rental counter, see below |
| `GpuMilliseconds` | `TimingCollector.GetCommandBufferGpuMs`, stamped by `PassGraphCollector.FinalizeFrame` | GPU execution time; see [GPU timing lag](#gpu-timing-always-lags) |
| `Switches` | `DrawHierarchyCollector` | **Empty unless a capture is armed** - this is the boundary into capture-only data |

### Counters

`CountersCollector` maintains a fixed registry (`CountersCollector.Registry`) of named counters,
always on, snapshotted into `ProfiledFrame.Counters` once per frame. Every counter has a
`CounterCategory` and `CounterUnit` (Count / Bytes / Milliseconds). Registry contents, by category:

- **EngineObject** (per `AllocBin`): `Live/{bin}` (count currently alive), `Resident/{bin}` (bytes currently resident)
- **AllocFree** (per `AllocBin`): `Alloc/{bin}`, `Free/{bin}` - counts of allocate/free calls *this frame*
- **BufferMemory** (per `BufferRoleBin`): `Resident/{role}` - resident bytes by buffer role (vertex/index/uniform/etc.)
- **BufferUpdate** (per `BufferOpBin`): `BufferOp/{op}` (count), `BufferOpBytes/{op}` (bytes) - map/unmap/update/copy calls this frame
- **Swapchain** (per `SwapBin`): `Swap/{bin}` - swapchain events this frame
- **Barrier** (per `BarrierBin`): `Barrier/{bin}` - resource barrier counts this frame
- **Submit** (per `SubmitKind`): `Submit/{kind}` - submit counts this frame
- **ResourceSet**: `ResourceSet/Binds` - resource set bind count this frame
- **DrawDispatch**: `Draw/Count`, `Dispatch/Count` - draw/dispatch call counts this frame

`Live`/`Resident` counters are gauges (persist frame to frame, incremented/decremented by
allocate/free); everything else is a per-frame delta, zeroed at `OnFrameBegin`.

`EditorProfiler.CounterHistory(name)` reads a single counter's value back across the whole 240-frame
ring - this works for *aged* frames too, since counters live at the frame level, untouched by
capture depth.

```csharp
// Plot resident vertex-buffer memory over the last 240 frames.
IReadOnlyList<double> residentBytes = profiler.CounterHistory("Resident/Vertex");

// Read one frame's full counter set, grouped by category.
foreach (CounterValue c in profiler.Latest!.Counters)
    Console.WriteLine($"[{c.Category}] {c.Name} = {c.Value} ({c.Unit})");
```

### Timing (CPU and GPU)

`TimingCollector` builds two independent flame-style trees, always on:

- **CPU tree** (`CpuRoot`): a `TimeSample` tree rooted at `"Frame"`, with `BeginView`/`EndView` and
  `BeginPass`/`EndPass` as automatic scopes, plus any manual `BeginSample`/`EndSample` calls nested
  inside. Wall-clock (`Stopwatch`), measured directly - not derived from anything else.
- **GPU tree** (`GpuRoot`): grouped by pass name (or `"Transfer"`), each leaf a `TimeSample` for one
  `RecordExecutionTime` callback. Only present if `RequestExecutionTiming` is on and Graphite
  actually reported execution times this frame (`GpuRoot` is `null` otherwise).

Manual CPU samples nest inside whatever view/pass scope is currently open, by calling straight
into the attached `IProfiler` (there's no separate "user" API - it's the same interface Graphite
itself calls):

```csharp
device.Profiler?.BeginSample("SkinningUpdate");
UpdateSkinning();
device.Profiler?.EndSample();
```

```csharp
// Print the CPU flame tree for the latest frame.
void PrintTree(TimeSample s, int depth)
{
    Console.WriteLine($"{new string(' ', depth * 2)}{s.Name}: {s.InclusiveMilliseconds:F3}ms");
    foreach (TimeSample child in s.Children)
        PrintTree(child, depth + 1);
}
if (profiler.Latest?.CpuRoot is { } root)
    PrintTree(root, 0);

// GPU tree only has data if RequestExecutionTiming was on for that frame.
if (profiler.Latest?.GpuRoot is { } gpu)
    PrintTree(gpu, 0);
```

#### GPU timing always lags

GPU execution time round-trips from the GPU well after the CPU-side pass/view that issued it has
closed - the profiling/UI layer is itself part of the pipeline, so by the time `RecordExecutionTime`
fires, at least one frame boundary (sometimes more) has already passed. There is no reliable way to
attribute a late GPU number to the *specific* past frame it measured, because `CommandBufferInfo.Id`
is not scoped by frame (see below). This is an accepted approximation, not a bug: a GPU number is
simply attributed to whatever command buffer node exists under that id in the frame that's live when
it arrives.

#### Command buffer ids are not stable across frames

`CommandBufferInfo.Id` comes from Graphite's `RenderContext.GetCommandBuffer`, which stamps a fresh
id via `Interlocked.Increment` on a global, never-resetting counter on **every rental** - even though
the underlying `CommandBuffer` object is pooled/reused internally by Graphite. So the same logical
command buffer (e.g. "pass 3's opaque geometry buffer") gets a brand-new, higher id every single
frame; ids never repeat. `ProfiledPass` accounts for this: `CommandBuffer` nodes are recycled via a
plain object pool (identity-agnostic), not persisted by id like `View`/`Pass` are - persisting them
by id would leak a dictionary entry per rental, forever.

## Capture-only data

Only built on a frame where `RequestCaptureNextFrame()` was called beforehand (`HasCaptureDepth ==
true`). `DrawHierarchyCollector` is the only collector gated this way - on every other frame it's a
no-op below the CommandBuffer level.

```csharp
// Walk the capture-depth tree for a frame - only meaningful if HasCaptureDepth is true.
ProfiledFrame frame = profiler.Latest!;
if (frame.HasCaptureDepth)
{
    foreach (ProfiledView view in frame.Views)
    foreach (ProfiledPass pass in view.Passes)
    foreach (ProfiledCommandBuffer cb in pass.CommandBuffers)
    foreach (ProfiledPipelineSwitch sw in cb.Switches)
    {
        Console.WriteLine($"{sw.ShaderName} ({sw.Variant}) - {sw.Objects.Count} objects, {sw.Draws.Count} loose draws");
        foreach (ProfiledCallingObject obj in sw.Objects)
            Console.WriteLine($"  {obj.Label}: {obj.Draws.Count} draw(s), culled={obj.Culled}");
    }
}
```

### PipelineSwitch level (`ProfiledPipelineSwitch`)

One per `RecordPipelineSwitch` event within an armed command buffer:

- `ShaderName`, `IsCompute`, `Stages`, `PassName`, `Variant`, `Tags`, `MaterialName` - identifies
  which shader/material/pass/variant this switch bound
- `State` (`ProfiledPipelineState`) - full blend/depth-stencil/rasterizer state for a graphics switch,
  or thread-group size for a compute switch (whichever doesn't apply is null)
- `Objects` - the calling objects drawn under this switch before the next one
- `Draws` - draws issued under this switch that never correlated to a `Renderable` event: a
  post-process blit, a fullscreen triangle, a user-invoked immediate draw, anything outside the
  normal culled-object pipeline. `DrawHierarchyCollector` flushes whatever's still buffered and
  unclaimed straight here whenever the switch changes, the view ends, or the frame ends - so these
  draws are never silently dropped, and never misattributed to some later, unrelated object either.

No stable identity across frames (a switch is "the Nth issued this frame in this command buffer"),
so unlike View/Pass/CommandBuffer these are always fresh-allocated, never pooled - acceptable since
captures are rare and inherently heavy.

### CallingObject level (`ProfiledCallingObject`)

One per renderable object drawn under a switch, correlated from the `Renderable` marker stream:

- `Label` (mesh/material name), `MaterialName`, `MeshName`, `Layer`, `Position`
- `Registered`, `Culled` - same booleans that feed the always-on view-level counts
- `Draws` - every draw/dispatch call this object issued (can be more than one if the object's draws
  straddled a pipeline rebind)

### DrawCall level (`ProfiledDrawCall`)

One per `RecordDraw`/`RecordDispatch` call:

- `Draw` or `Dispatch` (`DrawCallInfo?`/`DispatchCallInfo?`, whichever kind this call was)
- `Culled`
- `ReferenceBuffers` - vertex/index/bound buffers this draw referenced (`RecordDrawBuffers`), each
  with a `SnapshotResourceID` that's only valid (non-default) when a capture is armed

### Resource identity: `SnapshotResourceID`

`ResourceRef.Resource` (on Pass Inputs/Outputs) and `ReferenceBuffer.Resource` (on a DrawCall) both
carry a `SnapshotResourceID` - a `(ResourceId, Version, IsValid)` triple that points at a specific
version of a resource. `IsValid` is only ever true on an armed frame; on every other frame it's
`SnapshotResourceID.Invalid` (default), since there's no readback to point at.

## Snapshot-only data (never in a live `ProfiledFrame`)

A `Snapshot` (`Snapshot.cs`) is `{ Name, FrameIndex, Frame, Resources }` - the cloned, fully
independent `ProfiledFrame` (see [The two-tier model](#the-two-tier-model)) plus one thing that
**never** exists anywhere in the live ring, armed or not: actual resource bytes.

`SnapshotCapturer` assembles this in two phases:

1. **Mid-frame, per-pass (`HandleCapture`)**: for a pass with texture outputs, its render targets are
   copied to staging textures immediately (non-blocking), tagged with that pass's index as the
   resource's version. Draw-call buffers bound while a capture is armed are staged the same way,
   deduped by `(DeviceBuffer, Offset, ContentVersion)` so an unchanged buffer rebound across many
   passes/draws is only copied once.
2. **End of frame (`Finalize`)**: once the frame's GPU work has been submitted
   (`GraphicsDevice.WaitForIdle`/`SubmitAndWait`), every staged copy is mapped and read back to CPU
   as raw bytes:
   - `SnapshotResource` - one per distinct texture/buffer resource id, with `Kind`
     (`Texture`/`Buffer`) and a name
   - `SnapshotResourceVersion` - one per version of that resource captured this frame: `Subtextures`
     (populated for textures - one per color/depth attachment, with actual pixel bytes, mip0 only)
     or `BufferData`/`BufferMeta` (populated for buffers - raw bytes + usage/size/stride)

Textures/buffers that were only *read*, never captured via a pass's own output hook, still get a
readback pass here (tagged version 0, matching the "nothing wrote this resource" fallback used
elsewhere). None of this - not the staging textures, not the byte arrays - is ever attached to the
live `ProfiledFrame` in the ring; it only exists inside the `Snapshot` object handed to
`CaptureFinalizeHandler`.

```csharp
profiler.SnapshotCaptured += snapshot =>
{
    Console.WriteLine($"Captured frame {snapshot.FrameIndex}, {snapshot.Resources.Count} resources");

    foreach (SnapshotResource res in snapshot.Resources)
    {
        SnapshotResourceVersion latest = res.Versions[^1]; // versions are sorted ascending
        if (res.Kind == SnapshotResourceKind.Texture)
        {
            foreach (SnapshotSubTexture sub in latest.Subtextures)
                Console.WriteLine($"  {res.Name}/{sub.Name}: {sub.Width}x{sub.Height} {sub.Format}, {sub.Pixels.Length} bytes (mip0)");
        }
        else
        {
            Console.WriteLine($"  {res.Name}: {latest.BufferData.Length} bytes, {latest.BufferMeta?.Kind}");
        }
    }

    // Resolve which SnapshotResource a specific draw call's vertex buffer points at:
    ProfiledPipelineSwitch sw = snapshot.Frame.Views[0].Passes[0].CommandBuffers[0].Switches[0];
    ReferenceBuffer vb = sw.Objects[0].Draws[0].ReferenceBuffers[0];
    if (vb.Resource.IsValid)
    {
        SnapshotResource owner = snapshot.Resources.First(r => r.ResourceId == vb.Resource.ResourceId);
        SnapshotResourceVersion version = owner.Versions.First(v => v.Version == vb.Resource.Version);
    }
};
```

## History retention

`EditorProfiler` keeps one `ProfiledFrame[240]` ring (`RingSize`). Every frame's data - including the
capture-depth layer on an armed frame - lives at full fidelity in its ring slot until the write
pointer laps back around 240 frames later and that slot is `Reset()` for reuse. There is no separate
"aged"/"light" tier that drops data early: `History`, `Latest`, and `FrameAgo(n)` all read directly
from the ring, live, with no copying. The **only** place a frame is ever copied is the `Clone()` that
happens once, for an armed capture, to hand an independent snapshot off to
`CaptureFinalizeHandler` - because a `Snapshot` must survive indefinitely (saved to disk, held by the
user) while its originating ring slot keeps getting reset and reused for future frames.
