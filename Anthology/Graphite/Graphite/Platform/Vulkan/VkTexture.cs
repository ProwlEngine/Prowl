using System;
using System.Diagnostics;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkTexture : Texture
{
    private readonly VkGraphicsDevice _gd;
    private readonly Image _optimalImage;
    private readonly VkMemoryBlock _memoryBlock;
    private readonly Silk.NET.Vulkan.Buffer _stagingBuffer;
    private readonly uint _actualImageArrayLayers;

    public uint ActualArrayLayers => _actualImageArrayLayers;

    public Image OptimalDeviceImage => _optimalImage;
    public Silk.NET.Vulkan.Buffer StagingBuffer => _stagingBuffer;
    public VkMemoryBlock Memory => _memoryBlock;

    public Format VkFormat { get; }
    public SampleCountFlags VkSampleCount { get; }

    private readonly bool _isSwapchainTexture;

    public ResourceRefCount RefCount { get; }
    public bool IsSwapchainTexture => _isSwapchainTexture;
    public bool IsStaging => _stagingBuffer.Handle != 0;

    internal VkTexture(VkGraphicsDevice gd, ref TextureDescription description)
        : base(description)
    {
        _gd = gd;
        bool isCubemap = ((description.Usage) & TextureUsage.Cubemap) == TextureUsage.Cubemap;
        _actualImageArrayLayers = isCubemap
            ? 6 * ArrayLayers
            : ArrayLayers;
        VkSampleCount = VkFormats.ToVkSampleCount(SampleCount);
        VkFormat = VkFormats.ToVkPixelFormat(Format, (description.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil);

        bool isStaging = (Usage & TextureUsage.Staging) == TextureUsage.Staging;

        ulong allocatedSize = 0;
        if (!isStaging)
        {
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

            MemoryRequirements memoryRequirements;
            bool prefersDedicatedAllocation;
            if (_gd.GetImageMemoryRequirements2 != null)
            {
                ImageMemoryRequirementsInfo2KHR memReqsInfo2 = new() { SType = StructureType.ImageMemoryRequirementsInfo2Khr };
                memReqsInfo2.Image = _optimalImage;
                MemoryRequirements2KHR memReqs2 = new() { SType = StructureType.MemoryRequirements2Khr };
                MemoryDedicatedRequirementsKHR dedicatedReqs = new() { SType = StructureType.MemoryDedicatedRequirementsKhr };
                memReqs2.PNext = &dedicatedReqs;
                _gd.GetImageMemoryRequirements2(_gd.Device, &memReqsInfo2, &memReqs2);
                memoryRequirements = memReqs2.MemoryRequirements;
                prefersDedicatedAllocation = dedicatedReqs.PrefersDedicatedAllocation || dedicatedReqs.RequiresDedicatedAllocation;
            }
            else
            {
                _gd.Vk.GetImageMemoryRequirements(gd.Device, _optimalImage, out memoryRequirements);
                prefersDedicatedAllocation = false;
            }

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

        }
        else // isStaging
        {
            uint depthPitch = FormatHelpers.GetDepthPitch(
                FormatHelpers.GetRowPitch(Width, Format),
                Height,
                Format);
            uint stagingSize = depthPitch * Depth;
            for (uint level = 1; level < MipLevels; level++)
            {
                Util.GetMipDimensions(this, level, out uint mipWidth, out uint mipHeight, out uint mipDepth);

                depthPitch = FormatHelpers.GetDepthPitch(
                    FormatHelpers.GetRowPitch(mipWidth, Format),
                    mipHeight,
                    Format);

                stagingSize += depthPitch * mipDepth;
            }
            stagingSize *= ArrayLayers;

            BufferCreateInfo bufferCI = new() { SType = StructureType.BufferCreateInfo };
            bufferCI.Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit;
            bufferCI.Size = stagingSize;
            _gd.Vk.CreateBuffer(_gd.Device, in bufferCI, null, out _stagingBuffer).CheckResult();

            MemoryRequirements bufferMemReqs;
            bool prefersDedicatedAllocation;
            if (_gd.GetBufferMemoryRequirements2 != null)
            {
                BufferMemoryRequirementsInfo2KHR memReqInfo2 = new() { SType = StructureType.BufferMemoryRequirementsInfo2Khr };
                memReqInfo2.Buffer = _stagingBuffer;
                MemoryRequirements2KHR memReqs2 = new() { SType = StructureType.MemoryRequirements2Khr };
                MemoryDedicatedRequirementsKHR dedicatedReqs = new() { SType = StructureType.MemoryDedicatedRequirementsKhr };
                memReqs2.PNext = &dedicatedReqs;
                _gd.GetBufferMemoryRequirements2(_gd.Device, &memReqInfo2, &memReqs2);
                bufferMemReqs = memReqs2.MemoryRequirements;
                prefersDedicatedAllocation = dedicatedReqs.PrefersDedicatedAllocation || dedicatedReqs.RequiresDedicatedAllocation;
            }
            else
            {
                _gd.Vk.GetBufferMemoryRequirements(gd.Device, _stagingBuffer, out bufferMemReqs);
                prefersDedicatedAllocation = false;
            }

            // Use "host cached" memory when available, for better performance of GPU -> CPU transfers
            MemoryPropertyFlags propertyFlags = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit | MemoryPropertyFlags.HostCachedBit;
            if (!_gd.Vk.TryFindMemoryType(_gd.PhysicalDeviceMemProperties, bufferMemReqs.MemoryTypeBits, propertyFlags, out _))
            {
                propertyFlags ^= MemoryPropertyFlags.HostCachedBit;
            }
            _memoryBlock = _gd.MemoryManager.Allocate(
                _gd.PhysicalDeviceMemProperties,
                bufferMemReqs.MemoryTypeBits,
                propertyFlags,
                true,
                bufferMemReqs.Size,
                bufferMemReqs.Alignment,
                prefersDedicatedAllocation,
                default,
                _stagingBuffer);

            _gd.Vk.BindBufferMemory(_gd.Device, _stagingBuffer, _memoryBlock.DeviceMemory, _memoryBlock.Offset).CheckResult();
            allocatedSize = bufferMemReqs.Size;
        }

        if (!isStaging)
            InitializeLayout();
        RefCount = new ResourceRefCount(DestroyNative);

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
        RefCount = new ResourceRefCount(DestroyNative);
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

    internal SubresourceLayout GetSubresourceLayout(uint subresource)
    {
        bool staging = _stagingBuffer.Handle != 0;
        Util.GetMipLevelAndArrayLayer(this, subresource, out uint mipLevel, out uint arrayLayer);
        if (!staging)
        {
            ImageAspectFlags aspect = (Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil
              ? (ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit)
              : ImageAspectFlags.ColorBit;
            ImageSubresource imageSubresource = new()
            {
                ArrayLayer = arrayLayer,
                MipLevel = mipLevel,
                AspectMask = aspect,
            };

            _gd.Vk.GetImageSubresourceLayout(_gd.Device, _optimalImage, in imageSubresource, out SubresourceLayout layout);
            return layout;
        }
        else
        {
            uint blockSize = FormatHelpers.IsCompressedFormat(Format) ? 4u : 1u;
            Util.GetMipDimensions(this, mipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);
            uint rowPitch = FormatHelpers.GetRowPitch(mipWidth, Format);
            uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, mipHeight, Format);

            SubresourceLayout layout = new()
            {
                RowPitch = rowPitch,
                DepthPitch = depthPitch,
                ArrayPitch = depthPitch,
                Size = depthPitch,
            };
            layout.Offset = Util.ComputeSubresourceOffset(this, mipLevel, arrayLayer);

            return layout;
        }
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

    internal void SetStagingDimensions(uint width, uint height, uint depth, PixelFormat format)
    {
        Debug.Assert(_stagingBuffer.Handle != 0);
        Debug.Assert(Usage == TextureUsage.Staging);
        _description.Width = width;
        _description.Height = height;
        _description.Depth = depth;
        _description.Format = format;
    }

    private protected override void DisposeCore()
    {
        RefCount.Decrement();
    }

    private void DestroyNative()
    {
        _gd.ReleaseDefaultView(this);

        // Swapchain images belong to the swapchain, not to this wrapper.
        if (_isSwapchainTexture)
        {
            return;
        }

        bool isStaging = (Usage & TextureUsage.Staging) == TextureUsage.Staging;
        if (isStaging)
        {
            _gd.Vk.DestroyBuffer(_gd.Device, _stagingBuffer, null);
        }
        else
        {
            _gd.Vk.DestroyImage(_gd.Device, _optimalImage, null);
        }

        if (_memoryBlock.DeviceMemory.Handle != 0)
        {
            _gd.MemoryManager.Free(_memoryBlock);
        }

        DisposeCore_RecordFree();
    }
}
