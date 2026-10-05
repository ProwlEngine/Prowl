// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

using Silk.NET.Core.Native;
using Silk.NET.OpenXR;

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

/// <summary>
/// Headset controller input while <see cref="XR"/> is running. Everything reads as released, zero and untracked
/// when it is not, or when the application does not have input focus in the headset.
/// </summary>
public static class XRInput
{
    private static OpenXRInput? Source => XR.Session?.Input;

    /// <summary>Whether the controller in <paramref name="hand"/> is being tracked.</summary>
    public static bool IsTracked(XRHand hand) => Source?.GetGripPose(hand).IsTracked ?? false;

    /// <summary>How far the trigger is pulled, from 0 to 1.</summary>
    public static float GetTrigger(XRHand hand) => Source?.GetTrigger(hand) ?? 0f;

    /// <summary>How hard the grip is squeezed, from 0 to 1. Controllers with a grip button report 0 or 1.</summary>
    public static float GetGrip(XRHand hand) => Source?.GetGrip(hand) ?? 0f;

    public static float GetAxis(XRHand hand, XRAxis axis) => axis == XRAxis.Grip ? GetGrip(hand) : GetTrigger(hand);

    /// <summary>The thumbstick, or the touchpad on controllers that have one instead, each axis from -1 to 1.</summary>
    public static Float2 GetThumbstick(XRHand hand) => Source?.GetThumbstick(hand) ?? Float2.Zero;

    public static bool GetButton(XRHand hand, XRButton button) => Source?.GetButton(hand, button) ?? false;

    /// <summary>True on the frame the button went down.</summary>
    public static bool GetButtonDown(XRHand hand, XRButton button) => Source?.GetButtonDown(hand, button) ?? false;

    /// <summary>True on the frame the button came up.</summary>
    public static bool GetButtonUp(XRHand hand, XRButton button) => Source?.GetButtonUp(hand, button) ?? false;

    /// <summary>
    /// Vibrates the controller in <paramref name="hand"/>, replacing any vibration still playing on it.
    /// </summary>
    /// <param name="hand">The controller to vibrate.</param>
    /// <param name="amplitude">Strength from 0 to 1.</param>
    /// <param name="seconds">How long it lasts. Zero or less plays the shortest pulse the controller can.</param>
    /// <param name="frequency">Frequency in Hz. Zero lets the runtime pick.</param>
    public static void Vibrate(XRHand hand, float amplitude, float seconds, float frequency = 0f) => Source?.Vibrate(hand, amplitude, seconds, frequency);

    public static void StopVibration(XRHand hand) => Source?.StopVibration(hand);
}

