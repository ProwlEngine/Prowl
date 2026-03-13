using Silk.NET.Vulkan;
using static Veldrid.Vk.VulkanUtil;
using System.Diagnostics;
using System;

namespace Veldrid.Vk
{
    internal unsafe class VkTexture : Texture
    {
        private readonly VkGraphicsDevice _gd;
        private readonly Image _optimalImage;
        private readonly VkMemoryBlock _memoryBlock;
        private readonly Silk.NET.Vulkan.Buffer _stagingBuffer;
        private PixelFormat _format; // Static for regular images -- may change for shared staging images
        private readonly uint _actualImageArrayLayers;
        private bool _destroyed;

        // Immutable except for shared staging Textures.
        private uint _width;
        private uint _height;
        private uint _depth;

        public override uint Width => _width;

        public override uint Height => _height;

        public override uint Depth => _depth;

        public override PixelFormat Format => _format;

        public override uint MipLevels { get; }

        public override uint ArrayLayers { get; }
        public uint ActualArrayLayers => _actualImageArrayLayers;

        public override TextureUsage Usage { get; }

        public override TextureType Type { get; }

        public override TextureSampleCount SampleCount { get; }

        public override bool IsDisposed => _destroyed;

        public Image OptimalDeviceImage => _optimalImage;
        public Silk.NET.Vulkan.Buffer StagingBuffer => _stagingBuffer;
        public VkMemoryBlock Memory => _memoryBlock;

        public Format VkFormat { get; }
        public SampleCountFlags VkSampleCount { get; }

        private ImageLayout[] _imageLayouts;
        private bool _isSwapchainTexture;
        private string _name;

        public ResourceRefCount RefCount { get; }
        public bool IsSwapchainTexture => _isSwapchainTexture;

