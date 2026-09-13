// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Changes how one GameObject - and, by default, its descendants - contributes to a bake. Read
/// directly by <see cref="NavMeshGeometryCollector"/> at collection time; nothing here registers with
/// anything or survives past the bake that read it; a moved or toggled modifier only ever takes effect
/// on the next bake, the same as moving the geometry itself would.
/// </summary>
[AddComponentMenu("Navigation/NavMesh Modifier")]
public sealed class NavMeshModifier : MonoBehaviour
{
    /// <summary>Excludes this object's geometry from a bake entirely. It still renders and collides -
    /// this only changes what a navmesh bake sees.</summary>
    public bool IgnoreFromBuild;

    /// <summary>When true, this object's geometry is stamped with <see cref="Area"/> instead of the
    /// bake's default walkable area.</summary>
    public bool OverrideArea;

    /// <summary>The area (see <see cref="NavMeshAreas"/>) to stamp when <see cref="OverrideArea"/> is set.</summary>
    public int Area = NavMeshAreas.Walkable;

    /// <summary>Whether this modifier also applies to descendants that don't have their own. A
    /// descendant's own modifier always wins over an ancestor's, regardless of this flag.</summary>
    public bool ApplyToChildren = true;

    /// <summary>When true, this modifier applies to every agent type's bake. When false, only the
    /// types listed in <see cref="AgentTypeIds"/>; for any other type this modifier is transparent,
    /// as if it were not there at all.</summary>
    public bool AffectAllAgentTypes = true;

    /// <summary>Agent types this modifier affects when <see cref="AffectAllAgentTypes"/> is false.</summary>
    public List<int> AgentTypeIds = [];

    /// <summary>Whether this modifier applies to a bake for the given agent type.</summary>
    public bool AppliesTo(int agentTypeId) => AffectAllAgentTypes || AgentTypeIds.Contains(agentTypeId);
}
