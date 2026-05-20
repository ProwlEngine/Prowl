using Prowl.Vector;

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
}
