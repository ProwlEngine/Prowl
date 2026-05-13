using System.Linq;
using Silk.NET.Vulkan;
using Silk.NET.Core;
using static NeoVeldrid.Vk.VulkanUtil;
using System;
using System.Runtime.InteropServices;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;
using VkFenceHandle = Silk.NET.Vulkan.Fence;

namespace NeoVeldrid.Vk
{
    internal unsafe class VkSwapchain : Swapchain
    {
        private readonly VkGraphicsDevice _gd;
        private readonly SurfaceKHR _surface;
        private SwapchainKHR _deviceSwapchain;
        private readonly VkSwapchainFramebuffer _framebuffer;
        private VkFenceHandle _imageAvailableFence;
        private readonly uint _presentQueueIndex;
        private readonly Queue _presentQueue;
        private bool _syncToVBlank;
        private readonly SwapchainSource _swapchainSource;
        private readonly bool _colorSrgb;
        private bool? _newSyncToVBlank;
        private uint _currentImageIndex;
        private string _name;
        private bool _disposed;

        public override string Name { get => _name; set { _name = value; _gd.SetResourceName(this, value); } }
        public override Framebuffer Framebuffer => _framebuffer;
        public override bool SyncToVerticalBlank
        {
            get => _newSyncToVBlank ?? _syncToVBlank;
            set
            {
                if (_syncToVBlank != value)
                {
                    _newSyncToVBlank = value;
                }
            }
        }

        public override bool IsDisposed => _disposed;

        public SwapchainKHR DeviceSwapchain => _deviceSwapchain;
        public uint ImageIndex => _currentImageIndex;
        public VkFenceHandle ImageAvailableFence => _imageAvailableFence;
        public SurfaceKHR Surface => _surface;
        public Queue PresentQueue => _presentQueue;
        public uint PresentQueueIndex => _presentQueueIndex;
        public ResourceRefCount RefCount { get; }

        public VkSwapchain(VkGraphicsDevice gd, ref SwapchainDescription description) : this(gd, ref description, default) { }

        public VkSwapchain(VkGraphicsDevice gd, ref SwapchainDescription description, SurfaceKHR existingSurface)
        {
            _gd = gd;
            _syncToVBlank = description.SyncToVerticalBlank;
            _swapchainSource = description.Source;
            _colorSrgb = description.ColorSrgb;

            if (existingSurface.Handle == default)
            {
                _surface = VkSurfaceUtil.CreateSurface(gd, gd.Instance, _swapchainSource);
            }
            else
            {
                _surface = existingSurface;
            }

            if (!GetPresentQueueIndex(out _presentQueueIndex))
            {
                throw new NeoVeldridException($"The system does not support presenting the given Vulkan surface.");
            }
            _gd.Vk.GetDeviceQueue(_gd.Device, _presentQueueIndex, 0, out _presentQueue);

            _framebuffer = new VkSwapchainFramebuffer(gd, this, _surface, description.Width, description.Height, description.DepthFormat);

            CreateSwapchain(description.Width, description.Height);

            FenceCreateInfo fenceCI = new FenceCreateInfo
            {
                SType = StructureType.FenceCreateInfo,
                Flags = 0
            };
            _gd.Vk.CreateFence(_gd.Device, &fenceCI, null, out _imageAvailableFence);

            AcquireNextImage(_gd.Device, default, _imageAvailableFence);
            VkFenceHandle iaf = _imageAvailableFence;
            _gd.Vk.WaitForFences(_gd.Device, 1, &iaf, true, ulong.MaxValue);
            _gd.Vk.ResetFences(_gd.Device, 1, &iaf);

            RefCount = new ResourceRefCount(DisposeCore);
        }

        public override void Resize(uint width, uint height)
        {
            RecreateAndReacquire(width, height);
        }

