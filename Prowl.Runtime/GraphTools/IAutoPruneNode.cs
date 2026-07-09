// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Implement on a Node subclass that should be automatically removed from the graph
/// once some condition is met. The editor runs <see cref="ShouldPrune"/> on every
/// mutation (via the same per-frame pass as validation); nodes returning true are
/// deleted along with any dangling edges.
/// </summary>
/// <remarks>
/// Use for nodes that have no meaning on their own <see cref="RelayNode"/> is the
/// canonical example: a wire waypoint whose purpose ends the moment both endpoints
/// disappear. The prune pass is driven by the graph editor after every mutation so
/// the user never needs to clean up after a Delete or Cut.
/// </remarks>
public interface IAutoPruneNode
{
    /// <summary>Return true if this node has lost its reason to exist and should be
    /// removed. Called with the containing graph so implementations can inspect
    /// edges, surrounding nodes, etc.</summary>
    bool ShouldPrune(Graph graph);
}
