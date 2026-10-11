using System.Collections.Generic;
using System.Linq;

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

        foreach (RecordedView view in recording.Views)
        {
            ProfileNode viewNode = root.Add(new ProfileNode { Id = $"v{view.Index}", Kind = NodeKind.View, Name = view.Name, ViewIndex = view.Index });
            DeepView? deepView = deep?.Views.FirstOrDefault(v => v.Index == view.Index);

            foreach (RecordedPass pass in view.Passes)
            {
                RecordedCommandBuffer? buffer = recording.CommandBuffers.FirstOrDefault(b => b.ViewIndex == view.Index && b.PassIndex == pass.Index);
                ProfileNode passNode = viewNode.Add(new ProfileNode
                {
                    Id = $"{viewNode.Id}p{pass.Index}",
                    Kind = NodeKind.Pass,
                    Name = pass.Name,
                    ViewIndex = view.Index,
                    PassIndex = pass.Index,
                    Pass = pass,
                    CommandBuffer = buffer,
                    Milliseconds = buffer?.Milliseconds ?? 0,
                    CpuMilliseconds = pass.Stats.CpuMilliseconds,
                });

                viewNode.Milliseconds += passNode.Milliseconds;
                viewNode.CpuMilliseconds += passNode.CpuMilliseconds;

                DeepPass? deepPass = deepView?.Passes.FirstOrDefault(p => p.Index == pass.Index);
                if (deepPass != null && model.Index != null)
                    ExpandCommands(passNode, deepPass, model.Index);
            }

            root.Milliseconds += viewNode.Milliseconds;
            root.CpuMilliseconds += viewNode.CpuMilliseconds;
        }

        int outside = 0;
        foreach (RecordedCommandBuffer buffer in recording.CommandBuffers.Where(b => b.ViewIndex < 0))
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
        string boundMesh = "";
        int programCount = 0, markerCount = 0, drawOrdinal = 0;

        for (int i = 0; i < pass.Commands.Length; i++)
        {
            RecordedCommand command = pass.Commands[i];
            switch (command)
            {
                case SetPipelineCommand pipeline:
                    RecordedProgram? recorded = index.Program(pipeline.Program);
                    program = passNode.Add(new ProfileNode
                    {
                        Id = $"{passNode.Id}s{programCount++}",
                        Kind = NodeKind.Program,
                        Name = recorded?.Name ?? $"Program {pipeline.Program}",
                        Program = recorded,
                        Pipeline = pipeline,
                        CommandIndex = i,
                        Command = command,
                    });
                    ProfileNode? open = markers.Count > 0 ? markers.Last() : null;
                    if (open != null && open.CountDraws() == 0)
                    {
                        open.Parent!.Children.Remove(open);
                        program.Add(open);
                    }
                    else
                        markers.Clear();
                    break;

                case PushMarkerCommand push:
                    ProfileNode parent = markers.Count > 0 ? markers.Peek() : program ?? passNode;
                    markers.Push(parent.Add(new ProfileNode
                    {
                        Id = $"{passNode.Id}o{markerCount++}",
                        Kind = NodeKind.Marker,
                        Name = push.Name,
                        CommandIndex = i,
                        Command = command,
                    }));
                    break;

                case PopMarkerCommand:
                    if (markers.Count > 0)
                        markers.Pop();
                    break;

                case BindVertexBuffersCommand bind when bind.Changed.Length > 0:
                    boundMesh = MeshName(index.Resource(bind.Changed[0].Buffer.Resource)?.Name ?? "");
                    break;

                case DrawCommand or DrawIndexedCommand or DrawIndirectCommand or DrawIndexedIndirectCommand
                    or DrawIndirectCountCommand or DrawIndexedIndirectCountCommand or DispatchCommand or DispatchIndirectCommand:
                    ProfileNode owner = markers.Count > 0 ? markers.Peek() : program ?? passNode;
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

    public DeepView? ViewOf(ProfileNode node)
    {
        int view = node.Ancestor(NodeKind.View)?.ViewIndex ?? -1;
        return Deep?.Views.FirstOrDefault(v => v.Index == view);
    }

    public DeepPass? PassOf(ProfileNode node)
    {
        ProfileNode? pass = node.Ancestor(NodeKind.Pass);
        if (pass == null)
            return null;
        return ViewOf(pass)?.Passes.FirstOrDefault(p => p.Index == pass.PassIndex);
    }
}
