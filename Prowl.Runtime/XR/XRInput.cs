// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using Silk.NET.Core.Native;
using Silk.NET.OpenXR;
using Silk.NET.OpenXR.Extensions.EXT;

using XrAction = Silk.NET.OpenXR.Action;
using XrApi = Silk.NET.OpenXR.XR;

namespace Prowl.Runtime;

public enum XRHand
{
    Left,
    Right,
}

public enum XRButton
{
    Trigger,
    Grip,
    /// <summary>A on the right hand, X on the left.</summary>
    Primary,
    /// <summary>B on the right hand, Y on the left.</summary>
    Secondary,
    Menu,
    /// <summary>Clicking the thumbstick, or the touchpad on controllers that have one instead.</summary>
    Thumbstick,
}

public enum XRAxis
{
    Trigger,
    Grip,
}

/// <summary>A sensor that feels a finger resting on a control without pressing it. Controllers without one read as untouched.</summary>
public enum XRTouch
{
    Trigger,
    /// <summary>The thumbstick, or the touchpad on controllers that have one instead.</summary>
    Thumbstick,
    /// <summary>A on the right hand, X on the left.</summary>
    Primary,
    /// <summary>B on the right hand, Y on the left.</summary>
    Secondary,
    /// <summary>The rest beside the thumbstick that some controllers have.</summary>
    Thumbrest,
}

/// <summary>The joints of a tracked hand, in the order OpenXR reports them.</summary>
public enum XRHandJoint
{
    Palm,
    Wrist,
    ThumbMetacarpal,
    ThumbProximal,
    ThumbDistal,
    ThumbTip,
    IndexMetacarpal,
    IndexProximal,
    IndexIntermediate,
    IndexDistal,
    IndexTip,
    MiddleMetacarpal,
    MiddleProximal,
    MiddleIntermediate,
    MiddleDistal,
    MiddleTip,
    RingMetacarpal,
    RingProximal,
    RingIntermediate,
    RingDistal,
    RingTip,
    LittleMetacarpal,
    LittleProximal,
    LittleIntermediate,
    LittleDistal,
    LittleTip,
}

/// <summary>
/// A joint of a tracked hand in tracking space. The joint's +Z runs from the wrist toward the fingertips and +Y out
/// of the back of the hand, and <see cref="Radius"/> is how thick the hand is there.
/// </summary>
public struct XRHandJointPose
{
    public XRPose Pose;
    public float Radius;
}

/// <summary>
/// Headset controller and hand input while <see cref="XR"/> is running. Everything reads as released, zero and untracked
/// when it is not, or when the application does not have input focus in the headset.
/// </summary>
public static class XRInput
{
    private static OpenXRInput? Source => XR.Session?.Input;

    /// <summary>Whether the controller in <paramref name="hand"/> is being tracked.</summary>
    public static bool IsTracked(XRHand hand) => Source?.GetGripPose(hand).IsTracked ?? false;

    /// <summary>Whether a controller is in use in <paramref name="hand"/>, as far as the runtime knows.</summary>
    public static bool IsControllerConnected(XRHand hand) => GetControllerProfile(hand).Length > 0;

    /// <summary>
    /// The OpenXR interaction profile the runtime uses for the controller in <paramref name="hand"/>, such as
    /// <c>/interaction_profiles/oculus/touch_controller</c>, or empty when there is none.
    /// </summary>
    public static string GetControllerProfile(XRHand hand) => Source?.GetProfile(hand) ?? "";

    /// <summary>Raised when a controller is picked up, put down or swapped for another kind.</summary>
    public static event System.Action? ControllersChanged;

    /// <summary>How far the trigger is pulled, from 0 to 1.</summary>
    public static float GetTrigger(XRHand hand) => Source?.GetTrigger(hand) ?? 0f;

    /// <summary>How hard the grip is squeezed, from 0 to 1. Controllers with a grip button report 0 or 1.</summary>
    public static float GetGrip(XRHand hand) => Source?.GetGrip(hand) ?? 0f;

