using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Small runtime profiler hooks so a profiler can read engine-specific rendering data.
/// </summary>
public static class RenderProfilerHooks
{
    public static IRenderProfilerSink? Sink;
}


/// <summary>
/// Profiler sink interface for runtime rendering data.
/// </summary>
public interface IRenderProfilerSink
{
    void BeginFrame();
    void EndFrame();
    void BeginView(string name);
    void EndView();

    /// <summary>One per <see cref="IRenderable"/> considered in a pass draw loop.</summary>
    void Renderable(in RenderableRecord r);

    /// <summary>Emitted just before <c>cmd.SetShader</c> at a bind site.</summary>
    void ShaderBind(in ShaderBindRecord r);
}


public readonly struct RenderableRecord
{
    public string MaterialName { get; init; }
    public string MeshName { get; init; }
    public int Layer { get; init; }
    public Float3 Position { get; init; }
    public bool Registered { get; init; }
    public bool Culled { get; init; }

    /// <summary>Draws this object emitted this pass (0 if culled/skipped).</summary>
    public int DrawCallCount { get; init; }
}
