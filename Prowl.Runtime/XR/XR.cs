// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>Where tracked poses are measured from.</summary>
public enum XRTrackingOrigin
{
    /// <summary>The floor, so a pose's height is the device's height above it.</summary>
    Floor,
    /// <summary>Where the head was when tracking started.</summary>
    Seated,
}

public enum XRNode
{
    Head,
    /// <summary>Where the left hand holds the controller.</summary>
    LeftHand,
    /// <summary>Where the right hand holds the controller.</summary>
    RightHand,
    /// <summary>The left controller's pointing ray.</summary>
    LeftHandAim,
    /// <summary>The right controller's pointing ray.</summary>
    RightHandAim,
    /// <summary>Where the eyes look, along +Z, on headsets with eye tracking. See <see cref="XR.IsEyeTrackingSupported"/>.</summary>
    EyeGaze,
}

/// <summary>A tracked pose in tracking space, measured from the <see cref="XRTrackingOrigin"/>.</summary>
public struct XRPose
{
    public Float3 Position;
    public Quaternion Rotation;

    /// <summary>The position is known, though it may be estimated rather than tracked.</summary>
    public bool HasPosition;

    /// <summary>The rotation is known, though it may be estimated rather than tracked.</summary>
    public bool HasRotation;

    /// <summary>Both position and rotation are known.</summary>
    public readonly bool IsValid => HasPosition && HasRotation;

    /// <summary>The device is actively tracked.</summary>
    public bool IsTracked;

    /// <summary>How fast the device moves, in tracking space metres per second, as the runtime measures it.</summary>
    public Float3 LinearVelocity;

    /// <summary>How fast the device turns, in radians per second about each tracking space axis.</summary>
    public Float3 AngularVelocity;

    public bool HasLinearVelocity;
    public bool HasAngularVelocity;
}

/// <summary>An eye's pose relative to the head, and the tangents of its frustum's half angles (left and down negative).</summary>
public struct XRView
{
    public Float3 Position;
    public Quaternion Rotation;
    public float TanLeft;
    public float TanRight;
    public float TanUp;
    public float TanDown;
}

/// <summary>
/// Headset rendering and tracking through OpenXR.
/// <para>
/// While running, the first perspective <see cref="Camera"/> with no target texture and a <see cref="Camera.StereoTargetEye"/>
/// draws both eyes into the headset in one stereo view, using its transform as the head, and the window or Game View
/// shows one eye. Put a <see cref="TrackedPoseDriver"/> on the camera so the head moves it, under a parent that places
/// the play area in the world. The headset paces the frame loop while it shows frames, so VSync and
/// <see cref="Application.TargetFrameRate"/> are set aside meanwhile.
/// </para>
/// <para>
/// The graphics device has to be made for the headset, so set <see cref="InitializeOnStartup"/> before the window opens.
/// </para>
/// </summary>
public static class XR
{
    internal static OpenXRSession? Session { get; private set; }

    // A session lost to the runtime, a dropped link or an unplugged cable is reconnected once the headset is back.
    private static bool s_wanted;
    private static XRTrackingOrigin s_origin;
    private static long s_reconnectAt;
    private const long ReconnectMilliseconds = 2000;

    // The runtime connection made with the device, used by the first Start, and what the device was made with.
    private static OpenXRSession? s_prepared;
    private static bool s_deviceReady;
    private static string[] s_instanceExtensions = [];
    private static string[] s_deviceExtensions = [];
    private static bool s_eyesOnly;
    private static bool s_hooked;
    private static string? s_unavailable;

    /// <summary>
    /// Makes the graphics device for the headset when the window opens, which XR needs before <see cref="Start"/> can
    /// work: it connects to the OpenXR runtime, which may start it, and uses the GPU and Vulkan extensions it asks for.
    /// Set it before the game runs. Off, XR costs nothing.
    /// </summary>
    public static bool InitializeOnStartup { get; set; }

    /// <summary>Whether the graphics device was made for the headset, so <see cref="Start"/> can connect.</summary>
    public static bool IsDeviceReady => s_deviceReady;

    /// <summary>
    /// Scale applied to the headset's recommended eye resolution when <see cref="Start"/> is called. Below 1 trades
    /// sharpness for speed.
    /// </summary>
    public static float RenderScale { get; set; } = 1f;

    /// <summary>A session is running, whether or not the headset is showing it yet.</summary>
    public static bool IsRunning => Session != null;

