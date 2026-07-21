using System;

namespace Prowl.Graphite;

/// <summary>
/// Rasterizer state for a graphics program.
/// </summary>
public struct RasterizerStateDescription : IEquatable<RasterizerStateDescription>
{
    /// <summary>
    /// Which face gets culled.
    /// </summary>
    public FaceCullMode CullMode;
    /// <summary>
    /// Winding order for front face.
    /// </summary>
    public FrontFace FrontFace;
    /// <summary>
    /// Depth clipping on/off.
    /// </summary>
    public bool DepthClipEnabled;
    /// <summary>
    /// Scissor test on/off.
    /// </summary>
    public bool ScissorTestEnabled;

    /// <summary>
    /// Makes a new RasterizerStateDescription.
    /// </summary>
    /// <param name="cullMode">Which face gets culled.</param>
    /// <param name="frontFace">Winding order for front face.</param>
    /// <param name="depthClipEnabled">Depth clipping on/off.</param>
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
    /// Default: clockwise backface culling, solid fill, depth clip and scissor test both on.
    /// Settings:
    ///     CullMode = FaceCullMode.Back
    ///     FrontFace = FrontFace.Clockwise
    ///     DepthClipEnabled = true
    ///     ScissorTestEnabled = false
    /// </summary>
    public static readonly RasterizerStateDescription Default = new()
    {
        CullMode = FaceCullMode.Back,
        FrontFace = FrontFace.Clockwise,
        DepthClipEnabled = true,
        ScissorTestEnabled = false,
    };

    /// <summary>
    /// No culling, solid fill, depth clip and scissor test both on.
    /// Settings:
    ///     CullMode = FaceCullMode.None
    ///     FrontFace = FrontFace.Clockwise
    ///     DepthClipEnabled = true
    ///     ScissorTestEnabled = false
    /// </summary>
    public static readonly RasterizerStateDescription CullNone = new()
    {
        CullMode = FaceCullMode.None,
        FrontFace = FrontFace.Clockwise,
        DepthClipEnabled = true,
        ScissorTestEnabled = false,
    };

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare to.</param>
    /// <returns>True if all fields match.</returns>
    public readonly bool Equals(RasterizerStateDescription other)
    {
        return CullMode == other.CullMode
            && FrontFace == other.FrontFace
            && DepthClipEnabled.Equals(other.DepthClipEnabled)
            && ScissorTestEnabled.Equals(other.ScissorTestEnabled);
    }

    /// <summary>
    /// Hash code for this instance.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            (int)CullMode,
            (int)FrontFace,
            DepthClipEnabled.GetHashCode(),
            ScissorTestEnabled.GetHashCode());
    }
}
