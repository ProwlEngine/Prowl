namespace Prowl.Graphite;

public abstract partial class Frame
{
    private protected static void SubmitCommands_CheckEnded(CommandBuffer commandList)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (!commandList.HasEnded)
        {
            throw new RenderException("CommandBuffer.End() must be called before submitting.");
        }
    }

    private protected static void CheckCumulativeCaps_CheckHardCap(ulong cumulative, ulong hardCapBytes)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (cumulative > hardCapBytes)
        {
            throw new RenderException($"Transient buffer hard cap of {hardCapBytes} bytes exceeded.");
        }
    }
}
