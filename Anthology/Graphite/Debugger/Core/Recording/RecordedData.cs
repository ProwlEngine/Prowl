using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger;

/// <summary>A pass of a recorded view, with the work its command buffer recorded.</summary>
public sealed record RecordedPass(string Name, int Index, PassStats Stats);

/// <summary>A view of the recorded execution, with its passes in order.</summary>
public sealed record RecordedView(string Name, int Index, uint PixelWidth, uint PixelHeight, EquatableArray<RecordedPass> Passes);

/// <summary>GPU results for one command buffer. View and pass indices are -1 for work outside a pass.</summary>
public sealed record RecordedCommandBuffer(
    ulong Id,
    string Name,
    int ViewIndex,
    int PassIndex,
    double? Milliseconds,
    GpuVertexStats? VertexStats);

/// <summary>The optional features of the recording device.</summary>
public sealed record RecordedFeatures(
    bool GeometryShader,
    bool TessellationShaders,
    bool DrawIndirectBaseInstance,
    bool SamplerAnisotropy,
    bool DepthClipDisable,
    bool IndependentBlend,
    bool CommandBufferDebugMarkers,
    bool ShaderFloat64)
{
    internal static RecordedFeatures From(GraphicsDeviceFeatures f)
        => new(f.GeometryShader, f.TessellationShaders, f.DrawIndirectBaseInstance, f.SamplerAnisotropy, f.DepthClipDisable, f.IndependentBlend, f.CommandBufferDebugMarkers, f.ShaderFloat64);
}
