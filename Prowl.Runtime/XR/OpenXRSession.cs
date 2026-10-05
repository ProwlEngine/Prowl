// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.OpenXR;
using Silk.NET.OpenXR.Extensions.KHR;

using GLEnum = Silk.NET.OpenGL.GLEnum;
using XrApi = Silk.NET.OpenXR.XR;

namespace Prowl.Runtime;

/// <summary>
/// One OpenXR instance and session rendering through the engine's OpenGL context.
/// <para>
/// OpenXR forbids the GL context from being current on another thread during session, swapchain and frame
/// begin/end calls, and the render thread holds it for its whole life, so those calls run there as render
/// thread callbacks queued in order with the frame's command buffers. Everything else (events, frame waits,
/// locating, input) runs on the main thread.
/// </para>
/// </summary>
internal sealed unsafe partial class OpenXRSession : IDisposable
{
    private const string OpenGLExtension = "XR_KHR_opengl_enable";
    private const string LocalFloorExtension = "XR_EXT_local_floor";
    private const string DepthExtension = "XR_KHR_composition_layer_depth";
    private const string HandTrackingExtension = "XR_EXT_hand_tracking";
    private const string EyeGazeExtension = "XR_EXT_eye_gaze_interaction";

    // Projections reach the GPU remapped to GL's clip depth (RenderPipeline.ToGLClipDepth), so the near
    // and far planes span the whole window depth range.
    private const float MinWindowDepth = 0f;
    private const float MaxWindowDepth = 1f;
    private const long InfiniteDuration = long.MaxValue;

    private readonly XrApi _xr;
    private Instance _instance;
    private ulong _systemId;
    private Session _session;
    private Space _appSpace;
    private Space _viewSpace;
    private ReferenceSpaceType _appSpaceType;
    private Posef _appSpaceOffset = new() { Orientation = new Quaternionf(0, 0, 0, 1) };
    private bool _recenterRequested;
    private bool _handTrackingExtension, _eyeGazeExtension;
    private bool _handTrackingSupported, _eyeGazeSupported;

    private readonly Swapchain[] _swapchains = new Swapchain[2];
    private readonly uint[][] _swapchainImages = new uint[2][];
    private readonly Swapchain[] _depthSwapchains = new Swapchain[2];
    private readonly uint[][] _depthSwapchainImages = new uint[2][];
    private bool _depthExtension;
    private bool _submitDepth;
    private float _nearZ = 0.1f;
    private float _farZ = 100f;
    private Camera? _depthCamera;
    private readonly RenderTexture[] _eyeTextures = new RenderTexture[2];
    private uint _copyFramebuffer;

    private readonly View[] _appViews = new View[2];
    private readonly View[] _headViews = new View[2];
    private readonly XRView[] _eyeViews = new XRView[2];
    private readonly bool[] _eyeRendered = new bool[2];
    private readonly bool[] _eyeDepthWritten = new bool[2];
    private readonly Posef[] _renderedPoses = new Posef[2];
    private readonly HashSet<Camera> _stereoCameras = [];
    private XRPose _headPose;

    private FrameState _frameState;
    private bool _frameBegun;
    private bool _shouldRender;
    private SessionState _state = SessionState.Unknown;
    private bool _sessionRunning;
    private volatile bool _pacing;

    public OpenXRInput Input { get; private set; }
    public string RuntimeName { get; private set; } = "";
    public string SystemName { get; private set; } = "";
    public XRTrackingOrigin TrackingOrigin { get; private set; }
    public int EyeWidth { get; private set; }
    public int EyeHeight { get; private set; }

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
    public bool ShouldRender => _frameBegun && _shouldRender;

    private OpenXRSession(XrApi xr) => _xr = xr;

    public static OpenXRSession Create(XRTrackingOrigin origin, float renderScale)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("OpenXR currently needs Windows, since the session is bound to a WGL context.");

        XrApi xr;
        try { xr = XrApi.GetApi(); }
        catch (Exception e) { throw new InvalidOperationException($"Could not load the OpenXR loader (openxr_loader). {e.Message}", e); }

