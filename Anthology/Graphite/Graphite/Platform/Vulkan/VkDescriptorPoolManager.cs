using System;
using System.Collections.Generic;
using System.Diagnostics;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal partial class VkDescriptorPoolManager
{
    private readonly VkGraphicsDevice _gd;
    private readonly List<PoolInfo> _pools = [];
    private readonly object _lock = new();
    private readonly bool _freeDescriptorSets;

    /// <summary>Creates a pool manager for long-lived descriptor sets (supports per-set free).</summary>
    public VkDescriptorPoolManager(VkGraphicsDevice gd)
    {
        _gd = gd;
        _freeDescriptorSets = true;
        _pools.Add(CreateNewPool());
    }

    /// <summary>Creates a per-frame pool manager whose sets are reclaimed wholesale by <see cref="ResetAll"/>.</summary>
    public VkDescriptorPoolManager(VkGraphicsDevice gd, bool freeDescriptorSets)
    {
        _gd = gd;
        _freeDescriptorSets = freeDescriptorSets;
        _pools.Add(CreateNewPool());
    }

    /// <summary>
    /// Resets every <c>VkDescriptorPool</c> in this manager, invalidating all previously allocated sets.
    /// Used at frame-begin time to reclaim all sets from the previous frame occupying this ring slot.
    /// </summary>
    public void ResetAll()
    {
        lock (_lock)
        {
            ResetAll_RecordFrees();
            foreach (PoolInfo poolInfo in _pools)
            {
                _gd.Vk.ResetDescriptorPool(_gd.Device, poolInfo.Pool, 0);
                poolInfo.ResetCounters();
            }
        }
    }

    public unsafe DescriptorAllocationToken Allocate(DescriptorResourceCounts counts, DescriptorSetLayout setLayout)
    {
        lock (_lock)
        {
            DescriptorPool pool = GetPool(counts);
            DescriptorSetAllocateInfo dsAI = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo
            };
            dsAI.DescriptorSetCount = 1;
            dsAI.PSetLayouts = &setLayout;
            dsAI.DescriptorPool = pool;
            _gd.Vk.AllocateDescriptorSets(_gd.Device, in dsAI, out DescriptorSet set).CheckResult();
            Allocate_RecordAllocation();

            return new DescriptorAllocationToken(set, pool);
        }
    }

    public void Free(DescriptorAllocationToken token, DescriptorResourceCounts counts)
    {
        lock (_lock)
        {
            foreach (PoolInfo poolInfo in _pools)
            {
                if (poolInfo.Pool.Handle == token.Pool.Handle)
                {
                    poolInfo.Free(_gd, token, counts);
                    Free_RecordFree();
                }
            }
        }
    }

    private DescriptorPool GetPool(DescriptorResourceCounts counts)
    {
        lock (_lock)
        {
            foreach (PoolInfo poolInfo in _pools)
            {
                if (poolInfo.Allocate(counts))
                {
                    return poolInfo.Pool;
                }
            }

            PoolInfo newPool = CreateNewPool();
            _pools.Add(newPool);
            bool result = newPool.Allocate(counts);
            Debug.Assert(result);
            return newPool.Pool;
        }
    }

    private unsafe PoolInfo CreateNewPool()
    {
        uint totalSets = 1000;
        uint descriptorCount = 100;
        uint poolSizeCount = 8;
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[(int)poolSizeCount];
        sizes[0].Type = DescriptorType.UniformBuffer;
        sizes[0].DescriptorCount = descriptorCount;
        sizes[1].Type = DescriptorType.SampledImage;
        sizes[1].DescriptorCount = descriptorCount;
        sizes[2].Type = DescriptorType.Sampler;
        sizes[2].DescriptorCount = descriptorCount;
        sizes[3].Type = DescriptorType.StorageBuffer;
        sizes[3].DescriptorCount = descriptorCount;
        sizes[4].Type = DescriptorType.StorageImage;
        sizes[4].DescriptorCount = descriptorCount;
        sizes[5].Type = DescriptorType.UniformBufferDynamic;
        sizes[5].DescriptorCount = descriptorCount;
        sizes[6].Type = DescriptorType.StorageBufferDynamic;
        sizes[6].DescriptorCount = descriptorCount;
        sizes[7].Type = DescriptorType.CombinedImageSampler;
        sizes[7].DescriptorCount = descriptorCount;

        DescriptorPoolCreateInfo poolCI = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo
        };
        poolCI.Flags = _freeDescriptorSets ? DescriptorPoolCreateFlags.FreeDescriptorSetBit : 0;
        poolCI.MaxSets = totalSets;
        poolCI.PPoolSizes = sizes;
        poolCI.PoolSizeCount = poolSizeCount;

        _gd.Vk.CreateDescriptorPool(_gd.Device, in poolCI, null, out DescriptorPool descriptorPool).CheckResult();

        return new PoolInfo(descriptorPool, totalSets, descriptorCount);
    }

    internal unsafe void DestroyAll()
    {
        foreach (PoolInfo poolInfo in _pools)
        {
            _gd.Vk.DestroyDescriptorPool(_gd.Device, poolInfo.Pool, null);
        }
    }

    private class PoolInfo
    {
        public readonly DescriptorPool Pool;

        public uint RemainingSets;

        public uint UniformBufferCount;
        public uint UniformBufferDynamicCount;
        public uint SampledImageCount;
        public uint SamplerCount;
        public uint StorageBufferCount;
        public uint StorageBufferDynamicCount;
        public uint StorageImageCount;
        public uint CombinedImageSamplerCount;

        public PoolInfo(DescriptorPool pool, uint totalSets, uint descriptorCount)
        {
            Pool = pool;
            RemainingSets = totalSets;
            UniformBufferCount = descriptorCount;
            UniformBufferDynamicCount = descriptorCount;
            SampledImageCount = descriptorCount;
            SamplerCount = descriptorCount;
            StorageBufferCount = descriptorCount;
            StorageBufferDynamicCount = descriptorCount;
            StorageImageCount = descriptorCount;
            CombinedImageSamplerCount = descriptorCount;
        }

        internal bool Allocate(DescriptorResourceCounts counts)
        {
            if (RemainingSets > 0
                && UniformBufferCount >= counts.UniformBufferCount
                && UniformBufferDynamicCount >= counts.UniformBufferDynamicCount
                && SampledImageCount >= counts.SampledImageCount
                && SamplerCount >= counts.SamplerCount
                && StorageBufferCount >= counts.StorageBufferCount
                && StorageBufferDynamicCount >= counts.StorageBufferDynamicCount
                && StorageImageCount >= counts.StorageImageCount
                && CombinedImageSamplerCount >= counts.CombinedImageSamplerCount)
            {
                RemainingSets -= 1;
                UniformBufferCount -= counts.UniformBufferCount;
                UniformBufferDynamicCount -= counts.UniformBufferDynamicCount;
                SampledImageCount -= counts.SampledImageCount;
                SamplerCount -= counts.SamplerCount;
                StorageBufferCount -= counts.StorageBufferCount;
                StorageBufferDynamicCount -= counts.StorageBufferDynamicCount;
                StorageImageCount -= counts.StorageImageCount;
                CombinedImageSamplerCount -= counts.CombinedImageSamplerCount;
                return true;
            }
            else
            {
                return false;
            }
        }

        internal void Free(VkGraphicsDevice gd, DescriptorAllocationToken token, DescriptorResourceCounts counts)
        {
            DescriptorSet set = token.Set;
            gd.Vk.FreeDescriptorSets(gd.Device, Pool, 1, in set);

            RemainingSets += 1;
            UniformBufferCount += counts.UniformBufferCount;
            UniformBufferDynamicCount += counts.UniformBufferDynamicCount;
            SampledImageCount += counts.SampledImageCount;
            SamplerCount += counts.SamplerCount;
            StorageBufferCount += counts.StorageBufferCount;
            StorageBufferDynamicCount += counts.StorageBufferDynamicCount;
            StorageImageCount += counts.StorageImageCount;
            CombinedImageSamplerCount += counts.CombinedImageSamplerCount;
        }

        internal void ResetCounters()
        {
            RemainingSets = 1000;
            UniformBufferCount = 100;
            UniformBufferDynamicCount = 100;
            SampledImageCount = 100;
            SamplerCount = 100;
            StorageBufferCount = 100;
            StorageBufferDynamicCount = 100;
            StorageImageCount = 100;
            CombinedImageSamplerCount = 100;
        }
    }
}

internal readonly struct DescriptorAllocationToken
{
    public readonly DescriptorSet Set;
    public readonly DescriptorPool Pool;

    public DescriptorAllocationToken(DescriptorSet set, DescriptorPool pool)
    {
        Set = set;
        Pool = pool;
    }
}
