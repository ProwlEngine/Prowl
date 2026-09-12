// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;

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
    [SerializeField] private bool ignoreFromBuild;

    [Tooltip("Stamp this object's bake geometry with Area instead of the surface's default.")]
    [SerializeField] private bool overrideArea;

    [Tooltip("The area applied when Override Area is on.")]
    [NavMeshArea]
    [EnableIf(nameof(OverrideArea))]
    [SerializeField] private int area = NavMeshAreas.Walkable;

    [Tooltip("Also apply to child objects. A child's own modifier always takes precedence.")]
    [SerializeField] private bool applyToChildren = true;

    [Tooltip("Apply to bakes of every agent type. Turn off to pick specific types.")]
    [SerializeField] private bool affectAllAgentTypes = true;

    [Tooltip("Agent types whose bakes this modifier affects, when not affecting all.")]
    [NavMeshAgentType]
    [EnableIf(nameof(UsesExplicitAgentTypes))]
    [SerializeField] private List<int> affectedAgentTypeIds = [];

    public bool IgnoreFromBuild { get => ignoreFromBuild; set => ignoreFromBuild = value; }
    public bool OverrideArea { get => overrideArea; set => overrideArea = value; }
    public int Area { get => area; set => area = value; }
    public bool ApplyToChildren { get => applyToChildren; set => applyToChildren = value; }
    public bool AffectAllAgentTypes { get => affectAllAgentTypes; set => affectAllAgentTypes = value; }
    public List<int> AffectedAgentTypeIds { get => affectedAgentTypeIds; set => affectedAgentTypeIds = value; }

    private bool UsesExplicitAgentTypes => !AffectAllAgentTypes;

    /// <summary>Does this modifier apply to bakes for the given agent type?</summary>
    public bool AffectsAgentType(int agentTypeId)
        => AffectAllAgentTypes || AffectedAgentTypeIds.Contains(agentTypeId);
}
