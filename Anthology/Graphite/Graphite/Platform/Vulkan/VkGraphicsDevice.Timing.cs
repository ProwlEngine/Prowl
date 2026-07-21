using System.Collections.Concurrent;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

// GPU execution timing: a timestamp-query pair bracketing a command buffer's recorded commands,
// resolved once the buffer's submission fence completes. Pools are 2-query TIMESTAMP query pools,
// reused across submissions like the submission fences in VkGraphicsDevice.Submission.cs.
internal unsafe partial class VkGraphicsDevice
{
    private readonly ConcurrentQueue<QueryPool> _availableTimingQueryPools = new();

    // Writes the "start" timestamp into a fresh pool if the attached profiler wants execution
    // timing this submission; null otherwise. Must be called during recording, before End().
    internal QueryPool? BeginTiming(Silk.NET.Vulkan.CommandBuffer cb)
    {
        if (Profiler is not { RequestExecutionTiming: true })
            return null;

        QueryPool pool = GetFreeTimingQueryPool();
        _vk.CmdResetQueryPool(cb, pool, 0, 2);
        _vk.CmdWriteTimestamp(cb, PipelineStageFlags.TopOfPipeBit, pool, 0);
        return pool;
    }

    // Writes the "end" timestamp. Must be called during recording, right before End().
    internal void EndTiming(Silk.NET.Vulkan.CommandBuffer cb, QueryPool? pool)
    {
        if (pool is not { } p)
            return;

        _vk.CmdWriteTimestamp(cb, PipelineStageFlags.BottomOfPipeBit, p, 1);
    }

    // Blocks until both timestamps are available (the caller only reaches here once the
    // submission's fence has already signaled, so this does not actually wait on the GPU),
    // converts the tick delta to milliseconds, and returns the pool to the free list.
    internal double ResolveTiming(QueryPool pool)
    {
        ulong* timestamps = stackalloc ulong[2];
        _vk.GetQueryPoolResults(
            _device, pool, 0, 2,
            (nuint)(sizeof(ulong) * 2), timestamps, sizeof(ulong),
            QueryResultFlags.ResultWaitBit | QueryResultFlags.Result64Bit).CheckResult();

        double ticks = timestamps[1] > timestamps[0] ? timestamps[1] - timestamps[0] : 0;
        double nanoseconds = ticks * _physicalDeviceProperties.Limits.TimestampPeriod;

        _availableTimingQueryPools.Enqueue(pool);
        return nanoseconds / 1_000_000.0;
    }

    private QueryPool GetFreeTimingQueryPool()
    {
        if (_availableTimingQueryPools.TryDequeue(out QueryPool pool))
            return pool;

        QueryPoolCreateInfo ci = new(sType: StructureType.QueryPoolCreateInfo)
        {
            QueryType = QueryType.Timestamp,
            QueryCount = 2,
        };
        _vk.CreateQueryPool(_device, in ci, null, out QueryPool newPool).CheckResult();
        return newPool;
    }
}
