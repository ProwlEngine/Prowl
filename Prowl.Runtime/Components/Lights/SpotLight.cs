// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Vector;

using GLightType = Prowl.Graphite.Rendering.LightType;

namespace Prowl.Runtime;

/// <summary>A cone of light along Forward, like a flashlight. Its intensity is in candela.</summary>
[AddComponentMenu("Rendering/Lights/Spot Light")]
[ComponentIcon("")] // Lightbulb
public sealed class SpotLight : PunctualLight
{
    /// <summary>The whole angle of the cone, in degrees.</summary>
    public float SpotAngle { get => spotAngle; set { spotAngle = value; Wake(); } }
    [SerializeField, Range(1f, 179f)] private float spotAngle = 90.0f;

    /// <summary>How much of the cone fades out toward its edge, 0 for a hard edge and 1 for a fade all the way from the axis.</summary>
    public float SpotSoftness { get => spotSoftness; set { spotSoftness = value; Wake(); } }
    [SerializeField, Range(0f, 1f)] private float spotSoftness = 1f / 3f;

    private static readonly LightPreset[] s_presets =
    [
        new("Flashlight", LightUnit.Lumen, 300f, 6000f, SpotAngle: 20f),
        new("Downlight", LightUnit.Lumen, 800f, 3000f, SpotAngle: 60f),
        new("Car headlight", LightUnit.Lumen, 1500f, 4300f, SpotAngle: 40f),
        new("Stage spot", LightUnit.Lumen, 10000f, 3200f, SpotAngle: 25f),
    ];

    public override IReadOnlyList<LightPreset> Presets => s_presets;

    public override LightPreset DefaultPreset => s_presets[1];

    protected override void ApplyShape(LightPreset preset)
    {
        if (preset.SpotAngle is float angle) spotAngle = angle;
    }

    protected override bool HasShape(LightPreset preset) => preset.SpotAngle is not float angle || SpotAngle == angle;

    protected override float LumensPerStored => 2f * Maths.PI * (1f - Maths.Cos(Maths.Clamp(SpotAngle, 1f, 179f) * 0.5f * Maths.Deg2Rad));

    protected override void DescribeShape(ref LightDesc desc)
    {
        base.DescribeShape(ref desc);
        desc.Type = GLightType.Spot;
        desc.OuterAngle = Maths.Clamp(SpotAngle, 1f, 179f) * 0.5f;
        desc.InnerAngle = desc.OuterAngle * (1f - Maths.Clamp(SpotSoftness, 0f, 1f));
    }

    public override void DrawGizmosSelected()
    {
        Debug.DrawArrow(Transform.Position, Transform.Forward, Color.Yellow);
        Debug.DrawWireConeAngle(Transform.Position, Transform.Forward, Maths.Clamp(SpotAngle, 1f, 179f) * 0.5f, Range, Color.Yellow);
    }
}
