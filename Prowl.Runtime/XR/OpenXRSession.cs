// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.OpenXR;
using Silk.NET.OpenXR.Extensions.KHR;

using VkFormat = Silk.NET.Vulkan.Format;
using XrApi = Silk.NET.OpenXR.XR;

namespace Prowl.Runtime;

/// <summary>
/// One OpenXR instance and session rendering through Graphite's Vulkan device.
/// <para>
/// The instance comes first, before the device exists, since the device is made with the Vulkan extensions and on the
/// GPU the runtime names. The session follows on <see cref="Start"/>. Frame waits, events, locating and input run on
/// the main thread. Frame begin, the swapchain images and frame end run on Graphite's render thread around the frame's
/// submission, holding the graphics queue's lock, since the runtime may use that queue in them.
/// </para>
/// </summary>
internal sealed unsafe partial class OpenXRSession : IDisposable
{
    private const string VulkanExtension = "XR_KHR_vulkan_enable";
    private const string LocalFloorExtension = "XR_EXT_local_floor";
    private const string HandTrackingExtension = "XR_EXT_hand_tracking";
    private const string EyeGazeExtension = "XR_EXT_eye_gaze_interaction";
    private const long InfiniteDuration = long.MaxValue;

    // Swapchain formats in order of preference: the format the eyes render in, and the plain one the mirror reads it as
    private static readonly (VkFormat Vulkan, Graphite.PixelFormat Format, Graphite.PixelFormat Plain)[] s_formats =
    [
        (VkFormat.R8G8B8A8Srgb, Graphite.PixelFormat.R8_G8_B8_A8_UNorm_SRgb, Graphite.PixelFormat.R8_G8_B8_A8_UNorm),
        (VkFormat.B8G8R8A8Srgb, Graphite.PixelFormat.B8_G8_R8_A8_UNorm_SRgb, Graphite.PixelFormat.B8_G8_R8_A8_UNorm),
        (VkFormat.R8G8B8A8Unorm, Graphite.PixelFormat.R8_G8_B8_A8_UNorm, Graphite.PixelFormat.R8_G8_B8_A8_UNorm),
        (VkFormat.B8G8R8A8Unorm, Graphite.PixelFormat.B8_G8_R8_A8_UNorm, Graphite.PixelFormat.B8_G8_R8_A8_UNorm),
    ];

    private readonly XrApi _xr;
    private Instance _instance;
    private ulong _systemId;
    private KhrVulkanEnable? _vulkan;
    private Session _session;
    private Space _appSpace;
    private Space _viewSpace;
    private ReferenceSpaceType _appSpaceType;
    private Posef _appSpaceOffset = new() { Orientation = new Quaternionf(0, 0, 0, 1) };
    private bool _recenterRequested;
    private readonly List<Space> _retiredSpaces = [];
    private bool _localFloorExtension, _handTrackingExtension, _eyeGazeExtension;
    private bool _handTrackingSupported, _eyeGazeSupported;

    private object _queueLock = new();
    private readonly Swapchain[] _swapchains = new Swapchain[2];
    private readonly Graphite.Texture[][] _swapchainTextures = [[], []];
    private readonly Graphite.Framebuffer[][] _swapchainTargets = [[], []];
    private readonly Graphite.TextureView[] _eyeLayerViews = new Graphite.TextureView[2];
    private Graphite.Texture? _eyeTexture;
    private Graphite.Framebuffer? _eyeTarget;
    private Graphite.TextureView? _mirrorSource;
    private readonly EyesPass _eyesPass = new();
    private readonly MirrorPass _mirrorPass = new();
    private readonly List<MirrorView> _mirrors = [];
    private volatile bool _disposed;

    private readonly View[] _appViews = new View[2];
    private readonly View[] _headViews = new View[2];
    private readonly XRView[] _eyeViews = new XRView[2];
    private readonly Posef[] _renderedPoses = new Posef[2];
    private readonly Fovf[] _renderedFovs = new Fovf[2];
    private Camera? _eyesCamera;
    private XRPose _headPose;

    private FrameState _frameState;
    private bool _frameWaited;
    private bool _shouldRender;
    private SessionState _state = SessionState.Unknown;
    private bool _sessionRunning;
    private volatile bool _pacing;

    public OpenXRInput Input { get; private set; } = null!;
    public string RuntimeName { get; private set; } = "";
    public string SystemName { get; private set; } = "";
    public XRTrackingOrigin TrackingOrigin { get; private set; }
    public int EyeWidth { get; private set; }
    public int EyeHeight { get; private set; }

    /// <summary>The Vulkan instance extensions the runtime needs the device made with.</summary>
    public string[] VulkanInstanceExtensions { get; private set; } = [];

    /// <summary>The Vulkan device extensions the runtime needs the device made with.</summary>
    public string[] VulkanDeviceExtensions { get; private set; } = [];

    /// <summary>Seconds between the frames the headset displays, 0 until the first frame has been waited for and while the session is not running.</summary>
    public float DisplayPeriod => _sessionRunning ? _frameState.PredictedDisplayPeriod / 1e9f : 0f;

    /// <summary>True once the runtime wants frames from us, so <c>xrWaitFrame</c> paces the loop.</summary>
    public bool IsPacingFrames => _pacing;

    public bool IsVisible => _state is SessionState.Visible or SessionState.Focused;
    public bool IsFocused => _state == SessionState.Focused;

    /// <summary>The runtime is going away or exiting, so the session has to be torn down.</summary>
    public bool IsLost { get; private set; }

    /// <summary>Why a frame call on the render thread failed, so the main thread can stop XR instead of carrying on blind.</summary>
    public string? RenderThreadFailure { get => _renderThreadFailure; private set => _renderThreadFailure = value; }
    private volatile string? _renderThreadFailure;

    /// <summary>A frame is open and the runtime wants it drawn.</summary>
    public bool ShouldRender => _frameWaited && _shouldRender;

    public XRPose HeadPose => _headPose;

    /// <summary>The two layer texture the eyes draw into, the left display's image in layer 0.</summary>
    public Graphite.Texture? EyeTexture => _eyeTexture;

    /// <summary>Whether any camera drew the eyes this frame.</summary>
    public bool AnyEyeRendered => _eyesCamera.IsValid();

    private OpenXRSession(XrApi xr) => _xr = xr;

