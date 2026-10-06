namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsProgram
{
    private void DisposeCore_RecordFrees(int pipelineCount)
    {
        for (int i = 0; i < pipelineCount; i++)
            _gd.Counters.Free(AllocBin.Pipeline, 0);
    }
}
