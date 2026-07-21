namespace Prowl.Graphite;

/// <summary>
/// Vulkan-specific device creation options.
/// </summary>
public struct VulkanDeviceOptions
{
    /// <summary>
    /// Required instance extensions, enabled on the created VkInstance.
    /// </summary>
    public string[] InstanceExtensions;
    /// <summary>
    /// Required device extensions, enabled on the created VkDevice.
    /// </summary>
    public string[] DeviceExtensions;

    /// <summary>
    /// Makes a VulkanDeviceOptions.
    /// </summary>
    /// <param name="instanceExtensions">Required instance extensions, enabled on the VkInstance.</param>
    /// <param name="deviceExtensions">Required device extensions, enabled on the VkDevice.</param>
    public VulkanDeviceOptions(string[] instanceExtensions, string[] deviceExtensions)
    {
        InstanceExtensions = instanceExtensions;
        DeviceExtensions = deviceExtensions;
    }
}