    public static float GetAxis(XRHand hand, XRAxis axis) => axis == XRAxis.Grip ? GetGrip(hand) : GetTrigger(hand);

    /// <summary>The thumbstick, or the touchpad on controllers that have one instead, each axis from -1 to 1.</summary>
    public static Float2 GetThumbstick(XRHand hand) => Source?.GetThumbstick(hand) ?? Float2.Zero;

    public static bool GetButton(XRHand hand, XRButton button) => Source?.GetBool(hand, (int)button) ?? false;

    /// <summary>True on the frame the button went down.</summary>
    public static bool GetButtonDown(XRHand hand, XRButton button) => Source?.GetBoolDown(hand, (int)button) ?? false;

    /// <summary>True on the frame the button came up.</summary>
    public static bool GetButtonUp(XRHand hand, XRButton button) => Source?.GetBoolUp(hand, (int)button) ?? false;

    /// <summary>Whether a finger rests on the control, pressed or not.</summary>
    public static bool GetTouch(XRHand hand, XRTouch touch) => Source?.GetBool(hand, OpenXRInput.TouchIndex(touch)) ?? false;

    /// <summary>True on the frame a finger came to rest on the control.</summary>
    public static bool GetTouchDown(XRHand hand, XRTouch touch) => Source?.GetBoolDown(hand, OpenXRInput.TouchIndex(touch)) ?? false;

    /// <summary>True on the frame a finger left the control.</summary>
    public static bool GetTouchUp(XRHand hand, XRTouch touch) => Source?.GetBoolUp(hand, OpenXRInput.TouchIndex(touch)) ?? false;

    /// <summary>Whether the bare hand is being tracked, which on most headsets means its controller was put down.</summary>
    public static bool IsHandTracked(XRHand hand) => Source?.IsHandTracked(hand) ?? false;

    /// <summary>A joint of the tracked hand, untracked when the hand is not.</summary>
    public static XRHandJointPose GetHandJoint(XRHand hand, XRHandJoint joint) => Source?.GetHandJoint(hand, joint) ?? default;

    /// <summary>How closely the thumb and index fingertips pinch together, from 0 apart to 1 touching, 0 when the hand is not tracked.</summary>
    public static float GetPinch(XRHand hand) => Source?.GetPinch(hand) ?? 0f;

    /// <summary>
    /// Vibrates the controller in <paramref name="hand"/>, replacing any vibration still playing on it.
    /// </summary>
    /// <param name="hand">The controller to vibrate.</param>
    /// <param name="amplitude">Strength from 0 to 1.</param>
    /// <param name="seconds">How long it lasts. Zero or less plays the shortest pulse the controller can.</param>
    /// <param name="frequency">Frequency in Hz. Zero lets the runtime pick.</param>
    public static void Vibrate(XRHand hand, float amplitude, float seconds, float frequency = 0f) => Source?.Vibrate(hand, amplitude, seconds, frequency);

    public static void StopVibration(XRHand hand) => Source?.StopVibration(hand);

    internal static void RaiseControllersChanged() => ControllersChanged?.Invoke();

    internal static void ForgetHandlers() => ControllersChanged = null;
}

/// <summary>The OpenXR actions and trackers behind <see cref="XRInput"/> and the hand and eye poses of <see cref="XR.GetPose"/>.</summary>
internal sealed unsafe class OpenXRInput
{
    // Runtimes emulate one of these for controllers they have no binding for, so newer controllers still work.
    private static readonly string[] s_profiles =
    [
        "/interaction_profiles/khr/simple_controller",
        "/interaction_profiles/oculus/touch_controller",
        "/interaction_profiles/valve/index_controller",
        "/interaction_profiles/htc/vive_controller",
        "/interaction_profiles/microsoft/motion_controller",
    ];