    /// <summary>The headset was lost and XR is waiting for it to come back, trying again every couple of seconds.</summary>
    public static bool IsReconnecting => s_wanted && Session == null;

    /// <summary>The headset is showing this application's frames.</summary>
    public static bool IsVisible => Session?.IsVisible ?? false;

    /// <summary>The headset is showing this application and it receives controller input.</summary>
    public static bool IsFocused => Session?.IsFocused ?? false;

    public static string RuntimeName => Session?.RuntimeName ?? "";
    public static string HeadsetName => Session?.SystemName ?? "";

    /// <summary>The origin poses are measured from, which can fall back to <see cref="XRTrackingOrigin.Seated"/> when the headset has no floor level space.</summary>
    public static XRTrackingOrigin TrackingOrigin => Session?.TrackingOrigin ?? XRTrackingOrigin.Floor;

    /// <summary>The resolution each eye renders at.</summary>
    public static Int2 EyeTextureSize => Session != null ? new Int2(Session.EyeWidth, Session.EyeHeight) : Int2.Zero;

    /// <summary>How many frames a second the headset displays, 0 when not running or not known yet.</summary>
    public static float DisplayRefreshRate => Session is { DisplayPeriod: > 0f } session ? 1f / session.DisplayPeriod : 0f;

    /// <summary>Whether the headset can track bare hands. See <see cref="XRInput.IsHandTracked"/>.</summary>
    public static bool IsHandTrackingSupported => Session?.Input.HasHandTracking ?? false;

    /// <summary>Whether the headset can track where the eyes look, through <see cref="XRNode.EyeGaze"/>.</summary>
    public static bool IsEyeTrackingSupported => Session?.Input.HasEyeGaze ?? false;

    public static event Action? Started;
    public static event Action? Stopped;

    /// <summary>
    /// Raised when tracking space moved under the player: after <see cref="Recenter"/>, or when the headset's runtime
    /// recentres or redraws the play area itself. Poses jump on the frame this is raised.
    /// </summary>
    public static event Action? Recentered;

    /// <summary>
    /// Makes where the head is now the centre of tracking space, facing forward along it. With floor tracking the floor
    /// stays where it is, seated tracking also takes the head's height. Takes effect on the next frame.
    /// </summary>
    public static void Recenter() => Session?.RequestRecenter();

    /// <summary>
    /// The width and depth of the play area the player set up, in metres, centred on the tracking space's floor
    /// origin before any <see cref="Recenter"/>. False when the headset has no play area or does not report it.
    /// </summary>
    public static bool TryGetPlayAreaSize(out Float2 size)
    {
        size = Float2.Zero;
        return Session != null && Session.TryGetPlayAreaSize(out size);
    }

    internal static void RaiseRecentered() => Recentered?.Invoke();

    /// <summary>The headset paces frames, so the window waits on neither vsync nor the frame limiter.</summary>
    internal static bool IsPacingFrames => Session?.IsPacingFrames ?? false;

    /// <summary>
    /// Connects to the headset through the active OpenXR runtime. Returns false, and logs why, when there is no
    /// runtime, no headset, the runtime cannot render with Vulkan, or the device was not made for XR (see
    /// <see cref="InitializeOnStartup"/>).
    /// </summary>
    public static bool Start(XRTrackingOrigin origin = XRTrackingOrigin.Floor)
    {
        if (Session != null) return true;
        if (Graphics.IsHeadless)
        {
            Debug.LogWarning("XR needs a window and graphics device, so it cannot start headless.");
            return false;
        }
        if (!s_deviceReady)
        {
            Debug.LogError(s_unavailable != null
                ? $"XR could not start: the graphics device could not be made for a headset when the application started: {s_unavailable}"
                : "XR could not start: the graphics device was not made for a headset. Set XR.InitializeOnStartup before the game runs, or start the editor with --xr, with the headset connected.");
            return false;
        }

        if (!Connect(origin, quiet: false)) return false;
        s_wanted = true;
        s_origin = origin;
        return true;
    }

    /// <summary>Ends the session, and stops waiting for a lost headset to come back.</summary>
    public static void Stop()
    {
        s_wanted = false;
        Disconnect();
        s_prepared?.Dispose();
        s_prepared = null;
    }

