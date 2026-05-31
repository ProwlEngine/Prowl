using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// A snapshot of one texel's recorded state in the bake. Used by debug viewers to inspect what
/// the integrator actually saw for a given atlas pixel.
/// </summary>
public struct TexelDiagnostic
{
    /// <summary>True when the target index / coordinates were valid and the workspaces are allocated.</summary>
    public bool Valid;

    /// <summary>True if any triangle conservatively covered this texel.</summary>
    public bool Covered;

    /// <summary>The world-space sample position the integrator uses.</summary>
    public Float3 Position;

    /// <summary>Surface normal at the sample point.</summary>
    public Float3 Normal;

    /// <summary>Half-extent of the texel's world footprint, in metres. Scales the denoiser's position bandwidth.</summary>
    public float WorldRadius;

    /// <summary>Current RGB value stored at this texel in the target's pixel buffer.</summary>
    public Float3 LightmapValue;

    /// <summary>Indirect samples accumulated at this texel (continuous mode only).</summary>
    public int IndirectSampleCount;
}
