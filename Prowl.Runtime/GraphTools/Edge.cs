// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// A wire connecting an output port on one node to an input port on another. Stored
/// by Id+name rather than direct references so reordering nodes never breaks topology.
/// </summary>
public sealed class Edge
{
    public Guid Id = Guid.NewGuid();
    public Guid SourceNodeId;
    public string SourcePortName = "";
    public Guid TargetNodeId;
    public string TargetPortName = "";
}
