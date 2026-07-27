# Profiler Design document

Live inventory: what `TimingCollector`/`CountersCollector`/`PassGraphCollector` write every frame,
capture or not. Deep per-draw data (`ProfiledPipelineSwitch`/`CallingObject`/`DrawCall`, resource
bytes) is capture-tier only - see `SnapshotViewerDesign.md`. CPU timing is out of scope (GPU-only,
gated by the same Resume/Pause switch as `TrianglesDrawn`).

## Directly available

### Frame (`ProfiledFrame`)
- [x] `double FrameMilliseconds`
- [x] `double Fps`
- [x] `long FrameIndex`
- [x] `bool HasCaptureDepth`
- [x] `IReadOnlyList<ProfiledView> Views`
- [x] `IReadOnlyList<ProfiledCommandBuffer> FreeCommandBuffers`
- [x] `int TimelineElementCount` / `GetTimelineElement(int, out ProfiledView?, out ProfiledCommandBuffer?)`
  / `EnumerateTimeline()` - single ordered walk of views and free command buffers by first-touch order.
- [x] `double GpuMilliseconds` - sum of every view's plus every free command buffer's.
- [ ] `bool HasVramBudget` / `ulong VramBudgetBytes` / `ulong VramUsedBytes` - driver-reported VRAM
  budget/usage (`VK_EXT_memory_budget`), polled once per frame from `GraphicsDevice.GetMemoryBudget()`.
  `HasVramBudget` false (both byte fields `0`) if the extension is unavailable.
- [ ] `ulong TrianglesDrawn` - sum of every view's TrianglesDrawn.
- [ ] `ulong InputAssemblyVertices` - sum of every view's plus every free command buffer's.
- [ ] `int DrawCallCount` - sum of every view's DrawCallCount.
- [ ] `int DispatchCallCount` - sum of every view's DispatchCallCount.
- [x] `int ViewCount` - `Views.Count`.
- [ ] `int PassCount` - sum of every view's pass count.
- [ ] `int CommandBufferCount` - sum of every pass's command buffer count plus FreeCommandBuffers; also the frame's total submit count.

- [ ] `IReadOnlyList<CounterValue> Counters` - see Counters below

### View (`ProfiledView`)
- [ ] `double GpuMilliseconds`
- [ ] `int RegisteredObjects` / `int CulledObjects` / `int TotalObjects` / `int RenderedObjects`
- [ ] `int DrawCallCount`
- [ ] `int DispatchCallCount` - always-on, unlike DrawCallCount, since dispatches aren't scene renderables.
- [ ] `ulong TrianglesDrawn` (summed from Pass level)
- [ ] `ulong InputAssemblyVertices` - sum of this view's passes' InputAssemblyVertices.
- [ ] `ulong FragmentShaderInvocations` - sum of this view's passes' FragmentShaderInvocations.
- [ ] `double Overdraw` - `FragmentShaderInvocations / (PixelWidth * PixelHeight)`; `0` if PixelWidth/
  Height unset. 1.0 = every pixel shaded exactly once.
- [ ] `uint PixelWidth` / `uint PixelHeight` - render target size this view drew at, from
  `ViewInfo.PixelWidth`/`PixelHeight`, stamped every `BeginView`.
- [ ] `IReadOnlyList<ProfiledPass> Passes`
- [ ] `IReadOnlyList<PassEdge> Edges` - `int FromPass`, `int ToPass`, `ResourceRef Resource` (producer ->
  consumer between passes)
- [ ] `string Name`

### Pass (`ProfiledPass`, keyed by render-graph index)
- [ ] `double GpuMilliseconds`
- [ ] `ulong TrianglesDrawn` - sum of command buffers' `ClippingPrimitives`, from
  `VK_QUERY_TYPE_PIPELINE_STATISTICS`. Real hardware count: includes indirect draws, excludes
  clipped/backface-culled geometry. Reads `0` if not recording or `pipelineStatisticsQuery`
  unsupported
- [ ] `ulong InputAssemblyVertices` - sum of this pass's command buffers' InputAssemblyVertices.
- [ ] `ulong FragmentShaderInvocations` - sum of command buffers' `FragmentShaderInvocations`, from the
  same `VK_QUERY_TYPE_PIPELINE_STATISTICS` query as `TrianglesDrawn`. Numerator for `ProfiledView.Overdraw`.