    private const string EyeGazeProfile = "/interaction_profiles/ext/eye_gaze_interaction";
    private const string EyeGazePath = "/user/eyes_ext/input/gaze_ext/pose";

    private static readonly int s_buttonCount = Enum.GetValues<XRButton>().Length;
    private static readonly int s_boolCount = s_buttonCount + Enum.GetValues<XRTouch>().Length;
    private static readonly int s_jointCount = Enum.GetValues<XRHandJoint>().Length;

    /// <summary>Buttons come first among the yes or no controls, and touches after them.</summary>
    public static int TouchIndex(XRTouch touch) => s_buttonCount + (int)touch;

    private readonly XrApi _xr;
    private readonly Instance _instance;
    private readonly Session _session;
    private Space _appSpace;
    private readonly ActionSet _actionSet;
    private readonly ulong[] _handPaths = new ulong[2];

    private readonly XrAction _gripPose;
    private readonly XrAction _aimPose;
    private readonly XrAction _trigger;
    private readonly XrAction _grip;
    private readonly XrAction _thumbstick;
    private readonly XrAction _haptic;
    private readonly XrAction[] _bools;
    private readonly Space[] _gripSpaces = new Space[2];
    private readonly Space[] _aimSpaces = new Space[2];

    private readonly float[] _triggerValues = new float[2];
    private readonly float[] _gripValues = new float[2];
    private readonly Float2[] _thumbstickValues = new Float2[2];
    private readonly bool[,] _boolStates;
    private readonly bool[,] _previousBoolStates;
    private readonly bool[,] _boolFlicked;
    private readonly XRPose[] _gripPoses = new XRPose[2];
    private readonly XRPose[] _aimPoses = new XRPose[2];
    private readonly string[] _profiles = ["", ""];
    private bool _focused;

    private readonly XrAction _eyeGaze;
    private readonly Space _eyeGazeSpace;
    private XRPose _eyeGazePose;

    private readonly ExtHandTracking? _handTracking;
    private readonly HandTrackerEXT[] _handTrackers = new HandTrackerEXT[2];
    private readonly XRHandJointPose[,] _joints;
    private readonly bool[] _handTracked = new bool[2];

    public bool HasHandTracking => _handTracking != null;
    public bool HasEyeGaze => _eyeGaze.Handle != 0;

    private readonly record struct Binding(XrAction Action, string[] Left, string[] Right);