        public bool AcquireNextImage(Device device, VkSemaphore semaphore, VkFenceHandle fence)
        {
            if (_newSyncToVBlank != null)
            {
                _syncToVBlank = _newSyncToVBlank.Value;
                _newSyncToVBlank = null;
                RecreateAndReacquire(_framebuffer.Width, _framebuffer.Height);
                return false;
            }

            uint imageIndex = 0;
            Result result = _gd.KhrSwapchain.AcquireNextImage(
                device,
                _deviceSwapchain,
                ulong.MaxValue,
                semaphore,
                fence,
                &imageIndex);
            _currentImageIndex = imageIndex;
            _framebuffer.SetImageIndex(_currentImageIndex);
            if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
            {
                CreateSwapchain(_framebuffer.Width, _framebuffer.Height);
                return false;
            }
            else if (result != Result.Success)
            {
                throw new NeoVeldridException("Could not acquire next image from the Vulkan swapchain.");
            }

            return true;
        }

        private void RecreateAndReacquire(uint width, uint height)
        {
            if (CreateSwapchain(width, height))
            {
                if (AcquireNextImage(_gd.Device, default, _imageAvailableFence))
                {
                    VkFenceHandle iaf2 = _imageAvailableFence;
                    _gd.Vk.WaitForFences(_gd.Device, 1, &iaf2, true, ulong.MaxValue);
                    _gd.Vk.ResetFences(_gd.Device, 1, &iaf2);
                }
            }
        }

        private bool CreateSwapchain(uint width, uint height)
        {
            // Obtain the surface capabilities first -- this will indicate whether the surface has been lost.
            Result result = _gd.KhrSurface.GetPhysicalDeviceSurfaceCapabilities(_gd.PhysicalDevice, _surface, out SurfaceCapabilitiesKHR surfaceCapabilities);
            if (result == Result.ErrorSurfaceLostKhr)
            {
                throw new NeoVeldridException($"The Swapchain's underlying surface has been lost.");
            }

            if (surfaceCapabilities.MinImageExtent.Width == 0 && surfaceCapabilities.MinImageExtent.Height == 0
                && surfaceCapabilities.MaxImageExtent.Width == 0 && surfaceCapabilities.MaxImageExtent.Height == 0)
            {
                return false;
            }

            if (_deviceSwapchain.Handle != default)
            {
                _gd.WaitForIdle();
            }

            _currentImageIndex = 0;
            uint surfaceFormatCount = 0;
            result = _gd.KhrSurface.GetPhysicalDeviceSurfaceFormats(_gd.PhysicalDevice, _surface, ref surfaceFormatCount, null);
            CheckResult(result);
            SurfaceFormatKHR[] formats = new SurfaceFormatKHR[surfaceFormatCount];
            result = _gd.KhrSurface.GetPhysicalDeviceSurfaceFormats(_gd.PhysicalDevice, _surface, ref surfaceFormatCount, out formats[0]);
            CheckResult(result);

            Format desiredFormat = _colorSrgb
                ? Format.B8G8R8A8Srgb
                : Format.B8G8R8A8Unorm;

            SurfaceFormatKHR surfaceFormat = new SurfaceFormatKHR();
            if (formats.Length == 1 && formats[0].Format == Format.Undefined)
            {
                surfaceFormat = new SurfaceFormatKHR { ColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr, Format = desiredFormat };
            }
            else
            {
                foreach (SurfaceFormatKHR format in formats)
                {
                    if (format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr && format.Format == desiredFormat)
                    {
                        surfaceFormat = format;
                        break;
                    }
                }
                if (surfaceFormat.Format == Format.Undefined)
                {
                    if (_colorSrgb && surfaceFormat.Format != Format.R8G8B8A8Srgb)
                    {
                        throw new NeoVeldridException($"Unable to create an sRGB Swapchain for this surface.");
                    }

                    surfaceFormat = formats[0];
                }
            }

            uint presentModeCount = 0;
            result = _gd.KhrSurface.GetPhysicalDeviceSurfacePresentModes(_gd.PhysicalDevice, _surface, ref presentModeCount, null);
            CheckResult(result);
            PresentModeKHR[] presentModes = new PresentModeKHR[presentModeCount];
            result = _gd.KhrSurface.GetPhysicalDeviceSurfacePresentModes(_gd.PhysicalDevice, _surface, ref presentModeCount, out presentModes[0]);
            CheckResult(result);

            PresentModeKHR presentMode = PresentModeKHR.FifoKhr;

            if (_syncToVBlank)
            {
                if (presentModes.Contains(PresentModeKHR.FifoRelaxedKhr))
                {
                    presentMode = PresentModeKHR.FifoRelaxedKhr;
                }
            }
            else
            {
                if (presentModes.Contains(PresentModeKHR.MailboxKhr))
                {
                    presentMode = PresentModeKHR.MailboxKhr;
                }
                else if (presentModes.Contains(PresentModeKHR.ImmediateKhr))
                {
                    presentMode = PresentModeKHR.ImmediateKhr;
                }
            }

            uint maxImageCount = surfaceCapabilities.MaxImageCount == 0 ? uint.MaxValue : surfaceCapabilities.MaxImageCount;
            uint imageCount = Math.Min(maxImageCount, surfaceCapabilities.MinImageCount + 1);

            SwapchainCreateInfoKHR swapchainCI = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                PresentMode = presentMode,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = new Extent2D { Width = Util.Clamp(width, surfaceCapabilities.MinImageExtent.Width, surfaceCapabilities.MaxImageExtent.Width), Height = Util.Clamp(height, surfaceCapabilities.MinImageExtent.Height, surfaceCapabilities.MaxImageExtent.Height) },
                MinImageCount = imageCount,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit
            };