- [ ] `int PipelineSwitchCount` - sum of command buffers' `PipelineSwitchCount`; no shader/material
  identity, just a count
- [ ] `int RegisteredObjects` / `int CulledObjects` / `int TotalObjects` / `int RenderedObjects` - same shape as View, tracked per pass too.
- [ ] `int DrawCallCount` - same shape as View, tracked per pass too.
- [ ] `int DispatchCallCount` - same shape as View, tracked per pass too.
- [ ] `IReadOnlyList<ResourceRef> Inputs` / `IReadOnlyList<ResourceRef> Outputs` - `uint Id`,
  `string Name`, `ResourceRefKind Kind` (`Texture`/`Buffer`/`Unknown`), `SnapshotResourceID Resource`
  (always `Invalid` on a live frame)
- [ ] `IReadOnlyList<ProfiledCommandBuffer> CommandBuffers`
- [ ] `int Index` / `string Name`

### CommandBuffer (`ProfiledCommandBuffer`)
- [ ] `double GpuMilliseconds`
- [ ] `ulong ClippingPrimitives`
- [ ] `ulong InputAssemblyVertices` / `ulong InputAssemblyPrimitives` / `ulong ClippingInvocations`
- [ ] `ulong FragmentShaderInvocations` - from the pipeline-statistics query's `FragmentShaderInvocationsBit`.
- [ ] `int PipelineSwitchCount` - incremented once per `RecordPipelineSwitch` call on this command
  buffer, always-on regardless of capture. Shader name/material/state for each switch stays
  capture-tier only (`ProfiledPipelineSwitch`, gated on `_armed` in `DrawHierarchyCollector`) since
  that needs string/id retention this counter doesn't
- [ ] `string Name` / `ulong Id` (`Id` not stable frame-to-frame, see `PROFILING_MODEL.md`)
- [ ] `IReadOnlyList<ProfiledPipelineSwitch> Switches` - empty on a live frame, capture-tier only, see
  `SnapshotViewerDesign.md`

### Counters (`CountersCollector.Registry`, flat per-frame array of `CounterValue`: `string Name`,
`CounterCategory Category`, `CounterUnit Unit`, `double Value`)
- [ ] `Live/{bin}` (EngineObject, count) - object count, tracked for every `AllocBin`
  - `DeviceBuffer`, `Texture`, `TextureView`, `Sampler`, `Framebuffer`, `Pipeline`, `Shader`,
    `ResourceLayout`, `ResourceSet`, `CommandBuffer`
- [ ] `Resident/{bin}` (EngineObject, bytes) - only bins whose allocation call passes a real byte size;
  the rest always allocate/free with `0` bytes and stay zeroed
  - `DeviceBuffer`, `Texture`, `Shader`
- [ ] `Resident/{role}` (BufferMemory, per buffer usage class) - `RecordBufferAllocation`/`RecordBufferFree`
  fan a buffer's real byte size out to every usage-flag role it matches, so all roles contribute
  - `Vertex`, `Index`, `Uniform`, `StructuredReadOnly`, `StructuredReadWrite`, `Indirect`, `Staging`,
    `Dynamic`
- [ ] `Draw/Count`, `Dispatch/Count` (DrawDispatch)
- [ ] `Alloc/{bin}`, `Free/{bin}` (AllocFree, count) - same set as `Live/{bin}`, every `AllocBin`
  - `DeviceBuffer`, `Texture`, `TextureView`, `Sampler`, `Framebuffer`, `Pipeline`, `Shader`,
    `ResourceLayout`, `ResourceSet`, `CommandBuffer`
- [ ] `BufferOp/{op}` (BufferUpdate, count) - all `BufferOpBin` values
  - `Map`, `Unmap`, `Update`, `Copy`
- [ ] `BufferOpBytes/{op}` (BufferUpdate, bytes) - `Unmap` always records `0` bytes and stays zeroed
  - `Map`, `Update`, `Copy`
- [ ] `Submit/Graphics`, `Submit/Transfer` 
- [ ] `ResourceSet/Binds`
- [ ] `Swap/{bin}` (Swapchain) - all `SwapBin` values
  - `Present`, `Resize`, `Acquire`
- [ ] `Barrier/{bin}` - all `BarrierBin` values
  - `TextureTransition`, `BufferTransition`, `MemoryBarrier`

