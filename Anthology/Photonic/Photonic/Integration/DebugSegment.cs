using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// A recorded ray segment from a bake-time path trace. The demo uses these to draw the bounce
/// chain for a hovered texel each frame so you can see exactly where the path tracer's rays are
/// going (and which ones get occluded).
/// </summary>
public struct DebugSegment
{
    /// <summary>World-space ray origin.</summary>
    public Float3 Start;

    /// <summary>World-space ray endpoint: either the hit position or origin + dir * maxT if no hit.</summary>
    public Float3 End;

    /// <summary>0 = primary bounce direction from the texel, 1 = first bounce, etc.</summary>
    public int BounceIndex;

    /// <summary>True for shadow rays (NEE direct lighting); false for diffuse bounce rays.</summary>
    public bool IsShadow;

    /// <summary>True if this ray hit something within its <see cref="End"/>; false if it missed (only meaningful for shadow rays and bounce rays that go off into the sky).</summary>
    public bool Hit;
}
