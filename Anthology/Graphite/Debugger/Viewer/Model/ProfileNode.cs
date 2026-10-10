using System.Collections.Generic;

namespace Prowl.Graphite.Debugger.Viewer;

internal enum NodeKind
{
    Frame,
    View,
    Pass,
    CommandBuffer,
    Program,
    Marker,
    Draw,
}

internal sealed class ProfileNode
{
    public required string Id;
    public required NodeKind Kind;
    public required string Name;
    public ProfileNode? Parent;
    public readonly List<ProfileNode> Children = new();

    public double Milliseconds;
    public double CpuMilliseconds;

    public int ViewIndex = -1;
    public int PassIndex = -1;
    public RecordedPass? Pass;
    public RecordedCommandBuffer? CommandBuffer;

    public RecordedProgram? Program;
    public SetPipelineCommand? Pipeline;

    public int CommandIndex = -1;
    public int DrawOrdinal = -1;
    public int SubIndex;
    public RecordedCommand? Command;

    public bool IsTimed => Kind is NodeKind.Frame or NodeKind.View or NodeKind.Pass or NodeKind.CommandBuffer;

    public ProfileNode Add(ProfileNode child)
    {
        child.Parent = this;
        Children.Add(child);
        return child;
    }

    public ProfileNode? Ancestor(NodeKind kind)
    {
        for (ProfileNode? n = this; n != null; n = n.Parent)
            if (n.Kind == kind)
                return n;
        return null;
    }

    public void CollectDraws(List<ProfileNode> into)
    {
        if (Kind == NodeKind.Draw)
            into.Add(this);
        foreach (ProfileNode child in Children)
            child.CollectDraws(into);
    }

    public int CountDraws()
    {
        int count = Kind == NodeKind.Draw ? 1 : 0;
        foreach (ProfileNode child in Children)
            count += child.CountDraws();
        return count;
    }
}
