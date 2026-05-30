using Prowl.Vector;
using Prowl.Photonic.Integration;
using Prowl.Photonic.Rasterization;
using Prowl.Photonic.Raytracing;
using Prowl.Photonic.Sampling;

namespace Prowl.Photonic;

/// <summary>
/// A running bake: created by <see cref="LightmapBaker.Start"/>, driven via <see cref="Poll"/> /
/// <see cref="Wait"/>, terminated via <see cref="Cancel"/>.
/// </summary>
public sealed class Job
{
    private readonly BakeScene _scene;
    private readonly LightmapTarget[] _targets;
    private readonly BakeOptions _options;
    private readonly System.Threading.CancellationTokenSource _cts = new();
    private System.Threading.Tasks.Task? _task;

    private volatile string _activity = "Idle";
    private volatile int _iterationCount;

    /// <summary>
    /// Number of completed sample-accumulation passes in continuous mode. Increments after each
    /// iteration; the caller can watch it (e.g. once per render frame) and re-upload the atlas
    /// when it changes. Always 0 outside of continuous mode.
    /// </summary>
    public int IterationCount => _iterationCount;

    // ---- debug viz ---------------------------------------------------------------------------
    // Caller asks for the rays produced at a specific texel via SetDebugTexel(target, x, y); the
    // integrator publishes a fresh DebugSegment list per iteration that ReadDebugSegments() returns.

    private volatile int _debugTexelX = -1;
    private volatile int _debugTexelY = -1;
    private LightmapTarget? _debugTarget;

    internal int DebugTexelX => _debugTexelX;
    internal int DebugTexelY => _debugTexelY;
    internal LightmapTarget? DebugTarget => _debugTarget;

    private System.Collections.Generic.List<DebugSegment> _debugSegments = new();
    private readonly object _debugLock = new();

    /// <summary>
    /// Set which texel's rays the integrator should record each iteration. Pass <c>null</c>
    /// for <paramref name="target"/> (or coordinates outside the target) to disable recording.
    /// </summary>
    public void SetDebugTexel(LightmapTarget? target, int x, int y)
    {
        _debugTarget = target;
        _debugTexelX = target is null ? -1 : x;
        _debugTexelY = target is null ? -1 : y;
    }

    /// <summary>Returns a snapshot of the segments produced by the most recent recorded path for the debug texel.</summary>
    public System.Collections.Generic.List<DebugSegment> ReadDebugSegments()
    {
        lock (_debugLock) return new System.Collections.Generic.List<DebugSegment>(_debugSegments);
    }

    internal void PublishDebugSegments(System.Collections.Generic.List<DebugSegment> segs)
    {
        lock (_debugLock) _debugSegments = segs;
    }

    private Rasterization.TargetWorkspace[]? _workspacesPublic;

    private volatile Surfels.SurfelCloud? _surfelCloudPublic;

    /// <summary>
    /// Live reference to the surfel cloud (populated only when <see cref="BakeOptions.PathTracer"/>
    /// is <see cref="PathTracerKind.Surfel"/>). Returns null before the cloud has been generated or
    /// when the per-texel tracer is active. Surfel fields mutate from the worker thread between
    /// iterations: callers must accept tearing on reads and treat all fields as read-only.
    /// Exposed as internal; consumers gain access via InternalsVisibleTo.
    /// </summary>
    internal Surfels.SurfelCloud? SurfelCloud => _surfelCloudPublic;

