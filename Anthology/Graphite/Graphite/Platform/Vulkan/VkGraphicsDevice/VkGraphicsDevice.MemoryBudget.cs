using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    public override MemoryBudgetInfo GetMemoryBudget()
    {
        if (!_memoryBudgetSupported)
            return default;

        PhysicalDeviceMemoryBudgetPropertiesEXT budget = new(sType: StructureType.PhysicalDeviceMemoryBudgetPropertiesExt);
        PhysicalDeviceMemoryProperties2 props2 = new(sType: StructureType.PhysicalDeviceMemoryProperties2)
        {
            PNext = &budget
        };

        Vk.GetPhysicalDeviceMemoryProperties2(PhysicalDevice, &props2);

        ulong totalBudget = 0;
        ulong totalUsage = 0;
        for (int i = 0; i < PhysicalDeviceMemProperties.MemoryHeapCount; i++)
        {
            totalBudget += budget.HeapBudget[i];
            totalUsage += budget.HeapUsage[i];
        }

        return new MemoryBudgetInfo(true, totalBudget, totalUsage);
    }
}
