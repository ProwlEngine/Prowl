using System.Linq;

namespace Prowl.Graphite.Debugger.Viewer;

internal readonly record struct FrameMetrics(
    long Draws,
    long IndirectDraws,
    long Dispatches,
    long ShaderSwitches,
    long PipelineBinds,
    long ResourceSetBinds,
    long ResourceSetsBound,
    long PassBarriers,
    long Passes,
    ulong Vertices,
    ulong Triangles,
    ulong Clipped,
    ulong Fragments,
    ulong VramUsage,
    ulong VramBudget,
    long LiveDelta,
    long Maps,
    long MappedAndUpdatedBytes,
    long DeviceBarriers)
{
    public static readonly string[] AllocBins = { "DeviceBuffer", "Texture", "TextureView", "Sampler", "Framebuffer", "Pipeline", "Shader", "ResourceLayout", "ResourceSet", "CommandBuffer" };
    public static readonly string[] BufferRoleBins = { "Vertex", "Index", "Uniform", "StructuredReadOnly", "StructuredReadWrite", "Indirect", "Staging", "Dynamic" };
    public static readonly string[] BarrierBins = { "TextureTransition", "BufferTransition", "MemoryBarrier" };
    public static readonly string[] SwapBins = { "Present", "Resize", "Acquire" };
    public static readonly string[] BufferOpBins = { "Map", "Unmap", "Update", "Copy" };

    public const int Map = 0, Unmap = 1, Update = 2, Copy = 3;

    public static FrameMetrics From(Recording recording)
    {
        long draws = 0, indirect = 0, dispatches = 0, switches = 0, binds = 0, setBinds = 0, barriers = 0, passes = 0;
        foreach (RecordedView view in recording.Views)
            foreach (RecordedPass pass in view.Passes)
            {
                draws += pass.Stats.Draws;
                indirect += pass.Stats.IndirectDraws;
                dispatches += pass.Stats.Dispatches;
                switches += pass.Stats.ShaderSwitches;
                binds += pass.Stats.PipelineBinds;
                setBinds += pass.Stats.ResourceSetBinds;
                barriers += pass.Stats.Barriers;
                passes++;
            }

        ulong vertices = 0, triangles = 0, clipped = 0, fragments = 0;
        foreach (RecordedCommandBuffer buffer in recording.CommandBuffers)
            if (buffer.VertexStats is { } stats)
            {
                vertices += stats.InputAssemblyVertices;
                triangles += stats.InputAssemblyPrimitives;
                clipped += stats.ClippingPrimitives;
                fragments += stats.FragmentShaderInvocations;
            }

        RecordedCounters? c = recording.Counters;
        return new FrameMetrics(
            draws, indirect, dispatches, switches, binds,
            c?.ResourceSetBinds ?? setBinds,
            c?.ResourceSetsBound ?? 0,
            barriers, passes, vertices, triangles, clipped, fragments,
            c?.MemoryBudget.UsageBytes ?? 0,
            c?.MemoryBudget.BudgetBytes ?? 0,
            c?.Live.Sum() ?? 0,
            c != null ? c.BufferOps[Map] : 0,
            c != null ? c.BufferOpBytes[Map] + c.BufferOpBytes[Update] : 0,
            c?.Barriers.Sum() ?? 0);
    }
}