/// <summary>The OpenXR action set behind <see cref="XRInput"/> and the hand poses of <see cref="XR.GetPose"/>.</summary>
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

    private static readonly int s_buttonCount = System.Enum.GetValues<XRButton>().Length;

    private readonly XrApi _xr;
    private readonly Instance _instance;
    private readonly Session _session;
    private readonly Space _appSpace;
    private readonly ActionSet _actionSet;
    private readonly ulong[] _handPaths = new ulong[2];

    private readonly XrAction _gripPose;
    private readonly XrAction _aimPose;
    private readonly XrAction _trigger;
    private readonly XrAction _grip;
    private readonly XrAction _thumbstick;
    private readonly XrAction _haptic;
    private readonly XrAction[] _buttons;
    private readonly Space[] _gripSpaces = new Space[2];
    private readonly Space[] _aimSpaces = new Space[2];

    private readonly float[] _triggerValues = new float[2];
    private readonly float[] _gripValues = new float[2];
    private readonly Float2[] _thumbstickValues = new Float2[2];
    private readonly bool[,] _buttonStates;
    private readonly bool[,] _previousButtonStates;
    private readonly XRPose[] _gripPoses = new XRPose[2];
    private readonly XRPose[] _aimPoses = new XRPose[2];

    private readonly record struct Binding(XrAction Action, string[] Left, string[] Right);

    public OpenXRInput(XrApi xr, Instance instance, Session session, Space appSpace)
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

        _buttons = new XrAction[s_buttonCount];
        _buttonStates = new bool[2, s_buttonCount];
        _previousButtonStates = new bool[2, s_buttonCount];
        for (int i = 0; i < s_buttonCount; i++)
        {
            string name = ((XRButton)i).ToString();
            _buttons[i] = CreateAction(name.ToLowerInvariant() + "_button", name + " Button", ActionType.BooleanInput);
        }

        SuggestBindings(
        [
            new(_gripPose, ["/input/grip/pose"], ["/input/grip/pose"]),
            new(_aimPose, ["/input/aim/pose"], ["/input/aim/pose"]),
            new(_trigger, ["/input/trigger/value", "/input/select/click"], ["/input/trigger/value", "/input/select/click"]),
            new(_grip, ["/input/squeeze/value", "/input/squeeze/click"], ["/input/squeeze/value", "/input/squeeze/click"]),
            new(_thumbstick, ["/input/thumbstick", "/input/trackpad"], ["/input/thumbstick", "/input/trackpad"]),
            new(_haptic, ["/output/haptic"], ["/output/haptic"]),
            new(_buttons[(int)XRButton.Trigger], ["/input/trigger/value", "/input/select/click"], ["/input/trigger/value", "/input/select/click"]),
            new(_buttons[(int)XRButton.Grip], ["/input/squeeze/value", "/input/squeeze/click"], ["/input/squeeze/value", "/input/squeeze/click"]),
            new(_buttons[(int)XRButton.Primary], ["/input/x/click", "/input/a/click"], ["/input/a/click"]),
            new(_buttons[(int)XRButton.Secondary], ["/input/y/click", "/input/b/click"], ["/input/b/click"]),
            new(_buttons[(int)XRButton.Menu], ["/input/menu/click"], ["/input/menu/click"]),
            new(_buttons[(int)XRButton.Thumbstick], ["/input/thumbstick/click", "/input/trackpad/click"], ["/input/thumbstick/click", "/input/trackpad/click"]),
        ]);

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
    }

    private XrAction CreateAction(string name, string localizedName, ActionType type)
    {
        XrAction action;
        fixed (ulong* subactionPaths = _handPaths)
        {
            var info = new ActionCreateInfo
            {
                Type = StructureType.ActionCreateInfo,
                ActionType = type,
                CountSubactionPaths = 2,
                SubactionPaths = subactionPaths,
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

    /// <summary>Reads every action and locates the hands for the frame displayed at <paramref name="time"/>.</summary>
    public void Update(long time, bool focused)
    {
        System.Array.Copy(_buttonStates, _previousButtonStates, _buttonStates.Length);

        if (!focused)
        {
            System.Array.Clear(_buttonStates);
            System.Array.Clear(_triggerValues);
            System.Array.Clear(_gripValues);
            System.Array.Clear(_thumbstickValues);
            System.Array.Clear(_gripPoses);
            System.Array.Clear(_aimPoses);
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
            for (int i = 0; i < s_buttonCount; i++)
                _buttonStates[hand, i] = ReadBool(_buttons[i], hand);

            _gripPoses[hand] = LocateHand(_gripPose, _gripSpaces[hand], hand, time);
            _aimPoses[hand] = LocateHand(_aimPose, _aimSpaces[hand], hand, time);
        }
    }

    private ActionStateGetInfo GetInfo(XrAction action, int hand)
        => new() { Type = StructureType.ActionStateGetInfo, Action = action, SubactionPath = _handPaths[hand] };

    private float ReadFloat(XrAction action, int hand)
    {
        var info = GetInfo(action, hand);
        var state = new ActionStateFloat { Type = StructureType.ActionStateFloat };
        return _xr.GetActionStateFloat(_session, &info, &state) == Result.Success && state.IsActive != 0 ? state.CurrentState : 0f;
    }

    private Float2 ReadVector2(XrAction action, int hand)
    {
        var info = GetInfo(action, hand);
        var state = new ActionStateVector2f { Type = StructureType.ActionStateVector2f };
        if (_xr.GetActionStateVector2(_session, &info, &state) != Result.Success || state.IsActive == 0) return Float2.Zero;
        return new Float2(state.CurrentState.X, state.CurrentState.Y);
    }

    private bool ReadBool(XrAction action, int hand)
    {
        var info = GetInfo(action, hand);
        var state = new ActionStateBoolean { Type = StructureType.ActionStateBoolean };
        return _xr.GetActionStateBoolean(_session, &info, &state) == Result.Success && state.IsActive != 0 && state.CurrentState != 0;
    }

    private XRPose LocateHand(XrAction poseAction, Space space, int hand, long time)
    {
        var info = GetInfo(poseAction, hand);
        var state = new ActionStatePose { Type = StructureType.ActionStatePose };
        if (_xr.GetActionStatePose(_session, &info, &state) != Result.Success || state.IsActive == 0) return default;

        var location = new SpaceLocation { Type = StructureType.SpaceLocation };
        if (_xr.LocateSpace(space, _appSpace, time, &location) != Result.Success) return default;
        return OpenXRSession.ToProwl(location);
    }

    public XRPose GetGripPose(XRHand hand) => _gripPoses[(int)hand];
    public XRPose GetAimPose(XRHand hand) => _aimPoses[(int)hand];
    public float GetTrigger(XRHand hand) => _triggerValues[(int)hand];
    public float GetGrip(XRHand hand) => _gripValues[(int)hand];
    public Float2 GetThumbstick(XRHand hand) => _thumbstickValues[(int)hand];
    public bool GetButton(XRHand hand, XRButton button) => _buttonStates[(int)hand, (int)button];
    public bool GetButtonDown(XRHand hand, XRButton button) => _buttonStates[(int)hand, (int)button] && !_previousButtonStates[(int)hand, (int)button];
    public bool GetButtonUp(XRHand hand, XRButton button) => !_buttonStates[(int)hand, (int)button] && _previousButtonStates[(int)hand, (int)button];

    public void Vibrate(XRHand hand, float amplitude, float seconds, float frequency)
    {
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
        var info = new HapticActionInfo { Type = StructureType.HapticActionInfo, Action = _haptic, SubactionPath = _handPaths[(int)hand] };
        _xr.StopHapticFeedback(_session, &info);
    }
}
