// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Clay.Internal.Intermediate;

/// <summary>Mutable counterpart of <see cref="Camera"/>.</summary>
internal sealed class IntermediateCamera
{
    public string? Name { get; set; }
    public CameraProjection Projection { get; set; } = CameraProjection.Perspective;
    public float VerticalFovRadians { get; set; }
    public float? AspectRatio { get; set; }
    public float OrthographicHalfWidth { get; set; }
    public float OrthographicHalfHeight { get; set; }
    public float NearPlane { get; set; } = 0.01f;
    public float? FarPlane { get; set; }
}

/// <summary>Mutable counterpart of <see cref="Light"/>.</summary>
internal sealed class IntermediateLight
{
    public string? Name { get; set; }
    public LightType Type { get; set; } = LightType.Point;
    public Color Color { get; set; } = new(1f, 1f, 1f, 1f);
    public float Intensity { get; set; } = 1f;
    public float? Range { get; set; }
    public float InnerConeAngleRadians { get; set; }
    public float OuterConeAngleRadians { get; set; } = MathF.PI / 4f;
}
