using System.Collections.Generic;

namespace Prowl.Graphite.RenderGraph;

/// <summary>
/// Timing and capture hooks the framework calls while running a graph.
/// </summary>
public interface IPassProfiler
{
    /// <summary>Opens a nested sample.</summary>
    void BeginSample(string name);

    /// <summary>Closes the last sample.</summary>
    void EndSample();

    /// <summary>If true, pipeline calls Capture after each pass.</summary>
    bool RequestCapture { get; }

    /// <summary>
    /// Called between passes when RequestCapture is true. Gives you a transfer buffer to copy
    /// pass outputs to an intermediate texture.
    /// </summary>
    void Capture(IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer);
}
