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
/// While running, every perspective <see cref="Camera"/> with no target texture and a <see cref="Camera.StereoTargetEye"/>
/// renders once per eye into the headset, using its transform as the head. Put a <see cref="TrackedPoseDriver"/> on the
/// camera so the head moves it, under a parent that places the play area in the world. The headset paces the frame
/// loop while it shows frames, so VSync and <see cref="Application.TargetFrameRate"/> are set aside meanwhile.
/// </para>
/// </summary>
public static class XR
{
    internal static OpenXRSession? Session { get; private set; }

    /// <summary>
    /// Scale applied to the headset's recommended eye resolution when <see cref="Start"/> is called. Below 1 trades
    /// sharpness for speed.
    /// </summary>
    public static float RenderScale { get; set; } = 1f;

    /// <summary>A session is running, whether or not the headset is showing it yet.</summary>
    public static bool IsRunning => Session != null;

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

    public static event Action? Started;
    public static event Action? Stopped;

    /// <summary>The headset paces frames, so the window waits on neither vsync nor the frame limiter.</summary>
    internal static bool IsPacingFrames => Session?.IsPacingFrames ?? false;

    /// <summary>
    /// Connects to the headset through the active OpenXR runtime. Returns false, and logs why, when there is no
    /// runtime, no headset, or the runtime cannot render with OpenGL.
    /// </summary>
    public static bool Start(XRTrackingOrigin origin = XRTrackingOrigin.Floor)
    {
        if (Session != null) return true;
        if (Graphics.IsHeadless)
        {
            Debug.LogWarning("XR needs a window and graphics device, so it cannot start headless.");
            return false;
        }

        try
        {
            Session = OpenXRSession.Create(origin, RenderScale);
        }
        catch (Exception e)
        {
            Debug.LogError($"XR could not start: {e.Message}");
            return false;
        }

        Started?.Invoke();
        return true;
    }

    public static void Stop()
    {
        if (Session == null) return;

        OpenXRSession session = Session;
        Session = null;
        session.Dispose();
        Debug.Log("XR stopped.");
        Stopped?.Invoke();
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
            _ => default,
        };
    }

    /// <summary>Waits for the headset's next frame and reads its poses and input. Runs before the frame updates.</summary>
    internal static void BeginFrame()
    {
        if (Session == null) return;

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

        if (Session.IsLost) Stop();
    }

    /// <summary>Hands the frame's eyes to the headset. Runs after everything for the frame has been drawn.</summary>
    internal static void EndFrame() => Session?.EndFrame();

    /// <summary>Whether this render of <paramref name="camera"/> goes to the headset, one render per eye.</summary>
    public static bool ShouldRenderStereo(Camera camera, in RenderingData data)
        => Session != null
        && Session.ShouldRender
        && camera.StereoTargetEye != StereoTargetEyeMask.None
        && camera.Target.IsNotValid()
        && !camera.IsOrthographic
        && !data.IsSceneView;

    /// <summary>
    /// The texture an eye renders into this frame. Rendering into it is what submits that eye. The first camera
    /// to render in a frame provides the depth the headset reprojects late frames with, along with its clip
    /// planes, and <paramref name="writesDepth"/> is true only for that camera.
    /// </summary>
    public static RenderTexture GetEyeTexture(StereoEye eye, Camera camera, out bool writesDepth) => Session!.GetEyeTexture(eye, camera, out writesDepth);

    /// <summary>The eye's pose relative to the head and its frustum, for this frame.</summary>
    public static XRView GetEyeView(StereoEye eye) => Session!.GetEyeView(eye);
}

/// <summary>Moves the transform to follow a tracked device, relative to its parent, which stands for the play area.</summary>
[AddComponentMenu("XR/Tracked Pose Driver")]
public class TrackedPoseDriver : MonoBehaviour
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