        var session = new OpenXRSession(xr);
        try
        {
            session.Initialize(origin, renderScale);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void Initialize(XRTrackingOrigin origin, float renderScale)
    {
        List<string> available = GetAvailableExtensions();
        if (!available.Contains(OpenGLExtension))
            throw new NotSupportedException("The active OpenXR runtime does not support OpenGL. SteamVR and the Meta PC runtime do, Windows Mixed Reality does not.");

        var extensions = new List<string> { OpenGLExtension };
        bool localFloor = available.Contains(LocalFloorExtension);
        if (localFloor) extensions.Add(LocalFloorExtension);
        _depthExtension = available.Contains(DepthExtension);
        if (_depthExtension) extensions.Add(DepthExtension);
        _handTrackingExtension = available.Contains(HandTrackingExtension);
        if (_handTrackingExtension) extensions.Add(HandTrackingExtension);
        _eyeGazeExtension = available.Contains(EyeGazeExtension);
        if (_eyeGazeExtension) extensions.Add(EyeGazeExtension);

        CreateInstance(extensions);
        GetSystem();
        CheckGraphicsRequirements();
        ChooseEyeSize(renderScale);

        Graphics.SubmitRenderThreadCallbackAndWait(CreateSessionOnRenderThread);

        CreateSpaces(origin, localFloor);

        // Render textures allocate on first use, and the first use of an eye nothing drew would be the copy on
        // the render thread, which cannot wait on itself. Touching the framebuffer here allocates them up front.
        for (int i = 0; i < 2; i++)
        {
            _eyeTextures[i] = new RenderTexture(EyeWidth, EyeHeight, true, [TextureImageFormat.Color4b]);
            _ = _eyeTextures[i].frameBuffer;
        }

        Input = new OpenXRInput(_xr, _instance, _session, _appSpace, _handTrackingSupported, _eyeGazeSupported);

        string extras = (_submitDepth ? ", submitting depth" : "") + (_handTrackingSupported ? ", hand tracking" : "") + (_eyeGazeSupported ? ", eye tracking" : "");
        Debug.Log($"XR started on {SystemName} through {RuntimeName}, {EyeWidth}x{EyeHeight} per eye, tracking from the {TrackingOrigin.ToString().ToLowerInvariant()}{extras}.");
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

    // The spec requires this call before xrCreateSession, and the runtime tells us which GL versions it works with.
    private void CheckGraphicsRequirements()
    {
        if (!_xr.TryGetInstanceExtension<KhrOpenglEnable>(null, _instance, out var openGL))
            throw new NotSupportedException("Could not load the XR_KHR_opengl_enable functions.");

        var requirements = new GraphicsRequirementsOpenGLKHR { Type = StructureType.GraphicsRequirementsOpenglKhr };
        Check(openGL.GetOpenGlgraphicsRequirements(_instance, _systemId, &requirements), "xrGetOpenGLGraphicsRequirementsKHR");

        // XR_MAKE_VERSION packs major into the top 16 bits and minor into the next 16.
        ulong min = requirements.MinApiVersionSupported;
        ulong major = min >> 48, minor = (min >> 32) & 0xFFFF;
        if (major > 4 || (major == 4 && minor > 1))
            Debug.LogWarning($"The OpenXR runtime asks for OpenGL {major}.{minor}, newer than the 4.1 context Prowl creates. It may refuse the session.");
    }

    private void ChooseEyeSize(float renderScale)
    {
        uint count = 0;
        Check(_xr.EnumerateViewConfigurationView(_instance, _systemId, ViewConfigurationType.PrimaryStereo, 0, &count, (ViewConfigurationView*)null), "xrEnumerateViewConfigurationViews");
        if (count != 2)
            throw new NotSupportedException($"The headset reports {count} views, only stereo headsets are supported.");

        var views = stackalloc ViewConfigurationView[2];
        for (int i = 0; i < 2; i++)
            views[i] = new ViewConfigurationView { Type = StructureType.ViewConfigurationView };
        Check(_xr.EnumerateViewConfigurationView(_instance, _systemId, ViewConfigurationType.PrimaryStereo, count, &count, views), "xrEnumerateViewConfigurationViews");

        float scale = Maths.Clamp(renderScale, 0.1f, 2f);
        EyeWidth = (int)Math.Min(views[0].RecommendedImageRectWidth * scale, views[0].MaxImageRectWidth);
        EyeHeight = (int)Math.Min(views[0].RecommendedImageRectHeight * scale, views[0].MaxImageRectHeight);
        EyeWidth = Math.Max(EyeWidth, 1);
        EyeHeight = Math.Max(EyeHeight, 1);
    }

    private void CreateSessionOnRenderThread()
    {
        var binding = new GraphicsBindingOpenGLWin32KHR
        {
            Type = StructureType.GraphicsBindingOpenglWin32Khr,
            HDC = WglGetCurrentDC(),
            HGlrc = WglGetCurrentContext(),
        };
        if (binding.HGlrc == 0)
            throw new InvalidOperationException("The render thread has no current WGL context to bind the XR session to.");

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
        var formats = new long[count];
        fixed (long* ptr = formats)
            Check(_xr.EnumerateSwapchainFormats(_session, count, &count, ptr), "xrEnumerateSwapchainFormats");

        long colorFormat = ChooseColorFormat(formats);
        for (int eye = 0; eye < 2; eye++)
            (_swapchains[eye], _swapchainImages[eye]) = CreateSwapchain(colorFormat, SwapchainUsageFlags.ColorAttachmentBit);

        // Copying depth is a framebuffer blit, which needs the swapchain in the same format as the eye's depth.
        long depthFormat = (long)GLEnum.DepthComponent24;
        _submitDepth = _depthExtension && Array.IndexOf(formats, depthFormat) >= 0;
        if (_submitDepth)
            for (int eye = 0; eye < 2; eye++)
                (_depthSwapchains[eye], _depthSwapchainImages[eye]) = CreateSwapchain(depthFormat, SwapchainUsageFlags.DepthStencilAttachmentBit);

        _copyFramebuffer = Graphics.GL.GenFramebuffer();
    }

    private (Swapchain, uint[]) CreateSwapchain(long format, SwapchainUsageFlags usage)
    {
        var swapchainInfo = new SwapchainCreateInfo
        {
            Type = StructureType.SwapchainCreateInfo,
            UsageFlags = usage | SwapchainUsageFlags.TransferDstBit,
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

        uint imageCount = 0;
        Check(_xr.EnumerateSwapchainImages(swapchain, 0, &imageCount, (SwapchainImageBaseHeader*)null), "xrEnumerateSwapchainImages");
        var images = new SwapchainImageOpenGLKHR[imageCount];
        for (int i = 0; i < images.Length; i++)
            images[i].Type = StructureType.SwapchainImageOpenglKhr;
        fixed (SwapchainImageOpenGLKHR* ptr = images)
            Check(_xr.EnumerateSwapchainImages(swapchain, imageCount, &imageCount, (SwapchainImageBaseHeader*)ptr), "xrEnumerateSwapchainImages");

        var handles = new uint[imageCount];
        for (int i = 0; i < imageCount; i++)
            handles[i] = images[i].Image;
        return (swapchain, handles);
    }

    // The engine writes gamma encoded colour into plain RGBA8 targets, and the copy moves those bytes as they
    // are, so an sRGB swapchain shows them exactly as the window does. A linear one would be encoded twice.
    private static long ChooseColorFormat(long[] formats)
    {
        long srgb = (long)GLEnum.Srgb8Alpha8;
        long linear = (long)GLEnum.Rgba8;
        if (Array.IndexOf(formats, srgb) >= 0) return srgb;
        if (Array.IndexOf(formats, linear) >= 0)
        {
            Debug.LogWarning("The OpenXR runtime offers no sRGB swapchain, so the headset image will look washed out.");
            return linear;
        }
        throw new NotSupportedException("The OpenXR runtime offers no 8 bit RGBA swapchain format.");
    }

    private void CreateSpaces(XRTrackingOrigin origin, bool localFloor)
    {
        var supported = GetReferenceSpaces();

        ReferenceSpaceType appType = ReferenceSpaceType.Local;
        TrackingOrigin = XRTrackingOrigin.Seated;
        if (origin == XRTrackingOrigin.Floor)
        {
            if (localFloor && supported.Contains(ReferenceSpaceType.LocalFloor)) appType = ReferenceSpaceType.LocalFloor;
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

    // ─────────────────────── Frame loop ───────────────────────

    /// <summary>Handles runtime events, then waits for and begins the next frame and locates the head, eyes and hands for it.</summary>
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

        Graphics.SubmitRenderThreadCallback(BeginFrameOnRenderThread);
        _frameBegun = true;
        _eyeRendered[0] = _eyeRendered[1] = false;
        _eyeDepthWritten[0] = _eyeDepthWritten[1] = false;
        _stereoCameras.Clear();
        _depthCamera = null;

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
    /// thread, after the frames already queued that still use it.
    /// </summary>
    private void Recenter(long time)
    {
        _recenterRequested = false;
        var location = new SpaceLocation { Type = StructureType.SpaceLocation };
        if (_xr.LocateSpace(_viewSpace, _appSpace, time, &location) != Result.Success) return;
        const SpaceLocationFlags valid = SpaceLocationFlags.PositionValidBit | SpaceLocationFlags.OrientationValidBit;
        if ((location.LocationFlags & valid) != valid) return;

        _appSpaceOffset = RecenteredOffset(_appSpaceOffset, location.Pose, keepFloor: TrackingOrigin == XRTrackingOrigin.Floor);
        Space old = _appSpace;
        _appSpace = CreateReferenceSpace(_appSpaceType, _appSpaceOffset);
        Input.SetAppSpace(_appSpace);
        Graphics.SubmitRenderThreadCallback(() => _xr.DestroySpace(old));
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

    private void BeginFrameOnRenderThread()
    {
        try
        {
            var beginInfo = new FrameBeginInfo { Type = StructureType.FrameBeginInfo };
            Check(_xr.BeginFrame(_session, &beginInfo), "xrBeginFrame");
        }
        catch (Exception e)
        {
            RenderThreadFailure = e.Message;
        }
    }

    /// <summary>Queues the copy of the rendered eyes into the headset and the end of the frame, after everything drawn so far.</summary>
    public void EndFrame()
    {
        if (!_frameBegun) return;
        _frameBegun = false;

        bool left = _eyeRendered[0], right = _eyeRendered[1];
        bool submit = _shouldRender && (left || right);

        // A camera drawing a single eye still has to fill both views, so the other one shows the same image, from
        // the same place. Each view is sent with the pose its image was actually drawn from, so the runtime
        // reprojects it correctly even when the camera did not follow the head this frame.
        int leftSource = left ? 0 : 1;
        int rightSource = right ? 1 : 0;
        var frame = new SubmittedFrame
        {
            LeftSource = leftSource,
            RightSource = rightSource,
            LeftPose = _renderedPoses[leftSource],
            RightPose = _renderedPoses[rightSource],
            LeftFov = _appViews[leftSource].Fov,
            RightFov = _appViews[rightSource].Fov,
            LeftDepth = _eyeDepthWritten[leftSource],
            RightDepth = _eyeDepthWritten[rightSource],
            DisplayTime = _frameState.PredictedDisplayTime,
            NearZ = _nearZ,
            FarZ = _farZ,
            Space = _appSpace,
        };
        Graphics.SubmitRenderThreadCallback(() => EndFrameOnRenderThread(submit, frame));
    }

    private struct SubmittedFrame
    {
        public int LeftSource, RightSource;
        public Posef LeftPose, RightPose;
        public Fovf LeftFov, RightFov;
        public bool LeftDepth, RightDepth;
        public long DisplayTime;
        public float NearZ, FarZ;
        public Space Space;
    }

    private void EndFrameOnRenderThread(bool submit, SubmittedFrame frame)
    {
        var projectionViews = stackalloc CompositionLayerProjectionView[2];
        var depthInfos = stackalloc CompositionLayerDepthInfoKHR[2];
        var layer = new CompositionLayerProjection
        {
            Type = StructureType.CompositionLayerProjection,
            Space = frame.Space,
            ViewCount = 2,
            Views = projectionViews,
        };

        if (submit)
        {
            // Every begun frame has to end, so a failed copy still ends it, just with nothing to show.
            try
            {
                for (int eye = 0; eye < 2; eye++)
                {
                    bool leftEye = eye == 0;
                    // Depth goes with an eye only when the depth camera drew that eye, anything else is stale.
                    bool depth = _submitDepth && (leftEye ? frame.LeftDepth : frame.RightDepth);
                    CopyEyeToSwapchain(eye, leftEye ? frame.LeftSource : frame.RightSource, depth);
                    projectionViews[eye] = new CompositionLayerProjectionView
                    {
                        Type = StructureType.CompositionLayerProjectionView,
                        Pose = leftEye ? frame.LeftPose : frame.RightPose,
                        Fov = leftEye ? frame.LeftFov : frame.RightFov,
                        SubImage = SubImage(_swapchains[eye]),
                    };

                    if (!depth) continue;
                    depthInfos[eye] = new CompositionLayerDepthInfoKHR
                    {
                        Type = StructureType.CompositionLayerDepthInfoKhr,
                        SubImage = SubImage(_depthSwapchains[eye]),
                        MinDepth = MinWindowDepth,
                        MaxDepth = MaxWindowDepth,
                        NearZ = frame.NearZ,
                        FarZ = frame.FarZ,
                    };
                    projectionViews[eye].Next = &depthInfos[eye];
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Copying the eyes to the headset failed: {e.Message}");
                submit = false;
            }
        }

        var layers = stackalloc CompositionLayerBaseHeader*[1];
        layers[0] = (CompositionLayerBaseHeader*)&layer;
        var endInfo = new FrameEndInfo
        {
            Type = StructureType.FrameEndInfo,
            DisplayTime = frame.DisplayTime,
            EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
            LayerCount = submit ? 1u : 0u,
            Layers = submit ? layers : null,
        };

        try { Check(_xr.EndFrame(_session, &endInfo), "xrEndFrame"); }
        catch (Exception e) { RenderThreadFailure = e.Message; }
    }

    private SwapchainSubImage SubImage(Swapchain swapchain) => new()
    {
        Swapchain = swapchain,
        ImageRect = new Rect2Di { Offset = new Offset2Di(0, 0), Extent = new Extent2Di(EyeWidth, EyeHeight) },
    };

    private void CopyEyeToSwapchain(int eye, int sourceEye, bool depth)
    {
        uint source = _eyeTextures[sourceEye].frameBuffer.Handle;
        CopyToSwapchain(_swapchains[eye], _swapchainImages[eye], source, GLEnum.ColorAttachment0, GLEnum.ColorBufferBit);
        if (depth)
            CopyToSwapchain(_depthSwapchains[eye], _depthSwapchainImages[eye], source, GLEnum.DepthAttachment, GLEnum.DepthBufferBit);
    }

    private void CopyToSwapchain(Swapchain swapchain, uint[] images, uint sourceFramebuffer, GLEnum attachment, GLEnum mask)
    {
        var acquireInfo = new SwapchainImageAcquireInfo { Type = StructureType.SwapchainImageAcquireInfo };
        uint index;
        Check(_xr.AcquireSwapchainImage(swapchain, &acquireInfo, &index), "xrAcquireSwapchainImage");

        // A waited image has to go back whatever happens, or every later acquire on this swapchain fails. One whose
        // wait failed cannot be released, since the runtime only takes back images that were waited on.
        bool waited = false;
        try
        {
            var waitInfo = new SwapchainImageWaitInfo { Type = StructureType.SwapchainImageWaitInfo, Timeout = InfiniteDuration };
            Check(_xr.WaitSwapchainImage(swapchain, &waitInfo), "xrWaitSwapchainImage");
            waited = true;

            var gl = Graphics.GL;
            gl.BindFramebuffer(GLEnum.ReadFramebuffer, sourceFramebuffer);
            gl.BindFramebuffer(GLEnum.DrawFramebuffer, _copyFramebuffer);
            gl.FramebufferTexture2D(GLEnum.DrawFramebuffer, attachment, GLEnum.Texture2D, images[index], 0);
            gl.Disable(GLEnum.ScissorTest);
            gl.BlitFramebuffer(0, 0, EyeWidth, EyeHeight, 0, 0, EyeWidth, EyeHeight, (uint)mask, GLEnum.Nearest);
            gl.FramebufferTexture2D(GLEnum.DrawFramebuffer, attachment, GLEnum.Texture2D, 0, 0);
            gl.BindFramebuffer(GLEnum.Framebuffer, 0);
            Graphics.Executor.ResetFramebufferBindings();
        }
        finally
        {
            if (waited)
            {
                var releaseInfo = new SwapchainImageReleaseInfo { Type = StructureType.SwapchainImageReleaseInfo };
                _xr.ReleaseSwapchainImage(swapchain, &releaseInfo);
            }
        }
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

    // ─────────────────────── Queries ───────────────────────

    public XRPose HeadPose => _headPose;

    /// <summary>
    /// The eye's texture to render into this frame, which is also what marks the eye as drawn. The first camera
    /// to ask in a frame owns the depth the runtime reprojects with, so a camera layered on top, drawing only its
    /// own few objects, does not replace the world's depth with its own. <paramref name="writesDepth"/> says
    /// whether this camera is that one.
    /// </summary>
    public RenderTexture GetEyeTexture(StereoEye eye, Camera camera, out bool writesDepth)
    {
        int index = EyeIndex(eye);
        if (!_eyeRendered[index]) _renderedPoses[index] = RenderedPose(camera, index);
        _eyeRendered[index] = true;
        _stereoCameras.Add(camera);

        // The clip planes are in world units, the runtime reads depth in tracking space metres.
        if (_depthCamera.IsNotValid())
        {
            _depthCamera = camera;
            float scale = PlayAreaScale(camera.Transform);
            _nearZ = camera.NearClipPlane / scale;
            _farZ = camera.FarClipPlane / scale;
        }
        writesDepth = _depthCamera == camera;
        if (writesDepth) _eyeDepthWritten[index] = true;
        return _eyeTextures[index];
    }

    /// <summary>The eye texture <paramref name="camera"/> drew this frame, if it has already drawn into the headset.</summary>
    public bool HasRenderedStereo(Camera camera) => _stereoCameras.Contains(camera);

    /// <summary>Whether anything was drawn into either eye this frame.</summary>
    public bool AnyEyeRendered => _eyeRendered[0] || _eyeRendered[1];

    public RenderTexture EyeTexture(StereoEye eye) => _eyeTextures[EyeIndex(eye)];

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

    private static float PlayAreaScale(Transform head)
    {
        if (head.Parent == null) return 1f;
        float scale = MathF.Abs(head.Parent.LossyScale.X);
        return scale > 1e-6f ? scale : 1f;
    }

    public XRView GetEyeView(StereoEye eye) => _eyeViews[EyeIndex(eye)];

    private static int EyeIndex(StereoEye eye) => eye switch
    {
        StereoEye.Left => 0,
        StereoEye.Right => 1,
        _ => throw new ArgumentException("Only the left and right eyes have XR views.", nameof(eye)),
    };

    // ─────────────────────── Teardown ───────────────────────

    public void Dispose()
    {
        _pacing = false;

        if (_session.Handle != 0)
        {
            // Swapchains and the session go on the render thread, after anything already queued that uses them.
            try { Graphics.SubmitRenderThreadCallbackAndWait(DestroySessionOnRenderThread); }
            catch (Exception e) { Debug.LogError($"Destroying the XR session failed: {e.Message}"); }
        }

        for (int i = 0; i < 2; i++)
        {
            if (_eyeTextures[i].IsValid()) _eyeTextures[i].Dispose();
            _eyeTextures[i] = null;
        }

        // Action sets and spaces are children of the instance and session, so they go with them.
        if (_instance.Handle != 0) _xr.DestroyInstance(_instance);
        _instance = default;
        _xr.Dispose();
    }

    private void DestroySessionOnRenderThread()
    {
        for (int i = 0; i < 2; i++)
        {
            if (_swapchains[i].Handle != 0) _xr.DestroySwapchain(_swapchains[i]);
            if (_depthSwapchains[i].Handle != 0) _xr.DestroySwapchain(_depthSwapchains[i]);
        }
        _xr.DestroySession(_session);
        _session = default;

        if (_copyFramebuffer != 0) Graphics.GL.DeleteFramebuffer(_copyFramebuffer);
        _copyFramebuffer = 0;
    }

    // ─────────────────────── Helpers ───────────────────────

    // OpenXR is right handed with -Z forward, Prowl is left handed with +Z forward, so poses mirror across Z.
    internal static Float3 ToProwl(Vector3f v) => new(v.X, v.Y, -v.Z);
    internal static Quaternion ToProwl(Quaternionf q) => new(-q.X, -q.Y, q.Z, q.W);
    // A turn is an axis, which mirrors the other way to a position, as the axis part of a rotation does.
    internal static Float3 ToProwlAngular(Vector3f v) => new(-v.X, -v.Y, v.Z);
    internal static Vector3f ToXr(Float3 v) => new(v.X, v.Y, -v.Z);
    internal static Quaternionf ToXr(Quaternion q) => new(-q.X, -q.Y, q.Z, q.W);

    internal static XRPose ToProwl(in SpaceLocation location)
    {
        var flags = location.LocationFlags;
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

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetCurrentContext")]
    private static partial nint WglGetCurrentContext();

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetCurrentDC")]
    private static partial nint WglGetCurrentDC();
}