    /// <summary>
    /// Connects to the active runtime and its headset and asks what the Vulkan device needs, before the device exists.
    /// <see cref="Start"/> then begins the session on the device.
    /// </summary>
    public static OpenXRSession Prepare()
    {
        XrApi xr;
        try { xr = XrApi.GetApi(); }
        catch (Exception e) { throw new InvalidOperationException($"Could not load the OpenXR loader (openxr_loader). {e.Message}", e); }

        var session = new OpenXRSession(xr);
        try
        {
            session.CreateInstanceAndSystem();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void CreateInstanceAndSystem()
    {
        List<string> available = GetAvailableExtensions();
        if (!available.Contains(VulkanExtension))
            throw new NotSupportedException("The active OpenXR runtime does not support Vulkan.");

        var extensions = new List<string> { VulkanExtension };
        _localFloorExtension = available.Contains(LocalFloorExtension);
        if (_localFloorExtension) extensions.Add(LocalFloorExtension);
        _handTrackingExtension = available.Contains(HandTrackingExtension);
        if (_handTrackingExtension) extensions.Add(HandTrackingExtension);
        _eyeGazeExtension = available.Contains(EyeGazeExtension);
        if (_eyeGazeExtension) extensions.Add(EyeGazeExtension);

        CreateInstance(extensions);
        GetSystem();

        if (!_xr.TryGetInstanceExtension<KhrVulkanEnable>(null, _instance, out KhrVulkanEnable? vulkan))
            throw new NotSupportedException("Could not load the XR_KHR_vulkan_enable functions.");
        _vulkan = vulkan;

        CheckGraphicsRequirements();
        VulkanInstanceExtensions = ReadExtensionList(device: false);
        VulkanDeviceExtensions = ReadExtensionList(device: true);
    }

    private List<string> GetAvailableExtensions()
    {
        uint count = 0;
        Check(_xr.EnumerateInstanceExtensionProperties((byte*)null, 0, &count, (ExtensionProperties*)null), "xrEnumerateInstanceExtensionProperties");

        var properties = new ExtensionProperties[count];
        for (int i = 0; i < properties.Length; i++)
            properties[i].Type = StructureType.ExtensionProperties;

        var names = new List<string>((int)count);
        fixed (ExtensionProperties* ptr = properties)
        {
            Check(_xr.EnumerateInstanceExtensionProperties((byte*)null, count, &count, ptr), "xrEnumerateInstanceExtensionProperties");
            for (int i = 0; i < count; i++)
                names.Add(ReadString(ptr[i].ExtensionName));
        }
        return names;
    }

    private void CreateInstance(List<string> extensions)
    {
        var appInfo = new ApplicationInfo { ApiVersion = new Version64(1, 0, 0) };
        WriteString(appInfo.ApplicationName, 128, AppDomain.CurrentDomain.FriendlyName);
        WriteString(appInfo.EngineName, 128, "Prowl");

        nint names = SilkMarshal.StringArrayToPtr(extensions);
        try
        {
            var createInfo = new InstanceCreateInfo
            {
                Type = StructureType.InstanceCreateInfo,
                ApplicationInfo = appInfo,
                EnabledExtensionCount = (uint)extensions.Count,
                EnabledExtensionNames = (byte**)names,
            };

            Instance instance;
            Check(_xr.CreateInstance(&createInfo, &instance), "xrCreateInstance");
            _instance = instance;
        }
        finally
        {
            SilkMarshal.Free(names);
        }

        var properties = new InstanceProperties { Type = StructureType.InstanceProperties };
        if (_xr.GetInstanceProperties(_instance, &properties) == Result.Success)
            RuntimeName = ReadString(properties.RuntimeName);
    }

    private void GetSystem()
    {
        var getInfo = new SystemGetInfo { Type = StructureType.SystemGetInfo, FormFactor = FormFactor.HeadMountedDisplay };
        ulong systemId;
        Result result = _xr.GetSystem(_instance, &getInfo, &systemId);
        if (result == Result.ErrorFormFactorUnavailable)
            throw new InvalidOperationException("No headset found. Check it is connected and awake, and that its runtime is running.");
        Check(result, "xrGetSystem");
        _systemId = systemId;

        // An extension being there only says the runtime knows it, the headset itself says whether it can do it.
        var hands = new SystemHandTrackingPropertiesEXT { Type = StructureType.SystemHandTrackingPropertiesExt };
        var eyes = new SystemEyeGazeInteractionPropertiesEXT { Type = StructureType.SystemEyeGazeInteractionPropertiesExt };
        var properties = new SystemProperties { Type = StructureType.SystemProperties };
        if (_handTrackingExtension)
        {
            hands.Next = properties.Next;
            properties.Next = &hands;
        }
        if (_eyeGazeExtension)
        {
            eyes.Next = properties.Next;
            properties.Next = &eyes;
        }

        if (_xr.GetSystemProperties(_instance, _systemId, &properties) != Result.Success) return;
        SystemName = ReadString(properties.SystemName);
        _handTrackingSupported = _handTrackingExtension && hands.SupportsHandTracking != 0;
        _eyeGazeSupported = _eyeGazeExtension && eyes.SupportsEyeGazeInteraction != 0;
    }

    // The spec requires this call before xrCreateSession, and the runtime tells us which Vulkan versions it works with.
    private void CheckGraphicsRequirements()
    {
        var requirements = new GraphicsRequirementsVulkanKHR { Type = StructureType.GraphicsRequirementsVulkanKhr };
        Check(_vulkan!.GetVulkanGraphicsRequirements(_instance, _systemId, &requirements), "xrGetVulkanGraphicsRequirementsKHR");

        // XR_MAKE_VERSION packs major into the top 16 bits and minor into the next 16.
        ulong min = requirements.MinApiVersionSupported;
        ulong major = min >> 48, minor = (min >> 32) & 0xFFFF;
        if (major > 1 || (major == 1 && minor > 2))
            Debug.LogWarning($"The OpenXR runtime asks for Vulkan {major}.{minor}, newer than the 1.2 Graphite creates. It may refuse the session.");
    }

    private string[] ReadExtensionList(bool device)
    {
        string call = device ? "xrGetVulkanDeviceExtensionsKHR" : "xrGetVulkanInstanceExtensionsKHR";
        uint count = 0;
        Check(device
            ? _vulkan!.GetVulkanDeviceExtension(_instance, _systemId, 0, &count, (byte*)null)
            : _vulkan!.GetVulkanInstanceExtension(_instance, _systemId, 0, &count, (byte*)null), call);
        if (count == 0) return [];

        byte[] buffer = new byte[count];
        fixed (byte* text = buffer)
        {
            Check(device
                ? _vulkan.GetVulkanDeviceExtension(_instance, _systemId, count, &count, text)
                : _vulkan.GetVulkanInstanceExtension(_instance, _systemId, count, &count, text), call);
            return ReadString(text).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>The GPU the runtime renders on, given the device's VkInstance. For the device's PickPhysicalDevice.</summary>
    public nint PickPhysicalDevice(nint vulkanInstance)
    {
        VkHandle physicalDevice;
        Check(_vulkan!.GetVulkanGraphicsDevice(_instance, _systemId, new VkHandle(vulkanInstance), &physicalDevice), "xrGetVulkanGraphicsDeviceKHR");
        return physicalDevice.Handle;
    }

    /// <summary>
    /// Begins the session on the graphics device, which has to have been made with <paramref name="deviceInstanceExtensions"/>
    /// and <paramref name="deviceExtensions"/> on the GPU this runtime names.
    /// </summary>
    public void Start(XRTrackingOrigin origin, float renderScale, string[] deviceInstanceExtensions, string[] deviceExtensions)
    {
        Graphite.GraphicsDevice device = Graphics.Device ?? throw new InvalidOperationException("XR needs the graphics device.");
        if (!device.Features.Multiview)
            throw new NotSupportedException("The GPU cannot draw both eyes at once (Vulkan multiview).");

        foreach (string extension in VulkanInstanceExtensions)
            if (Array.IndexOf(deviceInstanceExtensions, extension) < 0)
                throw new NotSupportedException($"The runtime needs the Vulkan instance extension {extension}, which the graphics device was not made with. Restart the application with the headset connected.");
        foreach (string extension in VulkanDeviceExtensions)
            if (Array.IndexOf(deviceExtensions, extension) < 0)
                throw new NotSupportedException($"The runtime needs the Vulkan device extension {extension}, which the graphics device was not made with. Restart the application with the headset connected.");

        Graphite.BackendInfoVulkan vulkan = device.GetVulkanInfo();
        if (PickPhysicalDevice(vulkan.Instance) != vulkan.PhysicalDevice)
            throw new NotSupportedException("The runtime renders on a different GPU than the graphics device was made on. Restart the application with the headset connected.");
        _queueLock = vulkan.GraphicsQueueLock;

        ChooseEyeSize(renderScale);
        lock (_queueLock)
            CreateSessionAndSwapchains(vulkan);
        CreateEyeTargets(device);
        CreateSpaces(origin);

        Input = new OpenXRInput(_xr, _instance, _session, _appSpace, _handTrackingSupported, _eyeGazeSupported);

        string extras = (_handTrackingSupported ? ", hand tracking" : "") + (_eyeGazeSupported ? ", eye tracking" : "");
        Debug.Log($"XR started on {SystemName} through {RuntimeName}, {EyeWidth}x{EyeHeight} per eye, tracking from the {TrackingOrigin.ToString().ToLowerInvariant()}{extras}.");
    }

    private void ChooseEyeSize(float renderScale)
    {
        uint count = 0;
        Check(_xr.EnumerateViewConfigurationView(_instance, _systemId, ViewConfigurationType.PrimaryStereo, 0, &count, (ViewConfigurationView*)null), "xrEnumerateViewConfigurationViews");
        if (count != 2)
            throw new NotSupportedException($"The headset reports {count} views, only stereo headsets are supported.");

        ViewConfigurationView* views = stackalloc ViewConfigurationView[2];
        for (int i = 0; i < 2; i++)
            views[i] = new ViewConfigurationView { Type = StructureType.ViewConfigurationView };
        Check(_xr.EnumerateViewConfigurationView(_instance, _systemId, ViewConfigurationType.PrimaryStereo, count, &count, views), "xrEnumerateViewConfigurationViews");

        float scale = Maths.Clamp(renderScale, 0.1f, 2f);
        EyeWidth = (int)Math.Min(views[0].RecommendedImageRectWidth * scale, views[0].MaxImageRectWidth);
        EyeHeight = (int)Math.Min(views[0].RecommendedImageRectHeight * scale, views[0].MaxImageRectHeight);
        EyeWidth = Math.Max(EyeWidth, 1);
        EyeHeight = Math.Max(EyeHeight, 1);
    }

    private Graphite.PixelFormat _format, _plainFormat;

    private void CreateSessionAndSwapchains(Graphite.BackendInfoVulkan vulkan)
    {
        var binding = new GraphicsBindingVulkanKHR
        {
            Type = StructureType.GraphicsBindingVulkanKhr,
            Instance = new VkHandle(vulkan.Instance),
            PhysicalDevice = new VkHandle(vulkan.PhysicalDevice),
            Device = new VkHandle(vulkan.Device),
            QueueFamilyIndex = vulkan.GraphicsQueueFamilyIndex,
            QueueIndex = vulkan.GraphicsQueueIndex,
        };
        var createInfo = new SessionCreateInfo
        {
            Type = StructureType.SessionCreateInfo,
            Next = &binding,
            SystemId = _systemId,
        };
        Session session;
        Check(_xr.CreateSession(_instance, &createInfo, &session), "xrCreateSession");
        _session = session;

        uint count = 0;
        Check(_xr.EnumerateSwapchainFormats(_session, 0, &count, (long*)null), "xrEnumerateSwapchainFormats");
        long[] formats = new long[count];
        fixed (long* ptr = formats)
            Check(_xr.EnumerateSwapchainFormats(_session, count, &count, ptr), "xrEnumerateSwapchainFormats");

        long vulkanFormat = ChooseColorFormat(formats);
        for (int eye = 0; eye < 2; eye++)
            CreateSwapchain(eye, vulkanFormat);
    }

    // Graphite encodes for display itself when it draws into an sRGB target, so an sRGB swapchain shows the eyes as the
    // window shows the game. A plain one gets the encoded colour as linear and looks washed out.
    private long ChooseColorFormat(long[] formats)
    {
        foreach ((VkFormat vulkan, Graphite.PixelFormat format, Graphite.PixelFormat plain) in s_formats)
        {
            if (Array.IndexOf(formats, (long)vulkan) < 0) continue;
            if (format == plain) Debug.LogWarning("The OpenXR runtime offers no sRGB swapchain, so the headset image will look washed out.");
            _format = format;
            _plainFormat = plain;
            return (long)vulkan;
        }
        throw new NotSupportedException("The OpenXR runtime offers no 8 bit RGBA or BGRA swapchain format.");
    }

    private void CreateSwapchain(int eye, long format)
    {
        var swapchainInfo = new SwapchainCreateInfo
        {
            Type = StructureType.SwapchainCreateInfo,
            UsageFlags = SwapchainUsageFlags.ColorAttachmentBit | SwapchainUsageFlags.TransferDstBit,
            Format = format,
            SampleCount = 1,
            Width = (uint)EyeWidth,
            Height = (uint)EyeHeight,
            FaceCount = 1,
            ArraySize = 1,
            MipCount = 1,
        };
        Swapchain swapchain;
        Check(_xr.CreateSwapchain(_session, &swapchainInfo, &swapchain), "xrCreateSwapchain");
        _swapchains[eye] = swapchain;

        uint imageCount = 0;
        Check(_xr.EnumerateSwapchainImages(swapchain, 0, &imageCount, (SwapchainImageBaseHeader*)null), "xrEnumerateSwapchainImages");
        var images = new SwapchainImageVulkanKHR[imageCount];
        for (int i = 0; i < images.Length; i++)
            images[i].Type = StructureType.SwapchainImageVulkanKhr;
        fixed (SwapchainImageVulkanKHR* ptr = images)
            Check(_xr.EnumerateSwapchainImages(swapchain, imageCount, &imageCount, (SwapchainImageBaseHeader*)ptr), "xrEnumerateSwapchainImages");

        // The runtime hands colour images over in the colour attachment layout and wants them back in it.
        Graphite.ResourceFactory factory = Graphics.Device!.ResourceFactory;
        var description = Graphite.TextureDescription.Texture2D((uint)EyeWidth, (uint)EyeHeight, 1, 1, _format, Graphite.TextureUsage.RenderTarget);
        _swapchainTextures[eye] = new Graphite.Texture[imageCount];
        _swapchainTargets[eye] = new Graphite.Framebuffer[imageCount];
        for (int i = 0; i < imageCount; i++)
        {
            _swapchainTextures[eye][i] = factory.CreateTexture((ulong)images[i].Image, description, Graphite.TextureState.Attachment);
            _swapchainTextures[eye][i].Name = $"XR {(eye == 0 ? "left" : "right")} eye {i}";

            // The eyes draw into the image through this target, flipped into the layout the compositor reads.
            _swapchainTargets[eye][i] = factory.CreateFramebuffer(new Graphite.FramebufferDescription(null, _swapchainTextures[eye][i]));
            _swapchainTargets[eye][i].Name = $"XR {(eye == 0 ? "left" : "right")} eye target {i}";
        }
    }

    private void CreateEyeTargets(Graphite.GraphicsDevice device)
    {
        Graphite.ResourceFactory factory = device.ResourceFactory;
        _eyeTexture = factory.CreateTexture(Graphite.TextureDescription.Texture2D((uint)EyeWidth, (uint)EyeHeight, 1, 2, _format, Graphite.TextureUsage.RenderTarget | Graphite.TextureUsage.Sampled));
        _eyeTexture.Name = "XR eyes";
        _eyeTarget = factory.CreateFramebuffer(new Graphite.FramebufferDescription(null, _eyeTexture) { ViewCount = 2 });
        _eyeTarget.Name = "XR eyes";

        // The eyes copy samples each layer in the swapchain's own format: sampling an sRGB view linearises
        // and writing an sRGB attachment encodes again, so the compositor's bytes come out identical.
        for (int eye = 0; eye < 2; eye++)
            _eyeLayerViews[eye] = factory.CreateTextureView(new Graphite.TextureViewDescription(_eyeTexture, 0, 1, (uint)eye, 1, _format));

        // The mirror reads the left display's layer as plain bytes, which hold the colour encoded as the window holds it
        _mirrorSource = factory.CreateTextureView(new Graphite.TextureViewDescription(_eyeTexture, 0, 1, 0, 1, _plainFormat));

    }

    private void CreateSpaces(XRTrackingOrigin origin)
    {
        List<ReferenceSpaceType> supported = GetReferenceSpaces();

        ReferenceSpaceType appType = ReferenceSpaceType.Local;
        TrackingOrigin = XRTrackingOrigin.Seated;
        if (origin == XRTrackingOrigin.Floor)
        {
            if (_localFloorExtension && supported.Contains(ReferenceSpaceType.LocalFloor)) appType = ReferenceSpaceType.LocalFloor;
            else if (supported.Contains(ReferenceSpaceType.Stage)) appType = ReferenceSpaceType.Stage;

            if (appType != ReferenceSpaceType.Local) TrackingOrigin = XRTrackingOrigin.Floor;
            else Debug.LogWarning("The headset has no floor level tracking space, so tracking starts from the head instead.");
        }

        _appSpaceType = appType;
        _appSpace = CreateReferenceSpace(appType, _appSpaceOffset);
        _viewSpace = CreateReferenceSpace(ReferenceSpaceType.View, new Posef { Orientation = new Quaternionf(0, 0, 0, 1) });
    }

    private List<ReferenceSpaceType> GetReferenceSpaces()
    {
        uint count = 0;
        Check(_xr.EnumerateReferenceSpaces(_session, 0, &count, (ReferenceSpaceType*)null), "xrEnumerateReferenceSpaces");
        var spaces = new ReferenceSpaceType[count];
        fixed (ReferenceSpaceType* ptr = spaces)
            Check(_xr.EnumerateReferenceSpaces(_session, count, &count, ptr), "xrEnumerateReferenceSpaces");
        return new List<ReferenceSpaceType>(spaces);
    }

    private Space CreateReferenceSpace(ReferenceSpaceType type, Posef pose)
    {
        var createInfo = new ReferenceSpaceCreateInfo
        {
            Type = StructureType.ReferenceSpaceCreateInfo,
            ReferenceSpaceType = type,
            PoseInReferenceSpace = pose,
        };
        Space space;
        Check(_xr.CreateReferenceSpace(_session, &createInfo, &space), "xrCreateReferenceSpace");
        return space;
    }

    // ----------------------------------------------------------------
    //  Frame loop
    // ----------------------------------------------------------------

    /// <summary>Handles runtime events, then waits for the next frame and locates the head, eyes and hands for it.</summary>
    public void BeginFrame()
    {
        PollEvents();
        if (!_sessionRunning || IsLost)
        {
            // Nothing is tracked or held while the runtime is not running the session.
            _headPose = default;
            Input.Update(0, false);
            return;
        }

        var waitInfo = new FrameWaitInfo { Type = StructureType.FrameWaitInfo };
        var frameState = new FrameState { Type = StructureType.FrameState };
        Check(_xr.WaitFrame(_session, &waitInfo, &frameState), "xrWaitFrame");
        _frameState = frameState;

        _frameWaited = true;
        _eyesCamera = null;
        _mirrors.Clear();

        long time = _frameState.PredictedDisplayTime;
        if (_recenterRequested) Recenter(time);
        _shouldRender = _frameState.ShouldRender != 0 && LocateViews(time);
        _headPose = LocatePose(_viewSpace, _appSpace, time);
        Input.Update(time, IsFocused);
    }

    public void RequestRecenter() => _recenterRequested = true;

    /// <summary>
    /// Moves tracking space so the head is at its centre, facing along it. Only the turn about the vertical is taken,
    /// so the floor stays level, and floor tracking keeps the floor's height. The old space is destroyed on the render
    /// thread once the frames that still use it have ended.
    /// </summary>
    private void Recenter(long time)
    {
        _recenterRequested = false;
        var location = new SpaceLocation { Type = StructureType.SpaceLocation };
        if (_xr.LocateSpace(_viewSpace, _appSpace, time, &location) != Result.Success) return;
        const SpaceLocationFlags valid = SpaceLocationFlags.PositionValidBit | SpaceLocationFlags.OrientationValidBit;
        if ((location.LocationFlags & valid) != valid) return;

        _appSpaceOffset = RecenteredOffset(_appSpaceOffset, location.Pose, keepFloor: TrackingOrigin == XRTrackingOrigin.Floor);
        _retiredSpaces.Add(_appSpace);
        _appSpace = CreateReferenceSpace(_appSpaceType, _appSpaceOffset);
        Input.SetAppSpace(_appSpace);
        XR.RaiseRecentered();
    }

    /// <summary>
    /// The app space's pose in its reference space after recentring on <paramref name="head"/>, which is the head's
    /// pose in the current app space. The new space sits under the head, turned only by the head's yaw so it stays
    /// level, at floor height when <paramref name="keepFloor"/>. Worked in OpenXR's own axes.
    /// </summary>
    internal static Posef RecenteredOffset(Posef offset, Posef head, bool keepFloor)
    {
        float twist = MathF.Sqrt(head.Orientation.Y * head.Orientation.Y + head.Orientation.W * head.Orientation.W);
        Quaternion yaw = twist > 1e-4f ? new Quaternion(0f, head.Orientation.Y / twist, 0f, head.Orientation.W / twist) : Quaternion.Identity;
        var centre = new Float3(head.Position.X, keepFloor ? 0f : head.Position.Y, head.Position.Z);

        var rotation = new Quaternion(offset.Orientation.X, offset.Orientation.Y, offset.Orientation.Z, offset.Orientation.W);
        Float3 position = new Float3(offset.Position.X, offset.Position.Y, offset.Position.Z) + rotation * centre;
        Quaternion orientation = Quaternion.Normalize(rotation * yaw);
        return new Posef
        {
            Position = new Vector3f(position.X, position.Y, position.Z),
            Orientation = new Quaternionf(orientation.X, orientation.Y, orientation.Z, orientation.W),
        };
    }

    /// <summary>The play area's width and depth in metres, when the headset has one and reports it.</summary>
    public bool TryGetPlayAreaSize(out Float2 size)
    {
        Extent2Df bounds;
        if (_xr.GetReferenceSpaceBoundsRect(_session, ReferenceSpaceType.Stage, &bounds) == Result.Success && bounds.Width > 0f && bounds.Height > 0f)
        {
            size = new Float2(bounds.Width, bounds.Height);
            return true;
        }
        size = Float2.Zero;
        return false;
    }

    /// <summary>
    /// Hands the waited frame to this frame's submission: the copy of the eyes into the headset's images when a camera
    /// drew them, and the frame's begin and end around the submission on the render thread. Main thread.
    /// </summary>
    public void EndFrame()
    {
        if (!_frameWaited) return;
        _frameWaited = false;

        var frame = new SubmittedFrame
        {
            Submit = _shouldRender && _eyesCamera.IsValid(),
            DisplayTime = _frameState.PredictedDisplayTime,
            Space = _appSpace,
            RetiredSpaces = _retiredSpaces.ToArray(),
        };
        _retiredSpaces.Clear();
        for (int layer = 0; layer < 2; layer++)
        {
            frame.Poses[layer] = _renderedPoses[layer];
            frame.Fovs[layer] = _renderedFovs[layer];
        }

        if (frame.Submit)
            Graphics.AddHostPass(graph => _eyesPass.Add(graph, this, frame));
        Graphics.AddFrameCallbacks(() => FrameStarted(frame), succeeded => FrameSubmitted(frame, succeeded));
    }

    private sealed class SubmittedFrame
    {
        public bool Submit;
        public long DisplayTime;
        public Space Space;
        public Space[] RetiredSpaces = [];
        public readonly Posef[] Poses = new Posef[2];
        public readonly Fovf[] Fovs = new Fovf[2];
        public readonly uint[] Images = new uint[2];
        public readonly bool[] Waited = new bool[2];
        public readonly bool[] Copied = new bool[2];
        public bool Begun;
    }

    // Render thread, before the frame's work is recorded.
    private void FrameStarted(SubmittedFrame frame)
    {
        if (_disposed) return;
        lock (_queueLock)
        {
            try
            {
                var beginInfo = new FrameBeginInfo { Type = StructureType.FrameBeginInfo };
                Check(_xr.BeginFrame(_session, &beginInfo), "xrBeginFrame");
                frame.Begun = true;
            }
            catch (Exception e)
            {
                RenderThreadFailure = e.Message;
                return;
            }

            if (!frame.Submit) return;
            try
            {
                for (int eye = 0; eye < 2; eye++)
                {
                    var acquireInfo = new SwapchainImageAcquireInfo { Type = StructureType.SwapchainImageAcquireInfo };
                    uint index;
                    Check(_xr.AcquireSwapchainImage(_swapchains[eye], &acquireInfo, &index), "xrAcquireSwapchainImage");
                    frame.Images[eye] = index;

                    // Only a waited image can be released, so one whose wait failed is left to the runtime.
                    var waitInfo = new SwapchainImageWaitInfo { Type = StructureType.SwapchainImageWaitInfo, Timeout = InfiniteDuration };
                    Check(_xr.WaitSwapchainImage(_swapchains[eye], &waitInfo), "xrWaitSwapchainImage");
                    frame.Waited[eye] = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Getting the headset's images failed: {e.Message}");
                frame.Submit = false;
            }
        }
    }

    // Render thread, once the frame's command buffers, the eye copies among them, are on the queue.
    private void FrameSubmitted(SubmittedFrame frame, bool succeeded)
    {
        if (_disposed) return;
        lock (_queueLock)
        {
            for (int eye = 0; eye < 2; eye++)
            {
                if (!frame.Waited[eye]) continue;
                var releaseInfo = new SwapchainImageReleaseInfo { Type = StructureType.SwapchainImageReleaseInfo };
                _xr.ReleaseSwapchainImage(_swapchains[eye], &releaseInfo);
            }

            if (frame.Begun)
                EndFrame(frame, succeeded && frame.Submit && frame.Copied[0] && frame.Copied[1]);

            foreach (Space space in frame.RetiredSpaces)
                _xr.DestroySpace(space);
        }
    }

    private void EndFrame(SubmittedFrame frame, bool show)
    {
        CompositionLayerProjectionView* views = stackalloc CompositionLayerProjectionView[2];
        for (int eye = 0; eye < 2; eye++)
        {
            views[eye] = new CompositionLayerProjectionView
            {
                Type = StructureType.CompositionLayerProjectionView,
                Pose = frame.Poses[eye],
                Fov = frame.Fovs[eye],
                SubImage = new SwapchainSubImage
                {
                    Swapchain = _swapchains[eye],
                    ImageRect = new Rect2Di { Offset = new Offset2Di(0, 0), Extent = new Extent2Di(EyeWidth, EyeHeight) },
                },
            };
        }

        var layer = new CompositionLayerProjection
        {
            Type = StructureType.CompositionLayerProjection,
            Space = frame.Space,
            ViewCount = 2,
            Views = views,
        };
        CompositionLayerBaseHeader** layers = stackalloc CompositionLayerBaseHeader*[1];
        layers[0] = (CompositionLayerBaseHeader*)&layer;
        var endInfo = new FrameEndInfo
        {
            Type = StructureType.FrameEndInfo,
            DisplayTime = frame.DisplayTime,
            EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
            LayerCount = show ? 1u : 0u,
            Layers = show ? layers : null,
        };

        try { Check(_xr.EndFrame(_session, &endInfo), "xrEndFrame"); }
        catch (Exception e) { RenderThreadFailure = e.Message; }
    }

    private bool LocateViews(long time)
    {
        if (!LocateViews(_appSpace, time, _appViews)) return false;
        if (!LocateViews(_viewSpace, time, _headViews)) return false;

        for (int i = 0; i < 2; i++)
        {
            ref View view = ref _headViews[i];
            _eyeViews[i] = new XRView
            {
                Position = ToProwl(view.Pose.Position),
                Rotation = ToProwl(view.Pose.Orientation),
                TanLeft = MathF.Tan(view.Fov.AngleLeft),
                TanRight = MathF.Tan(view.Fov.AngleRight),
                TanUp = MathF.Tan(view.Fov.AngleUp),
                TanDown = MathF.Tan(view.Fov.AngleDown),
            };
        }
        return true;
    }

    // Rendering only needs the orientation. Runtimes keep reporting the last known position through a moment
    // of lost positional tracking, and dropping those frames would black out the headset instead.
    private bool LocateViews(Space space, long time, View[] views)
    {
        var locateInfo = new ViewLocateInfo
        {
            Type = StructureType.ViewLocateInfo,
            ViewConfigurationType = ViewConfigurationType.PrimaryStereo,
            DisplayTime = time,
            Space = space,
        };
        var viewState = new ViewState { Type = StructureType.ViewState };
        views[0] = new View { Type = StructureType.View };
        views[1] = new View { Type = StructureType.View };

        uint count = 0;
        fixed (View* ptr = views)
            Check(_xr.LocateView(_session, &locateInfo, &viewState, 2, &count, ptr), "xrLocateViews");

        return count == 2 && (viewState.ViewStateFlags & ViewStateFlags.OrientationValidBit) != 0;
    }

    public XRPose LocatePose(Space space, Space baseSpace, long time) => Locate(_xr, space, baseSpace, time);

    /// <summary>Where a space is at <paramref name="time"/> in <paramref name="baseSpace"/>, with how fast it moves and turns.</summary>
    internal static XRPose Locate(XrApi xr, Space space, Space baseSpace, long time)
    {
        var velocity = new SpaceVelocity { Type = StructureType.SpaceVelocity };
        var location = new SpaceLocation { Type = StructureType.SpaceLocation, Next = &velocity };
        if (xr.LocateSpace(space, baseSpace, time, &location) != Result.Success)
            return default;

        XRPose pose = ToProwl(location);
        pose.HasLinearVelocity = (velocity.VelocityFlags & SpaceVelocityFlags.LinearValidBit) != 0;
        pose.HasAngularVelocity = (velocity.VelocityFlags & SpaceVelocityFlags.AngularValidBit) != 0;
        if (pose.HasLinearVelocity) pose.LinearVelocity = ToProwl(velocity.LinearVelocity);
        if (pose.HasAngularVelocity) pose.AngularVelocity = ToProwlAngular(velocity.AngularVelocity);
        return pose;
    }

    private void PollEvents()
    {
        var buffer = new EventDataBuffer { Type = StructureType.EventDataBuffer };
        while (_xr.PollEvent(_instance, &buffer) == Result.Success)
        {
            switch (buffer.Type)
            {
                case StructureType.EventDataSessionStateChanged:
                    OnStateChanged(Unsafe.As<EventDataBuffer, EventDataSessionStateChanged>(ref buffer).State);
                    break;
                case StructureType.EventDataInstanceLossPending:
                    IsLost = true;
                    break;
                case StructureType.EventDataInteractionProfileChanged:
                    Input.RefreshControllers();
                    break;
                case StructureType.EventDataReferenceSpaceChangePending:
                    // The runtime recentred or the play area was redrawn, which moves the space our poses are in.
                    if (Unsafe.As<EventDataBuffer, EventDataReferenceSpaceChangePending>(ref buffer).ReferenceSpaceType == _appSpaceType)
                        XR.RaiseRecentered();
                    break;
            }
            buffer = new EventDataBuffer { Type = StructureType.EventDataBuffer };
        }
    }

    private void OnStateChanged(SessionState state)
    {
        _state = state;
        switch (state)
        {
            case SessionState.Ready:
                var beginInfo = new SessionBeginInfo
                {
                    Type = StructureType.SessionBeginInfo,
                    PrimaryViewConfigurationType = ViewConfigurationType.PrimaryStereo,
                };
                Check(_xr.BeginSession(_session, &beginInfo), "xrBeginSession");
                _sessionRunning = true;
                _pacing = true;
                break;
            case SessionState.Stopping:
                _pacing = false;
                _sessionRunning = false;
                // Frames already queued still end against the session, so it ends after them.
                Graphics.Renderer?.WaitForRenderThread();
                Check(_xr.EndSession(_session), "xrEndSession");
                break;
            case SessionState.LossPending:
            case SessionState.Exiting:
                _pacing = false;
                _sessionRunning = false;
                IsLost = true;
                break;
        }
    }

    // ----------------------------------------------------------------
    //  Eyes
    // ----------------------------------------------------------------

    /// <summary>Whether <paramref name="camera"/> may draw the eyes this frame: no other camera has.</summary>
    public bool CanDrawEyes(Camera camera) => _eyesCamera.IsNotValid() || _eyesCamera == camera;

    /// <summary>Whether <paramref name="camera"/> drew the eyes this frame.</summary>
    public bool HasRenderedStereo(Camera camera) => _eyesCamera.IsValid() && _eyesCamera == camera;

    /// <summary>
    /// Makes <paramref name="camera"/> the one drawing the eyes this frame, returning the two layer target it draws them
    /// into. True the first time it asks this frame, false when it already drew them.
    /// </summary>
    public bool ClaimEyes(Camera camera, out Graphite.Framebuffer target)
    {
        target = _eyeTarget!;
        if (HasRenderedStereo(camera)) return false;
        if (!CanDrawEyes(camera)) throw new InvalidOperationException($"Camera '{_eyesCamera!.GameObject.Name}' already draws the headset's eyes this frame.");

        _eyesCamera = camera;
        for (int layer = 0; layer < 2; layer++)
        {
            int eye = EyeIndex(EyeDrawnInLayer(camera, layer));
            _renderedPoses[layer] = RenderedPose(camera, eye);
            _renderedFovs[layer] = _appViews[eye].Fov;
        }
        return true;
    }

    /// <summary>
    /// The eye a layer of <paramref name="camera"/>'s stereo view is drawn from. A camera drawing a single eye fills both
    /// layers with it, so both displays show the same image, each sent with the pose it was drawn from.
    /// </summary>
    public static StereoEye EyeDrawnInLayer(Camera camera, int layer)
    {
        bool left = (camera.StereoTargetEye & StereoTargetEyeMask.Left) != 0;
        bool right = (camera.StereoTargetEye & StereoTargetEyeMask.Right) != 0;
        if (layer == 0) return left || !right ? StereoEye.Left : StereoEye.Right;
        return right || !left ? StereoEye.Right : StereoEye.Left;
    }

    /// <summary>
    /// Where the camera draws the eye from, in tracking space. The camera's parent stands for the play area, so the
    /// eye's place relative to it is its place in tracking space, measured in tracking space metres.
    /// </summary>
    private Posef RenderedPose(Camera camera, int index)
    {
        Transform head = camera.Transform;
        XRView view = _eyeViews[index];
        Float3 position = head.TransformPoint(view.Position);
        Quaternion rotation = head.Rotation * view.Rotation;
        if (head.Parent != null)
        {
            position = head.Parent.InverseTransformPoint(position);
            rotation = Quaternion.Inverse(head.Parent.Rotation) * rotation;
        }
        return new Posef { Position = ToXr(position), Orientation = ToXr(Quaternion.Normalize(rotation)) };
    }

    /// <summary>Shows the left display's image in <paramref name="target"/>, cropped to its aspect so it fills it without stretching.</summary>
    public void Mirror(Graphite.Framebuffer target)
    {
        foreach (MirrorView existing in _mirrors)
            if (existing.Target == target && !existing.Covered) return;

        float targetAspect = target.Width / (float)Math.Max(1u, target.Height);
        float eyeAspect = EyeWidth / (float)EyeHeight;
        Float4 crop = targetAspect > eyeAspect
            ? new Float4(0f, 0.5f - 0.5f * eyeAspect / targetAspect, 1f, eyeAspect / targetAspect)
            : new Float4(0.5f - 0.5f * targetAspect / eyeAspect, 0f, targetAspect / eyeAspect, 1f);

        var view = new MirrorView(target, _mirrorSource!, crop);
        _mirrors.Add(view);
        Graphics.AddHostPass(graph => _mirrorPass.Add(graph, this, view));
    }

    /// <summary>A later view drew over the whole of <paramref name="target"/>, so the mirror under it is left out.</summary>
    public void CoverMirror(Graphite.Framebuffer target)
    {
        foreach (MirrorView mirror in _mirrors)
            if (mirror.Target == target) mirror.Covered = true;
    }

    public XRView GetEyeView(StereoEye eye) => _eyeViews[EyeIndex(eye)];

    private static int EyeIndex(StereoEye eye) => eye switch
    {
        StereoEye.Left => 0,
        StereoEye.Right => 1,
        _ => throw new ArgumentException("Only the left and right eyes have XR views.", nameof(eye)),
    };

    // Renders each layer of the eyes into the image its display shows this frame, flipped into the
    // orientation the compositor reads.
    private sealed class EyesPass
    {
        private ShaderPass? _shader;
        private readonly Graphite.PropertySet _properties = new();

        public void Add(Graphite.FrameGraph graph, OpenXRSession session, SubmittedFrame frame)
        {
            if (session._disposed) return;
            for (int eye = 0; eye < 2; eye++)
            {
                if (!frame.Waited[eye]) continue;
                int index = eye;
                Graphite.TextureHandle target = graph.ImportTarget(session._swapchainTargets[index][frame.Images[index]]);
                Graphite.TextureHandle source = graph.Import(session._eyeTexture!);
                graph.AddPass($"XR {(index == 0 ? "left" : "right")} eye", pass =>
                {
                    // The target is the headset's image, written whole and handed back to the runtime.
                    pass.Write(target, firstWrite: new Graphite.TargetLoadStoreOps(new Graphite.AttachmentOps(Graphite.LoadAction.DontCare, Graphite.StoreAction.Store), Graphite.AttachmentOps.Discard));
                    pass.Read(source);
                }, context =>
                {
                    Draw(context.Command, session._eyeLayerViews[index]);
                    frame.Copied[index] = true;
                });
            }
        }

        private void Draw(Graphite.CommandBuffer cmd, Graphite.TextureView source)
        {
            if (_shader == null)
            {
                ShaderDefinition definition = ShaderParser.Parse(EmbeddedResources.ReadAllText("Assets/Defaults/Shaders/XREyes.shader"));
                definition.Create(Graphics.Device!, Graphics.Compiler, new Variant(), CompileMode.All);
                _shader = definition.Passes![0];
            }

            _properties.SetTexture("_MainTex", source, Graphics.GetSampler(Graphite.SamplerDescription.Linear));
            cmd.SetShader(_shader);
            cmd.SetVertexSource(Graphite.VertexSource.None);
            cmd.SetProperties(_properties);
            cmd.Draw(3);
        }
    }

    private sealed class MirrorView(Graphite.Framebuffer target, Graphite.TextureView source, Float4 crop)
    {
        public Graphite.TextureView Source => source;
        public Float4 Crop => crop;
        public volatile bool Covered;
        public Graphite.Framebuffer Target => target;
    }

    private sealed class MirrorPass
    {
        private ShaderPass? _shader;
        private readonly Graphite.PropertySet _properties = new();

        public void Add(Graphite.FrameGraph graph, OpenXRSession session, MirrorView view)
        {
            if (view.Covered || session._disposed) return;
            Graphite.TextureHandle target = graph.ImportTarget(view.Target);
            Graphite.TextureHandle source = graph.Import(session._eyeTexture!);
            graph.AddPass("XR Mirror", pass =>
            {
                pass.Write(target, firstWrite: new Graphite.TargetLoadStoreOps(new Graphite.AttachmentOps(Graphite.LoadAction.Load, Graphite.StoreAction.Store), Graphite.AttachmentOps.Discard));
                pass.Read(source);
            }, context => Draw(context.Command, view));
        }

        private void Draw(Graphite.CommandBuffer cmd, MirrorView view)
        {
            if (_shader == null)
            {
                ShaderDefinition definition = ShaderParser.Parse(EmbeddedResources.ReadAllText("Assets/Defaults/Shaders/XRMirror.shader"));
                definition.Create(Graphics.Device!, Graphics.Compiler, new Variant(), CompileMode.All);
                _shader = definition.Passes![0];
            }

            _properties.SetTexture("_MainTex", view.Source, Graphics.GetSampler(Graphite.SamplerDescription.Linear));
            _properties.SetFloat4("_Crop", view.Crop);
            cmd.SetShader(_shader);
            cmd.SetVertexSource(Graphite.VertexSource.None);
            cmd.SetProperties(_properties);
            cmd.Draw(3);
        }
    }

    // ----------------------------------------------------------------
    //  Teardown
    // ----------------------------------------------------------------

    public void Dispose()
    {
        _pacing = false;

        // Frames already queued end against the session first. A frame not yet submitted sees it gone and does nothing.
        Graphics.Renderer?.WaitForRenderThread();
        _disposed = true;

        if (_session.Handle != 0)
        {
            try
            {
                Graphics.Device?.WaitForIdle();
                lock (_queueLock)
                {
                    for (int i = 0; i < 2; i++)
                        if (_swapchains[i].Handle != 0) _xr.DestroySwapchain(_swapchains[i]);
                    _xr.DestroySession(_session);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Destroying the XR session failed: {e.Message}");
            }
            _session = default;
        }

        // Views added this frame may still draw into the eyes, so they go once the frames using them are done
        for (int eye = 0; eye < 2; eye++)
        {
            foreach (Graphite.Texture image in _swapchainTextures[eye])
                Graphics.DeferDispose(image);
            _swapchainTextures[eye] = [];
            foreach (Graphite.Framebuffer target in _swapchainTargets[eye])
                Graphics.DeferDispose(target);
            _swapchainTargets[eye] = [];
            Graphics.DeferDispose(_eyeLayerViews[eye]);
            _eyeLayerViews[eye] = null!;
        }
        Graphics.DeferDispose(_mirrorSource);
        Graphics.DeferDispose(_eyeTarget);
        Graphics.DeferDispose(_eyeTexture);
        _mirrorSource = null;
        _eyeTarget = null;
        _eyeTexture = null;

        // Action sets and spaces are children of the instance and session, so they go with them.
        if (_instance.Handle != 0) _xr.DestroyInstance(_instance);
        _instance = default;
        _xr.Dispose();
    }

    // ----------------------------------------------------------------
    //  Helpers
    // ----------------------------------------------------------------

    // OpenXR is right handed with -Z forward, Prowl is left handed with +Z forward, so poses mirror across Z.
    internal static Float3 ToProwl(Vector3f v) => new(v.X, v.Y, -v.Z);
    internal static Quaternion ToProwl(Quaternionf q) => new(-q.X, -q.Y, q.Z, q.W);
    // A turn is an axis, which mirrors the other way to a position, as the axis part of a rotation does.
    internal static Float3 ToProwlAngular(Vector3f v) => new(-v.X, -v.Y, v.Z);
    internal static Vector3f ToXr(Float3 v) => new(v.X, v.Y, -v.Z);
    internal static Quaternionf ToXr(Quaternion q) => new(-q.X, -q.Y, q.Z, q.W);

    internal static XRPose ToProwl(in SpaceLocation location)
    {
        SpaceLocationFlags flags = location.LocationFlags;
        return new XRPose
        {
            Position = ToProwl(location.Pose.Position),
            Rotation = ToProwl(location.Pose.Orientation),
            HasPosition = (flags & SpaceLocationFlags.PositionValidBit) != 0,
            HasRotation = (flags & SpaceLocationFlags.OrientationValidBit) != 0,
            IsTracked = (flags & SpaceLocationFlags.OrientationTrackedBit) != 0 && (flags & SpaceLocationFlags.PositionTrackedBit) != 0,
        };
    }

    internal static void Check(Result result, string call)
    {
        if (result < 0) throw new InvalidOperationException($"{call} failed with {result}.");
    }

    internal static string ReadString(byte* text) => Marshal.PtrToStringUTF8((nint)text) ?? "";

    /// <summary>Writes <paramref name="value"/> as a terminated UTF-8 string, cut short to fit the buffer.</summary>
    internal static void WriteString(byte* destination, int capacity, string value)
    {
        while (Encoding.UTF8.GetByteCount(value) > capacity - 1) value = value[..^1];
        int length = Encoding.UTF8.GetBytes(value, new Span<byte>(destination, capacity - 1));
        destination[length] = 0;
    }
}
