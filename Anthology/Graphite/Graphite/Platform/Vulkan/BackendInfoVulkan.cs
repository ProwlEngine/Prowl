#if !EXCLUDE_VULKAN_BACKEND
using System;
using System.Collections.ObjectModel;

using Prowl.Graphite.Vk;

using Silk.NET.Vulkan;

using static System.Net.WebRequestMethods;

using VkImageLayout = Silk.NET.Vulkan.ImageLayout;

namespace Prowl.Graphite;

/// <summary>
/// Vulkan-specific stuff, for interop with native code that touches Vulkan directly. Vulkan backend only.
/// </summary>
public class BackendInfoVulkan
{
    private readonly VkGraphicsDevice _gd;
    private readonly Lazy<ReadOnlyCollection<string>> _instanceLayers;
    private readonly ReadOnlyCollection<string> _instanceExtensions;
    private readonly Lazy<ReadOnlyCollection<ExtensionProperties>> _deviceExtensions;

    internal unsafe BackendInfoVulkan(VkGraphicsDevice gd)
    {
        _gd = gd;
        _instanceLayers = new Lazy<ReadOnlyCollection<string>>(() => new ReadOnlyCollection<string>(_gd.Vk.EnumerateInstanceLayers((LayerProperties*)0)));
        _instanceExtensions = new ReadOnlyCollection<string>(_gd.Vk.EnumerateInstanceExtensionProperties((byte*)0));
        _deviceExtensions = new Lazy<ReadOnlyCollection<ExtensionProperties>>(EnumerateDeviceExtensions);
    }

    /// <summary>The VkInstance handle.</summary>
    public IntPtr Instance => _gd.Instance.Handle;

    /// <summary>The VkDevice handle.</summary>
    public IntPtr Device => _gd.Device.Handle;

    /// <summary>The VkPhysicalDevice handle.</summary>
    public IntPtr PhysicalDevice => _gd.PhysicalDevice.Handle;

    /// <summary>The graphics VkQueue handle.</summary>
    public IntPtr GraphicsQueue => _gd.GraphicsQueue.Handle;

    /// <summary>Queue family index of the graphics queue.</summary>
    public uint GraphicsQueueFamilyIndex => _gd.GraphicsQueueIndex;

    /// <summary>Driver name. Can be null.</summary>
    public string DriverName => _gd.DriverName;

    /// <summary>Driver info string. Can be null.</summary>
    public string DriverInfo => _gd.DriverInfo;

    /// <summary>Available Vulkan instance layers (validation/debug layers etc).</summary>
    public ReadOnlyCollection<string> AvailableInstanceLayers => _instanceLayers.Value;

    /// <summary>Available Vulkan instance extensions (platform surface extensions etc).</summary>
    public ReadOnlyCollection<string> AvailableInstanceExtensions => _instanceExtensions;

    /// <summary>Available Vulkan device extensions.</summary>
    public ReadOnlyCollection<ExtensionProperties> AvailableDeviceExtensions => _deviceExtensions.Value;

    /// <summary>Overrides the tracked layout for a Texture. Use when an external lib creates the VkImage and we need to know its initial layout.</summary>
    /// <param name="texture">Texture to override.</param>
    /// <param name="layout">New layout.</param>
    public void OverrideImageLayout(Texture texture, uint layout)
    {
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        for (uint layer = 0; layer < vkTex.ArrayLayers; layer++)
        {
            for (uint level = 0; level < vkTex.MipLevels; level++)
            {
                vkTex.SetImageLayout(level, layer, (VkImageLayout)layout);
            }
        }
    }

    /// <summary>Gets the VkImage behind a Texture. Not usable on staging textures.</summary>
    /// <param name="texture">Texture to get the VkImage for.</param>
    /// <returns>The VkImage handle.</returns>
    public ulong GetVkImage(Texture texture)
    {
        VkTexture vkTexture = Util.AssertSubtype<Texture, VkTexture>(texture);
        if ((vkTexture.Usage & TextureUsage.Staging) != 0)
        {
            throw new RenderException(
                $"{nameof(GetVkImage)} cannot be used if the {nameof(Texture)} " +
                $"has {nameof(TextureUsage)}.{nameof(TextureUsage.Staging)}.");
        }

        return vkTexture.OptimalDeviceImage.Handle;
    }

    /// <summary>Transitions a Texture's VkImage to a new layout.</summary>
    /// <param name="texture">Texture to transition.</param>
    /// <param name="layout">New layout.</param>
    public void TransitionImageLayout(Texture texture, uint layout)
    {
        _gd.TransitionImageLayout(Util.AssertSubtype<Texture, VkTexture>(texture), (VkImageLayout)layout);
    }

    private unsafe ReadOnlyCollection<ExtensionProperties> EnumerateDeviceExtensions()
    {
        Silk.NET.Vulkan.ExtensionProperties[] vkProps = _gd.GetDeviceExtensionProperties();
        ExtensionProperties[] GraphiteProps = new ExtensionProperties[vkProps.Length];

        for (int i = 0; i < vkProps.Length; i++)
        {
            Silk.NET.Vulkan.ExtensionProperties prop = vkProps[i];
            GraphiteProps[i] = new ExtensionProperties(Util.GetString(prop.ExtensionName), prop.SpecVersion);
        }

        return new ReadOnlyCollection<ExtensionProperties>(GraphiteProps);
    }

    /// <summary>Describes a device extension, e.g. raytracing or mesh shaders.</summary>
    public readonly struct ExtensionProperties
    {
        /// <summary>Extension name, e.g. VK_KHR_swapchain.</summary>
        public readonly string Name;

        /// <summary>Spec version implemented.</summary>
        public readonly uint SpecVersion;


        internal ExtensionProperties(string name, uint specVersion)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            SpecVersion = specVersion;
        }


        /// <inheritdoc/>
        public override string ToString()
        {
            return Name;
        }
    }
}
#endif