    public OpenXRInput(XrApi xr, Instance instance, Session session, Space appSpace, bool handTracking, bool eyeGaze)
    {
        _xr = xr;
        _instance = instance;
        _session = session;
        _appSpace = appSpace;
        _handPaths[0] = StringToPath("/user/hand/left");
        _handPaths[1] = StringToPath("/user/hand/right");

        var setInfo = new ActionSetCreateInfo { Type = StructureType.ActionSetCreateInfo };
        OpenXRSession.WriteString(setInfo.ActionSetName, 64, "gameplay");
        OpenXRSession.WriteString(setInfo.LocalizedActionSetName, 128, "Gameplay");
        ActionSet actionSet;
        OpenXRSession.Check(_xr.CreateActionSet(_instance, &setInfo, &actionSet), "xrCreateActionSet");
        _actionSet = actionSet;

        _gripPose = CreateAction("grip_pose", "Grip Pose", ActionType.PoseInput);
        _aimPose = CreateAction("aim_pose", "Aim Pose", ActionType.PoseInput);
        _trigger = CreateAction("trigger", "Trigger", ActionType.FloatInput);
        _grip = CreateAction("grip", "Grip", ActionType.FloatInput);
        _thumbstick = CreateAction("thumbstick", "Thumbstick", ActionType.Vector2fInput);
        _haptic = CreateAction("haptic", "Haptic", ActionType.VibrationOutput);

        _bools = new XrAction[s_boolCount];
        _boolStates = new bool[2, s_boolCount];
        _previousBoolStates = new bool[2, s_boolCount];
        _boolFlicked = new bool[2, s_boolCount];
        foreach (XRButton button in Enum.GetValues<XRButton>())
            _bools[(int)button] = CreateAction(button.ToString().ToLowerInvariant() + "_button", button + " Button", ActionType.BooleanInput);
        foreach (XRTouch touch in Enum.GetValues<XRTouch>())
            _bools[TouchIndex(touch)] = CreateAction(touch.ToString().ToLowerInvariant() + "_touch", touch + " Touch", ActionType.BooleanInput);

        SuggestBindings(
        [
            new(_gripPose, ["/input/grip/pose"], ["/input/grip/pose"]),
            new(_aimPose, ["/input/aim/pose"], ["/input/aim/pose"]),
            new(_trigger, ["/input/trigger/value", "/input/select/click"], ["/input/trigger/value", "/input/select/click"]),
            new(_grip, ["/input/squeeze/value", "/input/squeeze/click"], ["/input/squeeze/value", "/input/squeeze/click"]),
            new(_thumbstick, ["/input/thumbstick", "/input/trackpad"], ["/input/thumbstick", "/input/trackpad"]),
            new(_haptic, ["/output/haptic"], ["/output/haptic"]),
            new(_bools[(int)XRButton.Trigger], ["/input/trigger/value", "/input/select/click"], ["/input/trigger/value", "/input/select/click"]),
            new(_bools[(int)XRButton.Grip], ["/input/squeeze/value", "/input/squeeze/click"], ["/input/squeeze/value", "/input/squeeze/click"]),
            new(_bools[(int)XRButton.Primary], ["/input/x/click", "/input/a/click"], ["/input/a/click"]),
            new(_bools[(int)XRButton.Secondary], ["/input/y/click", "/input/b/click"], ["/input/b/click"]),
            new(_bools[(int)XRButton.Menu], ["/input/menu/click"], ["/input/menu/click"]),
            new(_bools[(int)XRButton.Thumbstick], ["/input/thumbstick/click", "/input/trackpad/click"], ["/input/thumbstick/click", "/input/trackpad/click"]),
            new(_bools[TouchIndex(XRTouch.Trigger)], ["/input/trigger/touch"], ["/input/trigger/touch"]),
            new(_bools[TouchIndex(XRTouch.Thumbstick)], ["/input/thumbstick/touch", "/input/trackpad/touch"], ["/input/thumbstick/touch", "/input/trackpad/touch"]),
            new(_bools[TouchIndex(XRTouch.Primary)], ["/input/x/touch", "/input/a/touch"], ["/input/a/touch"]),
            new(_bools[TouchIndex(XRTouch.Secondary)], ["/input/y/touch", "/input/b/touch"], ["/input/b/touch"]),
            new(_bools[TouchIndex(XRTouch.Thumbrest)], ["/input/thumbrest/touch"], ["/input/thumbrest/touch"]),
        ]);

        // The eyes are a device of their own, so their action has no hands to tell apart.
        if (eyeGaze)
        {
            _eyeGaze = CreateAction("eye_gaze", "Eye Gaze", ActionType.PoseInput, handed: false);
            var suggestion = new ActionSuggestedBinding { Action = _eyeGaze, Binding = StringToPath(EyeGazePath) };
            if (Suggest(StringToPath(EyeGazeProfile), &suggestion, 1) != Result.Success)
                _eyeGaze = default;
        }

        var attachInfo = new SessionActionSetsAttachInfo
        {
            Type = StructureType.SessionActionSetsAttachInfo,
            CountActionSets = 1,
            ActionSets = &actionSet,
        };
        OpenXRSession.Check(_xr.AttachSessionActionSets(_session, &attachInfo), "xrAttachSessionActionSets");

        for (int hand = 0; hand < 2; hand++)
        {
            _gripSpaces[hand] = CreateActionSpace(_gripPose, _handPaths[hand]);
            _aimSpaces[hand] = CreateActionSpace(_aimPose, _handPaths[hand]);
        }
        if (HasEyeGaze) _eyeGazeSpace = CreateActionSpace(_eyeGaze, 0);

        _joints = new XRHandJointPose[2, s_jointCount];
        if (handTracking) _handTracking = CreateHandTrackers();
    }

