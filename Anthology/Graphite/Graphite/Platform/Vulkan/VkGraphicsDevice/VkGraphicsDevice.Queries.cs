using System.Collections.Concurrent;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal struct GpuQueries
{
    public QueryPool? Timing;
    public QueryPool? Stats;
}

internal unsafe partial class VkGraphicsDevice
{
    private const QueryPipelineStatisticFlags PipelineStatsFlags =
        QueryPipelineStatisticFlags.InputAssemblyVerticesBit |
        QueryPipelineStatisticFlags.InputAssemblyPrimitivesBit |
        QueryPipelineStatisticFlags.ClippingInvocationsBit |
        QueryPipelineStatisticFlags.ClippingPrimitivesBit |
        QueryPipelineStatisticFlags.FragmentShaderInvocationsBit;

    private const int StatsCounterCount = 5;

    private readonly ConcurrentQueue<QueryPool> _availableTimingPools = new();
    private readonly ConcurrentQueue<QueryPool> _availableStatsPools = new();

    internal GpuQueries BeginQueries(Silk.NET.Vulkan.CommandBuffer cb, bool enabled)
    {
        GpuQueries queries = default;
        if (!enabled)
            return queries;

        QueryPool timing = GetPool(_availableTimingPools, QueryType.Timestamp, 2, 0);
        Vk.CmdResetQueryPool(cb, timing, 0, 2);
        Vk.CmdWriteTimestamp(cb, PipelineStageFlags.TopOfPipeBit, timing, 0);
        queries.Timing = timing;

        if (_physicalDeviceFeatures.PipelineStatisticsQuery)
        {
            QueryPool stats = GetPool(_availableStatsPools, QueryType.PipelineStatistics, 1, PipelineStatsFlags);
            Vk.CmdResetQueryPool(cb, stats, 0, 1);
            Vk.CmdBeginQuery(cb, stats, 0, QueryControlFlags.None);
            queries.Stats = stats;
        }

        return queries;
    }

    internal void EndQueries(Silk.NET.Vulkan.CommandBuffer cb, in GpuQueries queries)
    {
        if (queries.Stats is { } stats)
            Vk.CmdEndQuery(cb, stats, 0);

        if (queries.Timing is { } timing)
            Vk.CmdWriteTimestamp(cb, PipelineStageFlags.BottomOfPipeBit, timing, 1);
    }

    private void ResolveQueries(in GpuQueries queries, in CommandBufferInfo info, bool isTransfer, IGpuStatsProfiler? gpuStats)
    {
        if (queries.Timing is { } timing)
        {
            ulong* timestamps = stackalloc ulong[2];
            Vk.GetQueryPoolResults(
                Device, timing, 0, 2,
                (nuint)(sizeof(ulong) * 2), timestamps, sizeof(ulong),
                QueryResultFlags.ResultWaitBit | QueryResultFlags.Result64Bit).CheckResult();
            _availableTimingPools.Enqueue(timing);

            double ticks = timestamps[1] > timestamps[0] ? timestamps[1] - timestamps[0] : 0;
            double milliseconds = ticks * _physicalDeviceProperties.Limits.TimestampPeriod / 1_000_000.0;
            gpuStats?.RecordExecutionTime(info, isTransfer, milliseconds);
        }

        if (queries.Stats is { } stats)
        {
            ulong* results = stackalloc ulong[StatsCounterCount];
            Vk.GetQueryPoolResults(
                Device, stats, 0, 1,
                (nuint)(sizeof(ulong) * StatsCounterCount), results, sizeof(ulong) * StatsCounterCount,
                QueryResultFlags.ResultWaitBit | QueryResultFlags.Result64Bit).CheckResult();
            _availableStatsPools.Enqueue(stats);

            GpuVertexStats vertexStats = new(results[0], results[1], results[2], results[3], results[4]);
            gpuStats?.RecordGpuVertexStats(info, in vertexStats);
        }
    }

    private QueryPool GetPool(ConcurrentQueue<QueryPool> free, QueryType type, uint count, QueryPipelineStatisticFlags statistics)
    {
        if (free.TryDequeue(out QueryPool pool))
            return pool;

        QueryPoolCreateInfo ci = new(sType: StructureType.QueryPoolCreateInfo)
        {
            QueryType = type,
            QueryCount = count,
            PipelineStatistics = statistics,
        };
        Vk.CreateQueryPool(Device, in ci, null, out QueryPool newPool).CheckResult();
        return newPool;
    }
}
