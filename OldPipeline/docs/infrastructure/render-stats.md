# RenderStats

Source: [`Rendering/RenderStats.cs`](../../Prowl.Runtime/Rendering/RenderStats.cs).

Static per-frame counters written by the pipeline and by low-level `Graphics` draws, read by the editor. Not to be
confused with the editor Render Profiler (`Prowl.Editor/GUI/RenderProfiler`), which is separate, command-buffer based,
and is kept.

## `RenderStats.Frame`

| Group | Counters |
|---|---|
| Geometry (color) | `DrawCalls`, `InstancedDrawCalls`, `Triangles`, `Vertices`, `Batches` |
| Culling | `RenderablesCollected`, `RenderablesCulled`, `RenderablesDrawn` |
| Shadows | `ShadowDrawCalls`, `ShadowInstancedDrawCalls`, `ShadowTriangles`, `ShadowVertices`, `ShadowBatches`, `ShadowPasses`, `ShadowRenderablesCollected/Culled/Drawn` |
| Lighting | `Lights`, `DirectionalLights`, `PointLights`, `SpotLights`, `ShadowCasters` |
| Effects | `ImageEffects`, `ImageEffectPasses` |
| Cameras | `Cameras` |
| Timing (ms, CPU encode time on the main thread) | `FrameTimeMs`, `ColorPassMs`, `ShadowPassMs`, `PostFxMs` |

## API

`BeginFrame()` resets, `EndFrame()` publishes `Last` and pushes `FrameTimeMs` (unscaled delta) into a 120-entry ring
buffer (`FrameTimeHistory`, `FrameTimeIndex`). `BeginShadowPass/EndShadowPass` route subsequent counts into the shadow
fields; `BeginColorPass/EndColorPass`, `BeginPostFx/EndPostFx` time sections (post-fx accumulates across both stages).
`AddCamera`, `AddBatch`, `AddRenderables`, `AddLightCounts`, `AddImageEffect`.

## Notes

- Timings are CPU encoding time, not GPU time.
- `BeginFrame/EndFrame` are driven by the editor Game View only; multi-camera frames are summed.
- All sections share one start timestamp. `BeginColorPass` is called before the opaque draws, but `BeginPostFx` for
  the AfterOpaques effects restarts that timestamp, so `ColorPassMs` ends up measuring AfterOpaques effects +
  transparents + world UI, and the opaque encode time is not counted anywhere. The prepass is also outside every
  section.
