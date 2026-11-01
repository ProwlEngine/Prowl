// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Vector;

using GLightType = Prowl.Graphite.Rendering.LightType;

namespace Prowl.Runtime;

/// <summary>A glowing capsule along Forward, like a fluorescent tube, <see cref="Length"/> long and <see cref="Radius"/> thick.</summary>
[AddComponentMenu("Rendering/Lights/Tube Light")]
[ComponentIcon("")] // Lightbulb
public sealed class TubeLight : AreaLight
{
    public float Length { get => length; set { length = value; Wake(); } }
    [SerializeField] private float length = 1.0f;

    public float Radius { get => radius; set { radius = value; Wake(); } }
    [SerializeField] private float radius = 0.0125f;

    private static readonly LightPreset[] s_presets =
    [
        new("Fluorescent tube", LightUnit.Lumen, 2800f, 4000f, Length: 1.2f, Radius: 0.0125f),
        new("LED strip, 1 m", LightUnit.Lumen, 1000f, 3000f, Length: 1f, Radius: 0.005f),
    ];

    public override IReadOnlyList<LightPreset> Presets => s_presets;

    public override float SurfaceArea
    {
        get
        {
            float r = Maths.Max(Radius, 0f);
            return 2f * Maths.PI * r * Maths.Max(Length, 0f) + 4f * Maths.PI * r * r;
        }
    }

    protected override float ProjectedArea
    {
        get
        {
            float r = Maths.Max(Radius, 0f);
            return 2f * r * Maths.Max(Length, 0f) + Maths.PI * r * r;
        }
    }

    protected override void ApplyShape(LightPreset preset)
    {
        if (preset.Length is float presetLength) length = presetLength;
        if (preset.Radius is float presetRadius) radius = presetRadius;
    }

    protected override bool HasShape(LightPreset preset)
        => (preset.Length is not float l || Length == l) && (preset.Radius is not float r || Radius == r);

    protected override void DescribeShape(ref LightDesc desc)
    {
        base.DescribeShape(ref desc);
        desc.Type = GLightType.Tube;
        desc.Size = new Float2(Maths.Max(Length, 0f), 0f);
        desc.SourceRadius = Radius;
    }

    public override void DrawGizmos()
    {
        base.DrawGizmos();
        Float3 half = Transform.Forward * (Length * 0.5f);
        Debug.DrawWireCapsule(Transform.Position - half, Transform.Position + half, Maths.Max(Radius, 0.02f), Color.Yellow);
    }
}
