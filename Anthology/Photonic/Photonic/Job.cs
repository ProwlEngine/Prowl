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
            // 1) Build BLAS per unique mesh -----------------------------------------------------
            var meshToBlas = new System.Collections.Generic.Dictionary<BakeMesh, int>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            var blasList = new System.Collections.Generic.List<Blas>();
            // collect all instances from all targets so the TLAS sees every potential occluder
            var allInstances = new System.Collections.Generic.List<BakeInstance>();
            for (int t = 0; t < _targets.Length; t++)
                allInstances.AddRange(_targets[t].Instances);

            foreach (var inst in allInstances)
            {
                if (!meshToBlas.ContainsKey(inst.Mesh))
                {
                    meshToBlas[inst.Mesh] = blasList.Count;
                    var b = new Blas(inst.Mesh);
                    b.Build();
                    blasList.Add(b);
                }
            }

            var instances = allInstances.ToArray();
            var blas = blasList.ToArray();
            var instanceToBlas = new int[instances.Length];
            for (int i = 0; i < instances.Length; i++) instanceToBlas[i] = meshToBlas[instances[i].Mesh];

            // Pre-resolve materials per (BLAS, material-group) to remove the dictionary lookup from
            // the per-texel / per-bounce hot path.
            var resolvedMats = new BakeMaterial?[blas.Length][];
            for (int bi = 0; bi < blas.Length; bi++)
            {
                var groups = blas[bi].Mesh.MaterialGroups;
                var arr = new BakeMaterial?[groups.Count];
                for (int g = 0; g < arr.Length; g++) arr[g] = _scene.FindMaterial(groups[g].MaterialName);
                resolvedMats[bi] = arr;
            }

            _cts.Token.ThrowIfCancellationRequested();
            _activity = "Build TLAS";

            // 2) Build TLAS over instances ------------------------------------------------------
            var refs = new Tlas.InstanceRef[instances.Length];
            for (int i = 0; i < instances.Length; i++)
            {
                var w = instances[i].WorldTransform;
                if (!Float4x4.Invert(w, out var inv)) inv = Float4x4.Identity;
                refs[i] = new Tlas.InstanceRef
                {
                    InstanceIndex = i,
                    BlasIndex = instanceToBlas[i],
                    WorldFromLocal = w,
                    LocalFromWorld = inv,
                    IsIdentity = IsIdentityTransform(w),
                };
            }
            var tlas = new Tlas();
            tlas.Build(refs, blas);

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
                            ? integrator.ComputeDirectLighting(s.Position, s.Normal)
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
    /// Surfel-mode continuous bake. Each iter: trace one path sample per surfel, accumulate into
    /// the surfel's own running average. At end of iter, splat surfels onto every covered texel
    /// via a normal-weighted, distance-weighted kernel using the spatial grid for fast neighbour
    /// lookups. Optional per-texel direct lighting added on top.
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

        while (!_cts.IsCancellationRequested)
        {
            int iter = _iterationCount + 1;
            _activity = $"Surfels iter {iter}: {N} surfels x {samplesPerIter}s x {bounces} bounces";
            ulong iterMix = (ulong)iter * 0x6A09E667F3BCC908UL;

            // (1) Trace samplesPerIter indirect paths per surfel; fold into its running sum.
            try
            {
                System.Threading.Tasks.Parallel.For(0, N, parallelOpts, i =>
                {
                    ref var s = ref surfels[i];
                    ulong seed = (ulong)i * 0x9E3779B97F4A7C15UL ^ baseSeed ^ iterMix;
                    var rng = new Sampler(seed);
                    var ind = integrator.IntegrateIndirect(
                        s.Position, s.Normal, Float3.One,
                        samplesPerIter, bounces, ref rng,
                        jitterWorldRadius: 0f);
                    s.IndirectSum += ind;
                    s.SampleCount += samplesPerIter;
                });
            }
            catch (System.OperationCanceledException) { return; }

            // (2) Interpolate surfels onto every covered texel. For each texel: read the single
            //     spatial-grid cell it sits in (surfels are pre-registered in every cell their
            //     influence sphere overlaps, so this catches every surfel that could reach the
            //     texel). Weight by per-surfel radius falloff: w = (1 - d/R)^2 * dotN.
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
                        System.Span<int> bestIdx = stackalloc int[64];
                        System.Span<float> bestW  = stackalloc float[64];
                        System.Span<int> candidateBuf = stackalloc int[1024];
                        int cap = System.Math.Min(64, maxNeighbors);

                        for (int x = 0; x < W; x++)
                        {
                            int idx = y * W + x;
                            if (!ws.Covered[idx]) continue;
                            var ts = ws.Samples[idx];
                            int kept = 0;

                            int candidateCount = cloud.QueryCell(ts.Position, candidateBuf);
                            for (int ci = 0; ci < candidateCount; ci++)
                            {
                                int sIdx = candidateBuf[ci];
                                ref var s = ref surfels[sIdx];
                                if (s.SampleCount <= 0) continue;
                                float dotN = Float3.Dot(ts.Normal, s.Normal);
                                if (dotN < normalThr) continue;
                                var d = s.Position - ts.Position;
                                float dsq = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
                                float r = s.Radius;
                                if (dsq > r * r) continue;
                                float dist = (float)System.Math.Sqrt(dsq);
                                float falloff = 1f - dist / r;
                                float w = dotN * falloff * falloff;
                                if (w <= 0) continue;
                                if (kept < cap)
                                {
                                    bestIdx[kept] = sIdx; bestW[kept] = w; kept++;
                                }
                                else
                                {
                                    int worst = 0;
                                    for (int j = 1; j < cap; j++) if (bestW[j] < bestW[worst]) worst = j;
                                    if (w > bestW[worst]) { bestIdx[worst] = sIdx; bestW[worst] = w; }
                                }
                            }

                            Float3 indirect = Float3.Zero;
                            float wsum = 0f;
                            for (int j = 0; j < kept; j++)
                            {
                                ref var s = ref surfels[bestIdx[j]];
                                float w = bestW[j];
                                indirect += s.IndirectSum * (w / s.SampleCount);
                                wsum += w;
                            }
                            Float3 final = wsum > 0 ? indirect * (1f / wsum) : Float3.Zero;
                            // Direct lighting always evaluated per-texel when enabled, regardless of
                            // whether any surfels contributed -- a texel in an empty surfel cell still
                            // gets correct sun lighting, no "interpolation hole" blackouts.
                            if (_options.IncludeDirectLighting)
                                final += integrator.ComputeDirectLighting(ts.Position, ts.Normal);
                            pixels[idx * 3    ] = final.X;
                            pixels[idx * 3 + 1] = final.Y;
                            pixels[idx * 3 + 2] = final.Z;
                        }
                    });
                }
                catch (System.OperationCanceledException) { return; }
            }

            // (3) Dilate seams (same as the per-texel path).
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

    private static bool IsIdentityTransform(Float4x4 m)
    {
        const float E = 1e-6f;
        return System.Math.Abs(m.c0.X - 1) < E && System.Math.Abs(m.c0.Y) < E && System.Math.Abs(m.c0.Z) < E && System.Math.Abs(m.c0.W) < E
            && System.Math.Abs(m.c1.X) < E && System.Math.Abs(m.c1.Y - 1) < E && System.Math.Abs(m.c1.Z) < E && System.Math.Abs(m.c1.W) < E
            && System.Math.Abs(m.c2.X) < E && System.Math.Abs(m.c2.Y) < E && System.Math.Abs(m.c2.Z - 1) < E && System.Math.Abs(m.c2.W) < E
            && System.Math.Abs(m.c3.X) < E && System.Math.Abs(m.c3.Y) < E && System.Math.Abs(m.c3.Z) < E && System.Math.Abs(m.c3.W - 1) < E;
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
