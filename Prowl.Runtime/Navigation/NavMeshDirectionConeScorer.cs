// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>Disqualifies a candidate outside a cone of <paramref name="angle"/> total degrees, pointing
/// <paramref name="forward"/> from <paramref name="origin"/> - a flanking position query, say: candidates
/// roughly behind a target rather than in front of it. A candidate exactly at <paramref name="origin"/>
/// (no direction to measure at all) is never disqualified by this - there is nothing meaningful to reject
/// it for.</summary>
public sealed class NavMeshDirectionConeScorer(Float3 origin, Float3 forward, float angle) : ITacticalScorer
{
    public float Score(Float3 point)
    {
        Float3 toPoint = point - origin;
        if (Float3.Length(toPoint) < 0.0001f) return 1f;

        float cos = Maths.Clamp(Float3.Dot(Float3.Normalize(forward), Float3.Normalize(toPoint)), -1f, 1f);
        float angleToPoint = MathF.Acos(cos) * (180f / MathF.PI);
        return angleToPoint <= angle * 0.5f ? 1f : -1f;
    }
}
