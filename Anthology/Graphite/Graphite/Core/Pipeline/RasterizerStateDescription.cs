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
    /// Depth bias on/off.
    /// </summary>
    public bool DepthBiasEnabled;
    /// <summary>
    /// Constant depth bias added to every fragment.
    /// </summary>
    public float DepthBiasConstantFactor;
    /// <summary>
    /// Depth bias scaled by the slope of the polygon.
    /// </summary>
    public float DepthBiasSlopeFactor;
    /// <summary>
    /// Maximum depth bias magnitude, 0 for no clamp.
    /// </summary>
    public float DepthBiasClamp;

    /// <summary>
    /// New rasterizer state description.
    /// </summary>
    /// <param name="cullMode">Face to cull.</param>
    /// <param name="frontFace">Front face winding.</param>
    /// <param name="depthClipEnabled">Depth clip on/off.</param>
    /// <param name="scissorTestEnabled">Scissor test on/off.</param>
    /// <param name="depthBiasEnabled">Depth bias on/off.</param>
    /// <param name="depthBiasConstantFactor">Constant depth bias.</param>
    /// <param name="depthBiasSlopeFactor">Slope scaled depth bias.</param>
    /// <param name="depthBiasClamp">Maximum depth bias magnitude.</param>
    public RasterizerStateDescription(
        FaceCullMode cullMode,
        FrontFace frontFace,
        bool depthClipEnabled,
        bool scissorTestEnabled,
        bool depthBiasEnabled = false,
        float depthBiasConstantFactor = 0f,
        float depthBiasSlopeFactor = 0f,
        float depthBiasClamp = 0f)
    {
        CullMode = cullMode;
        FrontFace = frontFace;
        DepthClipEnabled = depthClipEnabled;
        ScissorTestEnabled = scissorTestEnabled;
        DepthBiasEnabled = depthBiasEnabled;
        DepthBiasConstantFactor = depthBiasConstantFactor;
        DepthBiasSlopeFactor = depthBiasSlopeFactor;
        DepthBiasClamp = depthBiasClamp;
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
        DepthBiasEnabled = false,
        DepthBiasConstantFactor = 0f,
        DepthBiasSlopeFactor = 0f,
        DepthBiasClamp = 0f,
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
        DepthBiasEnabled = false,
        DepthBiasConstantFactor = 0f,
        DepthBiasSlopeFactor = 0f,
        DepthBiasClamp = 0f,
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
            && ScissorTestEnabled.Equals(other.ScissorTestEnabled)
            && DepthBiasEnabled.Equals(other.DepthBiasEnabled)
            && DepthBiasConstantFactor.Equals(other.DepthBiasConstantFactor)
            && DepthBiasSlopeFactor.Equals(other.DepthBiasSlopeFactor)
            && DepthBiasClamp.Equals(other.DepthBiasClamp);
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
            ScissorTestEnabled.GetHashCode(),
            DepthBiasEnabled.GetHashCode(),
            DepthBiasConstantFactor.GetHashCode(),
            DepthBiasSlopeFactor.GetHashCode(),
            DepthBiasClamp.GetHashCode());
    }
}
