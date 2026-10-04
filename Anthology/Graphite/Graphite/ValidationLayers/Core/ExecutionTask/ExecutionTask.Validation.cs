namespace Prowl.Graphite;

public abstract partial class ExecutionTask
{
    private protected static void SubmitCommands_CheckEnded(GraphicsDevice gd, CommandBuffer commandList)
    {
        if (!gd.ValidationEnabled)
            return;

        if (!commandList.HasEnded)
        {
            throw new RenderException("CommandBuffer.End() must be called before submitting.");
        }
    }

    private protected static void CheckCumulativeCaps_CheckHardCap(GraphicsDevice gd, ulong cumulative, ulong hardCapBytes)
    {
        if (!gd.ValidationEnabled)
            return;

        if (cumulative > hardCapBytes)
        {
            throw new RenderException($"Transient buffer hard cap of {hardCapBytes} bytes exceeded.");
        }
    }
}
