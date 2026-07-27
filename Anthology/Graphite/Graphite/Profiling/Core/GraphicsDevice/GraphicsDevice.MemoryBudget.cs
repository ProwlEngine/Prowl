namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>
    /// Polls the device's current VRAM budget/usage, as reported by the driver (accounts for other
    /// processes sharing the GPU, not just this process's own tracked allocations). False
    /// <see cref="MemoryBudgetInfo.IsSupported"/> means the backend or device has no way to report this
    /// (e.g. VK_EXT_memory_budget unavailable) - callers should fall back to the profiler's own
    /// Resident/{bin} counters in that case.
    /// </summary>
    public virtual MemoryBudgetInfo GetMemoryBudget() => default;
}

/// <summary>
/// Driver-reported VRAM budget/usage for this device, summed across every device-local memory heap.
/// </summary>
public readonly struct MemoryBudgetInfo
{
    /// <summary>False if the backend/device can't report this - BudgetBytes/UsageBytes are both 0.</summary>
    public bool IsSupported { get; }

    /// <summary>Total bytes the driver currently allows this process to use across device-local heaps.</summary>
    public ulong BudgetBytes { get; }

    /// <summary>Total bytes currently in use across device-local heaps, as estimated by the driver.</summary>
    public ulong UsageBytes { get; }

    public MemoryBudgetInfo(bool isSupported, ulong budgetBytes, ulong usageBytes)
    {
        IsSupported = isSupported;
        BudgetBytes = budgetBytes;
        UsageBytes = usageBytes;
    }
}
