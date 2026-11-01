// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>A light that shines from a glowing surface, its <see cref="Light.Intensity"/> in nits. Area lights cast no shadows yet.</summary>
public abstract class AreaLight : LocalLight
{
    public override IReadOnlyList<LightUnit> Units => s_units;
    private static readonly LightUnit[] s_units = [LightUnit.Lumen, LightUnit.Nit];

    /// <summary>The surface the light shines from, in square metres.</summary>
    public abstract float SurfaceArea { get; }

    /// <summary>The surface's area seen from straight ahead, in square metres.</summary>
    protected abstract float ProjectedArea { get; }

    public override float AxisCandela => Intensity * ProjectedArea;

    protected override float LumensPerStored => Maths.PI * SurfaceArea;
}