    private static bool Connect(XRTrackingOrigin origin, bool quiet)
    {
        OpenXRSession? session = null;
        try
        {
            session = s_prepared ?? OpenXRSession.Prepare();
            s_prepared = null;
            session.Start(origin, RenderScale, s_instanceExtensions, s_deviceExtensions);
        }
        catch (Exception e)
        {
            session?.Dispose();
            if (!quiet) Debug.LogError($"XR could not start: {e.Message}");
            return false;
        }

        Session = session;
        if (!s_hooked)
        {
            // Ends the frame even when the frame loop threw before it did, since a waited frame left open stalls the runtime
            Graphics.BeforeSubmit += EndFrame;
            s_hooked = true;
        }
        Started?.Invoke();
        return true;
    }

    private static void Disconnect()
    {
        if (Session == null) return;

        OpenXRSession session = Session;
        Session = null;
        session.Dispose();
        Graphics.VSync = Application.VSync;
        Debug.Log("XR stopped.");
        Stopped?.Invoke();
    }

    /// <summary>Drops every handler on the XR and XR input events, for when the code that added them is gone, like at the end of play mode.</summary>
    internal static void ForgetHandlers()
    {
        Started = null;
        Stopped = null;
        Recentered = null;
        XRInput.ForgetHandlers();
    }

    /// <summary>The pose of a tracked device for the frame being displayed, in tracking space.</summary>
    public static XRPose GetPose(XRNode node)
    {
        if (Session == null) return default;
        return node switch
        {
            XRNode.Head => Session.HeadPose,
            XRNode.LeftHand => Session.Input.GetGripPose(XRHand.Left),
            XRNode.RightHand => Session.Input.GetGripPose(XRHand.Right),
            XRNode.LeftHandAim => Session.Input.GetAimPose(XRHand.Left),
            XRNode.RightHandAim => Session.Input.GetAimPose(XRHand.Right),
            XRNode.EyeGaze => Session.Input.EyeGazePose,
            _ => default,
        };
    }

    /// <summary>Waits for the headset's next frame and reads its poses and input. Runs before the frame updates.</summary>
    internal static void BeginFrame()
    {
        if (Session == null)
        {
            if (!IsReconnecting || Environment.TickCount64 < s_reconnectAt) return;
            s_reconnectAt = Environment.TickCount64 + ReconnectMilliseconds;
            if (!Connect(s_origin, quiet: true)) return;
            Debug.Log("XR reconnected to the headset.");
        }

        try
        {
            Session.BeginFrame();
        }
        catch (Exception e)
        {
            Debug.LogError($"XR frame failed, stopping XR: {e.Message}");
            Stop();
            return;
        }

        if (Session.RenderThreadFailure is { } failure)
        {
            Debug.LogError($"XR frame failed on the render thread, stopping XR: {failure}");
            Stop();
            return;
        }

        if (Session.IsLost)
        {
            Debug.LogWarning("Lost the headset. XR will reconnect when it is back.");
            Disconnect();
            s_reconnectAt = Environment.TickCount64 + ReconnectMilliseconds;
            return;
        }

        // The window presenting with vsync would hold back the frames the headset paces
        bool vsync = Application.VSync && !Session.IsPacingFrames;
        if (Graphics.VSync != vsync) Graphics.VSync = vsync;
    }

    /// <summary>Hands the frame's eyes to the headset. Runs after everything for the frame has been drawn.</summary>
    internal static void EndFrame() => Session?.EndFrame();

    /// <summary>
    /// Draws the headset's eyes from the scene's cameras when nothing else drew them this frame, as when the editor's
    /// Game View is hidden, so the headset never goes dark while the game runs.
    /// </summary>
    internal static void RenderMissedEyes(Scene? scene)
    {
        if (Session is not { ShouldRender: true } session || session.AnyEyeRendered || scene.IsNotValid()) return;

        s_eyesOnly = true;
        try
        {
            foreach (Camera camera in scene.GatherActiveCameras())
            {
                if (!ShouldRenderStereo(camera)) continue;
                try { camera.Render(); }
                catch (Exception e) { Debug.LogError($"Drawing camera '{camera.GameObject.Name}' into the headset failed: {e.Message}"); }
                if (session.AnyEyeRendered) break;
            }
        }
        finally
        {
            s_eyesOnly = false;
        }
    }

    /// <summary>Whether <paramref name="camera"/> already drew its eyes into the headset this frame.</summary>
    public static bool HasRenderedStereo(Camera camera) => Session?.HasRenderedStereo(camera) ?? false;

