namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsProgram
{
    private void DisposeCore_RecordFrees(int pipelineCount)
    {
        if (_gd.Profiler is not { } profiler)
            return;

        for (int i = 0; i < pipelineCount; i++)
            profiler.Free(AllocBin.Pipeline, 0);
    }
}