            FixedArray2<uint> queueFamilyIndices = new FixedArray2<uint>(_gd.GraphicsQueueIndex, _gd.PresentQueueIndex);

            if (_gd.GraphicsQueueIndex != _gd.PresentQueueIndex)
            {
                swapchainCI.ImageSharingMode = SharingMode.Concurrent;
                swapchainCI.QueueFamilyIndexCount = 2;
                swapchainCI.PQueueFamilyIndices = &queueFamilyIndices.First;
            }
            else
            {
                swapchainCI.ImageSharingMode = SharingMode.Exclusive;
                swapchainCI.QueueFamilyIndexCount = 0;
            }

            swapchainCI.PreTransform = SurfaceTransformFlagsKHR.IdentityBitKhr;
            swapchainCI.CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr;
            swapchainCI.Clipped = true;

            SwapchainKHR oldSwapchain = _deviceSwapchain;
            swapchainCI.OldSwapchain = oldSwapchain;

            result = _gd.KhrSwapchain.CreateSwapchain(_gd.Device, &swapchainCI, null, out _deviceSwapchain);
            CheckResult(result);
            if (oldSwapchain.Handle != default)
            {
                _gd.KhrSwapchain.DestroySwapchain(_gd.Device, oldSwapchain, null);
            }

            _framebuffer.SetNewSwapchain(_deviceSwapchain, width, height, surfaceFormat, swapchainCI.ImageExtent);
            return true;
        }

        private bool GetPresentQueueIndex(out uint queueFamilyIndex)
        {
            uint graphicsQueueIndex = _gd.GraphicsQueueIndex;
            uint presentQueueIndex = _gd.PresentQueueIndex;

            if (QueueSupportsPresent(graphicsQueueIndex, _surface))
            {
                queueFamilyIndex = graphicsQueueIndex;
                return true;
            }
            else if (graphicsQueueIndex != presentQueueIndex && QueueSupportsPresent(presentQueueIndex, _surface))
            {
                queueFamilyIndex = presentQueueIndex;
                return true;
            }

            queueFamilyIndex = 0;
            return false;
        }

        private bool QueueSupportsPresent(uint queueFamilyIndex, SurfaceKHR surface)
        {
            Result result = _gd.KhrSurface.GetPhysicalDeviceSurfaceSupport(
                _gd.PhysicalDevice,
                queueFamilyIndex,
                surface,
                out Bool32 supported);
            CheckResult(result);
            return supported;
        }

        public override void Dispose()
        {
            RefCount.Decrement();
        }

        private void DisposeCore()
        {
            _gd.Vk.DestroyFence(_gd.Device, _imageAvailableFence, null);
            _framebuffer.Dispose();
            _gd.KhrSwapchain.DestroySwapchain(_gd.Device, _deviceSwapchain, null);
            _gd.KhrSurface.DestroySurface(_gd.Instance, _surface, null);

            _disposed = true;
        }
    }
}
