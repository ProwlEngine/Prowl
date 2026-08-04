using System.Collections.Generic;
using System.Diagnostics;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

/// <summary>Pool manager for long-lived descriptor sets, supports per-set free.</summary>
internal unsafe partial class VkDescriptorPoolManager
{
    private const uint SetsPerPool = 1000;
    private const uint DescriptorsPerPool = 100;

    private static readonly DescriptorType[] s_poolTypes =
    [
        DescriptorType.UniformBufferDynamic,
        DescriptorType.SampledImage,
        DescriptorType.Sampler,
        DescriptorType.StorageBuffer,
        DescriptorType.StorageImage,
        DescriptorType.CombinedImageSampler,
    ];

    private readonly VkGraphicsDevice _gd;
    private readonly List<PoolInfo> _pools = [];
    private readonly object _lock = new();

    public VkDescriptorPoolManager(VkGraphicsDevice gd)
    {
        _gd = gd;
        _pools.Add(CreateNewPool());
    }

    public DescriptorAllocationToken Allocate(in DescriptorResourceCounts counts, DescriptorSetLayout setLayout)
    {
        lock (_lock)
        {
            DescriptorPool pool = GetPool(counts);
            DescriptorSetAllocateInfo dsAI = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout,
                DescriptorPool = pool,
            };
            _gd.Vk.AllocateDescriptorSets(_gd.Device, in dsAI, out DescriptorSet set).CheckResult();
            RecordAllocation();

            return new DescriptorAllocationToken(set, pool);
        }
    }

    public void Free(DescriptorAllocationToken token, in DescriptorResourceCounts counts)
    {
        lock (_lock)
        {
            foreach (PoolInfo poolInfo in _pools)
            {
                if (poolInfo.Pool.Handle != token.Pool.Handle)
                    continue;

                poolInfo.Free(_gd, token, counts);
                RecordFree();
            }
        }
    }

    internal void DestroyAll()
    {
        foreach (PoolInfo poolInfo in _pools)
            _gd.Vk.DestroyDescriptorPool(_gd.Device, poolInfo.Pool, null);
    }

    // Caller holds _lock.
    private DescriptorPool GetPool(in DescriptorResourceCounts counts)
    {
        foreach (PoolInfo poolInfo in _pools)
        {
            if (poolInfo.Allocate(counts))
                return poolInfo.Pool;
        }

        PoolInfo newPool = CreateNewPool();
        _pools.Add(newPool);
        bool result = newPool.Allocate(counts);
        Debug.Assert(result);
        return newPool.Pool;
    }

    private PoolInfo CreateNewPool()
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[s_poolTypes.Length];
        for (int i = 0; i < s_poolTypes.Length; i++)
            sizes[i] = new DescriptorPoolSize { Type = s_poolTypes[i], DescriptorCount = DescriptorsPerPool };

        DescriptorPoolCreateInfo poolCI = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = SetsPerPool,
            PPoolSizes = sizes,
            PoolSizeCount = (uint)s_poolTypes.Length,
        };

        _gd.Vk.CreateDescriptorPool(_gd.Device, in poolCI, null, out DescriptorPool descriptorPool).CheckResult();

        return new PoolInfo(descriptorPool);
    }

    private sealed class PoolInfo(DescriptorPool pool)
    {
        public readonly DescriptorPool Pool = pool;

        private uint _remainingSets = SetsPerPool;
        private DescriptorResourceCounts _remaining = DescriptorResourceCounts.All(DescriptorsPerPool);

        internal bool Allocate(in DescriptorResourceCounts counts)
        {
            if (_remainingSets == 0 || !_remaining.Covers(counts))
                return false;

            _remainingSets--;
            _remaining -= counts;
            return true;
        }

        internal void Free(VkGraphicsDevice gd, DescriptorAllocationToken token, in DescriptorResourceCounts counts)
        {
            DescriptorSet set = token.Set;
            gd.Vk.FreeDescriptorSets(gd.Device, Pool, 1, in set);

            _remainingSets++;
            _remaining += counts;
        }
    }
}

internal readonly record struct DescriptorAllocationToken(DescriptorSet Set, DescriptorPool Pool);