        internal VkTexture(VkGraphicsDevice gd, ref TextureDescription description)
        {
            _gd = gd;
            _width = description.Width;
            _height = description.Height;
            _depth = description.Depth;
            MipLevels = description.MipLevels;
            ArrayLayers = description.ArrayLayers;
            bool isCubemap = ((description.Usage) & TextureUsage.Cubemap) == TextureUsage.Cubemap;
            _actualImageArrayLayers = isCubemap
                ? 6 * ArrayLayers
                : ArrayLayers;
            _format = description.Format;
            Usage = description.Usage;
            Type = description.Type;
            SampleCount = description.SampleCount;
            VkSampleCount = VkFormats.VdToVkSampleCount(SampleCount);
            VkFormat = VkFormats.VdToVkPixelFormat(Format, (description.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil);

            bool isStaging = (Usage & TextureUsage.Staging) == TextureUsage.Staging;

            if (!isStaging)
            {
                ImageCreateInfo imageCI = new ImageCreateInfo { SType = StructureType.ImageCreateInfo };
                imageCI.MipLevels = MipLevels;
                imageCI.ArrayLayers = _actualImageArrayLayers;
                imageCI.ImageType = VkFormats.VdToVkTextureType(Type);
                imageCI.Extent.Width = Width;
                imageCI.Extent.Height = Height;
                imageCI.Extent.Depth = Depth;
                imageCI.InitialLayout = ImageLayout.Preinitialized;
                imageCI.Usage = VkFormats.VdToVkTextureUsage(Usage);
                imageCI.Tiling = isStaging ? ImageTiling.Linear : ImageTiling.Optimal;
                imageCI.Format = VkFormat;
                imageCI.Flags = ImageCreateFlags.CreateMutableFormatBit;

                imageCI.Samples = VkSampleCount;
                if (isCubemap)
                {
                    imageCI.Flags |= ImageCreateFlags.CreateCubeCompatibleBit;
                }

                uint subresourceCount = MipLevels * _actualImageArrayLayers * Depth;
                Result result = _gd.Vk.CreateImage(gd.Device, in imageCI, null, out _optimalImage);
                CheckResult(result);

                MemoryRequirements memoryRequirements;
                bool prefersDedicatedAllocation;
                if (_gd.GetImageMemoryRequirements2 != null)
                {
                    ImageMemoryRequirementsInfo2KHR memReqsInfo2 = new ImageMemoryRequirementsInfo2KHR { SType = StructureType.ImageMemoryRequirementsInfo2Khr };
                    memReqsInfo2.Image = _optimalImage;
                    MemoryRequirements2KHR memReqs2 = new MemoryRequirements2KHR { SType = StructureType.MemoryRequirements2Khr };
                    MemoryDedicatedRequirementsKHR dedicatedReqs = new MemoryDedicatedRequirementsKHR { SType = StructureType.MemoryDedicatedRequirementsKhr };
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

                VkMemoryBlock memoryToken = gd.MemoryManager.Allocate(
                    gd.PhysicalDeviceMemProperties,
                    memoryRequirements.MemoryTypeBits,
                    MemoryPropertyFlags.DeviceLocalBit,
                    false,
                    memoryRequirements.Size,
                    memoryRequirements.Alignment,
                    prefersDedicatedAllocation,
                    _optimalImage,
                    default);
                _memoryBlock = memoryToken;
                result = _gd.Vk.BindImageMemory(gd.Device, _optimalImage, _memoryBlock.DeviceMemory, _memoryBlock.Offset);
                CheckResult(result);

                _imageLayouts = new ImageLayout[subresourceCount];
                for (int i = 0; i < _imageLayouts.Length; i++)
                {
                    _imageLayouts[i] = ImageLayout.Preinitialized;
                }
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

                BufferCreateInfo bufferCI = new BufferCreateInfo { SType = StructureType.BufferCreateInfo };
                bufferCI.Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit;
                bufferCI.Size = stagingSize;
                Result result = _gd.Vk.CreateBuffer(_gd.Device, in bufferCI, null, out _stagingBuffer);
                CheckResult(result);

                MemoryRequirements bufferMemReqs;
                bool prefersDedicatedAllocation;
                if (_gd.GetBufferMemoryRequirements2 != null)
                {
                    BufferMemoryRequirementsInfo2KHR memReqInfo2 = new BufferMemoryRequirementsInfo2KHR { SType = StructureType.BufferMemoryRequirementsInfo2Khr };
                    memReqInfo2.Buffer = _stagingBuffer;
                    MemoryRequirements2KHR memReqs2 = new MemoryRequirements2KHR { SType = StructureType.MemoryRequirements2Khr };
                    MemoryDedicatedRequirementsKHR dedicatedReqs = new MemoryDedicatedRequirementsKHR { SType = StructureType.MemoryDedicatedRequirementsKhr };
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
                var propertyFlags = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit | MemoryPropertyFlags.HostCachedBit;
                if (!TryFindMemoryType(_gd.PhysicalDeviceMemProperties, bufferMemReqs.MemoryTypeBits, propertyFlags, out _))
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

                result = _gd.Vk.BindBufferMemory(_gd.Device, _stagingBuffer, _memoryBlock.DeviceMemory, _memoryBlock.Offset);
                CheckResult(result);
            }

            ClearIfRenderTarget();
            TransitionIfSampled();
            RefCount = new ResourceRefCount(RefCountedDispose);
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
        {
            Debug.Assert(width > 0 && height > 0);
            _gd = gd;
            MipLevels = mipLevels;
            _width = width;
            _height = height;
            _depth = 1;
            VkFormat = vkFormat;
            _format = VkFormats.VkToVdPixelFormat(VkFormat);
            ArrayLayers = arrayLayers;
            Usage = usage;
            Type = TextureType.Texture2D;
            SampleCount = sampleCount;
            VkSampleCount = VkFormats.VdToVkSampleCount(sampleCount);
            _optimalImage = existingImage;
            _imageLayouts = new[] { ImageLayout.Undefined };
            _isSwapchainTexture = true;

            ClearIfRenderTarget();
            RefCount = new ResourceRefCount(DisposeCore);
        }

        private void ClearIfRenderTarget()
        {
            // If the image is going to be used as a render target, we need to clear the data before its first use.
            if ((Usage & TextureUsage.RenderTarget) != 0)
            {
                _gd.ClearColorTexture(this, new ClearColorValue(0, 0, 0, 0));
            }
            else if ((Usage & TextureUsage.DepthStencil) != 0)
            {
                _gd.ClearDepthTexture(this, new ClearDepthStencilValue(0, 0));
            }
        }

        private void TransitionIfSampled()
        {
            if ((Usage & TextureUsage.Sampled) != 0)
            {
                _gd.TransitionImageLayout(this, ImageLayout.ShaderReadOnlyOptimal);
            }
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
                ImageSubresource imageSubresource = new ImageSubresource
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

                SubresourceLayout layout = new SubresourceLayout()
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

        internal void TransitionImageLayout(
            CommandBuffer cb,
            uint baseMipLevel,
            uint levelCount,
            uint baseArrayLayer,
            uint layerCount,
            ImageLayout newLayout)
        {
            if (_stagingBuffer.Handle != 0)
            {
                return;
            }

            ImageLayout oldLayout = _imageLayouts[CalculateSubresource(baseMipLevel, baseArrayLayer)];
#if DEBUG
            for (uint level = 0; level < levelCount; level++)
            {
                for (uint layer = 0; layer < layerCount; layer++)
                {
                    if (_imageLayouts[CalculateSubresource(baseMipLevel + level, baseArrayLayer + layer)] != oldLayout)
                    {
                        throw new VeldridException("Unexpected image layout.");
                    }
                }
            }
#endif
            if (oldLayout != newLayout)
            {
                ImageAspectFlags aspectMask;
                if ((Usage & TextureUsage.DepthStencil) != 0)
                {
                    aspectMask = FormatHelpers.IsStencilFormat(Format)
                        ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
                        : ImageAspectFlags.DepthBit;
                }
                else
                {
                    aspectMask = ImageAspectFlags.ColorBit;
                }
                VulkanUtil.TransitionImageLayout(
                    _gd.Vk,
                    cb,
                    OptimalDeviceImage,
                    baseMipLevel,
                    levelCount,
                    baseArrayLayer,
                    layerCount,
                    aspectMask,
                    _imageLayouts[CalculateSubresource(baseMipLevel, baseArrayLayer)],
                    newLayout);

                for (uint level = 0; level < levelCount; level++)
                {
                    for (uint layer = 0; layer < layerCount; layer++)
                    {
                        _imageLayouts[CalculateSubresource(baseMipLevel + level, baseArrayLayer + layer)] = newLayout;
                    }
                }
            }
        }

        internal void TransitionImageLayoutNonmatching(
            CommandBuffer cb,
            uint baseMipLevel,
            uint levelCount,
            uint baseArrayLayer,
            uint layerCount,
            ImageLayout newLayout)
        {
            if (_stagingBuffer.Handle != 0)
            {
                return;
            }

            for (uint level = baseMipLevel; level < baseMipLevel + levelCount; level++)
            {
                for (uint layer = baseArrayLayer; layer < baseArrayLayer + layerCount; layer++)
                {
                    uint subresource = CalculateSubresource(level, layer);
                    ImageLayout oldLayout = _imageLayouts[subresource];

                    if (oldLayout != newLayout)
                    {
                        ImageAspectFlags aspectMask;
                        if ((Usage & TextureUsage.DepthStencil) != 0)
                        {
                            aspectMask = FormatHelpers.IsStencilFormat(Format)
                                ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
                                : ImageAspectFlags.DepthBit;
                        }
                        else
                        {
                            aspectMask = ImageAspectFlags.ColorBit;
                        }
                        VulkanUtil.TransitionImageLayout(
                            _gd.Vk,
                            cb,
                            OptimalDeviceImage,
                            level,
                            1,
                            layer,
                            1,
                            aspectMask,
                            oldLayout,
                            newLayout);

                        _imageLayouts[subresource] = newLayout;
                    }
                }
            }
        }

        internal ImageLayout GetImageLayout(uint mipLevel, uint arrayLayer)
        {
            return _imageLayouts[CalculateSubresource(mipLevel, arrayLayer)];
        }

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                _gd.SetResourceName(this, value);
            }
        }

        internal void SetStagingDimensions(uint width, uint height, uint depth, PixelFormat format)
        {
            Debug.Assert(_stagingBuffer.Handle != 0);
            Debug.Assert(Usage == TextureUsage.Staging);
            _width = width;
            _height = height;
            _depth = depth;
            _format = format;
        }

        private protected override void DisposeCore()
        {
            RefCount.Decrement();
        }

        private void RefCountedDispose()
        {
            if (!_destroyed)
            {
                base.Dispose();

                _destroyed = true;

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
            }
        }

        internal void SetImageLayout(uint mipLevel, uint arrayLayer, ImageLayout layout)
        {
            _imageLayouts[CalculateSubresource(mipLevel, arrayLayer)] = layout;
        }
    }
}
