using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.Debugging;

using Prowl.Graphite.Debugger.Trace;

using Color = System.Drawing.Color;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed partial class ProfilerView
{
    private sealed class BoundState
    {
        public readonly SortedDictionary<uint, RecordedVertexBinding> Vertex = new();
        public BindIndexBufferCommand? Index;
        public readonly SortedDictionary<string, RecordedProperty> Properties = new();
        public SetPipelineCommand? Pipeline;
        public SetFramebufferCommand? Framebuffer;
    }

    private static BoundState Fold(DeepPass pass, int upTo)
    {
        BoundState state = new();
        for (int i = 0; i <= upTo && i < pass.Commands.Length; i++)
        {
            switch (pass.Commands[i])
            {
                case SetFramebufferCommand fb: state.Framebuffer = fb; break;
                case SetPipelineCommand p: state.Pipeline = p; break;
                case BindVertexBuffersCommand b:
                    foreach (RecordedVertexBinding v in b.Changed)
                        state.Vertex[v.Slot] = v;
                    break;
                case BindIndexBufferCommand ib: state.Index = ib; break;
                case SetPropertiesCommand sp:
                    foreach (string removed in sp.Removed)
                        state.Properties.Remove(removed);
                    foreach (RecordedProperty prop in sp.Changed)
                        state.Properties[prop.Name] = prop;
                    break;
            }
        }
        return state;
    }

    private static (string Shader, string ShaderPass, string[] Keywords) ParseProgramName(string name)
    {
        int bracket = name.IndexOf(" [", StringComparison.Ordinal);
        string head = bracket >= 0 ? name[..bracket] : name;
        string[] keywords = bracket >= 0 ? name[(bracket + 2)..].TrimEnd(']').Split(", ", StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        int slash = head.LastIndexOf('/');
        return slash > 0 ? (head[..slash], head[(slash + 1)..], keywords) : (head, "0", keywords);
    }

    private static string UniformValue(RecordedProperty prop)
    {
        byte[] bytes = prop.Uniform.Items.ToArray();
        bool wide = prop.UniformType is >= UniformScalarType.Double1 and <= UniformScalarType.Double4 or UniformScalarType.Double4x4;
        int size = wide ? 8 : 4;
        int count = bytes.Length / size;
        if (count == 0)
            return "-";
        IEnumerable<string> values = Enumerable.Range(0, Math.Min(4, count)).Select(i => prop.UniformType switch
        {
            >= UniformScalarType.Int1 and <= UniformScalarType.Int4 => BitConverter.ToInt32(bytes, i * 4).ToString(),
            >= UniformScalarType.UInt1 and <= UniformScalarType.UInt4 => BitConverter.ToUInt32(bytes, i * 4).ToString(),
            _ when wide => Style.F((float)BitConverter.ToDouble(bytes, i * 8), "0.###"),
            _ => Style.F(BitConverter.ToSingle(bytes, i * 4), "0.###"),
        });
        string joined = string.Join(", ", values);
        return count == 1 ? joined : count > 4 ? $"[{joined}, ...]" : $"({joined})";
    }

    private void ProgramTab(FrameModel model, ProfileNode node, string tab)
    {
        RecordedProgram? program = node.Program;
        SetPipelineCommand pipeline = node.Pipeline!;
        if (program == null)
        {
            KvSection("Program", 420, new Kv?[] { new("Program key", pipeline.Program.ToString()), new("Recorded", "Missing from capture", Style.Bad) });
            return;
        }

        (string shader, string shaderPass, string[] keywords) = ParseProgramName(program.Name);
        string graphPass = node.Ancestor(NodeKind.Pass)!.Name;

        switch (tab)
        {
            case "Program":
                {
                    int draws = node.CountDraws();
                    int markers = node.Children.Count(c => c.Kind == NodeKind.Marker);
                    HeroStrip(new Stat[]
                    {
                        new("Draws", draws.ToString(), "in this pass"),
                        new("Renderers", markers.ToString(), $"{node.Children.Count - markers} loose draws"),
                        new("Kind", program.IsCompute ? "Compute" : "Graphics", shaderPass),
                        new("SPIR-V", Style.Bytes(program.Stages.Sum(s => (double)s.Code.Length)), $"{program.Stages.Length} stages"),
                    });
                    Collapsible("Program details", () =>
                    {
                        KvSection("Program", 420, new Kv?[]
                        {
                            new("Name", program.Name),
                            new("Shader", shader),
                            new("Shader pass", shaderPass),
                            new("Graph pass", graphPass),
                            new("Program key", program.Key.ToString()),
                            new("Pipeline id", pipeline.PipelineId.ToString()),
                            new("Stages", string.Join(" / ", program.Stages.Select(s => $"{s.Stage} ({s.EntryPoint})"))),
                            program.IsCompute ? new("Thread group", $"{program.ThreadGroupX} x {program.ThreadGroupY} x {program.ThreadGroupZ}") : new("Topology", pipeline.Topology?.ToString() ?? "-"),
                            new("Outputs", pipeline.Outputs is { } outputs ? Describe.Outputs(outputs) : "-"),
                            new("Specialization", program.Specializations.Length > 0 ? string.Join(", ", program.Specializations.Select(c => $"{c.Id}={c.Value}")) : "-"),
                        });
                    });
                    Collapsible("Vertex inputs", () => TableSection("Vertex inputs", 420, "",
                        new[] { "Semantic", "Location", "Format", "Offset" }, new[] { 1.2f, 0.7f, 1.4f, 0.6f },
                        program.VertexLayouts.Length > 0
                            ? program.VertexLayouts.SelectMany(l => (l.Elements ?? []).Select(e => R(Describe.Name(e.Name), l.Location.ToString(), e.Format.ToString(), e.Offset.ToString()))).ToList()
                            : new List<Row> { R("SV_VertexID", "-", "uint (system)", "-") }));
                    break;
                }

            case "Bindings":
                TableSection("Resource layouts", 0, "Bound resources are per draw: open a draw",
                    new[] { "Set", "Binding", "Name", "Kind", "Stages" }, new[] { 0.5f, 0.6f, 1.4f, 1.4f, 0.8f },
                    Describe.Elements(program.Layouts).Select(l => R(l.Set.ToString(), l.Element.BindingIndex.ToString(), Describe.Name(l.Element.Name), l.Element.Kind.ToString(), l.Element.Stages.ToString())).ToList());
                break;

            default:
                StateSections(program, pipeline);
                ChipsSection("Bound variant keywords", 0, "From the program name ShaderDef sets",
                    keywords.Length > 0
                        ? keywords.Select(k => new Chip(k, "", Style.Alpha(Style.Ink, 10), Style.Border, Style.InkSoft)).ToList()
                        : new List<Chip> { new("No keywords", "", Style.Alpha(Style.Ink, 10), Style.Border, Style.InkDim) });
                KvSection("Variant", 420, new Kv?[]
                {
                    new("Shader", shader),
                    new("Shader pass", shaderPass),
                    new("Variant hash", Style.Hex32((ulong)(uint)string.Join(",", keywords).GetHashCode(StringComparison.Ordinal))),
                    new("Axes", keywords.Length.ToString()),
                });
                break;
        }
    }

    private void StateSections(RecordedProgram program, SetPipelineCommand pipeline)
    {
        if (program.Rasterizer is { } r)
            KvSection("Rasterizer", 320, new Kv?[]
            {
                new("Cull mode", r.CullMode.ToString()),
                new("Front face", r.FrontFace.ToString()),
                new("Depth clip", r.DepthClipEnabled ? "On" : "Off"),
                new("Depth bias", r.DepthBiasEnabled ? "On" : "Off"),
                new("Depth bias (const / slope / clamp)", $"{Style.F(r.DepthBiasConstantFactor, "0.##")} / {Style.F(r.DepthBiasSlopeFactor, "0.##")} / {Style.F(r.DepthBiasClamp, "0.##")}"),
            });

        if (program.DepthStencil is { } d)
            KvSection("Depth / stencil", 320, new Kv?[]
            {
                new("Depth test", d.DepthTestEnabled ? "On" : "Off", d.DepthTestEnabled ? Style.Good : Style.InkDim),
                new("Depth write", d.DepthWriteEnabled ? "On" : "Off", d.DepthWriteEnabled ? Style.Good : Style.InkDim),
                new("Compare", d.DepthComparison.ToString()),
                new("Stencil", d.StencilTestEnabled ? "On" : "Off"),
                new("Reference", Style.Hex32(d.StencilReference, 2)),
                new("Read / write mask", $"{Style.Hex32(d.StencilReadMask, 2)} / {Style.Hex32(d.StencilWriteMask, 2)}"),
                new("Front (fail / pass / depth-fail / func)", Describe.Stencil(d.StencilFront)),
                new("Back (fail / pass / depth-fail / func)", Describe.Stencil(d.StencilBack)),
            });

        KvSection("Input assembly", 320, new Kv?[]
        {
            new("Topology", pipeline.Topology?.ToString() ?? "-"),
            new("Vertex attributes", Describe.Elements(program.VertexLayouts).Count().ToString()),
            new("Dynamic state", "Viewport / Scissor / StencilRef"),
        });

        List<Row> blend = program.Blend is { } b
            ? (b.AttachmentStates ?? []).Select((t, i) => R($"RT{i}", t.BlendEnabled ? "On" : "Off", Describe.Color(t), Describe.Alpha(t), t.ColorWriteMask.ToString())).ToList()
            : new List<Row>();
        TableSection("Blend", 0, "",
            new[] { "Target", "Enabled", "Color (src / op / dst)", "Alpha (src / op / dst)", "Write mask" }, new[] { 0.5f, 0.6f, 2f, 2f, 0.8f },
            blend.Count > 0 ? blend : new List<Row> { R("-", "No color targets") });
    }

    private void MarkerTab(FrameModel model, ProfileNode node, string tab)
    {
        RendererInfo? info = _source.Renderer(node.Name);
        DeepPass pass = model.PassOf(node)!;
        List<ProfileNode> draws = new();
        node.CollectDraws(draws);
        BoundState bound = Fold(pass, draws.Count > 0 ? draws[0].CommandIndex : node.CommandIndex);
        RecordedResource? vb = bound.Vertex.Count > 0 ? model.Index!.Resource(bound.Vertex.First().Value.Buffer.Resource) : null;
        RecordedResource? ib = bound.Index != null ? model.Index!.Resource(bound.Index.Buffer.Resource) : null;
        string mesh = FrameModel.MeshName(vb?.Name ?? "-");
        ProfileNode? program = node.Ancestor(NodeKind.Program);

        switch (tab)
        {
            case "Overview":
                HeroStrip(new Stat[]
                {
                    new("Mesh", mesh, $"{draws.Count} submeshes"),
                    new("Triangles", bound.Index != null ? Style.Int(bound.Index.IndexCount / 3.0) : "-", "first submesh"),
                    new("Draws", draws.Count.ToString(), "this program"),
                    new("Screen coverage", info != null ? Style.F(info.ScreenCoverage, "0.00") + "%" : "-", info != null ? Style.F(info.CameraDistance, "0.0") + " m from camera" : "no engine data"),
                });
                Collapsible("Renderer", () => KvSection("Renderer", 420, new Kv?[]
                {
                    new("Name", node.Name),
                    new("Source", "Debug marker (PushMarker / PopMarker)"),
                    info != null ? new("Instance ID", info.InstanceId.ToString()) : null,
                    info != null ? new("Renderer type", info.RendererType) : null,
                    info != null ? new("Layer", info.Layer) : null,
                    info != null ? new("Static", info.StaticFlags) : null,
                    info != null ? new("Cast shadows", info.CastShadows ? "On" : "Off") : null,
                    info != null ? new("Receive shadows", info.ReceiveShadows ? "On" : "Off") : null,
                    info != null ? new("Light probes", info.LightProbes) : null,
                    info != null ? new("Lightmap index", info.LightmapIndex >= 0 ? info.LightmapIndex.ToString() : "-") : null,
                    info != null ? new("Sorting order", info.SortingOrder.ToString()) : null,
                }));
                if (info != null)
                    Collapsible("Transform and bounds", () => KvSection("Transform & bounds", 420, new Kv?[]
                    {
                        new("Position", $"{Style.F(info.Position.X, "0.00")}, {Style.F(info.Position.Y, "0.00")}, {Style.F(info.Position.Z, "0.00")}"),
                        new("Rotation", $"{Style.F(info.Rotation.X, "0.0")}, {Style.F(info.Rotation.Y, "0.0")}, {Style.F(info.Rotation.Z, "0.0")}"),
                        new("Scale", $"{Style.F(info.Scale.X, "0.##")}, {Style.F(info.Scale.Y, "0.##")}, {Style.F(info.Scale.Z, "0.##")}"),
                    }));
                break;

            case "Material":
                if (info == null)
                {
                    KvSection("Material", 420, new Kv?[] { new("Material", "No engine data for this marker", Style.InkDim) });
                    break;
                }
                MaterialInfo mat = info.Material;
                KvSection("Material", 420, new Kv?[]
                {
                    new("Name", mat.Name),
                    new("Shader", program != null ? ParseProgramName(program.Name).Shader : "-"),
                    new("Render queue", mat.RenderQueue),
                    new("GPU instancing", mat.Instancing ? "On" : "Off"),
                    new("Double sided", mat.DoubleSided ? "On" : "Off"),
                    new("Material CB", Style.Bytes(mat.ConstantBufferBytes)),
                    new("Keywords", program != null ? string.Join(" ", ParseProgramName(program.Name).Keywords) : "-"),
                });
                TableSection("Properties", 420, "",
                    new[] { "Property", "Type", "Value" }, new[] { 1.4f, 0.8f, 1.6f },
                    mat.Properties.Select(p => R(p.Name, p.Type, p.Value)).ToList());
                TableSection("Textures", 0, "",
                    new[] { "Slot", "Texture", "Size", "Format", "Mips", "Memory" }, new[] { 1f, 1.4f, 0.7f, 0.6f, 0.5f, 0.7f },
                    mat.Textures.Select(t => R(t.Slot, t.Texture, t.Size.ToString(), t.Format, t.Mips.ToString(), Style.Bytes(t.Bytes))).ToList());
                break;

            default:
                {
                    var vbBytes = vb != null ? model.Index!.Contents(vb.Id, pass) : null;
                    var ibBytes = ib != null ? model.Index!.Contents(ib.Id, pass) : null;
                    int vertexCount = vbBytes?.Data.Length / 32 ?? 0;
                    int indexCount = (int)(bound.Index?.IndexCount ?? 0);
                    KvSection("Geometry", 420, new Kv?[]
                    {
                    new("Mesh", mesh),
                    new("Vertices", Style.Int(vertexCount)),
                    new("Indices", Style.Int(indexCount)),
                    new("Triangles", Style.Int(indexCount / 3)),
                    new("Submeshes", draws.Count.ToString()),
                    new("Vertex stride", bound.Vertex.Count > 0 ? bound.Vertex.First().Value.Stride + " B" : "-"),
                    new("Index format", bound.Index?.Format.ToString() ?? "-"),
                    new("VB size", Style.Bytes(vb?.Buffer?.SizeInBytes ?? 0)),
                    new("IB size", Style.Bytes(ib?.Buffer?.SizeInBytes ?? 0)),
                    });

                    const string context = "marker:Mesh";
                    List<ViewerItem> items = new();
                    List<Row> rows = new();
                    foreach ((RecordedResource? res, string kind, string prefix) in new[] { (vb, "Vertex", "vb:"), (ib, "Index", "ib:") })
                    {
                        if (res == null)
                            continue;
                        string key = prefix + res.Id.Value;
                        items.Add(new ViewerItem(key, res.Name));
                        rows.Add(new Row
                        {
                            Cells = new[] { kind, res.Name, Style.Bytes(res.Buffer?.SizeInBytes ?? 0) },
                            Pick = () => _viewerSelection[context] = key,
                        });
                        Mark(rows[^1], 1, key);
                    }
                    if (items.Count > 0)
                    {
                        string selectedKey = SelectedKey(context, items);
                        for (int i = 0; i < rows.Count; i++)
                            rows[i].Selected = items[i].Key == selectedKey;
                    }
                    ResourceTable(model, node, context, "Buffers", "Click to preview / drag the mesh to orbit",
                        new[] { "Kind", "Buffer", "Size" }, new[] { 0.8f, 1.8f, 0.8f }, rows, items);
                    break;
                }
        }
    }

    private void DrawTab(FrameModel model, ProfileNode node, string tab)
    {
        DeepIndex index = model.Index!;
        DeepPass pass = model.PassOf(node)!;
        DeepView view = model.ViewOf(node)!;
        ProfileNode passNode = node.Ancestor(NodeKind.Pass)!;
        ProfileNode? program = node.Ancestor(NodeKind.Program);
        ProfileNode? marker = node.Ancestor(NodeKind.Marker);
        List<ProfileNode> passDraws = new();
        passNode.CollectDraws(passDraws);
        int position = passDraws.IndexOf(node);
        BoundState bound = Fold(pass, node.CommandIndex);
        RecordedResource? vb = bound.Vertex.Count > 0 ? index.Resource(bound.Vertex.First().Value.Buffer.Resource) : null;
        RecordedResource? ib = bound.Index != null ? index.Resource(bound.Index.Buffer.Resource) : null;
        string mesh = FrameModel.MeshName(vb?.Name ?? "-");

        switch (tab)
        {
            case "Overview":
                {
                    RecordedCommand cmd = node.Command!;
                    uint instances = cmd switch { DrawIndexedCommand d => d.InstanceCount, DrawCommand d => d.InstanceCount, _ => 1 };
                    uint indexCount = cmd switch { DrawIndexedCommand d => d.IndexCount, DrawCommand d => d.VertexCount, _ => bound.Index?.IndexCount ?? 0 };
                    HeroStrip(new Stat[]
                    {
                        new("Command", CommandName(cmd), $"EID {node.CommandIndex}"),
                        new("Index count", Style.Int(indexCount), bound.Index != null ? "indexed" : "vertices"),
                        new("Instances", instances.ToString(), $"{Style.Int(indexCount / 3.0 * instances)} primitives"),
                        new("Mesh", mesh, marker?.Name ?? "no marker"),
                    });
                    Collapsible("Draw call", () => KvSection("Draw call", 0, new Kv?[]
                    {
                        new("Replay event index", node.DrawOrdinal.ToString()),
                        new("Arguments", CommandArgs(index, cmd)),
                        cmd is DrawIndexedCommand di ? new("First index", Style.Int(di.FirstIndex)) : null,
                        cmd is DrawIndexedCommand dv ? new("Vertex offset", dv.VertexOffset.ToString()) : null,
                        new("Topology", bound.Pipeline?.Topology?.ToString() ?? "-"),
                        new("Renderer (marker)", marker?.Name ?? "- (no marker)"),
                        new("Submesh", node.SubIndex.ToString()),
                        new("Program", program?.Name ?? "-"),
                        new("Pipeline id", program?.Pipeline?.PipelineId.ToString() ?? "-"),
                        new("Index in pass", $"{position + 1} of {passDraws.Count}"),
                    }));
                    break;
                }

            case "Geometry":
                {
                    const string context = "draw:Geometry";
                    List<Row> rows = new();
                    List<ViewerItem> items = new();
                    foreach (RecordedVertexBinding binding in bound.Vertex.Values)
                    {
                        RecordedResource? r = index.Resource(binding.Buffer.Resource);
                        string key = "vb:" + binding.Buffer.Resource.Value;
                        items.Add(new ViewerItem(key, r?.Name ?? key));
                        string layout = program?.Program != null ? string.Join(" ", Describe.Elements(program.Program.VertexLayouts).Select(e => Describe.Name(e.Name)[..1] + Describe.Components(e.Format))) : "";
                        rows.Add(new Row
                        {
                            Cells = new[] { $"VB {binding.Slot}", r?.Name ?? "-", binding.Offset.ToString(), $"{binding.Stride} B ({layout})", Style.Bytes(r?.Buffer?.SizeInBytes ?? 0) },
                            Pick = () => _viewerSelection[context] = key,
                        });
                        Mark(rows[^1], 1, key);
                    }
                    if (bound.Index != null)
                    {
                        string key = "ib:" + bound.Index.Buffer.Resource.Value;
                        items.Add(new ViewerItem(key, ib?.Name ?? key));
                        uint first = node.Command is DrawIndexedCommand d ? d.FirstIndex : 0;
                        rows.Add(new Row
                        {
                            Cells = new[] { "IB", ib?.Name ?? "-", (first * Describe.IndexSize(bound.Index.Format)).ToString(), bound.Index.Format.ToString(), Style.Bytes(ib?.Buffer?.SizeInBytes ?? 0) },
                            Pick = () => _viewerSelection[context] = key,
                        });
                        Mark(rows[^1], 1, key);
                    }
                    if (items.Count > 0)
                    {
                        string selectedKey = SelectedKey(context, items);
                        for (int i = 0; i < rows.Count; i++)
                            rows[i].Selected = items[i].Key == selectedKey;
                    }
                    ResourceTable(model, node, context, "Bound geometry", "Click to preview / layout from the program vertex layouts",
                        new[] { "Slot", "Buffer", "Offset", "Stride / format", "Size" }, new[] { 0.6f, 1.6f, 0.6f, 1.2f, 0.7f }, rows, items);
                    break;
                }

            case "Targets":
                TargetsTab(model, node, view, pass, passDraws, position);
                break;

            default:
                {
                    RecordedProgram? recorded = program?.Program;
                    const string context = "draw:State";
                    string? firstBuffer = bound.Properties.Values.FirstOrDefault(p => p.Kind is PropertyKind.Buffer or PropertyKind.UniformBuffer) is { } fb ? "buf:" + fb.Resource.Resource.Value : null;
                    string? currentBuffer = _viewerSelection.TryGetValue(context, out string? chosen) ? chosen : firstBuffer;
                    List<Row> resources = bound.Properties.Values
                        .Where(p => p.Kind != PropertyKind.Uniform)
                        .Select(p =>
                        {
                            string? key = p.Kind is PropertyKind.Buffer or PropertyKind.UniformBuffer ? "buf:" + p.Resource.Resource.Value : null;
                            return new Row
                            {
                                Cells = new[] { p.Name, p.Kind.ToString(), index.Resource(p.Resource.Resource)?.Name is { } n ? $"{n} v{p.Resource.Version}" : "-" },
                                Pick = key != null ? () => _viewerSelection[context] = key : null,
                                Selected = key != null && key == currentBuffer,
                            };
                        })
                        .ToList();
                    List<ViewerItem> buffers = bound.Properties.Values
                        .Where(p => p.Kind is PropertyKind.Buffer or PropertyKind.UniformBuffer)
                        .Select(p => new ViewerItem("buf:" + p.Resource.Resource.Value, index.Resource(p.Resource.Resource)?.Name ?? p.Name))
                        .DistinctBy(i => i.Key)
                        .ToList();
                    ResourceTable(model, node, context, "Bound resources", "Folded from SetProperties / click a buffer to preview",
                        new[] { "Name", "Kind", "Resource" }, new[] { 1.4f, 1f, 1.8f },
                        resources.Count > 0 ? resources : new List<Row> { R("-", "None") }, buffers);

                    List<Row> uniforms = bound.Properties.Values
                        .Where(p => p.Kind == PropertyKind.Uniform)
                        .Select(p => R(p.Name, p.UniformType.ToString(), UniformValue(p)))
                        .ToList();
                    TableSection("Uniform properties", 420, "Packed into the material constant buffer",
                        new[] { "Name", "Type", "Value" }, new[] { 1.4f, 0.8f, 2f },
                        uniforms.Count > 0 ? uniforms : new List<Row> { R("-", "None") });

                    if (recorded != null)
                    {
                        var ds = recorded.DepthStencil;
                        var rs = recorded.Rasterizer;
                        BlendAttachmentDescription? blend = recorded.Blend?.AttachmentStates is { Length: > 0 } b ? b[0] : null;
                        KvSection("Pipeline state (inherited)", 420, new Kv?[]
                        {
                        new("Cull", rs?.CullMode.ToString() ?? "-"),
                        new("Depth", ds != null ? $"{(ds.Value.DepthTestEnabled ? "On" : "Off")} / write {(ds.Value.DepthWriteEnabled ? "On" : "Off")} / {ds.Value.DepthComparison}" : "-"),
                        new("Stencil", ds != null ? (ds.Value.StencilTestEnabled ? $"On / ref {Style.Hex32(ds.Value.StencilReference, 2)}" : "Off") : "-"),
                        new("Blend RT0", blend is { } rt0 ? $"{(rt0.BlendEnabled ? "On" : "Off")} / {Describe.Color(rt0)}" : "No color target"),
                        new("Topology", bound.Pipeline?.Topology?.ToString() ?? "-"),
                        new("Depth bias", rs is { DepthBiasEnabled: true } ? $"{Style.F(rs.Value.DepthBiasConstantFactor, "0.##")} / {Style.F(rs.Value.DepthBiasSlopeFactor, "0.##")}" : "Off"),
                        });
                    }

                    break;
                }
        }
    }
}
