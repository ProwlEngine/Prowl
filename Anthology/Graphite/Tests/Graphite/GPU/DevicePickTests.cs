using System;

using Silk.NET.Vulkan;

using Xunit;

#if TEST_VULKAN
namespace Prowl.Graphite.Tests;

// The physical device a host names through VulkanDeviceOptions.PickPhysicalDevice, the way an XR runtime does.
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public unsafe class DevicePickTests
{
    [Fact]
    public void PickPhysicalDevice_DecidesTheDevice()
    {
        string? pickedName = null;
        VulkanDeviceOptions options = new([], [])
        {
            PickPhysicalDevice = instance =>
            {
                Silk.NET.Vulkan.Vk vk = Silk.NET.Vulkan.Vk.GetApi();
                uint count = 0;
                vk.EnumeratePhysicalDevices(new Instance(instance), ref count, null);
                PhysicalDevice[] devices = new PhysicalDevice[count];
                fixed (PhysicalDevice* pointer = devices)
                    vk.EnumeratePhysicalDevices(new Instance(instance), ref count, pointer);

                PhysicalDevice last = devices[^1];
                vk.GetPhysicalDeviceProperties(last, out PhysicalDeviceProperties properties);
                pickedName = Silk.NET.Core.Native.SilkMarshal.PtrToString((nint)properties.DeviceName);
                return last.Handle;
            },
        };

        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true), vkOptions: options);

        Assert.Equal(pickedName, device.DeviceName);
    }


    [Fact]
    public void PickPhysicalDevice_ReturningAForeignHandle_Throws()
    {
        VulkanDeviceOptions options = new([], []) { PickPhysicalDevice = _ => new IntPtr(0x1234) };

        Assert.Throws<RenderException>(() => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true), vkOptions: options));
    }
}
#endif
