namespace Prowl.Graphite.Vk;

internal partial class ResourceRefCount
{
    private static void Increment_CheckNotDisposed(int ret)
    {
        if (ret == 0)
        {
            throw new RenderException("An attempt was made to reference a disposed resource.");
        }
    }
}