    /// <summary>
    /// Read back the integrator's recorded data for one texel of the given target. Returns
    /// <see cref="TexelDiagnostic.Valid"/> = false until the rasterise phase has completed,
    /// or if the target wasn't part of this bake.
    /// </summary>
    public TexelDiagnostic GetTexelInfo(LightmapTarget target, int x, int y)
    {
        var ws = _workspacesPublic;
        if (ws is null || target is null) return default;
        int targetIndex = -1;
        for (int i = 0; i < _targets.Length; i++)
        {
            if (object.ReferenceEquals(_targets[i], target)) { targetIndex = i; break; }
        }
        if (targetIndex < 0) return default;
        var w = ws[targetIndex];
        if (w is null) return default;
        if (x < 0 || x >= w.Width || y < 0 || y >= w.Height) return default;
        int idx = y * w.Width + x;
        var s = w.Samples[idx];
        return new TexelDiagnostic
        {
            Valid = true,
            Covered = w.Covered[idx],
            Position = s.Position,
            Normal = s.Normal,
            WorldRadius = s.WorldRadius,
            LightmapValue = new Float3(
                w.Target.PixelsRGB[idx * 3],
                w.Target.PixelsRGB[idx * 3 + 1],
                w.Target.PixelsRGB[idx * 3 + 2]),
            IndirectSampleCount = w.IndirectSampleCount is null ? 0 : w.IndirectSampleCount[idx],
        };
    }

    /// <summary>
    /// Run the edge-avoiding denoiser over every target's converged atlas, then re-dilate the seams.
    /// No-op unless <see cref="BakeOptions.Denoise"/> is set or the bake never rasterized. Mutates the
    /// pixel buffers in place using the rasterizer's retained per-texel guides, so call it once after
    /// the bake has stopped (<see cref="Cancel"/> then <see cref="Wait"/>); reads after this see the
    /// denoised result.
    /// </summary>
    public void Denoise()
    {
        if (!_options.Denoise) return;
        var ws = _workspacesPublic;
        if (ws is null) return;

        for (int t = 0; t < ws.Length; t++)
        {
            var w = ws[t];
            if (w is null) continue;

            Imaging.LightmapDenoiser.Run(w.Target.PixelsRGB, w.Covered, w.Samples, w.Width, w.Height,
                _options.DenoiseIterations, _options.DenoiseColorPhi, _options.DenoiseNormalPhi, _options.DenoisePositionScale);

            if (_options.DilatePixels > 0)
            {
                var coveredCopy = (bool[])w.Covered.Clone();
                Imaging.Dilate.Run(w.Target.PixelsRGB, coveredCopy, w.Width, w.Height, _options.DilatePixels);
            }
        }
    }

    /// <summary>Current state.</summary>
    public JobStatus Status { get; private set; } = JobStatus.Pending;

    /// <summary>Exception captured if <see cref="Status"/> is <see cref="JobStatus.Failed"/>.</summary>
    public System.Exception? Error { get; private set; }

    /// <summary>Free-text description of what the bake is doing right now (texel rasterisation, integration, etc.).</summary>
    public string Activity => _activity;

    private System.Action<int>? _iterationComplete;

    /// <summary>
    /// Fires after the worker thread finishes folding a full iteration into the atlas. Argument
    /// is the iteration count just completed. The callback runs on the worker thread; don't touch
    /// UI state directly from it without marshalling.
    /// </summary>
    public event System.Action<int> OnIterationComplete
    {
        add    { _iterationComplete += value; }
        remove { _iterationComplete -= value; }
    }

    private Job(BakeScene scene, System.Collections.Generic.IReadOnlyList<LightmapTarget> targets, BakeOptions options)
    {
        _scene = scene;
        _targets = new LightmapTarget[targets.Count];
        for (int i = 0; i < targets.Count; i++) _targets[i] = targets[i];
        _options = options;
    }

    /// <summary>Internal entry point used by <see cref="LightmapBaker.Start"/>.</summary>
    internal static Job Start(BakeScene scene,
                              System.Collections.Generic.IReadOnlyList<LightmapTarget> targets,
                              BakeOptions options)
    {
        var j = new Job(scene, targets, options);
        j._task = System.Threading.Tasks.Task.Run(j.Run, j._cts.Token);
        return j;
    }

    /// <summary>Returns true while the job is still running.</summary>
    public bool Poll() => _task is not null && !_task.IsCompleted;

