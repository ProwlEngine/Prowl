// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Disqualifies a candidate whose visibility to (or from) <see cref="Target"/> doesn't match what was
/// asked for - a hard requirement, not a preference, so a "must be hidden" query never returns a position
/// a target can actually see. Visibility is a single physics raycast between the two points' eye heights;
/// a hit anywhere along it means blocked (not visible), matching the common "can this ray reach that point
/// unobstructed" definition of line of sight.
/// </summary>
public sealed class NavMeshLineOfSightScorer(Scene scene, Float3 target, bool wantsVisible, float eyeHeight = 1.6f) : ITacticalScorer
{
    public float Score(Float3 point)
    {
        Float3 eye = point + new Float3(0, eyeHeight, 0);
        Float3 targetEye = target + new Float3(0, eyeHeight, 0);
        Float3 toTarget = targetEye - eye;
        float distance = Float3.Length(toTarget);

        bool blocked = distance > 0.001f && scene.Physics.Raycast(eye, Float3.Normalize(toTarget), distance, out RaycastHit _);
        bool isVisible = !blocked;
        return isVisible == wantsVisible ? 1f : -1f;
    }
}
