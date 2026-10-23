// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>A titled box drawn behind a set of nodes, shared by every node graph asset. Editor only, no compiler reads it.</summary>
public sealed class GraphGroupRecord
{
    public string Id = string.Empty;
    /// <summary>The scope this sits inside, such as a state machine node, or empty for the graph itself.</summary>
    public string Owner = string.Empty;
    public string Title = "Group";
    public Float2 Position;
    public Float2 Size = new(260f, 180f);

    public GraphGroupRecord Clone() => (GraphGroupRecord)MemberwiseClone();
}

/// <summary>A note left on the canvas of any node graph asset. Editor only, no compiler reads it.</summary>
public sealed class GraphNoteRecord
{
    public string Id = string.Empty;
    /// <summary>The scope this sits inside, such as a state machine node, or empty for the graph itself.</summary>
    public string Owner = string.Empty;
    public string Text = string.Empty;
    public Float2 Position;
    public Float2 Size = new(190f, 130f);

    public GraphNoteRecord Clone() => (GraphNoteRecord)MemberwiseClone();
}