    /// <summary>
    /// Block until the job ends. If the bake threw, the inner exception is rethrown here
    /// (rather than wrapped in <see cref="System.AggregateException"/>).
    /// </summary>
    public void Wait()
    {
        try { _task?.Wait(); }
        catch (System.AggregateException ae) when (ae.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ae.InnerException).Throw();
        }
    }

    /// <summary>
    /// Block until <paramref name="iterations"/> additional accumulator iterations have completed
    /// (or the bake is cancelled / fails). Useful for headless / scripted bakes:
    /// <c>baker.Start().RunIterations(64); baker.Cancel();</c>.
    /// </summary>
    public void RunIterations(int iterations)
    {
        if (iterations <= 0) return;
        int target = _iterationCount + iterations;
        while (Poll() && _iterationCount < target)
        {
            System.Threading.Thread.Sleep(1);
        }
    }

    /// <summary>Request cancellation. The job ends as soon as the worker reaches its next cancel-check.</summary>
    public void Cancel() => _cts.Cancel();

    private void Run()
    {
        try
        {
            _activity = "Build BVH";
            // Collect all instances from all targets so the TLAS sees every potential occluder.
            // This array is canonical: hit.InstanceIndex indexes into it.
            var allInstances = new System.Collections.Generic.List<BakeInstance>();
            for (int t = 0; t < _targets.Length; t++)
                allInstances.AddRange(_targets[t].Instances);
            var instances = allInstances.ToArray();

            _cts.Token.ThrowIfCancellationRequested();
            _activity = "Build TLAS";

            // Build BLAS/TLAS + pre-resolved materials (shared with BakeProbes).
            var accel = Integration.BakeAcceleration.Build(_scene, instances);
            var blas = accel.Blas;
            var instanceToBlas = accel.InstanceToBlas;
            var resolvedMats = accel.ResolvedMats;
            var tlas = accel.Tlas;

            // 3) For each target: rasterize UV1 to texel samples --------------------------------
            //    Build the workspaces array fully before publishing it. GetTexelInfo (called from
            //    the render thread) reads _workspacesPublic; publishing after the fill loop avoids
            //    a brief window where slot[i] is still null.
            var workspaces = new TargetWorkspace[_targets.Length];
            for (int t = 0; t < _targets.Length; t++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                _activity = $"Rasterise UV ({_targets[t].Name})";
                workspaces[t] = new TargetWorkspace(_targets[t]);
                RasterizeTarget(workspaces[t], instances);
            }
            _workspacesPublic = workspaces;

            // Publish per-target coverage masks (1 = a triangle covers this texel) for tooling,
            // runtime seam handling, and re-bakes.
            for (int t = 0; t < _targets.Length; t++)
            {
                var ws = workspaces[t];
                var mask = new byte[ws.Width * ws.Height];
                for (int i = 0; i < mask.Length; i++) mask[i] = ws.Covered[i] ? (byte)1 : (byte)0;
                _targets[t].CoverageMask = mask;
            }

            // 3.5) Surfel cloud (only when the surfel path tracer is active). Generated once
            //      from the same instance set as the TLAS.
            Surfels.SurfelCloud? surfelCloud = null;
            if (_options.PathTracer == PathTracerKind.Surfel)
            {
                _activity = "Generate surfels";
                surfelCloud = Surfels.SurfelGenerator.Generate(instances, new Surfels.SurfelGenerationOptions
                {
                    SurfelsPerSquareMeter = _options.SurfelsPerSquareMeter,
                    Seed = _options.Seed,
                    NormalRejection = _options.SurfelNormalRejection,
                    PoissonRadiusFactor = _options.SurfelPoissonRadiusFactor,
                    PoissonAlignThreshold = _options.SurfelPoissonAlignThreshold,
                });
                _surfelCloudPublic = surfelCloud;
            }

            // 4) Integrate ----------------------------------------------------------------------
            //    The bake is always progressive: each iteration accumulates SamplesPerIteration
            //    indirect samples per texel (or per surfel, in surfel mode) and updates the
            //    atlas. Callers stop the bake via Cancel() when they've seen enough iterations.
            if (surfelCloud is not null)
                IntegrateContinuousSurfel(workspaces, tlas, instances, blas, instanceToBlas, resolvedMats, surfelCloud);
            else
                IntegrateContinuous(workspaces, tlas, instances, blas, instanceToBlas, resolvedMats);
            _activity = "Cancelled";
            Status = JobStatus.Cancelled;
        }
        catch (System.OperationCanceledException)
        {
            Status = JobStatus.Cancelled;
            _activity = "Cancelled";
        }
        catch (System.Exception ex)
        {
            Status = JobStatus.Failed;
            Error = ex;
            _activity = $"Failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Continuous / temporal bake: pre-compute deterministic direct lighting once per texel, then
    /// loop forever folding new indirect samples into a running average until cancelled. After each
    /// iteration <see cref="LightmapTarget.PixelsRGB"/> reflects <c>direct + indirectSum/count</c>
    /// for every covered texel, so a viewer that polls <see cref="IterationCount"/> can show a
    /// live preview converging.
    /// </summary>
    private void IntegrateContinuous(TargetWorkspace[] workspaces, Tlas tlas, BakeInstance[] instances,
                                     Blas[] blas, int[] instanceToBlas, BakeMaterial?[][] resolvedMats)
    {
        var integrator = new PathIntegrator(tlas, _scene, instances, blas, instanceToBlas, resolvedMats, _options);
        var parallelOpts = new System.Threading.Tasks.ParallelOptions
        {
            CancellationToken = _cts.Token,
            MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism > 0
                ? _options.MaxDegreeOfParallelism
                : System.Environment.ProcessorCount,
        };

        // ---- pre-compute direct lighting once (deterministic with current light types). -------
        _activity = "Pre-compute direct lighting";
        for (int ti = 0; ti < workspaces.Length; ti++)
        {
            var ws = workspaces[ti];
            ws.AllocateContinuousBuffers();
            int W = ws.Width, H = ws.Height;
            try
            {
                System.Threading.Tasks.Parallel.For(0, H, parallelOpts, y =>
                {
                    for (int x = 0; x < W; x++)
                    {
                        int idx = y * W + x;
                        if (!ws.Covered[idx]) continue;
                        var s = ws.Samples[idx];
                        int blasIdx = instanceToBlas[s.InstanceIndex];
                        var mat = resolvedMats[blasIdx][s.MaterialGroupIndex];
                        Float3 albedo;
                        Float3 emissive = mat is not null ? mat.Emissive : Float3.Zero;
                        if (_options.IgnoreAlbedo)
                        {
                            albedo = Float3.One;
                        }
                        else
                        {
                            albedo = mat is not null ? mat.DiffuseColor : new Float3(0.7f);
                            if (mat is not null && mat.DiffuseTexture is not null)
                            {
                                var samp = mat.DiffuseTexture.SampleLinearRGBA(s.UV0.X, s.UV0.Y);
                                albedo = albedo * new Float3(samp.X, samp.Y, samp.Z);
                            }
                        }
                        if (mat is not null && mat.EmissiveTexture is not null)
                        {
                            var samp = mat.EmissiveTexture.SampleLinearRGBA(s.UV0.X, s.UV0.Y);
                            emissive = emissive * new Float3(samp.X, samp.Y, samp.Z);
                        }
                        // No texel-albedo multiply here: the lightmap stores incoming irradiance;
                        // the runtime shader multiplies by the texel's diffuse texture.
                        _ = albedo;
                        var direct = (_scene.Lights.Count > 0 && _options.IncludeDirectLighting)
                            ? integrator.ComputeBakedDirectLighting(s.Position, s.Normal)
                            : Float3.Zero;
                        ws.DirectCache![idx] = direct + emissive;
                    }
                });
            }
            catch (System.OperationCanceledException) { throw; }
        }

        // ---- temporal accumulation loop -------------------------------------------------------
        int samplesPerIter = System.Math.Max(1, _options.SamplesPerIteration);
        ulong baseSeed = _options.Seed;
        int bounces = System.Math.Max(0, _options.Bounces);

        while (!_cts.IsCancellationRequested)
        {
            int iter = _iterationCount + 1;
            _activity = $"Continuous iter {iter} ({samplesPerIter}s x {bounces} bounces)";
            ulong iterMix = (ulong)iter * 0x6A09E667F3BCC908UL;

            for (int ti = 0; ti < workspaces.Length; ti++)
            {
                if (_cts.IsCancellationRequested) break;
                var ws = workspaces[ti];
                int W = ws.Width, H = ws.Height;
                var sums = ws.IndirectSum!;
                var counts = ws.IndirectSampleCount!;
                var direct = ws.DirectCache!;
                var pixels = ws.Target.PixelsRGB;

                try
                {
                    System.Threading.Tasks.Parallel.For(0, H, parallelOpts, y =>
                    {
                        for (int x = 0; x < W; x++)
                        {
                            int idx = y * W + x;
                            if (!ws.Covered[idx]) continue;
                            var s = ws.Samples[idx];
                            int blasIdx = instanceToBlas[s.InstanceIndex];
                            var mat = resolvedMats[blasIdx][s.MaterialGroupIndex];
                            Float3 albedo;
                            if (_options.IgnoreAlbedo)
                            {
                                albedo = Float3.One;
                            }
                            else
                            {
                                albedo = mat is not null ? mat.DiffuseColor : new Float3(0.7f);
                                if (mat is not null && mat.DiffuseTexture is not null)
                                {
                                    var samp = mat.DiffuseTexture.SampleLinearRGBA(s.UV0.X, s.UV0.Y);
                                    albedo = albedo * new Float3(samp.X, samp.Y, samp.Z);
                                }
                            }

                            // Per-texel-per-iter deterministic seed so paths are reproducible.
                            ulong seed = ((ulong)x * 0x9E3779B97F4A7C15UL ^ ((ulong)y * 0xC6BC279692B5C323UL))
                                       ^ baseSeed ^ iterMix;
                            var rng = new Sampling.Sampler(seed);

                            Float3 ind = integrator.IntegrateIndirect(s.Position, s.Normal, albedo,
                                                                       samplesPerIter, bounces, ref rng,
                                                                       jitterWorldRadius: s.WorldRadius);
                            int contributionCount = samplesPerIter;
                            if (x == DebugTexelX && y == DebugTexelY && object.ReferenceEquals(_debugTarget, ws.Target))
                            {
                                var segs = new System.Collections.Generic.List<DebugSegment>(8);
                                integrator.RecordDirectShadowRays(s.Position, s.Normal, segs);
                                var debugRng = new Sampling.Sampler(seed);
                                integrator.TracePathRecorded(s.Position, s.Normal, Float3.One, bounces, ref debugRng, segs, s.WorldRadius);
                                PublishDebugSegments(segs);
                            }
                            sums[idx] = sums[idx] + ind;
                            counts[idx] += contributionCount;

                            // Refresh the display pixel: direct (cached) + indirect average.
                            int n = counts[idx];
                            var avg = n > 0 ? sums[idx] * (1f / n) : Float3.Zero;
                            var final = direct[idx] + avg;
                            pixels[idx * 3    ] = final.X;
                            pixels[idx * 3 + 1] = final.Y;
                            pixels[idx * 3 + 2] = final.Z;
                        }
                    });
                }
                catch (System.OperationCanceledException) { return; }
            }

            // Optional dilation pass so chart seams don't bleed black while the viewer polls. We
            // dilate a fresh COPY of Covered so the bake's own coverage state stays untouched.
            if (_options.DilatePixels > 0)
            {
                for (int ti = 0; ti < workspaces.Length; ti++)
                {
                    if (_cts.IsCancellationRequested) return;
                    var ws = workspaces[ti];
                    var coveredCopy = (bool[])ws.Covered.Clone();
                    Imaging.Dilate.Run(ws.Target.PixelsRGB, coveredCopy, ws.Width, ws.Height, _options.DilatePixels);
                }
            }

            // Publish: the viewer polls IterationCount and re-uploads when it changes.
            _iterationCount = iter;
            _iterationComplete?.Invoke(iter);
        }
    }

    /// <summary>
    /// Surfel-mode continuous bake. Each iter: every surfel casts a few uniform-hemisphere rays and
    /// projects the radiance arriving along them into its directional SH-L1 accumulator. A ray's
    /// radiance is one bounce - direct lighting at the hit plus the indirect cached in the cloud
    /// from previous iterations - so light propagates one surfel-hop per pass and converges to full
    /// multi-bounce GI. Surfels are then interpolated onto every covered texel, blended in SH space
    /// and reconstructed for the texel's own normal, with optional per-texel direct lighting on top.
    /// </summary>
    private void IntegrateContinuousSurfel(
        TargetWorkspace[] workspaces, Tlas tlas,
        BakeInstance[] instances, Blas[] blas, int[] instanceToBlas,
        BakeMaterial?[][] resolvedMats,
        Surfels.SurfelCloud cloud)
    {
        var integrator = new PathIntegrator(tlas, _scene, instances, blas, instanceToBlas, resolvedMats, _options);
        var parallelOpts = new System.Threading.Tasks.ParallelOptions
        {
            CancellationToken = _cts.Token,
            MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism > 0
                ? _options.MaxDegreeOfParallelism : System.Environment.ProcessorCount,
        };

        var surfels = cloud.Surfels;
        int N = surfels.Length;

        _activity = $"Surfels: allocate ({N} surfels)";
        for (int ti = 0; ti < workspaces.Length; ti++) workspaces[ti].AllocateContinuousBuffers();

        ulong baseSeed = _options.Seed;
        int bounces = System.Math.Max(0, _options.Bounces);
        int samplesPerIter = System.Math.Max(1, _options.SamplesPerIteration);
        int maxNeighbors = System.Math.Max(1, _options.SurfelMaxNeighbors);
        float normalThr = System.Math.Clamp(_options.SurfelNormalThreshold, -1f, 1f);
        bool doIndirect = bounces > 0;

        while (!_cts.IsCancellationRequested)
        {
            int iter = _iterationCount + 1;
            _activity = $"Surfels iter {iter}: {N} surfels x {samplesPerIter}s (progressive multi-bounce)";
            ulong iterMix = (ulong)iter * 0x6A09E667F3BCC908UL;

            if (doIndirect)
            {
                // (1) Gather one bounce per surfel into its SH accumulator. Each ray's radiance is
                //     direct lighting at its hit plus the indirect already cached in the cloud from
                //     PREVIOUS iterations (read via the frozen ShEstimate snapshot). So light
                //     advances one surfel-hop per pass and converges to full multi-bounce GI over
                //     time -- no deep per-ray paths needed.
                try
                {
                    System.Threading.Tasks.Parallel.For(0, N, parallelOpts, i =>
                    {
                        ref var s = ref surfels[i];
                        ulong seed = (ulong)i * 0x9E3779B97F4A7C15UL ^ baseSeed ^ iterMix;
                        var rng = new Sampler(seed);
                        var sh = integrator.IntegrateSurfelRadiance(
                            s.Position, s.Normal, samplesPerIter, ref rng,
                            cloud, normalThr, maxNeighbors);
                        s.ShAccum.Add(sh);
                        s.SampleCount += samplesPerIter;
                    });
                }
                catch (System.OperationCanceledException) { return; }

                // (2) Publish the refreshed mean. Done as a separate barrier'd pass so the gather
                //     above only ever reads last iteration's estimate, never a half-written one.
                try
                {
                    System.Threading.Tasks.Parallel.For(0, N, parallelOpts, i =>
                    {
                        ref var s = ref surfels[i];
                        s.ShEstimate = s.SampleCount > 0 ? s.ShAccum.Scaled(1f / s.SampleCount) : default;
                    });
                }
                catch (System.OperationCanceledException) { return; }
            }

            // (3) Interpolate surfels onto every covered texel. SampleIrradianceOverPi reads the
            //     single spatial-grid cell the texel sits in (surfels are pre-registered in every
            //     cell their influence sphere overlaps), blends the nearby surfels in SH space and
            //     reconstructs irradiance for THIS texel's normal -- so a surfel feeds texels of
            //     differing orientation without leaking.
            _activity = $"Surfels iter {iter}: interpolate to texels";
            for (int ti = 0; ti < workspaces.Length; ti++)
            {
                if (_cts.IsCancellationRequested) return;
                var ws = workspaces[ti];
                int W = ws.Width, H = ws.Height;
                var pixels = ws.Target.PixelsRGB;
                try
                {
                    System.Threading.Tasks.Parallel.For(0, H, parallelOpts, y =>
                    {
                        for (int x = 0; x < W; x++)
                        {
                            int idx = y * W + x;
                            if (!ws.Covered[idx]) continue;
                            var ts = ws.Samples[idx];

                            Float3 final = doIndirect
                                ? cloud.SampleIrradianceOverPi(ts.Position, ts.Normal, normalThr, maxNeighbors)
                                : Float3.Zero;
                            // Direct lighting always evaluated per-texel when enabled, regardless of
                            // whether any surfels contributed -- a texel in an empty surfel cell still
                            // gets correct sun lighting, no "interpolation hole" blackouts.
                            if (_options.IncludeDirectLighting)
                                final += integrator.ComputeBakedDirectLighting(ts.Position, ts.Normal);
                            pixels[idx * 3    ] = final.X;
                            pixels[idx * 3 + 1] = final.Y;
                            pixels[idx * 3 + 2] = final.Z;
                        }
                    });
                }
                catch (System.OperationCanceledException) { return; }
            }

            // (4) Dilate seams (same as the per-texel path).
            if (_options.DilatePixels > 0)
            {
                for (int ti = 0; ti < workspaces.Length; ti++)
                {
                    if (_cts.IsCancellationRequested) return;
                    var ws = workspaces[ti];
                    var coveredCopy = (bool[])ws.Covered.Clone();
                    Imaging.Dilate.Run(ws.Target.PixelsRGB, coveredCopy, ws.Width, ws.Height, _options.DilatePixels);
                }
            }

            _iterationCount = iter;
            _iterationComplete?.Invoke(iter);
        }
    }

    private static void RasterizeTarget(TargetWorkspace ws, BakeInstance[] allInstances)
    {
        for (int i = 0; i < allInstances.Length; i++)
        {
            var inst = allInstances[i];
            if (inst.Target != ws.Target) continue;
            if (!inst.ReceivesLighting) continue;
            var mesh = inst.Mesh;
            if (!mesh.UVLayers.TryGetValue(inst.BakeUVLayer, out var bakeUVs)) continue;
            if (!mesh.UVLayers.TryGetValue("UV0", out var uv0)) uv0 = bakeUVs;

            var positions = mesh.Positions;
            var normals = mesh.Normals;
            for (int g = 0; g < mesh.MaterialGroups.Count; g++)
            {
                var grp = mesh.MaterialGroups[g];
                var idx = grp.Indices;
                for (int k = 0; k < idx.Length; k += 3)
                {
                    int i0 = idx[k], i1 = idx[k + 1], i2 = idx[k + 2];
                    ConservativeTexelMapper.MapTriangle(ws, inst, i,
                        positions[i0], positions[i1], positions[i2],
                        normals[i0],   normals[i1],   normals[i2],
                        bakeUVs[i0],   bakeUVs[i1],   bakeUVs[i2],
                        uv0[i0],       uv0[i1],       uv0[i2],
                        g);
                }
            }
        }
    }

}