## Derived (computed on read, no storage)

- [ ] Frame budget usage % = `FrameMilliseconds / target frame time`
- [ ] Culled % / Registered % per view = `CulledObjects / TotalObjects`, `RegisteredObjects / TotalObjects`
- [ ] Net Live delta between frames = `Live/{bin}[N] - Live/{bin}[N-1]` (via `EditorProfiler.History`)
- [ ] Pass % of view GPU time = `pass.GpuMilliseconds / view.GpuMilliseconds`
- [ ] View % of frame GPU time = `view.GpuMilliseconds / Frame.FrameMilliseconds`
- [ ] Average draw calls per rendered object = `DrawCallCount / RenderedObjects`
- [ ] Average bytes per buffer op = `BufferOpBytes/{op} / BufferOp/{op}`
- [ ] Total resident memory = sum of all `Resident/{bin}` + `Resident/{role}`
- [ ] Total barrier count = sum of `Barrier/{bin}`
- [ ] Pass fan-in/fan-out = `Inputs.Count` / `Outputs.Count`
- [ ] Graph in/out-degree per pass = scan `Edges` for `FromPass == X` / `ToPass == X`
- [ ] Critical path length through pass DAG = longest chain walk through `Edges`
- [ ] Feedback-pass flag = pass whose `Inputs` and `Outputs` share a resource id
- [ ] Transfer vs render GPU time split = sum `FreeCommandBuffers[].GpuMilliseconds` vs sum of every
  `View.Passes[].CommandBuffers[].GpuMilliseconds`
- [ ] Frame pacing jitter = stddev of `FrameMilliseconds` over N frames of `History`
- [ ] Pipeline switch density per command buffer = `CommandBuffer.PipelineSwitchCount / CommandBuffer.GpuMilliseconds`
  - a proxy for how much state rebinding one command buffer does per unit of GPU time, no capture needed

## Pipeline switch stats: wired live, material identity still gated

`IProfiler.RecordPipelineSwitch` (`EditorProfiler.cs:375`) fires every `ShouldRecord` frame - gated
on `!_paused`, not on capture-armed. `PassGraphCollector.OnPipelineSwitch` increments
`ProfiledCommandBuffer.PipelineSwitchCount` on every call, always-on; `ProfiledPass.PipelineSwitchCount`
sums it per pass. Both are plain `int` counters - no shader name, material name, or state retained.

`DrawHierarchy.OnPipelineSwitch` (`DrawHierarchyCollector.cs:142`) still early-returns on `!_armed` -
shader/material identity (`ProfiledPipelineSwitch.ShaderName`/`MaterialName`/`State`) is still
capture-tier only, deliberately, since it needs string/id retention the plain counters don't. A
distinct-material-count-per-frame (via a reused `HashSet<string>`, cleared each `OnFrameBegin`) would
be the next always-on step here if wanted, but isn't wired yet.

## Not trackable live (real gaps)

- CPU-side GPU-wait/stall time (fence/`WaitForIdle` blocking) - not tracked anywhere
- Command buffer submit-to-execute latency - only total GPU ms is measured, not queue wait time

## Closed, not planned

- GPU memory bandwidth per frame - not derivable from any CPU-observable data (no vertex stride
  tracked, texture sample bytes depend on runtime GPU state, and render-target bandwidth needs actual
  byte-width-per-pixel math on top of `ProfiledView.Overdraw`, not just the invocation count itself).
  `BufferOp`/`BufferOpBytes` are CPU-side map/update/copy traffic only. A real number needs
  vendor-specific hardware counters
  (`VK_KHR_performance_query`, AMD GPUPerfAPI, Nsight Perf SDK) - out of scope for this profiler.

## Triangle count: two tiers

- `ulong ProfiledPass.TrianglesDrawn` / `ulong ProfiledView.TrianglesDrawn` - real GPU-reported count
  from a pipeline-statistics query per command buffer. Use for pass/view geometry load; includes
  indirect draws, excludes clipped/culled geometry.
- `ulong? ProfiledDrawCall.TriangleCount` - capture-tier only, CPU-side estimate for one draw from
  `Topology` + `VertexOrIndexCount` + `InstanceCount`. `null` for indirect draws. Never summed across
  a pass/command buffer - would silently undercount vs the real GPU number, or misreport entirely on
  a pass with indirect draws.
