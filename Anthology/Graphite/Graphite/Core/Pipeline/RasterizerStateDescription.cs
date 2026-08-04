using System;

namespace Prowl.Graphite;

/// <summary>
/// Rasterizer state.
/// </summary>
public struct RasterizerStateDescription : IEquatable<RasterizerStateDescription>
{
    /// <summary>
    /// Face to cull.
    /// </summary>
    public FaceCullMode CullMode;
    /// <summary>
    /// Front face winding.
    /// </summary>
    public FrontFace FrontFace;
    /// <summary>
    /// Depth clip on/off.
    /// </summary>
    public bool DepthClipEnabled;
    /// <summary>
    /// Scissor test on/off.
    /// </summary>
    public bool ScissorTestEnabled;

    /// <summary>
    /// New rasterizer state description.
    /// </summary>
    /// <param name="cullMode">Face to cull.</param>
    /// <param name="frontFace">Front face winding.</param>
    /// <param name="depthClipEnabled">Depth clip on/off.</param>
    /// <param name="scissorTestEnabled">Scissor test on/off.</param>
    public RasterizerStateDescription(
        FaceCullMode cullMode,
        FrontFace frontFace,
        bool depthClipEnabled,
        bool scissorTestEnabled)
    {
        CullMode = cullMode;
        FrontFace = frontFace;
        DepthClipEnabled = depthClipEnabled;
        ScissorTestEnabled = scissorTestEnabled;
    }

    /// <summary>
    /// Default: backface culling, clockwise front, depth clip on, scissor off.
    /// </summary>
    public static readonly RasterizerStateDescription Default = new()
    {
        CullMode = FaceCullMode.Back,
        FrontFace = FrontFace.Clockwise,
        DepthClipEnabled = true,
        ScissorTestEnabled = false,
    };

    /// <summary>
    /// No culling, clockwise front, depth clip on, scissor off.
    /// </summary>
    public static readonly RasterizerStateDescription CullNone = new()
    {
        CullMode = FaceCullMode.None,
        FrontFace = FrontFace.Clockwise,
        DepthClipEnabled = true,
        ScissorTestEnabled = false,
    };

    /// <summary>
    /// Field-by-field equality.
    /// </summary>
    /// <param name="other">Other instance.</param>
    /// <returns>True if all fields match.</returns>
    public readonly bool Equals(RasterizerStateDescription other)
    {
        return CullMode == other.CullMode
            && FrontFace == other.FrontFace
            && DepthClipEnabled.Equals(other.DepthClipEnabled)
            && ScissorTestEnabled.Equals(other.ScissorTestEnabled);
    }

    /// <summary>
    /// Hash code.
    /// </summary>
    /// <returns>Hash.</returns>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            (int)CullMode,
            (int)FrontFace,
            DepthClipEnabled.GetHashCode(),
            ScissorTestEnabled.GetHashCode());
    }
}
