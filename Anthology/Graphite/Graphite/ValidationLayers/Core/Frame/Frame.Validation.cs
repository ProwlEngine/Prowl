using System.Diagnostics;

namespace Prowl.Graphite;

public abstract partial class Frame
{
    [Conditional("VALIDATE_USAGE")]
    private protected static void SubmitCommands_CheckEnded(CommandBuffer commandList)
    {
#if VALIDATE_USAGE
        if (!commandList.HasEnded)
        {
            throw new RenderException("CommandBuffer.End() must be called before submitting.");
        }
#endif
    }

    [Conditional("VALIDATE_USAGE")]
    private protected static void CheckCumulativeCaps_CheckHardCap(ulong cumulative, ulong hardCapBytes)
    {
#if VALIDATE_USAGE
        if (cumulative > hardCapBytes)
        {
            throw new RenderException($"Transient buffer hard cap of {hardCapBytes} bytes exceeded.");
        }
#endif
    }
}
