using System;

namespace Prowl.Veldrid;

/// <summary>
/// A <see cref="ShaderProgram"/> component describing the properties of the rasterizer.
/// </summary>
public struct RasterizerStateDescription : IEquatable<RasterizerStateDescription>
{
    /// <summary>
    /// Controls which face will be culled.
    /// </summary>
    public FaceCullMode CullMode;
    /// <summary>
    /// Controls the winding order used to determine the front face of primitives.
    /// </summary>
    public FrontFace FrontFace;
    /// <summary>
    /// Controls whether depth clipping is enabled.
    /// </summary>
    public bool DepthClipEnabled;
    /// <summary>
    /// Controls whether the scissor test is enabled.
    /// </summary>
    public bool ScissorTestEnabled;

    /// <summary>
    /// Constructs a new RasterizerStateDescription.
    /// </summary>
    /// <param name="cullMode">Controls which face will be culled.</param>
    /// <param name="frontFace">Controls the winding order used to determine the front face of primitives.</param>
    /// <param name="depthClipEnabled">Controls whether depth clipping is enabled.</param>
    /// <param name="scissorTestEnabled">Controls whether the scissor test is enabled.</param>
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
    /// Describes the default rasterizer state, with clockwise backface culling, solid polygon filling, and both depth
    /// clipping and scissor tests enabled.
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
    /// Describes a rasterizer state with no culling, solid polygon filling, and both depth
    /// clipping and scissor tests enabled.
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
    /// <param name="other">The instance to compare to.</param>
    /// <returns>True if all elements are equal; false otherswise.</returns>
    public bool Equals(RasterizerStateDescription other)
    {
        return CullMode == other.CullMode
            && FrontFace == other.FrontFace
            && DepthClipEnabled.Equals(other.DepthClipEnabled)
            && ScissorTestEnabled.Equals(other.ScissorTestEnabled);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            (int)CullMode,
            (int)FrontFace,
            DepthClipEnabled.GetHashCode(),
            ScissorTestEnabled.GetHashCode());
    }
}
