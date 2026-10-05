using System;
using System.Diagnostics;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkTexture : Texture
{
    private readonly VkGraphicsDevice _gd;
    private readonly Image _optimalImage;
    private readonly VkMemoryBlock _memoryBlock;
    private readonly uint _actualImageArrayLayers;

    public uint ActualArrayLayers => _actualImageArrayLayers;

    public Image OptimalDeviceImage => _optimalImage;
    public VkMemoryBlock Memory => _memoryBlock;

    public Format VkFormat { get; }
    public SampleCountFlags VkSampleCount { get; }

    private readonly bool _isSwapchainTexture;
    private readonly object _defaultViewLock = new();
    private VkTextureView? _defaultView;

    public bool IsSwapchainTexture => _isSwapchainTexture;

    public VkTextureView DefaultView
    {
        get
        {
            lock (_defaultViewLock)
                return _defaultView ??= (VkTextureView)_gd.ResourceFactory.CreateTextureView(this);
        }
    }

    internal VkTexture(VkGraphicsDevice gd, in TextureDescription description)
        : base(description)
    {
        _gd = gd;
        bool isCubemap = description.Type == TextureType.TextureCube;
        _actualImageArrayLayers = isCubemap
            ? 6 * ArrayLayers
            : ArrayLayers;
        VkSampleCount = VkFormats.ToVkSampleCount(SampleCount);
        VkFormat = VkFormats.ToVkPixelFormat(Format, (description.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil);

        ulong allocatedSize;
        ImageCreateInfo imageCI = new() { SType = StructureType.ImageCreateInfo };
        imageCI.MipLevels = MipLevels;
        imageCI.ArrayLayers = _actualImageArrayLayers;
        imageCI.ImageType = VkFormats.ToVkTextureType(Type);
        imageCI.Extent.Width = Width;
        imageCI.Extent.Height = Height;
        imageCI.Extent.Depth = Depth;
        imageCI.InitialLayout = ImageLayout.Undefined;
        imageCI.Usage = VkFormats.ToVkTextureUsage(Usage);
        imageCI.Tiling = ImageTiling.Optimal;
        imageCI.Format = VkFormat;
        imageCI.Flags = ImageCreateFlags.CreateMutableFormatBit;

        imageCI.Samples = VkSampleCount;
        if (isCubemap)
        {
            imageCI.Flags |= ImageCreateFlags.CreateCubeCompatibleBit;
        }

        _gd.Vk.CreateImage(gd.Device, in imageCI, null, out _optimalImage).CheckResult();

        ImageMemoryRequirementsInfo2 memReqsInfo2 = new()
        {
            SType = StructureType.ImageMemoryRequirementsInfo2,
            Image = _optimalImage
        };
        MemoryDedicatedRequirements dedicatedReqs = new() { SType = StructureType.MemoryDedicatedRequirements };
        MemoryRequirements2 memReqs2 = new() { SType = StructureType.MemoryRequirements2, PNext = &dedicatedReqs };
        _gd.Vk.GetImageMemoryRequirements2(_gd.Device, &memReqsInfo2, &memReqs2);
        MemoryRequirements memoryRequirements = memReqs2.MemoryRequirements;
        bool prefersDedicatedAllocation = dedicatedReqs.PrefersDedicatedAllocation || dedicatedReqs.RequiresDedicatedAllocation;

        _memoryBlock = gd.MemoryManager.Allocate(
            gd.PhysicalDeviceMemProperties,
            memoryRequirements.MemoryTypeBits,
            MemoryPropertyFlags.DeviceLocalBit,
            false,
            memoryRequirements.Size,
            memoryRequirements.Alignment,
            prefersDedicatedAllocation,
            _optimalImage,
            default);
        _gd.Vk.BindImageMemory(gd.Device, _optimalImage, _memoryBlock.DeviceMemory, _memoryBlock.Offset).CheckResult();
        allocatedSize = memoryRequirements.Size;

        InitializeLayout();

        Constructor_RecordAllocation((long)allocatedSize);
    }

    // Used to construct Swapchain textures.
    internal VkTexture(
        VkGraphicsDevice gd,
        uint width,
        uint height,
        uint mipLevels,
        uint arrayLayers,
        Format vkFormat,
        TextureUsage usage,
        TextureSampleCount sampleCount,
        Image existingImage)
        : base(new TextureDescription(
            width, height, 1, mipLevels, arrayLayers,
            VkFormats.ToPixelFormat(vkFormat), usage, TextureType.Texture2D, sampleCount))
    {
        Debug.Assert(width > 0 && height > 0);
        _gd = gd;
        VkFormat = vkFormat;
        VkSampleCount = VkFormats.ToVkSampleCount(sampleCount);
        _optimalImage = existingImage;
        _actualImageArrayLayers = arrayLayers;
        _isSwapchainTexture = true;

        InitializeLayout();
    }

    private void InitializeLayout()
    {
        if ((Usage & TextureUsage.RenderTarget) != 0)
            _gd.ClearColorTexture(this, new ClearColorValue(0, 0, 0, 0));
        else if ((Usage & TextureUsage.DepthStencil) != 0)
            _gd.ClearDepthTexture(this, new ClearDepthStencilValue(0, 0));
        else
            _gd.TransitionFromUndefined(this, VkBarriers.RestingLayout(this));
    }

    internal ImageAspectFlags AspectMask
    {
        get
        {
            if ((Usage & TextureUsage.DepthStencil) == 0)
                return ImageAspectFlags.ColorBit;

            return FormatHelpers.IsStencilFormat(Format)
                ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
                : ImageAspectFlags.DepthBit;
        }
    }

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    private protected override void DisposeCore()
    {
        _gd.DisposeWhenRetired(DestroyNative);
    }

    private void DestroyNative()
    {
        VkTextureView? defaultView;
        lock (_defaultViewLock)
        {
            defaultView = _defaultView;
            _defaultView = null;
        }
        defaultView?.Dispose();

        // Swapchain images belong to the swapchain, not to this wrapper.
        if (_isSwapchainTexture)
        {
            return;
        }

        _gd.Vk.DestroyImage(_gd.Device, _optimalImage, null);

        if (_memoryBlock.DeviceMemory.Handle != 0)
        {
            _gd.MemoryManager.Free(_memoryBlock);
        }

        DisposeCore_RecordFree();
    }
}