    /// <summary>One tracker per hand, or none when the runtime will not make them, which leaves hands untracked.</summary>
    private ExtHandTracking? CreateHandTrackers()
    {
        if (!_xr.TryGetInstanceExtension<ExtHandTracking>(null, _instance, out var extension)) return null;
        for (int hand = 0; hand < 2; hand++)
        {
            var info = new HandTrackerCreateInfoEXT
            {
                Type = StructureType.HandTrackerCreateInfoExt,
                Hand = hand == 0 ? HandEXT.LeftExt : HandEXT.RightExt,
                HandJointSet = HandJointSetEXT.DefaultExt,
            };
            HandTrackerEXT tracker;
            if (extension.CreateHandTracker(_session, &info, &tracker) != Result.Success)
            {
                Debug.LogWarning("The headset reports hand tracking but would not track the hands.");
                return null;
            }
            _handTrackers[hand] = tracker;
        }
        return extension;
    }

    private XrAction CreateAction(string name, string localizedName, ActionType type, bool handed = true)
    {
        XrAction action;
        fixed (ulong* subactionPaths = _handPaths)
        {
            var info = new ActionCreateInfo
            {
                Type = StructureType.ActionCreateInfo,
                ActionType = type,
                CountSubactionPaths = handed ? 2u : 0u,
                SubactionPaths = handed ? subactionPaths : null,
            };
            OpenXRSession.WriteString(info.ActionName, 64, name);
            OpenXRSession.WriteString(info.LocalizedActionName, 128, localizedName);
            OpenXRSession.Check(_xr.CreateAction(_actionSet, &info, &action), "xrCreateAction");
        }
        return action;
    }

    /// <summary>
    /// For every profile, binds each action to the first of its candidate paths the runtime accepts. Each path is
    /// tried on its own first, since one path a profile lacks makes the runtime reject the whole suggestion.
    /// </summary>
    private void SuggestBindings(Binding[] bindings)
    {
        var accepted = new List<ActionSuggestedBinding>();
        foreach (string profilePath in s_profiles)
        {
            ulong profile = StringToPath(profilePath);
            accepted.Clear();

            foreach (Binding binding in bindings)
            {
                for (int hand = 0; hand < 2; hand++)
                {
                    string prefix = hand == 0 ? "/user/hand/left" : "/user/hand/right";
                    foreach (string candidate in hand == 0 ? binding.Left : binding.Right)
                    {
                        var suggestion = new ActionSuggestedBinding { Action = binding.Action, Binding = StringToPath(prefix + candidate) };
                        if (Suggest(profile, &suggestion, 1) != Result.Success) continue;
                        accepted.Add(suggestion);
                        break;
                    }
                }
            }

            if (accepted.Count == 0) continue;
            var all = accepted.ToArray();
            fixed (ActionSuggestedBinding* ptr = all)
                OpenXRSession.Check(Suggest(profile, ptr, all.Length), "xrSuggestInteractionProfileBindings");
        }
    }

    private Result Suggest(ulong profile, ActionSuggestedBinding* bindings, int count)
    {
        var suggested = new InteractionProfileSuggestedBinding
        {
            Type = StructureType.InteractionProfileSuggestedBinding,
            InteractionProfile = profile,
            CountSuggestedBindings = (uint)count,
            SuggestedBindings = bindings,
        };
        return _xr.SuggestInteractionProfileBinding(_instance, &suggested);
    }

