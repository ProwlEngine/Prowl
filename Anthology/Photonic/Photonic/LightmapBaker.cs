using Prowl.Vector;
using Prowl.Photonic.Integration;
using Prowl.Photonic.Sampling;

namespace Prowl.Photonic;

/// <summary>
/// Top-level entry point: one baker per bake. Owns a <see cref="BakeScene"/>, a set of
/// <see cref="LightmapTarget"/>s, and the running <see cref="Job"/>.
/// </summary>
/// <example>
/// <code>
/// using var baker = new LightmapBaker();
/// var scene = baker.BeginScene("Bake");
/// var mesh  = scene.BeginMesh("floor")
///                  .AddVertices(positions, normals)
///                  .AddUVLayer("UV0", materialUVs)
///                  .AddUVLayer("UV1", lightmapUVs)
///                  .AddMaterialGroup("mat0", indices)
///                  .End();
/// scene.CreatePointLight("sun", Float4x4.CreateTranslation(0, 5, 0),
///                        new Float3(10, 10, 10), range: 20f);
/// var target = baker.CreateTextureTarget("page0", 512, 512);
/// target.AddBakeInstance(mesh, Float4x4.Identity);
/// scene.End();
/// var job = baker.Start();
/// job.Wait();
/// var hdr = target.ReadHDR();
/// </code>
/// </example>
public sealed class LightmapBaker : System.IDisposable
{
    private BakeScene? _scene;
    private readonly System.Collections.Generic.List<LightmapTarget> _targets = new();
    private Job? _job;

    /// <summary>Global bake settings. Tweak before calling <see cref="Start"/>.</summary>
    public BakeOptions Options { get; } = new();

    /// <summary>The scene attached to this baker, or null if <see cref="BeginScene"/> hasn't run yet.</summary>
    public BakeScene? Scene => _scene;

    /// <summary>Texture targets created on this baker.</summary>
    public System.Collections.Generic.IReadOnlyList<LightmapTarget> Targets => _targets;

    /// <summary>The active job after <see cref="Start"/>, or null if no job has been started.</summary>
    public Job? Job => _job;

    /// <summary>Create the (single) scene this baker owns.</summary>
    public BakeScene BeginScene(string name)
    {
        if (_scene is not null) throw new System.InvalidOperationException("Scene already begun on this baker.");
        _scene = new BakeScene(name);
        return _scene;
    }

    /// <summary>Allocate a new texture target (one atlas page). Width/height should match the UV1 packing.</summary>
    public LightmapTarget CreateTextureTarget(string name, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(width), "Target dimensions must be positive.");
        var t = new LightmapTarget(name, width, height);
        _targets.Add(t);
        return t;
    }

    /// <summary>
    /// Kick off the bake on a background thread. Use <see cref="Job.Poll"/> /
    /// <see cref="Job.OnIterationComplete"/> / <see cref="Job.Activity"/> to drive a UI.
    /// </summary>
    public Job Start()
    {
        if (_job is not null) throw new System.InvalidOperationException("Bake already started.");
        if (_scene is null)   throw new System.InvalidOperationException("No scene has been begun.");
        if (!_scene.Ended)    throw new System.InvalidOperationException("Scene was not ended (call BakeScene.End()).");
        if (_targets.Count == 0) throw new System.InvalidOperationException("No targets to bake into.");

        _job = Job.Start(_scene, _targets, Options);
        return _job;
    }

    /// <summary>Synchronously cancel any running job. Safe to call from any thread.</summary>
    public void Cancel() => _job?.Cancel();

    public void Dispose() => _job?.Cancel();

    /// <summary>
    /// Raised for non-fatal bake issues (e.g. a mesh with no normals). Subscribe to surface them in
    /// an editor log. May fire on the calling thread (scene build) or a worker thread.
    /// </summary>
    public static event System.Action<string>? Warning;

    internal static void RaiseWarning(string message) => Warning?.Invoke(message);

    /// <summary>
    /// Bake light-probe spherical harmonics (9-coefficient RGB, SH-L2) at the given world positions.
    /// Each probe captures the full directional radiance environment so dynamic (non-lightmapped)
    /// objects can reconstruct ambient irradiance for any normal (see <see cref="Sh9Rgb.EvaluateIrradiance"/>).
    ///
    /// <para>Blocking and synchronous (probe counts are normally modest and this is an editor-time
    /// operation). Uses the same scene geometry, lights, bounce count, and environment as a lightmap
    /// bake — the instances added to this baker's targets act as the occluders, so call after the
    /// scene + targets are set up (<see cref="BakeScene.End"/> must have run).</para>
    /// </summary>
    /// <param name="positions">World-space probe positions.</param>
    /// <param name="samplesPerProbe">Uniform-sphere rays traced per probe. Higher = smoother SH, slower.</param>
    /// <param name="bounces">Indirect bounces; defaults to <see cref="BakeOptions.Bounces"/> when null.</param>
    public Sh9Rgb[] BakeProbes(System.Collections.Generic.IReadOnlyList<Float3> positions,
                               int samplesPerProbe = 256, int? bounces = null)
    {
        if (positions is null) throw new System.ArgumentNullException(nameof(positions));
        if (_scene is null) throw new System.InvalidOperationException("No scene has been begun.");
        if (!_scene.Ended) throw new System.InvalidOperationException("Scene was not ended (call BakeScene.End()).");

        var result = new Sh9Rgb[positions.Count];
        if (positions.Count == 0) return result;

        // All instances across all targets act as occluders / bounce surfaces for the probes.
        var allInstances = new System.Collections.Generic.List<BakeInstance>();
        for (int t = 0; t < _targets.Count; t++)
            allInstances.AddRange(_targets[t].Instances);
        if (allInstances.Count == 0)
            throw new System.InvalidOperationException(
                "BakeProbes needs scene geometry: add mesh instances to a target (they act as occluders) before baking probes.");

        var accel = BakeAcceleration.Build(_scene, allInstances.ToArray());
        var integrator = new PathIntegrator(_scene, accel.Blas, accel.MergedMats, Options);

        int b = System.Math.Max(0, bounces ?? Options.Bounces);
        int samples = System.Math.Max(1, samplesPerProbe);
        ulong baseSeed = Options.Seed ^ 0x5DEECE66D1234567UL;

        var parallelOpts = new System.Threading.Tasks.ParallelOptions
        {
            MaxDegreeOfParallelism = Options.MaxDegreeOfParallelism > 0
                ? Options.MaxDegreeOfParallelism
                : System.Environment.ProcessorCount,
        };

        // Snapshot positions to an array for thread-safe indexed access.
        var pos = new Float3[positions.Count];
        for (int i = 0; i < pos.Length; i++) pos[i] = positions[i];

        System.Threading.Tasks.Parallel.For(0, pos.Length, parallelOpts, i =>
        {
            var rng = new Sampler((ulong)i * 0x9E3779B97F4A7C15UL ^ baseSeed);
            result[i] = integrator.IntegrateProbe(pos[i], samples, b, ref rng);
        });

        return result;
    }
}
