// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Motion;

using MotionEvent = Prowl.Motion.AnimationEvent;

namespace Prowl.Runtime;

/// <summary>Whether a clip node loops, or takes the clip's own setting.</summary>
public enum ClipLooping
{
    /// <summary>Whatever the clip was imported as.</summary>
    FromClip,
    Loop,
    Once,
}

/// <summary>What a <see cref="ClipEvent"/> marks, which decides what reads it.</summary>
public enum ClipEventKind
{
    /// <summary>A named moment or stretch, for gameplay: a footstep sound, a hit frame. Read by Event Fired.</summary>
    Named,
    /// <summary>A foot planting or passing. Read by Foot Phase, and by nodes that keep feet in step.</summary>
    Foot,
    /// <summary>A stretch that allows or blocks leaving the clip. Read by Transition Window.</summary>
    TransitionWindow,
    /// <summary>The stretch an Orientation Warp may turn the clip in.</summary>
    OrientationWarp,
    /// <summary>The stretch a Target Warp may bend the clip's travel in, and along which axes.</summary>
    TargetWarp,
    /// <summary>A stretch where a Root Motion Override lets the clip's own root motion through.</summary>
    RootMotion,
    /// <summary>A stretch sampled a frame at a time rather than smoothly.</summary>
    SnapToFrame,
}

/// <summary>
/// A marker on an animation clip: a moment, or a stretch when it has a length. Times are in seconds
/// from the start of the clip. The kinds that need a choice keep it in <see cref="Option"/>: the foot
/// phase, the transition rule, the target warp rule or the frame snap mode.
/// </summary>
public sealed class ClipEvent
{
    public ClipEventKind Kind;
    public float Time;

    /// <summary>How long the event lasts, in seconds. Zero is a single moment.</summary>
    public float Length;

    /// <summary>The event's name, or for a transition window the optional id a condition can filter on.</summary>
    public string Name = string.Empty;

    public int Option;

    /// <summary>How long a root motion stretch takes to hand back to the override, in seconds.</summary>
    public float BlendTime = 0.1f;

    public ClipEvent Clone() => (ClipEvent)MemberwiseClone();

    /// <summary>Whether the kind only makes sense over a stretch of the clip rather than at a moment.</summary>
    public static bool IsStretch(ClipEventKind kind)
        => kind is ClipEventKind.TransitionWindow or ClipEventKind.OrientationWarp or ClipEventKind.TargetWarp
            or ClipEventKind.RootMotion or ClipEventKind.SnapToFrame;

    /// <summary>The names of the choices <see cref="Option"/> picks between for a kind, or null when it has none.</summary>
    public static string[]? OptionNames(ClipEventKind kind) => kind switch
    {
        ClipEventKind.Foot => Enum.GetNames<FootPhase>(),
        ClipEventKind.TransitionWindow => Enum.GetNames<TransitionRule>(),
        ClipEventKind.TargetWarp => Enum.GetNames<TargetWarpRule>(),
        ClipEventKind.SnapToFrame => Enum.GetNames<FrameSnapMode>(),
        _ => null,
    };

    /// <summary>The event as Motion plays it, in the normalized time of a clip this long.</summary>
    public MotionEvent ToMotion(float clipDuration)
    {
        float scale = clipDuration > 0f ? 1f / clipDuration : 0f;
        float start = Time * scale;
        float length = Length * scale;

        return Kind switch
        {
            ClipEventKind.Foot => new FootEvent((FootPhase)Option, start, length),
            ClipEventKind.TransitionWindow => new TransitionEvent((TransitionRule)Option, start, length, Id(Name)),
            ClipEventKind.OrientationWarp => new OrientationWarpEvent(start, length),
            ClipEventKind.TargetWarp => new TargetWarpEvent((TargetWarpRule)Option, start, length),
            ClipEventKind.RootMotion => new RootMotionEvent(start, length, BlendTime),
            ClipEventKind.SnapToFrame => new SnapToFrameEvent((FrameSnapMode)Option, start, length),
            _ => new IdEvent(Id(Name), start, length),
        };
    }

    private static StringID Id(string name) => name.Length > 0 ? new StringID(name) : default;
}
