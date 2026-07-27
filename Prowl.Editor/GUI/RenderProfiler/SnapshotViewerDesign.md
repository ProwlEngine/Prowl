# Snapshot Viewer Design document

What a `Snapshot` adds on top of the live tier in `ProfilerDesign.md`. Everything in that document
(frame/view/pass/command-buffer timing, counters, `PipelineSwitchCount`) is still present and
unchanged in a Snapshot's `Frame` - this doc only covers what becomes additionally available because
`HasCaptureDepth == true` and `Resources` exists: the deep draw hierarchy
(`ProfiledPipelineSwitch`/`CallingObject`/`DrawCall`), resolved resource identity, and actual GPU
resource bytes.

## Directly available (beyond the live tier)

### Snapshot
- [ ] `string? Name`
- [ ] `long FrameIndex`
- [ ] `ProfiledFrame Frame` - the same type covered in `ProfilerDesign.md`, but with `HasCaptureDepth ==
  true` and every `CommandBuffer.Switches` populated
- [ ] `IReadOnlyList<SnapshotResource> Resources`

### CommandBuffer - now populated
- [ ] `IReadOnlyList<ProfiledPipelineSwitch> Switches` - empty on every live frame, this is the entire
  reason a capture exists

### PipelineSwitch (`ProfiledPipelineSwitch`)
- [ ] `string ShaderName`, `string Variant` - exactly which shader permutation drew what
- [ ] `string MaterialName` - which material asset issued this switch
- [ ] `ProfiledPipelineState? State` - `BlendStateDescription? BlendState`,
  `DepthStencilStateDescription? DepthStencilState`, `RasterizerStateDescription? RasterizerState`,
  `uint? ThreadGroupSizeX/Y/Z` - exact GPU state bound; invaluable when debugging a visual bug (wrong
  blend mode, wrong depth test, backface culling off, etc.), or dispatch shape for a compute switch
- [ ] `IReadOnlyList<ProfiledCallingObject> Objects` - the calling objects drawn under this switch
- [ ] `bool IsCompute`, `ShaderStages Stages` - graphics vs compute classification for the switch
- [ ] `IReadOnlyList<ProfiledDrawCall> Draws` - every draw issued under this switch, in order; each
  `ProfiledCallingObject` owns a contiguous range of this array via `DrawStart`/`DrawEnd` (see
  `GetDraws`) - a draw outside every object's range is a loose one that never correlated to a
  `Renderable` (fullscreen blits, post-process triangles, user-invoked immediate draws)
- [ ] `GetDraws(ProfiledCallingObject)` - `ReadOnlySpan<ProfiledDrawCall>` slice of `Draws` that object
  owns
- [ ] `string PassName` - redundant with the pass this switch is nested under; useful mainly once
  switches are flattened/searched outside their tree
- [ ] `IReadOnlyDictionary<string, string>? Tags` - freeform key/value; usefulness depends entirely on
  what's tagged

### CallingObject (`ProfiledCallingObject`)
- [ ] `string Label`, `string MaterialName`, `string MeshName` - exactly what object drew what,
  per-object instead of a view-level aggregate
- [ ] `bool Registered`, `bool Culled` - per-object culling result (the live tier only has the
  view-level totals these roll up into)
- [ ] `Vector.Float3 Position` - where in world space, useful for cross-referencing against a scene view
- [ ] `int Layer` - render layer/mask
- [ ] `int DrawStart`, `int DrawEnd` - range of indices into the owning switch's `Draws` this object
  claims (can span >1 draw if its draws straddled a pipeline rebind); use
  `ProfiledPipelineSwitch.GetDraws(obj)` rather than slicing `Draws` by hand

### DrawCall (`ProfiledDrawCall`)
- [ ] `DrawCallInfo? Draw` - `DrawKind Kind`, `uint VertexOrIndexCount`, `uint InstanceCount`,
  `uint DrawCount`, `bool IsIndirect`, `PrimitiveTopology Topology`
- [ ] `DispatchCallInfo? Dispatch` - `uint GroupCountX/Y/Z`, `bool IsIndirect`
- [ ] `bool Culled`
- [ ] `ReferenceBuffer[] ReferenceBuffers` - `string Name`, `uint SizeInBytes`, `uint ContentVersion`,
  `bool ReadOnly`, `SnapshotResourceID Resource` - exactly which buffer bytes this specific draw
  pulled from

### Resource identity resolution
- [ ] `ResourceRef.Resource` (pass inputs/outputs) and `ReferenceBuffer.Resource` (draw calls): a
  `SnapshotResourceID` (`uint ResourceId`, `uint Version`, `bool IsValid`) - always `Invalid` on a
  live frame, resolvable here, letting any pass or draw call be linked to one exact
  `SnapshotResourceVersion`

### SnapshotResource / SnapshotResourceVersion (never exists on a live frame)
- [ ] `SnapshotResource`: `uint ResourceId`, `string Name`, `SnapshotResourceKind Kind`
  (`Texture`/`Buffer`), `IReadOnlyList<SnapshotResourceVersion> Versions`
