using System.Diagnostics;

namespace NeoVeldrid.Vk;

internal partial class ResourceRefCount
{
    [Conditional("VALIDATE_USAGE")]
    private static void Increment_CheckNotDisposed(int ret)
    {
#if VALIDATE_USAGE
        if (ret == 0)
        {
            throw new NeoVeldridException("An attempt was made to reference a disposed resource.");
        }
#endif
    }
}
