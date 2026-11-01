// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Vector;

using GLightType = Prowl.Graphite.Rendering.LightType;

namespace Prowl.Runtime;

/// <summary>A one sided glowing disk facing Forward.</summary>
[AddComponentMenu("Rendering/Lights/Disk Light")]
[ComponentIcon("")] // Lightbulb
public sealed class DiskLight : AreaLight
{
    public float Diameter { get => diameter; set { diameter = value; Wake(); } }
    [SerializeField] private float diameter = 1.0f;

    private static readonly LightPreset[] s_presets =
    [
        new("Ceiling disk", LightUnit.Lumen, 1000f, 3000f, Width: 0.3f),
        new("Round panel", LightUnit.Lumen, 2000f, 4000f, Width: 0.45f),
    ];

    public override IReadOnlyList<LightPreset> Presets => s_presets;

    public override float SurfaceArea => Maths.PI * 0.25f * Maths.Max(Diameter, 0f) * Maths.Max(Diameter, 0f);

    protected override float ProjectedArea => SurfaceArea;

    protected override void ApplyShape(LightPreset preset)
    {
        if (preset.Width is float presetWidth) diameter = presetWidth;
    }

    protected override bool HasShape(LightPreset preset) => preset.Width is not float w || Diameter == w;

    protected override void DescribeShape(ref LightDesc desc)
    {
        base.DescribeShape(ref desc);
        desc.Type = GLightType.Disk;
        desc.Size = new Float2(Maths.Max(Diameter, 0f) * 0.5f, 0f);
    }

    public override void DrawGizmos()
    {
        base.DrawGizmos();
        Debug.DrawWireCircle(Transform.Position, Transform.Forward, Diameter * 0.5f, Color.Yellow);
        Debug.DrawArrow(Transform.Position, Transform.Forward * 0.5f, Color.Yellow);
    }
}
