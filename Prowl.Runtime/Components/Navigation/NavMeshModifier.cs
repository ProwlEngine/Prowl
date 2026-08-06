// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>
/// Changes how this GameObject (and, by default, its children) contributes to navmesh bakes:
/// exclude it entirely, or override the area its geometry is stamped with. Mirrors Unity's
/// NavMeshModifier. Resolved at geometry-collection time — the nearest modifier up the
/// hierarchy wins, an object's own modifier always beats an inherited one, and a modifier
/// with <see cref="ApplyToChildren"/> off covers only its own object. Changing a modifier
/// does not rebake anything by itself; rebuild the surface (or the affected tiles) to apply.
/// One modifier per GameObject: additional NavMeshModifier components on the same object are
/// ignored (matches Unity).
/// </summary>
[AddComponentMenu("Navigation/NavMesh Modifier")]
[ComponentIcon("")] // pen ruler
public class NavMeshModifier : MonoBehaviour
{
    [Tooltip("Exclude this object's geometry from navmesh bakes entirely.")]
    public bool IgnoreFromBuild;

    [Tooltip("Stamp this object's bake geometry with Area instead of the surface's default.")]
    public bool OverrideArea;

    [Tooltip("The area applied when Override Area is on.")]
    [NavMeshArea]
    [EnableIf(nameof(OverrideArea))]
    public int Area = NavMeshAreas.Walkable;

    [Tooltip("Also apply to child objects. A child's own modifier always takes precedence.")]
    public bool ApplyToChildren = true;

    [Tooltip("Apply to bakes of every agent type. Turn off to pick specific types.")]
    public bool AffectAllAgentTypes = true;

    [Tooltip("Agent types whose bakes this modifier affects, when not affecting all.")]
    [NavMeshAgentType]
    [EnableIf(nameof(UsesExplicitAgentTypes))]
    public List<int> AffectedAgentTypeIds = [];

    private bool UsesExplicitAgentTypes => !AffectAllAgentTypes;

    /// <summary>Does this modifier apply to bakes for the given agent type?</summary>
    public bool AffectsAgentType(int agentTypeId)
        => AffectAllAgentTypes || AffectedAgentTypeIds.Contains(agentTypeId);
}
