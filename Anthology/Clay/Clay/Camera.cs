// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Clay;

/// <summary>Which projection a <see cref="Camera"/> describes.</summary>
public enum CameraProjection
{
    /// <summary>Perspective projection, driven by <see cref="Camera.VerticalFovRadians"/>.</summary>
    Perspective,

    /// <summary>Orthographic projection, driven by the magnification pair.</summary>
    Orthographic,
}

/// <summary>
/// A camera defined by the source file. Position and orientation come from the
/// <see cref="ModelNode"/> that references it; this carries only the lens.
/// </summary>
/// <remarks>
/// The camera aims along its node's forward axis and is +Y up. In the source that is -Z, and
/// <c>ConvertCoordinateSystem</c> mirrors it to +Z along with everything else, so after the standard
/// pipeline a camera points the same way the engine's own do.
/// </remarks>
public sealed class Camera
{
    /// <summary>Camera name, or <c>null</c> when the source did not name it.</summary>
    public string? Name { get; init; }

    /// <summary>Which of the two parameter sets below applies.</summary>
    public required CameraProjection Projection { get; init; }

    /// <summary>Vertical field of view in radians. Perspective only.</summary>
    public float VerticalFovRadians { get; init; }

    /// <summary>
    /// Width divided by height, or <c>null</c> when the source left it undefined, which means the
    /// camera should take the aspect of whatever it is rendering into. Perspective only.
    /// </summary>
    public float? AspectRatio { get; init; }

    /// <summary>Half the width of the view volume. Orthographic only.</summary>
    public float OrthographicHalfWidth { get; init; }

    /// <summary>Half the height of the view volume. Orthographic only.</summary>
    public float OrthographicHalfHeight { get; init; }

    /// <summary>Distance to the near clipping plane. Always greater than zero.</summary>
    public float NearPlane { get; init; } = 0.01f;

    /// <summary>
    /// Distance to the far clipping plane, or <c>null</c> for an infinite projection. glTF allows a
    /// perspective camera to omit it; orthographic cameras always have one.
    /// </summary>
    public float? FarPlane { get; init; }
}