    private Space CreateActionSpace(XrAction action, ulong subactionPath)
    {
        var info = new ActionSpaceCreateInfo
        {
            Type = StructureType.ActionSpaceCreateInfo,
            Action = action,
            SubactionPath = subactionPath,
            PoseInActionSpace = new Posef { Orientation = new Quaternionf(0, 0, 0, 1) },
        };
        Space space;
        OpenXRSession.Check(_xr.CreateActionSpace(_session, &info, &space), "xrCreateActionSpace");
        return space;
    }

    private ulong StringToPath(string path)
    {
        nint text = SilkMarshal.StringToPtr(path);
        try
        {
            ulong result;
            OpenXRSession.Check(_xr.StringToPath(_instance, (byte*)text, &result), "xrStringToPath");
            return result;
        }
        finally
        {
            SilkMarshal.Free(text);
        }
    }

    /// <summary>Poses are located in this space from now on, after tracking space was recentred.</summary>
    public void SetAppSpace(Space space) => _appSpace = space;

    /// <summary>Reads which controller the runtime uses in each hand, raising <see cref="XRInput.ControllersChanged"/> when that changed.</summary>
    public void RefreshControllers()
    {
        bool changed = false;
        for (int hand = 0; hand < 2; hand++)
        {
            string profile = "";
            var state = new InteractionProfileState { Type = StructureType.InteractionProfileState };
            if (_xr.GetCurrentInteractionProfile(_session, _handPaths[hand], &state) == Result.Success && state.InteractionProfile != 0)
                profile = PathToString(state.InteractionProfile);
            changed |= profile != _profiles[hand];
            _profiles[hand] = profile;
        }
        if (changed) XRInput.RaiseControllersChanged();
    }

    private string PathToString(ulong path)
    {
        const int capacity = 256;
        byte* text = stackalloc byte[capacity];
        uint length;
        return _xr.PathToString(_instance, path, capacity, &length, text) == Result.Success ? OpenXRSession.ReadString(text) : "";
    }

    /// <summary>Reads every action and locates the hands and eyes for the frame displayed at <paramref name="time"/>.</summary>
    public void Update(long time, bool focused)
    {
        Array.Copy(_boolStates, _previousBoolStates, _boolStates.Length);
        Array.Clear(_boolFlicked);
        bool wasFocused = _focused;
        _focused = focused;

        if (!focused)
        {
            Array.Clear(_boolStates);
            Array.Clear(_triggerValues);
            Array.Clear(_gripValues);
            Array.Clear(_thumbstickValues);
            Array.Clear(_gripPoses);
            Array.Clear(_aimPoses);
            Array.Clear(_joints);
            Array.Clear(_handTracked);
            _eyeGazePose = default;
            return;
        }

        var activeSet = new ActiveActionSet { ActionSet = _actionSet };
        var syncInfo = new ActionsSyncInfo
        {
            Type = StructureType.ActionsSyncInfo,
            CountActiveActionSets = 1,
            ActiveActionSets = &activeSet,
        };
        OpenXRSession.Check(_xr.SyncAction(_session, &syncInfo), "xrSyncActions");

        for (int hand = 0; hand < 2; hand++)
        {
            _triggerValues[hand] = ReadFloat(_trigger, hand);
            _gripValues[hand] = ReadFloat(_grip, hand);
            _thumbstickValues[hand] = ReadVector2(_thumbstick, hand);
            for (int i = 0; i < s_boolCount; i++)
            {
                _boolStates[hand, i] = ReadBool(_bools[i], hand, out bool changed);
                // Changed but back where it was means it went both ways between two frames, a tap shorter than a frame.
                _boolFlicked[hand, i] = wasFocused && changed && _boolStates[hand, i] == _previousBoolStates[hand, i];
            }

            _gripPoses[hand] = LocateAction(_gripPose, _gripSpaces[hand], _handPaths[hand], time);
            _aimPoses[hand] = LocateAction(_aimPose, _aimSpaces[hand], _handPaths[hand], time);
            LocateHandJoints(hand, time);
        }

        _eyeGazePose = HasEyeGaze ? LocateAction(_eyeGaze, _eyeGazeSpace, 0, time) : default;
    }