- [ ] `SnapshotResourceVersion`: `uint Version`, `IReadOnlyList<SnapshotSubTexture> Subtextures`,
  `byte[] BufferData`, `SnapshotBufferMeta? BufferMeta`
- [ ] `SnapshotSubTexture`: `string Name`, `PixelFormat Format`, `uint Width/Height/Depth`,
  `uint MipLevels`, `byte[] Pixels` - actual pixel bytes per texture attachment (mip0), the thing
  that makes a texture viewer possible at all
- [ ] `SnapshotBufferMeta`: `BufferUsage Kind`, `uint SizeBytes`, `uint Stride`,
  `IReadOnlyList<BufferField> Layout` - actual bytes for a buffer, plus type/size metadata
- [ ] `BufferField`: `string Name`, `string Type`, `uint Offset`, `uint SizeBytes` - only present when
  layout metadata was supplied; lets a raw byte buffer be decoded into named fields instead of shown
  as a hex dump
- [ ] Multiple `SnapshotResourceVersion` entries per `SnapshotResource` - a render target gets one
  version per pass that wrote it this frame, not just its final state. A live frame only ever shows
  "now"; a snapshot shows every intermediate state a reused render target passed through

## Implicit / derived data unique to a snapshot

- [ ] Triangle count per draw call = `ProfiledDrawCall.TriangleCount` (`ulong?`, computed, not stored),
  see DrawCall above. Real topology-aware math (`DrawCallInfo.Topology`), not a triangle-list
  assumption - but still `null` for indirect draws regardless of tier, since even the capture tier
  never resolves an indirect draw's actual GPU-side vertex count
- [ ] Per-pass / per-view / per-frame triangle totals - use `ProfiledPass.TrianglesDrawn` /
  `ProfiledView.TrianglesDrawn` instead (the real GPU-reported pipeline-statistics aggregate, tracked
  live, not just in a capture) rather than summing per-draw `TriangleCount` - see `ProfilerDesign.md`'s
  "Triangle count: two tiers" for why summing the CPU per-draw estimate would be wrong
- [ ] Distinct material/shader count per pass or frame - count unique `MaterialName`/`ShaderName` across
  all `Switches` (`ProfiledPipelineSwitch.MaterialName`/`ShaderName`, both `string`) - the only tier
  where material identity exists at all; `ProfiledCommandBuffer.PipelineSwitchCount`/
  `ProfiledPass.PipelineSwitchCount` (see `ProfilerDesign.md`) give the raw switch count live, but
  never which shader/material - that's snapshot-only
- [ ] Pipeline switch count per pass/frame - `Switches.Count` per command buffer, same number the live
  tier now tracks as `PipelineSwitchCount` (see `ProfilerDesign.md`) - redundant here except for
  cross-checking against the live counter
- [ ] Reconstructed mesh geometry - pair a draw's vertex `ReferenceBuffer` with its index
  `ReferenceBuffer` and decode `BufferMeta.Layout` to rebuild real mesh data - what the orbit-cam
  mesh viewer needs
- [ ] Pixel-level texture inspection (per-channel histogram, min/max, single-texel readback at a
  zoomed-in coordinate) - only possible because `Subtextures` carries real pixel bytes
- [ ] Per-object draw call count = `CallingObject.DrawEnd - CallingObject.DrawStart` - was only ever a
  view-level rollup live
- [ ] Buffer versions written per frame for one resource = `SnapshotResource.Versions.Count` - shows
  exactly how many times a render target/buffer was overwritten this frame
- [ ] Draw-buffer reuse rate - how many distinct draws reference the same `(Buffer, Offset)` pair before
  its `ContentVersion` changes (mirrors `SnapshotCapturer`'s own dedup logic)
- [ ] Instanced draw visibility - `InstanceCount` is a real field now, not a wishlist item; instanced
  draws are no longer invisible the way they are in the live tier
- [ ] Overdraw approximation - for a given screen pixel, count how many `CallingObject`/`DrawCall`
  entries under a pass wrote to it, cross-referencing captured depth/position data (heavier to
  compute than a glance, but possible where it isn't live)
- [ ] Per-DrawCall culled ratio vs. the view-level `RenderedObjects`/`TotalObjects` rollup - finer
  grained, rarely worth it over the aggregate
- [ ] Pipeline switch density per switch-bearing command buffer = `Switches.Count /
  CommandBuffer.GpuMilliseconds` - same formula as the live-tier version in `ProfilerDesign.md`, just
  computed from the capture-tier list instead of the always-on counter

## Still not available, even in a snapshot

- GPU memory bandwidth for a specific draw - `ReferenceBuffers` says what was bound, not how many
  bytes the GPU actually streamed executing the draw
- Exact overdraw (not the approximation above) - would need a real depth-complexity pass, not just
  cross-referencing captured draws
- CPU-side GPU-wait/stall time - still nowhere in the model, capture or not
- VRAM budget vs used - still just resident-byte counters, no configured limit to compare against
