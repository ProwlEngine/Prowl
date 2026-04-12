using System;
using System.Collections.Generic;
using System.Diagnostics;
using Silk.NET.Vulkan;

namespace NeoVeldrid.Vk
{
    internal class VkDescriptorPoolManager
    {
        private readonly VkGraphicsDevice _gd;
        private readonly List<PoolInfo> _pools = new List<PoolInfo>();
        private readonly object _lock = new object();

        public VkDescriptorPoolManager(VkGraphicsDevice gd)
        {
            _gd = gd;
            _pools.Add(CreateNewPool());
        }

        public unsafe DescriptorAllocationToken Allocate(DescriptorResourceCounts counts, DescriptorSetLayout setLayout)
        {
            lock (_lock)
            {
                DescriptorPool pool = GetPool(counts);
                DescriptorSetAllocateInfo dsAI = new DescriptorSetAllocateInfo
                {
                    SType = StructureType.DescriptorSetAllocateInfo
                };
                dsAI.DescriptorSetCount = 1;
                dsAI.PSetLayouts = &setLayout;
                dsAI.DescriptorPool = pool;
                DescriptorSet set;
                Result result = _gd.Vk.AllocateDescriptorSets(_gd.Device, in dsAI, out set);
                VulkanUtil.CheckResult(result);

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
            uint poolSizeCount = 7;
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

            DescriptorPoolCreateInfo poolCI = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo
            };
            poolCI.Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit;
            poolCI.MaxSets = totalSets;
            poolCI.PPoolSizes = sizes;
            poolCI.PoolSizeCount = poolSizeCount;

            DescriptorPool descriptorPool;
            Result result = _gd.Vk.CreateDescriptorPool(_gd.Device, in poolCI, null, out descriptorPool);
            VulkanUtil.CheckResult(result);

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
                    && StorageImageCount >= counts.StorageImageCount)
                {
                    RemainingSets -= 1;
                    UniformBufferCount -= counts.UniformBufferCount;
                    UniformBufferDynamicCount -= counts.UniformBufferDynamicCount;
                    SampledImageCount -= counts.SampledImageCount;
                    SamplerCount -= counts.SamplerCount;
                    StorageBufferCount -= counts.StorageBufferCount;
                    StorageBufferDynamicCount -= counts.StorageBufferDynamicCount;
                    StorageImageCount -= counts.StorageImageCount;
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

                // Every counter decremented in Allocate must be incremented here; the two methods
                // must stay symmetric or the pool slowly leaks descriptor headroom under churn.
                RemainingSets += 1;
                UniformBufferCount += counts.UniformBufferCount;
                UniformBufferDynamicCount += counts.UniformBufferDynamicCount;
                SampledImageCount += counts.SampledImageCount;
                SamplerCount += counts.SamplerCount;
                StorageBufferCount += counts.StorageBufferCount;
                StorageBufferDynamicCount += counts.StorageBufferDynamicCount;
                StorageImageCount += counts.StorageImageCount;
            }
        }
    }

    internal struct DescriptorAllocationToken
    {
        public readonly DescriptorSet Set;
        public readonly DescriptorPool Pool;

        public DescriptorAllocationToken(DescriptorSet set, DescriptorPool pool)
        {
            Set = set;
            Pool = pool;
        }
    }
}
