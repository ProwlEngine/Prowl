using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger.Recording;

public sealed record LightPass(
    string Name,
    int Index,
    int ViewIndex,
    PassStats Stats,
    EquatableArray<string> Inputs,
    EquatableArray<string> Outputs);

public sealed record LightView(
    string Name,
    int Index,
    uint PixelWidth,
    uint PixelHeight,
    EquatableArray<LightPass> Passes);

public sealed record LightCommandBuffer(
    ulong Id,
    string Name,
    bool IsTransfer,
    int? ViewIndex,
    int? PassIndex,
    double GpuMilliseconds,
    GpuVertexStats? VertexStats);

public sealed record LightFrame(
    ulong ExecutionId,
    EquatableArray<LightView> Views,
    EquatableArray<LightCommandBuffer> CommandBuffers)
{
    public double PassGpuMilliseconds(int viewIndex, int passIndex)
    {
        double total = 0;
        foreach (LightCommandBuffer commandBuffer in CommandBuffers)
        {
            if (commandBuffer.ViewIndex == viewIndex && commandBuffer.PassIndex == passIndex)
            {
                total += commandBuffer.GpuMilliseconds;
            }
        }

        return total;
    }

    public double TotalGpuMilliseconds
    {
        get
        {
            double total = 0;
            foreach (LightCommandBuffer commandBuffer in CommandBuffers)
            {
                total += commandBuffer.GpuMilliseconds;
            }

            return total;
        }
    }
}