    private void LocateHandJoints(int hand, long time)
    {
        _handTracked[hand] = false;
        if (_handTracking == null) return;

        var joints = stackalloc HandJointLocationEXT[s_jointCount];
        var velocities = stackalloc HandJointVelocityEXT[s_jointCount];
        var velocityList = new HandJointVelocitiesEXT
        {
            Type = StructureType.HandJointVelocitiesExt,
            JointCount = (uint)s_jointCount,
            JointVelocities = velocities,
        };
        var locations = new HandJointLocationsEXT
        {
            Type = StructureType.HandJointLocationsExt,
            Next = &velocityList,
            JointCount = (uint)s_jointCount,
            JointLocations = joints,
        };
        var info = new HandJointsLocateInfoEXT { Type = StructureType.HandJointsLocateInfoExt, BaseSpace = _appSpace, Time = time };
        if (_handTracking.LocateHandJoints(_handTrackers[hand], &info, &locations) != Result.Success || locations.IsActive == 0)
        {
            for (int j = 0; j < s_jointCount; j++) _joints[hand, j] = default;
            return;
        }

        _handTracked[hand] = true;
        for (int j = 0; j < s_jointCount; j++)
            _joints[hand, j] = ToProwl(joints[j], velocities[j]);
    }

    private static XRHandJointPose ToProwl(in HandJointLocationEXT joint, in HandJointVelocityEXT velocity)
    {
        var flags = joint.LocationFlags;
        var pose = new XRPose
        {
            Position = OpenXRSession.ToProwl(joint.Pose.Position),
            Rotation = OpenXRSession.ToProwl(joint.Pose.Orientation),
            HasPosition = (flags & SpaceLocationFlags.PositionValidBit) != 0,
            HasRotation = (flags & SpaceLocationFlags.OrientationValidBit) != 0,
            IsTracked = (flags & SpaceLocationFlags.OrientationTrackedBit) != 0 && (flags & SpaceLocationFlags.PositionTrackedBit) != 0,
            HasLinearVelocity = (velocity.VelocityFlags & SpaceVelocityFlags.LinearValidBit) != 0,
            HasAngularVelocity = (velocity.VelocityFlags & SpaceVelocityFlags.AngularValidBit) != 0,
        };
        if (pose.HasLinearVelocity) pose.LinearVelocity = OpenXRSession.ToProwl(velocity.LinearVelocity);
        if (pose.HasAngularVelocity) pose.AngularVelocity = OpenXRSession.ToProwlAngular(velocity.AngularVelocity);
        return new XRHandJointPose { Pose = pose, Radius = joint.Radius };
    }

    private ActionStateGetInfo GetInfo(XrAction action, ulong subactionPath)
        => new() { Type = StructureType.ActionStateGetInfo, Action = action, SubactionPath = subactionPath };

    private float ReadFloat(XrAction action, int hand)
    {
        var info = GetInfo(action, _handPaths[hand]);
        var state = new ActionStateFloat { Type = StructureType.ActionStateFloat };
        return _xr.GetActionStateFloat(_session, &info, &state) == Result.Success && state.IsActive != 0 ? state.CurrentState : 0f;
    }

    private Float2 ReadVector2(XrAction action, int hand)
    {
        var info = GetInfo(action, _handPaths[hand]);
        var state = new ActionStateVector2f { Type = StructureType.ActionStateVector2f };
        if (_xr.GetActionStateVector2(_session, &info, &state) != Result.Success || state.IsActive == 0) return Float2.Zero;
        return new Float2(state.CurrentState.X, state.CurrentState.Y);
    }