    /// <summary>
    /// Whether <paramref name="camera"/> renders to the headset while XR runs: the frame wants drawing, the camera is a
    /// perspective one drawing to the screen with eyes to draw, and no other camera drew the eyes this frame.
    /// </summary>
    public static bool ShouldRenderStereo(Camera camera)
        => Session != null
        && Session.ShouldRender
        && camera.StereoTargetEye != StereoTargetEyeMask.None
        && camera.Target.IsNotValid()
        && !camera.IsOrthographic
        && Session.CanDrawEyes(camera);

    /// <summary>The eye's pose relative to the head and its frustum, for this frame.</summary>
    public static XRView GetEyeView(StereoEye eye) => Session?.GetEyeView(eye) ?? default;

    /// <summary>
    /// The two layer target the headset's eyes draw into this frame, the left display's image in layer 0. Drawing
    /// <paramref name="camera"/> into it, through <see cref="Camera.BuildView"/>, is what sends the frame to the
    /// headset with the camera's pose. One camera draws the eyes per frame.
    /// </summary>
    public static Graphite.Framebuffer GetEyeTarget(Camera camera)
    {
        OpenXRSession session = Session ?? throw new InvalidOperationException("XR is not running.");
        session.ClaimEyes(camera, out Graphite.Framebuffer target);
        return target;
    }

    /// <summary>The texture behind <see cref="GetEyeTarget"/>, two layers, the left display's image first.</summary>
    public static Graphite.Texture GetEyeTexture(Camera camera) => GetEyeTarget(camera).ColorTargets[0].Target;

    /// <summary>The eye a layer of <paramref name="camera"/>'s stereo view draws, which is the same eye in both for a camera drawing one.</summary>
    public static StereoEye EyeDrawnInLayer(Camera camera, int layer) => OpenXRSession.EyeDrawnInLayer(camera, layer);

    /// <summary>Claims the eyes for <paramref name="camera"/>, true the first time this frame, when its stereo view has to be added.</summary>
    internal static bool ClaimEyes(Camera camera, out Graphite.Framebuffer target) => Session!.ClaimEyes(camera, out target);

    /// <summary>Shows the eye <paramref name="camera"/> drew in <paramref name="target"/>, where it would have drawn without XR.</summary>
    internal static void MirrorEyes(Camera camera, Graphite.Framebuffer target)
    {
        if (s_eyesOnly || Session is not { } session || !session.HasRenderedStereo(camera)) return;
        session.Mirror(target);
    }

    /// <summary>A mono view drawing over <paramref name="target"/> after a mirror, which leaves the mirror out when it covers it all.</summary>
    internal static void CoverMirror(Graphite.Framebuffer target, Float4 viewport)
    {
        if (Session == null || viewport.X > 0f || viewport.Y > 0f || viewport.Z < 1f || viewport.W < 1f) return;
        Session.CoverMirror(target);
    }

    /// <summary>
    /// The Vulkan instance and device extensions and the GPU the device needs for the headset when
    /// <see cref="InitializeOnStartup"/> is set, none otherwise. Connects to the runtime to ask. Called as the device is made.
    /// </summary>
    internal static Graphite.VulkanDeviceOptions VulkanOptions()
    {
        if (!InitializeOnStartup) return new([], []);

        OpenXRSession prepared;
        try
        {
            prepared = OpenXRSession.Prepare();
        }
        catch (Exception e)
        {
            s_unavailable = e.Message;
            Debug.LogWarning($"XR is unavailable until the application restarts: {e.Message}");
            return new([], []);
        }

        s_prepared = prepared;
        s_instanceExtensions = prepared.VulkanInstanceExtensions;
        s_deviceExtensions = prepared.VulkanDeviceExtensions;
        s_deviceReady = true;
        return new(prepared.VulkanInstanceExtensions, prepared.VulkanDeviceExtensions) { PickPhysicalDevice = prepared.PickPhysicalDevice };
    }
}

/// <summary>A headset eye.</summary>
public enum StereoEye
{
    Mono,
    Left,
    Right,
}

/// <summary>Moves the transform to follow a tracked device, relative to its parent, which stands for the play area.</summary>
[AddComponentMenu("XR/Tracked Pose Driver")]
public class TrackedPoseDriver : Component
{
    public XRNode Node = XRNode.Head;
    public bool TrackPosition = true;
    public bool TrackRotation = true;

    public override void Update()
    {
        XRPose pose = XR.GetPose(Node);
        if (TrackPosition && pose.HasPosition) Transform.LocalPosition = pose.Position;
        if (TrackRotation && pose.HasRotation) Transform.LocalRotation = pose.Rotation;
    }
}
