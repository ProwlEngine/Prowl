// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Vector;

using GLightType = Prowl.Graphite.Rendering.LightType;

namespace Prowl.Runtime;

/// <summary>Light in every direction from a point, like a bare bulb. Its intensity is in candela.</summary>
[AddComponentMenu("Rendering/Lights/Point Light")]
[ComponentIcon("")] // Lightbulb
public sealed class PointLight : PunctualLight
{
    private static readonly LightPreset[] s_presets =
    [
        new("Candle", LightUnit.Lumen, 12f, 1900f),
        new("40 W bulb", LightUnit.Lumen, 450f, 2700f),
        new("60 W bulb", LightUnit.Lumen, 800f, 2700f),
        new("100 W bulb", LightUnit.Lumen, 1600f, 2700f),
        new("Street lamp", LightUnit.Lumen, 15000f, 2000f),
    ];

    public override IReadOnlyList<LightPreset> Presets => s_presets;

    public override LightPreset DefaultPreset => s_presets[2];

    protected override float LumensPerStored => 4f * Maths.PI;

    protected override void DescribeShape(ref LightDesc desc)
    {
        base.DescribeShape(ref desc);
        desc.Type = GLightType.Point;
    }
}