    private bool ReadBool(XrAction action, int hand, out bool changed)
    {
        var info = GetInfo(action, _handPaths[hand]);
        var state = new ActionStateBoolean { Type = StructureType.ActionStateBoolean };
        bool read = _xr.GetActionStateBoolean(_session, &info, &state) == Result.Success && state.IsActive != 0;
        changed = read && state.ChangedSinceLastSync != 0;
        return read && state.CurrentState != 0;
    }

    private XRPose LocateAction(XrAction poseAction, Space space, ulong subactionPath, long time)
    {
        var info = GetInfo(poseAction, subactionPath);
        var state = new ActionStatePose { Type = StructureType.ActionStatePose };
        if (_xr.GetActionStatePose(_session, &info, &state) != Result.Success || state.IsActive == 0) return default;
        return OpenXRSession.Locate(_xr, space, _appSpace, time);
    }

    public XRPose GetGripPose(XRHand hand) => _gripPoses[(int)hand];
    public XRPose GetAimPose(XRHand hand) => _aimPoses[(int)hand];
    public XRPose EyeGazePose => _eyeGazePose;
    public string GetProfile(XRHand hand) => _profiles[(int)hand];
    public float GetTrigger(XRHand hand) => _triggerValues[(int)hand];
    public float GetGrip(XRHand hand) => _gripValues[(int)hand];
    public Float2 GetThumbstick(XRHand hand) => _thumbstickValues[(int)hand];
    public bool GetBool(XRHand hand, int index) => _boolStates[(int)hand, index];

    public bool GetBoolDown(XRHand hand, int index)
        => (_boolStates[(int)hand, index] && !_previousBoolStates[(int)hand, index]) || _boolFlicked[(int)hand, index];

    public bool GetBoolUp(XRHand hand, int index)
        => (!_boolStates[(int)hand, index] && _previousBoolStates[(int)hand, index]) || _boolFlicked[(int)hand, index];

    public bool IsHandTracked(XRHand hand) => _handTracked[(int)hand];
    public XRHandJointPose GetHandJoint(XRHand hand, XRHandJoint joint) => _joints[(int)hand, (int)joint];
    public float GetPinch(XRHand hand) => _handTracked[(int)hand] ? Pinch(GetHandJoint(hand, XRHandJoint.ThumbTip), GetHandJoint(hand, XRHandJoint.IndexTip)) : 0f;

    /// <summary>
    /// How closely two fingertips pinch, from the gap between their surfaces: touching at half a centimetre or
    /// less, apart from three centimetres on.
    /// </summary>
    internal static float Pinch(in XRHandJointPose thumb, in XRHandJointPose index)
    {
        if (!thumb.Pose.HasPosition || !index.Pose.HasPosition) return 0f;
        float gap = Float3.Distance(thumb.Pose.Position, index.Pose.Position) - thumb.Radius - index.Radius;
        return 1f - Maths.Clamp((gap - 0.005f) / 0.025f, 0f, 1f);
    }

    // Haptics are only allowed while the session runs, and only reach the controllers while it has focus.
    public void Vibrate(XRHand hand, float amplitude, float seconds, float frequency)
    {
        if (!_focused) return;
        var vibration = new HapticVibration
        {
            Type = StructureType.HapticVibration,
            // -1 is XR_MIN_HAPTIC_DURATION, the shortest pulse the controller supports.
            Duration = seconds > 0f ? (long)(seconds * 1_000_000_000.0) : -1,
            Frequency = frequency,
            Amplitude = Maths.Clamp(amplitude, 0f, 1f),
        };
        var info = new HapticActionInfo { Type = StructureType.HapticActionInfo, Action = _haptic, SubactionPath = _handPaths[(int)hand] };
        _xr.ApplyHapticFeedback(_session, &info, (HapticBaseHeader*)&vibration);
    }

    public void StopVibration(XRHand hand)
    {
        if (!_focused) return;
        var info = new HapticActionInfo { Type = StructureType.HapticActionInfo, Action = _haptic, SubactionPath = _handPaths[(int)hand] };
        _xr.StopHapticFeedback(_session, &info);
    }
}
