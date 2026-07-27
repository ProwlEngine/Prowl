using System.Collections.Concurrent;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

// GPU vertex/primitive counts: a single pipeline-statistics query bracketing a command buffer's
// recorded commands, resolved once the buffer's submission fence completes. Pools are 1-query
// PIPELINE_STATISTICS query pools, reused across submissions like the timing pools in
// VkGraphicsDevice.Timing.cs - same lifecycle, same "resolved only after the fence already signaled"
// non-blocking behavior.
internal unsafe partial class VkGraphicsDevice
{
    // Only the counters relevant to vertex/primitive/overdraw counting - order here must match ascending
    // bit value among the flags actually requested, since that's the order GetQueryPoolResults writes
    // them in (InputAssemblyVertices=bit0, InputAssemblyPrimitives=bit1, ClippingInvocations=bit5,
    // ClippingPrimitives=bit6, FragmentShaderInvocations=bit7).
    private const QueryPipelineStatisticFlags PipelineStatsFlags =
        QueryPipelineStatisticFlags.InputAssemblyVerticesBit |
        QueryPipelineStatisticFlags.InputAssemblyPrimitivesBit |
        QueryPipelineStatisticFlags.ClippingInvocationsBit |
        QueryPipelineStatisticFlags.ClippingPrimitivesBit |
        QueryPipelineStatisticFlags.FragmentShaderInvocationsBit;

    private readonly ConcurrentQueue<QueryPool> _availablePipelineStatsQueryPools = new();

    // Writes/resets the query into a fresh pool if the device supports pipelineStatisticsQuery and the
    // attached profiler wants execution timing this submission (same gate RequestExecutionTiming
    // already uses for GPU timing - no separate toggle). Must be called during recording, before End().
    internal QueryPool? BeginPipelineStats(Silk.NET.Vulkan.CommandBuffer cb)
    {
        if (!_physicalDeviceFeatures.PipelineStatisticsQuery || Profiler is not { RequestGPUStatistics: true })
            return null;

        QueryPool pool = GetFreePipelineStatsQueryPool();
        _vk.CmdResetQueryPool(cb, pool, 0, 1);
        _vk.CmdBeginQuery(cb, pool, 0, QueryControlFlags.None);
        return pool;
    }

    // Ends the query. Must be called during recording, right before End().
    internal void EndPipelineStats(Silk.NET.Vulkan.CommandBuffer cb, QueryPool? pool)
    {
        if (pool is not { } p)
            return;

        _vk.CmdEndQuery(cb, p, 0);
    }

    // Blocks until the query's results are available (the caller only reaches here once the
    // submission's fence has already signaled, so this does not actually wait on the GPU), and returns
    // the pool to the free list.
    internal GpuVertexStats ResolvePipelineStats(QueryPool pool)
    {
        ulong* results = stackalloc ulong[5];
        _vk.GetQueryPoolResults(
            _device, pool, 0, 1,
            (nuint)(sizeof(ulong) * 5), results, sizeof(ulong) * 5,
            QueryResultFlags.ResultWaitBit | QueryResultFlags.Result64Bit).CheckResult();

        _availablePipelineStatsQueryPools.Enqueue(pool);
        return new GpuVertexStats(results[0], results[1], results[2], results[3], results[4]);
    }

    private QueryPool GetFreePipelineStatsQueryPool()
    {
        if (_availablePipelineStatsQueryPools.TryDequeue(out QueryPool pool))
            return pool;

        QueryPoolCreateInfo ci = new(sType: StructureType.QueryPoolCreateInfo)
        {
            QueryType = QueryType.PipelineStatistics,
            QueryCount = 1,
            PipelineStatistics = PipelineStatsFlags,
        };
        _vk.CreateQueryPool(_device, in ci, null, out QueryPool newPool).CheckResult();
        return newPool;
    }
}
