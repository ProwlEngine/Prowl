using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Silk.NET.Core;
using Silk.NET.Vulkan;

using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkSwapchain : Swapchain
{
    private readonly VkGraphicsDevice _gd;
    private SurfaceKHR _surface;
    private SwapchainKHR _deviceSwapchain;
    private readonly VkSwapchainFramebuffer _framebuffer;
    private readonly Stack<VkSemaphore> _freeAcquireSemaphores = new();
    private VkSemaphore[] _imageAcquireSemaphores = [];
    private VkSemaphore[] _presentSemaphores = [];
    private ulong _pendingAcquire;
    private bool _imageAcquired;
    private bool _recreatePending;
    private bool _suboptimal;
    private Extent2D _createdExtent;
    private SurfaceTransformFlagsKHR _createdTransform;
    private SwapchainTransform _transform;
    private readonly uint _presentQueueIndex;
    private readonly Queue _presentQueue;
    private bool _syncToVBlank;
    private readonly bool _colorSrgb;
    private readonly bool _preRotate;
    private bool? _newSyncToVBlank;
    private uint _currentImageIndex;

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

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

    public SwapchainKHR DeviceSwapchain => _deviceSwapchain;
    public uint ImageIndex => _currentImageIndex;
    public bool ImageAcquired => _imageAcquired;
    public VkSemaphore PresentSemaphore => _presentSemaphores[_currentImageIndex];
    internal VkSemaphore TakePendingAcquire() => new(System.Threading.Interlocked.Exchange(ref _pendingAcquire, 0));
    public SurfaceKHR Surface => _surface;
    public override SwapchainTransform Transform => _transform;
    public override bool HasSurface => _surface.Handle != 0;
    public Queue PresentQueue => _presentQueue;
    public uint PresentQueueIndex => _presentQueueIndex;

    public VkSwapchain(VkGraphicsDevice gd, in SwapchainDescription description) : this(gd, description, default) { }

    public VkSwapchain(VkGraphicsDevice gd, in SwapchainDescription description, SurfaceKHR existingSurface)
    {
        _gd = gd;
        _syncToVBlank = description.SyncToVerticalBlank;
        _colorSrgb = description.ColorSrgb;
        _preRotate = description.PreRotate;

        if (existingSurface.Handle == default)
        {
            _surface = description.Source.GetSurface(gd.Instance);
        }
        else
        {
            _surface = existingSurface;
        }

        if (!GetPresentQueueIndex(out _presentQueueIndex))
        {
            throw new RenderException("The system does not support presenting the given Vulkan surface.");
        }
        _gd.Vk.GetDeviceQueue(_gd.Device, _presentQueueIndex, 0, out _presentQueue);

        _framebuffer = new VkSwapchainFramebuffer(gd, this, _surface, description.Width, description.Height);

        if (CreateSwapchain(description.Width, description.Height))
            AcquireNextImage();
    }

    internal override void RequireDepth(PixelFormat format) => _framebuffer.RequireDepth(format);

    public override void Resize(uint width, uint height)
    {
        _gd.Counters.RecordSwap(SwapBin.Resize);
        RecreateAndReacquire(width, height);
    }

    public override void DetachSurface()
    {
        if (_surface.Handle != 0)
            ReleaseSurface();
    }

    public override void AttachSurface(SwapchainSource source, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(source);
        DetachSurface();
        SurfaceKHR surface = source.GetSurface(_gd.Instance);
        if (!QueueSupportsPresent(_presentQueueIndex, surface))
        {
            _gd.KhrSurface.DestroySurface(_gd.Instance, surface, null);
            throw new RenderException("The swapchain's present queue cannot present to the new surface.");
        }
        _surface = surface;
        if (CreateSwapchain(width, height))
            AcquireNextImage();
    }

    // Waits for the images to go out of use, then lets the swapchain, its images and the surface go
    private void ReleaseSurface()
    {
        _gd.ConsumePendingAcquires(this);
        _imageAcquired = false;
        _recreatePending = false;
        _suboptimal = false;
        _gd.WaitForIdle();
        if (_presentQueueIndex != _gd.GraphicsQueueIndex)
        {
            lock (this)
                _gd.Vk.QueueWaitIdle(_presentQueue);
        }

        _framebuffer.ReleaseSwapchain();
        if (_deviceSwapchain.Handle != default)
            _gd.KhrSwapchain.DestroySwapchain(_gd.Device, _deviceSwapchain, null);
        _deviceSwapchain = default;
        _gd.KhrSurface.DestroySurface(_gd.Instance, _surface, null);
        _surface = default;
    }

    public void AcquireNextImage()
    {
        if (_surface.Handle == 0)
            return;

        if (_suboptimal && !_recreatePending)
        {
            _suboptimal = false;
            _recreatePending = SurfaceChanged();
        }

        if (_newSyncToVBlank != null)
        {
            _syncToVBlank = _newSyncToVBlank.Value;
            _newSyncToVBlank = null;
            _recreatePending = true;
        }

        if (_recreatePending)
        {
            RecreateAndReacquire(_framebuffer.Width, _framebuffer.Height);
            return;
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            VkSemaphore semaphore = RentAcquireSemaphore();
            uint imageIndex = 0;
            Result result = _gd.KhrSwapchain.AcquireNextImage(
                _gd.Device,
                _deviceSwapchain,
                ulong.MaxValue,
                semaphore,
                default,
                &imageIndex);

            if (result == Result.ErrorSurfaceLostKhr)
            {
                _freeAcquireSemaphores.Push(semaphore);
                ReleaseSurface();
                return;
            }

            if (result == Result.ErrorOutOfDateKhr)
            {
                _freeAcquireSemaphores.Push(semaphore);
                _imageAcquired = false;
                _gd.ConsumePendingAcquires(this);
                if (!CreateSwapchain(_framebuffer.Width, _framebuffer.Height))
                    return;
                continue;
            }

            if (result != Result.Success && result != Result.SuboptimalKhr)
            {
                _freeAcquireSemaphores.Push(semaphore);
                throw new RenderException("Could not acquire next image from the Vulkan swapchain.");
            }

            _currentImageIndex = imageIndex;
            _framebuffer.SetImageIndex(_currentImageIndex);

            VkSemaphore previous = _imageAcquireSemaphores[imageIndex];
            if (previous.Handle != 0)
                _freeAcquireSemaphores.Push(previous);
            _imageAcquireSemaphores[imageIndex] = semaphore;
            System.Threading.Interlocked.Exchange(ref _pendingAcquire, semaphore.Handle);

            _imageAcquired = true;
            _suboptimal = result == Result.SuboptimalKhr;
            _gd.Counters.RecordSwap(SwapBin.Acquire);
            return;
        }
    }

    internal void MarkPresented(Result presentResult)
    {
        _imageAcquired = false;
        if (presentResult == Result.ErrorSurfaceLostKhr)
            ReleaseSurface();
        else if (presentResult == Result.ErrorOutOfDateKhr)
            _recreatePending = true;
        else if (presentResult == Result.SuboptimalKhr)
            _suboptimal = true;
    }

    // A suboptimal swapchain is remade only when the surface changed since, so one the compositor keeps turning is not remade every frame
    private bool SurfaceChanged()
    {
        Result result = _gd.KhrSurface.GetPhysicalDeviceSurfaceCapabilities(_gd.PhysicalDevice, _surface, out SurfaceCapabilitiesKHR capabilities);
        if (result != Result.Success)
            return true;
        return capabilities.CurrentTransform != _createdTransform
            || capabilities.CurrentExtent.Width != _createdExtent.Width
            || capabilities.CurrentExtent.Height != _createdExtent.Height;
    }

    private VkSemaphore RentAcquireSemaphore()
    {
        if (_freeAcquireSemaphores.Count > 0)
            return _freeAcquireSemaphores.Pop();
        return CreateSemaphore();
    }

    private VkSemaphore CreateSemaphore()
    {
        SemaphoreCreateInfo semaphoreCI = new(sType: StructureType.SemaphoreCreateInfo);
        _gd.Vk.CreateSemaphore(_gd.Device, &semaphoreCI, null, out VkSemaphore semaphore).CheckResult();
        return semaphore;
    }

    private void RecreateAndReacquire(uint width, uint height)
    {
        _recreatePending = false;
        _gd.ConsumePendingAcquires(this);
        _imageAcquired = false;
        if (CreateSwapchain(width, height))
            AcquireNextImage();
    }

    private void ResetSemaphores()
    {
        for (int i = 0; i < _imageAcquireSemaphores.Length; i++)
        {
            if (_imageAcquireSemaphores[i].Handle != 0)
                _freeAcquireSemaphores.Push(_imageAcquireSemaphores[i]);
        }

        int imageCount = _framebuffer.ImageCount;
        _imageAcquireSemaphores = new VkSemaphore[imageCount];

        foreach (VkSemaphore semaphore in _presentSemaphores)
            _gd.Vk.DestroySemaphore(_gd.Device, semaphore, null);
        _presentSemaphores = new VkSemaphore[imageCount];
        for (int i = 0; i < imageCount; i++)
            _presentSemaphores[i] = CreateSemaphore();
    }

    private bool CreateSwapchain(uint width, uint height)
    {
        if (_surface.Handle == 0)
            return false;

        Result result = _gd.KhrSurface.GetPhysicalDeviceSurfaceCapabilities(_gd.PhysicalDevice, _surface, out SurfaceCapabilitiesKHR surfaceCapabilities);
        if (result == Result.ErrorSurfaceLostKhr)
        {
            ReleaseSurface();
            return false;
        }
        result.CheckResult();

        if (surfaceCapabilities.MinImageExtent.Width == 0 && surfaceCapabilities.MinImageExtent.Height == 0
            && surfaceCapabilities.MaxImageExtent.Width == 0 && surfaceCapabilities.MaxImageExtent.Height == 0)
        {
            return false;
        }

        if (_deviceSwapchain.Handle != default)
        {
            _gd.WaitForIdle();
            if (_presentQueueIndex != _gd.GraphicsQueueIndex)
            {
                lock (this)
                    _gd.Vk.QueueWaitIdle(_presentQueue);
            }
        }

        _currentImageIndex = 0;
        SurfaceFormatKHR surfaceFormat = ChooseSurfaceFormat();
        PresentModeKHR presentMode = ChoosePresentMode();

        uint maxImageCount = surfaceCapabilities.MaxImageCount == 0 ? uint.MaxValue : surfaceCapabilities.MaxImageCount;
        uint imageCount = Math.Min(maxImageCount, surfaceCapabilities.MinImageCount + 1);

        // When CurrentExtent is defined (not 0xFFFFFFFF) the spec requires the swapchain to match it
        // exactly. MoltenVK reports the CAMetalLayer's pixel size here; ignoring it and using the
        // caller's logical size yields perpetual VK_SUBOPTIMAL_KHR on Retina displays.
        Extent2D imageExtent = surfaceCapabilities.CurrentExtent.Width != uint.MaxValue
            ? surfaceCapabilities.CurrentExtent
            : new Extent2D
            {
                Width = Math.Clamp(width, surfaceCapabilities.MinImageExtent.Width, surfaceCapabilities.MaxImageExtent.Width),
                Height = Math.Clamp(height, surfaceCapabilities.MinImageExtent.Height, surfaceCapabilities.MaxImageExtent.Height)
            };

        (SurfaceTransformFlagsKHR preTransform, SwapchainTransform transform) = ChooseTransform(surfaceCapabilities.SupportedTransforms, surfaceCapabilities.CurrentTransform, _preRotate);
        if (transform is SwapchainTransform.Rotate90 or SwapchainTransform.Rotate270)
            imageExtent = new Extent2D(imageExtent.Height, imageExtent.Width);

        SwapchainCreateInfoKHR swapchainCI = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            PresentMode = presentMode,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = imageExtent,
            MinImageCount = imageCount,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | (surfaceCapabilities.SupportedUsageFlags & ImageUsageFlags.TransferDstBit)
        };

        uint* queueFamilyIndices = stackalloc uint[2] { _gd.GraphicsQueueIndex, _gd.PresentQueueIndex };

        if (_gd.GraphicsQueueIndex != _gd.PresentQueueIndex)
        {
            swapchainCI.ImageSharingMode = SharingMode.Concurrent;
            swapchainCI.QueueFamilyIndexCount = 2;
            swapchainCI.PQueueFamilyIndices = queueFamilyIndices;
        }
        else
        {
            swapchainCI.ImageSharingMode = SharingMode.Exclusive;
            swapchainCI.QueueFamilyIndexCount = 0;
        }

        swapchainCI.PreTransform = preTransform;
        swapchainCI.CompositeAlpha = ChooseCompositeAlpha(surfaceCapabilities.SupportedCompositeAlpha);
        swapchainCI.Clipped = true;

        SwapchainKHR oldSwapchain = _deviceSwapchain;
        swapchainCI.OldSwapchain = oldSwapchain;

        _gd.KhrSwapchain.CreateSwapchain(_gd.Device, &swapchainCI, null, out _deviceSwapchain).CheckResult();
        if (oldSwapchain.Handle != default)
        {
            _gd.KhrSwapchain.DestroySwapchain(_gd.Device, oldSwapchain, null);
        }

        _framebuffer.SetNewSwapchain(_deviceSwapchain, surfaceFormat, swapchainCI.ImageExtent);
        ResetSemaphores();
        _createdExtent = surfaceCapabilities.CurrentExtent;
        _createdTransform = surfaceCapabilities.CurrentTransform;
        _transform = transform;
        _suboptimal = false;
        return true;
    }

    /// <summary>
    /// The pre transform to create with and the turn the host draws for. Without pre rotation the identity when the
    /// surface allows it, the compositor turning the images, else the surface's current transform as it must be.
    /// </summary>
    internal static (SurfaceTransformFlagsKHR PreTransform, SwapchainTransform Transform) ChooseTransform(SurfaceTransformFlagsKHR supported, SurfaceTransformFlagsKHR current, bool preRotate)
    {
        if (!preRotate && (supported & SurfaceTransformFlagsKHR.IdentityBitKhr) != 0)
            return (SurfaceTransformFlagsKHR.IdentityBitKhr, SwapchainTransform.Identity);

        SwapchainTransform transform = current switch
        {
            SurfaceTransformFlagsKHR.Rotate90BitKhr => SwapchainTransform.Rotate90,
            SurfaceTransformFlagsKHR.Rotate180BitKhr => SwapchainTransform.Rotate180,
            SurfaceTransformFlagsKHR.Rotate270BitKhr => SwapchainTransform.Rotate270,
            _ => SwapchainTransform.Identity,
        };
        return (current, transform);
    }

    /// <summary>The first of opaque, inherit, premultiplied and postmultiplied the surface supports. Android surfaces often allow only inherit.</summary>
    internal static CompositeAlphaFlagsKHR ChooseCompositeAlpha(CompositeAlphaFlagsKHR supported)
    {
        ReadOnlySpan<CompositeAlphaFlagsKHR> modes = [CompositeAlphaFlagsKHR.OpaqueBitKhr, CompositeAlphaFlagsKHR.InheritBitKhr, CompositeAlphaFlagsKHR.PreMultipliedBitKhr, CompositeAlphaFlagsKHR.PostMultipliedBitKhr];
        foreach (CompositeAlphaFlagsKHR mode in modes)
        {
            if ((supported & mode) != 0)
                return mode;
        }
        return CompositeAlphaFlagsKHR.OpaqueBitKhr;
    }

    private SurfaceFormatKHR ChooseSurfaceFormat()
    {
        uint surfaceFormatCount = 0;
        _gd.KhrSurface.GetPhysicalDeviceSurfaceFormats(_gd.PhysicalDevice, _surface, ref surfaceFormatCount, null).CheckResult();
        SurfaceFormatKHR[] formats = new SurfaceFormatKHR[surfaceFormatCount];
        _gd.KhrSurface.GetPhysicalDeviceSurfaceFormats(_gd.PhysicalDevice, _surface, ref surfaceFormatCount, out formats[0]).CheckResult();

        return ChooseSurfaceFormat(formats, _colorSrgb)
            ?? throw new RenderException("Unable to create an sRGB Swapchain for this surface.");
    }

    /// <summary>
    /// An 8 bit BGRA or RGBA format of the wanted encoding in the sRGB colour space, BGRA first as desktops prefer it and
    /// Android has only RGBA. Without sRGB any other format will do, with it null when none matches.
    /// </summary>
    internal static SurfaceFormatKHR? ChooseSurfaceFormat(ReadOnlySpan<SurfaceFormatKHR> formats, bool srgb)
    {
        ReadOnlySpan<Format> wanted = srgb ? [Format.B8G8R8A8Srgb, Format.R8G8B8A8Srgb] : [Format.B8G8R8A8Unorm, Format.R8G8B8A8Unorm];
        if (formats.Length == 1 && formats[0].Format == Format.Undefined)
            return new SurfaceFormatKHR { ColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr, Format = wanted[0] };

        foreach (Format format in wanted)
        {
            foreach (SurfaceFormatKHR candidate in formats)
            {
                if (candidate.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr && candidate.Format == format)
                    return candidate;
            }
        }

        return srgb || formats.Length == 0 ? null : formats[0];
    }

    private PresentModeKHR ChoosePresentMode()
    {
        uint presentModeCount = 0;
        _gd.KhrSurface.GetPhysicalDeviceSurfacePresentModes(_gd.PhysicalDevice, _surface, ref presentModeCount, null).CheckResult();
        PresentModeKHR[] presentModes = new PresentModeKHR[presentModeCount];
        _gd.KhrSurface.GetPhysicalDeviceSurfacePresentModes(_gd.PhysicalDevice, _surface, ref presentModeCount, out presentModes[0]).CheckResult();

        if (_syncToVBlank)
        {
            return presentModes.Contains(PresentModeKHR.FifoRelaxedKhr)
                ? PresentModeKHR.FifoRelaxedKhr
                : PresentModeKHR.FifoKhr;
        }

        if (presentModes.Contains(PresentModeKHR.MailboxKhr))
        {
            return PresentModeKHR.MailboxKhr;
        }
        if (presentModes.Contains(PresentModeKHR.ImmediateKhr))
        {
            return PresentModeKHR.ImmediateKhr;
        }

        return PresentModeKHR.FifoKhr;
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
        _gd.KhrSurface.GetPhysicalDeviceSurfaceSupport(
            _gd.PhysicalDevice,
            queueFamilyIndex,
            surface,
            out Bool32 supported).CheckResult();
        return supported;
    }

    private protected override void DisposeCore()
    {
        _gd.DisposeWhenRetired(DestroyNative);
    }

    private void DestroyNative()
    {
        _gd.ConsumePendingAcquires(this);
        _gd.WaitForGraphicsQueueIdle();

        foreach (VkSemaphore semaphore in _freeAcquireSemaphores)
            _gd.Vk.DestroySemaphore(_gd.Device, semaphore, null);
        foreach (VkSemaphore semaphore in _imageAcquireSemaphores)
        {
            if (semaphore.Handle != 0)
                _gd.Vk.DestroySemaphore(_gd.Device, semaphore, null);
        }
        foreach (VkSemaphore semaphore in _presentSemaphores)
            _gd.Vk.DestroySemaphore(_gd.Device, semaphore, null);

        _framebuffer.Dispose();
        if (_deviceSwapchain.Handle != default)
            _gd.KhrSwapchain.DestroySwapchain(_gd.Device, _deviceSwapchain, null);
        if (_surface.Handle != 0)
            _gd.KhrSurface.DestroySurface(_gd.Instance, _surface, null);
    }
}
