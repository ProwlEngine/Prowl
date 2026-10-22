using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed class FrameModel
{
    public required Recording Recording;
    public required FrameTiming Timing;
    public DeepRecording? Deep;
    public DeepIndex? Index;
    public required ProfileNode Root;
    public readonly Dictionary<string, ProfileNode> ById = new();

    public ulong Number => Recording.ExecutionId;

    public static FrameModel Build(Recording recording, FrameTiming timing, DeepRecording? deep)
    {
        ProfileNode root = new() { Id = "f", Kind = NodeKind.Frame, Name = $"Frame #{recording.ExecutionId}" };
        FrameModel model = new() { Recording = recording, Timing = timing, Deep = deep, Index = deep != null ? new DeepIndex(deep) : null, Root = root };

        Dictionary<int, ProfileNode> instances = new();
        foreach (RecordedGroup instance in recording.Groups)
        {
            ProfileNode parentNode = instance.ParentIndex < 0 ? root : instances.GetValueOrDefault(instance.ParentIndex, root);
            ProfileNode instanceNode = parentNode.Add(new ProfileNode
            {
                Id = $"i{instance.Index}",
                Kind = NodeKind.Group,
                Name = instance.Name,
                GroupIndex = instance.Index,
                Group = instance,
            });
            instances[instance.Index] = instanceNode;
            DeepGroup? deepInstance = deep?.Groups.FirstOrDefault(i => i.Index == instance.Index);

            foreach (RecordedPass pass in instance.Passes)
            {
                RecordedCommandBuffer? buffer = recording.CommandBuffers.FirstOrDefault(b => b.GroupIndex == instance.Index && b.PassIndex == pass.Index);
                ProfileNode passNode = instanceNode.Add(new ProfileNode
                {
                    Id = $"{instanceNode.Id}p{pass.Index}",
                    Kind = NodeKind.Pass,
                    Name = pass.Name,
                    GroupIndex = instance.Index,
                    PassIndex = pass.Index,
                    Pass = pass,
                    CommandBuffer = buffer,
                    Milliseconds = buffer?.Milliseconds ?? 0,
                    CpuMilliseconds = pass.Stats.CpuMilliseconds,
                });

                for (ProfileNode? n = instanceNode; n != null; n = n.Parent)
                {
                    n.Milliseconds += passNode.Milliseconds;
                    n.CpuMilliseconds += passNode.CpuMilliseconds;
                }

                DeepPass? deepPass = deepInstance?.Passes.FirstOrDefault(p => p.Index == pass.Index);
                if (deepPass != null && model.Index != null)
                    ExpandCommands(passNode, deepPass, model.Index);
            }
        }

        int outside = 0;
        foreach (RecordedCommandBuffer buffer in recording.CommandBuffers.Where(b => b.GroupIndex < 0 && !b.Name.StartsWith(CaptureCopy.BufferPrefix, System.StringComparison.Ordinal)))
        {
            ProfileNode node = root.Add(new ProfileNode
            {
                Id = $"c{outside++}",
                Kind = NodeKind.CommandBuffer,
                Name = buffer.Name,
                CommandBuffer = buffer,
                Milliseconds = buffer.Milliseconds ?? 0,
            });
            root.Milliseconds += node.Milliseconds;
        }

        Register(model.ById, root);
        return model;
    }

    private static void ExpandCommands(ProfileNode passNode, DeepPass pass, DeepIndex index)
    {
        ProfileNode? program = null;
        Stack<ProfileNode> markers = new();
        bool passGroup = false;
        string boundMesh = "";
        int programCount = 0, markerCount = 0, drawOrdinal = 0;

        for (int i = 0; i < pass.Commands.Length; i++)
        {
            RecordedCommand command = pass.Commands[i];
            ProfileNode scope = markers.Count > 0 ? markers.Peek() : passNode;
            switch (command)
            {
                case SetPipelineCommand pipeline:
                    if (scope.Kind == NodeKind.Marker && scope.Parent is { Kind: NodeKind.Program, Parent: { } above } && scope.CountDraws() == 0)
                    {
                        scope.Parent.Children.Remove(scope);
                        above.Add(scope);
                    }
                    RecordedProgram? recorded = index.Program(pipeline.Program);
                    program = scope.Add(new ProfileNode
                    {
                        Id = $"{passNode.Id}s{programCount++}",
                        Kind = NodeKind.Program,
                        Name = recorded?.Name ?? $"Program {pipeline.Program}",
                        Program = recorded,
                        Pipeline = pipeline,
                        CommandIndex = i,
                        Command = command,
                    });
                    break;

                case PushMarkerCommand push when markers.Count == 0 && !passGroup && push.Meta == null && push.Name == pass.Name:
                    passGroup = true;
                    break;

                case PushMarkerCommand push:
                    ProfileNode parent = program != null && program.Parent == scope ? program : scope;
                    markers.Push(parent.Add(new ProfileNode
                    {
                        Id = $"{passNode.Id}o{markerCount++}",
                        Kind = NodeKind.Marker,
                        Name = push.Name,
                        CommandIndex = i,
                        Command = command,
                        Meta = push.Meta,
                    }));
                    break;

                case PopMarkerCommand:
                    if (markers.Count > 0)
                        markers.Pop();
                    else
                        passGroup = false;
                    break;

                case BindVertexBuffersCommand bind when bind.Changed.Length > 0:
                    boundMesh = MeshName(index.Resource(bind.Changed[0].Buffer.Resource)?.Name ?? "");
                    break;

                case DrawCommand or DrawIndexedCommand or DrawIndirectCommand or DrawIndexedIndirectCommand
                    or DrawIndirectCountCommand or DrawIndexedIndirectCountCommand or DispatchCommand or DispatchIndirectCommand:
                    ProfileNode owner = program != null && program.Parent == scope ? program : scope;
                    bool dispatch = command is DispatchCommand or DispatchIndirectCommand;
                    int sub = owner.Children.Count(c => c.Kind == NodeKind.Draw);
                    string name = dispatch ? $"Dispatch {sub}"
                        : owner.Kind == NodeKind.Marker ? $"Draw {sub} / SubMesh {sub}"
                        : $"Draw {sub} / {boundMesh}";
                    owner.Add(new ProfileNode
                    {
                        Id = $"{passNode.Id}d{drawOrdinal}",
                        Kind = NodeKind.Draw,
                        Name = name,
                        CommandIndex = i,
                        DrawOrdinal = drawOrdinal++,
                        SubIndex = sub,
                        Command = command,
                    });
                    break;
            }
        }
    }

    public static string MeshName(string bufferName)
    {
        if (bufferName.EndsWith("_VB") || bufferName.EndsWith("_IB"))
            return bufferName[..^3];
        return bufferName;
    }

    private static void Register(Dictionary<string, ProfileNode> into, ProfileNode node)
    {
        into[node.Id] = node;
        foreach (ProfileNode child in node.Children)
            Register(into, child);
    }

    public IEnumerable<ProfileNode> Groups => ById.Values.Where(n => n.Kind == NodeKind.Group).OrderBy(n => n.GroupIndex);

    public DeepGroup? GroupOf(ProfileNode node)
    {
        int instance = node.Ancestor(NodeKind.Group)?.GroupIndex ?? -1;
        return Deep?.Groups.FirstOrDefault(i => i.Index == instance);
    }

    public DeepPass? PassOf(ProfileNode node)
    {
        ProfileNode? pass = node.Ancestor(NodeKind.Pass);
        if (pass == null)
            return null;
        return GroupOf(pass)?.Passes.FirstOrDefault(p => p.Index == pass.PassIndex);
    }
}
