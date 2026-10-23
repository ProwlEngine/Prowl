using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;

using Prowl.OrigamiUI;
using OrigamiGroup = Prowl.OrigamiUI.GraphGroup;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed partial class ProfilerView
{
    private const float FramePassWidth = 240f;
    private const float AliasWidth = 200f;
    private const int BundleMin = 3;
    private const float KindPad = 28f;
    private const float KindTitle = 40f;
    private const float ColumnGap = 90f;
    private const float RowGap = 28f;
    private const float PillGap = 7f;
    private const float HugGap = 9f;
    private const float LaneStep = 150f;
    private const float MaxSetback = 100f;
    private const float KindGap = 240f;
    private const float StackGap = 90f;
    private const float FrameGraphHeight = 720f;
    private const int SharedThreshold = 3;
    private const int FoldLimit = 4;
    private const int AliasSpan = 3;
    private const int AliasFan = 4;
    private const float PillWidth = 44f;
    private const float MinRun = 40f;
    private const string FbKey = "*fb";

    private string? _frameGraphSelection;

    private sealed class KindPass
    {
        public required string Name;
        public required int Occurrence;
        public required DeepPass Pass;
        public required int GroupIndex;
        public int First;
        public int Level;
        public int Count;
        public int Merged = 1;
        public bool HasOut;
        public HashSet<int> Seen = new();
        public List<RecordedAccess> Accesses = new();
        public readonly Dictionary<(string Key, bool Output), HashSet<string>> RawNames = new();
    }

    private sealed class KindResource
    {
        public required string Key;
        public required string Label;
        public GraphResourceOrigin Origin;
        public GraphResourceKind Kind;
        public readonly HashSet<string> Members = new();
        public readonly HashSet<TraceResourceId> Backings = new();
        public bool Read;
        public bool Written;
        public bool Linked;
        public bool Bundle;
        public bool External => Origin != GraphResourceOrigin.Transient || Linked;
        public string PortLabel => Bundle || Key == FbKey || Members.Count <= 1 ? Label : $"{Label} x{Members.Count}";
        public string Tooltip => string.Join("\n", Members.OrderBy(m => m).Take(24)) + (Members.Count > 24 ? $"\n+{Members.Count - 24} more" : "");
    }

    private sealed class KindAlias
    {
        public required string Id;
        public required KindResource Resource;
        public readonly List<KindPass> Consumers = new();
        public GraphNode Node = null!;
    }

    private sealed class KindSink
    {
        public required string Id;
        public required KindResource Resource;
        public required KindPass Writer;
        public GraphNode Node = null!;
    }

    private sealed record ResourceTarget(KindPass Pass, KindResource Resource, KindBlock Owner, bool Output, IReadOnlyList<KindPass> Consumers);

    private sealed class KindBlock
    {
        public required string Kind;
        public int Depth;
        public int Layer;
        public int Weight;
        public readonly List<DeepGroup> Groups = new();
        public readonly List<KindPass> Passes = new();
        public readonly Dictionary<string, KindResource> Resources = new();
        public readonly HashSet<string> Parents = new();
        public readonly List<KindAlias> Aliases = new();
        public readonly List<KindSink> Sinks = new();
        public readonly Dictionary<string, GraphNode> PassNodes = new();
        public readonly List<(KindPass From, KindPass To, string Key)> Flows = new();
        public GraphNode Entry = null!;
        public GraphNode Exit = null!;
        public Dictionary<string, RawResource> Raw = new();
    }

    private sealed class RawResource
    {
        public GraphResourceOrigin Origin;
        public GraphResourceKind Kind;
        public string? BackingName;
        public bool Written;
        public readonly HashSet<TraceResourceId> Backings = new();
        public readonly HashSet<string> Passes = new();
    }

    private const string RootFamily = "";

    private static string FamilyOf(DeepGroup group) => group.Index < 0 ? RootFamily : Regex.Replace(group.Name, "[0-9]+", "#");

    private static string KindLabel(string kind) => kind == RootFamily ? "Execution" : kind;

    private static string ShortName(string raw, string? backing)
    {
        string name = raw.StartsWith("Graphite.Import", StringComparison.Ordinal) && !string.IsNullOrEmpty(backing) ? backing : raw;
        if (name.StartsWith("Graphite.", StringComparison.Ordinal))
            name = name["Graphite.".Length..];
        return name.Length > 0 ? name : raw;
    }

    private static List<KindBlock> BuildKinds(IReadOnlyList<DeepGroup> scope, DeepRecording deep, DeepIndex index)
    {
        Dictionary<string, KindBlock> kinds = new();
        Dictionary<string, Dictionary<string, RawResource>> raws = new();
        Dictionary<string, HashSet<string>> folded = new();
        Dictionary<int, DeepGroup> byIndex = deep.Groups.Where(i => i.Index >= 0).ToDictionary(i => i.Index);

        int DepthOf(DeepGroup instance)
        {
            int depth = 0;
            while (byIndex.TryGetValue(instance.ParentIndex, out DeepGroup? parent))
            {
                depth++;
                instance = parent;
            }
            return depth;
        }

        foreach (DeepGroup instance in scope)
        {
            if (!folded.TryGetValue(FamilyOf(instance), out HashSet<string>? set))
                folded[FamilyOf(instance)] = set = new();
            foreach (var g in instance.Passes.GroupBy(p => p.Name).Where(g => g.Count() > FoldLimit))
                set.Add(g.Key);
        }

        foreach (DeepGroup instance in scope)
        {
            if (!kinds.TryGetValue(FamilyOf(instance), out KindBlock? block))
            {
                kinds[FamilyOf(instance)] = block = new KindBlock { Kind = FamilyOf(instance) };
                raws[FamilyOf(instance)] = new();
            }
            Dictionary<string, RawResource> raw = raws[FamilyOf(instance)];
            block.Groups.Add(instance);
            block.Depth = Math.Max(block.Depth, DepthOf(instance));
            if (byIndex.TryGetValue(instance.ParentIndex, out DeepGroup? owner) && FamilyOf(owner) != FamilyOf(instance))
                block.Parents.Add(FamilyOf(owner));

            Dictionary<string, int> seen = new();
            foreach (DeepPass pass in instance.Passes)
            {
                seen.TryGetValue(pass.Name, out int occurrence);
                seen[pass.Name] = occurrence + 1;
                bool fold = folded[FamilyOf(instance)].Contains(pass.Name);
                int slot = fold ? 0 : occurrence;
                KindPass? entry = block.Passes.FirstOrDefault(p => p.Name == pass.Name && p.Occurrence == slot);
                if (entry == null)
                {
                    entry = new KindPass { Name = pass.Name, Occurrence = slot, Pass = pass, GroupIndex = instance.Index, First = pass.Index };
                    block.Passes.Add(entry);
                }
                if (entry.Seen.Add(instance.Index))
                    entry.Count++;
                entry.Merged = Math.Max(entry.Merged, fold ? occurrence + 1 : 1);
                entry.First = Math.Min(entry.First, pass.Index);
                foreach (RecordedAccess a in pass.Accesses)
                {
                    if (!entry.Accesses.Any(x => x.Resource == a.Resource && x.IsOutput == a.IsOutput))
                        entry.Accesses.Add(a);
                    if (!raw.TryGetValue(a.Resource, out RawResource? info))
                        raw[a.Resource] = info = new RawResource { Kind = a.Kind };
                    info.Written |= a.IsOutput;
                    info.Passes.Add(pass.Name + ":" + slot);
                }
            }

            foreach (RecordedGraphResource resource in instance.Resources)
            {
                if (!raw.TryGetValue(resource.Name, out RawResource? info))
                    raw[resource.Name] = info = new RawResource { Kind = resource.Kind };
                if (resource.Origin != GraphResourceOrigin.Transient)
                    info.Origin = resource.Origin;
                foreach (RecordedBacking backing in resource.Backings)
                    info.Backings.Add(backing.Id);
                if (info.BackingName == null && resource.Backings.Length > 0)
                    info.BackingName = index.Resource(resource.Backings[0].Id)?.Name;
            }
        }

        foreach (KindBlock block in kinds.Values)
        {
            Dictionary<string, string> keyOf = new();
            foreach ((string name, RawResource info) in raws[block.Kind])
            {
                bool shared = info.Kind == GraphResourceKind.Buffer && info.Origin != GraphResourceOrigin.Transient && info.Written && info.Passes.Count >= SharedThreshold;
                string label = ShortName(name, info.BackingName);
                string key = shared ? FbKey : Regex.Replace(label, "[0-9]+", "#");
                keyOf[name] = key;
                if (!block.Resources.TryGetValue(key, out KindResource? resource))
                    block.Resources[key] = resource = new KindResource { Key = key, Label = shared ? "Frame buffers" : key, Kind = info.Kind };
                resource.Members.Add(shared ? label : name);
                resource.Backings.UnionWith(info.Backings);
                if (info.Origin != GraphResourceOrigin.Transient)
                    resource.Origin = info.Origin;
            }

            foreach (KindPass pass in block.Passes)
            {
                List<RecordedAccess> mapped = new();
                foreach (RecordedAccess a in pass.Accesses)
                {
                    KindResource resource = block.Resources[keyOf[a.Resource]];
                    if (a.IsOutput)
                        resource.Written = true;
                    else
                        resource.Read = true;
                    if (!mapped.Any(x => x.Resource == resource.Key && x.IsOutput == a.IsOutput))
                        mapped.Add(a with { Resource = resource.Key });
                    if (!pass.RawNames.TryGetValue((resource.Key, a.IsOutput), out HashSet<string>? names))
                        pass.RawNames[(resource.Key, a.IsOutput)] = names = new();
                    names.Add(a.Resource);
                }
                pass.Accesses = mapped;
            }
            block.Passes.Sort((a, b) => a.First.CompareTo(b.First));
            block.Raw = raws[block.Kind];
        }

        return kinds.Values.OrderBy(k => k.Depth).ThenBy(k => k.Kind).ToList();
    }

    private static System.Drawing.Color KindColor(int ordinal, bool root)
    {
        if (root)
            return Style.Theme.Primary.C500;
        return (ordinal % 8) switch
        {
            0 => Style.Theme.Blue.C500,
            1 => Style.Theme.Green.C500,
            2 => Style.Theme.Amber.C500,
            3 => Style.Theme.Red.C500,
            4 => Style.Theme.Blue.C300,
            5 => Style.Theme.Green.C300,
            6 => Style.Theme.Amber.C300,
            _ => Style.Theme.Red.C300,
        };
    }

    private static string Family(string key)
    {
        int cut = key.IndexOfAny(new[] { '.', '/' });
        return cut > 0 ? key[..cut] : key;
    }

    private static float NamedPillWidth(string title) => Math.Min(AliasWidth, 28f + title.Length * 6.5f);

    private static System.Drawing.Color ResourceColor(GraphResourceKind kind)
        => kind == GraphResourceKind.Buffer ? Style.Theme.Red.C300 : Style.Good;

    private static GraphPort Port(GraphNode node, bool input, string id, string label, System.Drawing.Color color, string? tooltip = null)
    {
        List<GraphPort> ports = input ? node.Inputs : node.Outputs;
        GraphPort? port = ports.FirstOrDefault(p => p.Id == id);
        if (port == null)
        {
            port = new GraphPort(id, label) { Color = color, Tooltip = tooltip };
            ports.Add(port);
        }
        return port;
    }

    private static KindResource MakeBundle(KindBlock kind, List<KindResource> parts, string key)
    {
        KindResource bundle = new()
        {
            Key = key,
            Label = parts.Count > 1 ? $"{parts[0].Label} +{parts.Count - 1} more" : parts[0].Label,
            Kind = parts.All(x => x.Kind == GraphResourceKind.Buffer) ? GraphResourceKind.Buffer : GraphResourceKind.Texture,
            Bundle = true,
            Read = parts.Any(x => x.Read),
            Written = parts.Any(x => x.Written),
        };
        foreach (KindResource part in parts)
        {
            bundle.Members.UnionWith(part.Members.Count > 0 ? part.Members : new[] { part.Label });
            bundle.Backings.UnionWith(part.Backings);
            if (part.Origin != GraphResourceOrigin.Transient)
                bundle.Origin = part.Origin;
        }
        kind.Resources[key] = bundle;
        return bundle;
    }

    private void FrameGraphTab(FrameModel model)
        => GraphTab(model, model.Deep!.Groups.ToArray(), "frame|" + model.Number, "Render graph / whole frame");

    private void GraphTab(FrameModel model, IReadOnlyList<DeepGroup> scope, string controllerKey, string title)
    {
        DeepRecording deep = model.Deep!;
        List<KindBlock> kinds = BuildKinds(scope, deep, model.Index!);
        OrigamiMetrics metrics = Origami.Current.Metrics;

        Dictionary<TraceResourceId, List<(KindBlock Kind, KindResource Resource)>> byBacking = new();
        foreach (KindBlock block in kinds)
            foreach (KindResource resource in block.Resources.Values)
                foreach (TraceResourceId id in resource.Backings)
                {
                    if (!byBacking.TryGetValue(id, out var list))
                        byBacking[id] = list = new();
                    list.Add((block, resource));
                }
        foreach (var list in byBacking.Values)
            if (list.Select(e => e.Kind).Distinct().Count() > 1)
                foreach (var e in list)
                    e.Resource.Linked = true;

        List<GraphNode> nodes = new();
        List<GraphConnection> connections = new();
        List<OrigamiGroup> groups = new();
        HashSet<string> wired = new();

        void Wire(string fromNode, string fromPort, string toNode, string toPort, System.Drawing.Color color, bool flow = false)
        {
            string id = $"{fromNode}.{fromPort}>{toNode}.{toPort}";
            if (wired.Add(id))
                connections.Add(new GraphConnection(fromNode, fromPort, toNode, toPort) { Color = color, Id = id, Flow = flow });
        }

        string PassId(KindBlock k, KindPass p) => $"p:{k.Kind}:{p.Name}:{p.Occurrence}";

        foreach (KindBlock kind in kinds)
        {
            foreach (KindPass p in kind.Passes)
                kind.PassNodes[PassId(kind, p)] = new GraphNode
                {
                    Id = PassId(kind, p),
                    Title = p.Name,
                    Width = FramePassWidth,
                    Accent = Style.KindDot(NodeKind.Pass),
                    Badge = p.Merged > 1 ? new GraphBadge("x" + p.Merged) : p.Count > 1 ? new GraphBadge("x" + p.Count) : null,
                    Tooltip = $"{p.Count} of {kind.Groups.Count} {KindLabel(kind.Kind)} group(s)" + (p.Merged > 1 ? $"\n{p.Merged} passes folded" : ""),
                    Outline = _frameGraphSelection == PassId(kind, p) ? Style.Selection : null,
                    UserData = p,
                };

            if (kind.Passes.Count > 0)
            {
                kind.Entry = new GraphNode { Id = "e:" + kind.Kind, HeaderOnly = true, Width = PillWidth, Accent = Style.Snapshot, Pinned = true, Tooltip = KindLabel(kind.Kind) + " begins" };
                kind.Exit = new GraphNode { Id = "x:" + kind.Kind, HeaderOnly = true, Width = PillWidth, Accent = Style.Snapshot, Pinned = true, Tooltip = KindLabel(kind.Kind) + " ends" };
                string first = PassId(kind, kind.Passes[0]), last = PassId(kind, kind.Passes[^1]);
                Port(kind.Entry, false, "seq", "", Style.Snapshot);
                Port(kind.PassNodes[first], true, "seq", "", Style.Snapshot);
                Wire(kind.Entry.Id, "seq", first, "seq", Style.Snapshot, true);
                Port(kind.PassNodes[last], false, "seq", "", Style.Snapshot);
                Port(kind.Exit, true, "seq", "", Style.Snapshot);
                Wire(last, "seq", kind.Exit.Id, "seq", Style.Snapshot, true);
                for (int i = 0; i < kind.Passes.Count; i++)
                {
                    bool root = kind.Kind == RootFamily;
                    if (root)
                        kind.Passes[i].Level = i;
                    if (i == 0)
                        continue;
                    KindPass before = kind.Passes[i - 1];
                    if (root)
                        before.HasOut = true;
                    Port(kind.PassNodes[PassId(kind, before)], false, "seq", "", Style.Snapshot);
                    Port(kind.PassNodes[PassId(kind, kind.Passes[i])], true, "seq", "", Style.Snapshot);
                    Wire(PassId(kind, before), "seq", PassId(kind, kind.Passes[i]), "seq", Style.Snapshot, true);
                }
            }

            Dictionary<string, KindPass> lastWriter = new();
            Dictionary<string, List<KindPass>> unsourced = new();
            List<KindPass> fbWriters = new(), fbReaders = new();
            KindPass? previous = null;
            foreach (KindPass p in kind.Passes)
            {
                if (previous != null)
                    p.Level = Math.Max(p.Level, previous.Level + 1);
                previous = p;
                GraphNode node = kind.PassNodes[PassId(kind, p)];
                foreach (var access in p.Accesses.GroupBy(a => a.Resource))
                {
                    KindResource r = kind.Resources[access.Key];
                    bool writes = access.Any(a => a.IsOutput);
                    bool reads = access.Any(a => !a.IsOutput);
                    if (access.Key == FbKey)
                    {
                        if (writes)
                            fbWriters.Add(p);
                        if (reads)
                            fbReaders.Add(p);
                        continue;
                    }
                    if (lastWriter.TryGetValue(access.Key, out KindPass? w) && w != p)
                    {
                        Port(kind.PassNodes[PassId(kind, w)], false, "o:" + access.Key, r.PortLabel, Style.Read, r.Tooltip);
                        Port(node, true, "i:" + access.Key, r.PortLabel, Style.Read, r.Tooltip);
                        Wire(PassId(kind, w), "o:" + access.Key, node.Id, "i:" + access.Key, Style.Read);
                        kind.Flows.Add((w, p, access.Key));
                        p.Level = Math.Max(p.Level, w.Level + 1);
                        w.HasOut = true;
                    }
                    else if (reads || (writes && r.External))
                    {
                        if (!unsourced.TryGetValue(access.Key, out var list))
                            unsourced[access.Key] = list = new();
                        list.Add(p);
                    }
                    if (writes)
                        lastWriter[access.Key] = p;
                }
            }

            List<(KindResource Resource, List<KindPass> Consumers)> units = new();
            int bundles = 0;
            foreach (var group in unsourced.GroupBy(u => kind.Resources[u.Key].Kind + "|" + Family(u.Key) + "|" + string.Join(",", u.Value.Select(c => c.Name + ":" + c.Occurrence))))
            {
                var members = group.ToList();
                if (members.Count >= BundleMin)
                    units.Add((MakeBundle(kind, members.Select(m => kind.Resources[m.Key]).ToList(), $"*in{bundles++}"), members[0].Value));
                else
                    foreach (var m in members)
                        units.Add((kind.Resources[m.Key], m.Value));
            }
            if (fbReaders.Count > 0 && kind.Kind != RootFamily)
                units.Add((kind.Resources[FbKey], new List<KindPass> { fbReaders[0] }));

            foreach ((KindResource r, List<KindPass> consumers) in units)
            {
                KindAlias? current = null;
                foreach (KindPass p in consumers)
                {
                    if (current == null || p.Level - current.Consumers.Min(c => c.Level) > AliasSpan || current.Consumers.Count >= AliasFan)
                    {
                        current = new KindAlias { Id = $"s:{kind.Kind}:{r.Key}:{kind.Aliases.Count}", Resource = r };
                        kind.Aliases.Add(current);
                    }
                    current.Consumers.Add(p);
                }
            }

            foreach ((string key, KindPass writer) in lastWriter)
            {
                KindResource r = kind.Resources[key];
                if (r.Written && r.External)
                    kind.Sinks.Add(new KindSink { Id = $"k:{PassId(kind, writer)}:{key}", Resource = r, Writer = writer });
            }
            foreach (KindPass writer in fbWriters)
            {
                KindResource shared = kind.Resources[FbKey];
                KindResource own = new() { Key = FbKey, Label = "Frame buffers", Kind = GraphResourceKind.Buffer, Origin = shared.Origin };
                foreach (string raw in writer.RawNames[(FbKey, true)])
                {
                    own.Members.Add(kind.Raw.TryGetValue(raw, out RawResource? info) ? ShortName(raw, info.BackingName) : raw);
                    if (info != null)
                        own.Backings.UnionWith(info.Backings);
                }
                kind.Sinks.Add(new KindSink { Id = $"k:{PassId(kind, writer)}:{FbKey}", Resource = own, Writer = writer });
            }
        }

        bool HasConsumer(KindBlock owner, KindSink sink)
            => kinds.Any(k => k != owner && k.Aliases.Any(a => a.Resource.Backings.Overlaps(sink.Resource.Backings)));

        foreach (KindBlock kind in kinds)
        {
            kind.Sinks.RemoveAll(sk => sk.Resource.Origin != GraphResourceOrigin.Imported && sk.Resource.Origin != GraphResourceOrigin.Target && !HasConsumer(kind, sk));

            int sinkBundles = 0;
            foreach (var group in kind.Sinks.Where(sk => sk.Resource.Key != FbKey).GroupBy(sk => (sk.Writer, sk.Resource.Kind, Family(sk.Resource.Key))).Where(g => g.Count() >= BundleMin).ToList())
            {
                var members = group.ToList();
                kind.Sinks.RemoveAll(sk => members.Contains(sk));
                kind.Sinks.Add(new KindSink { Id = $"k:{PassId(kind, group.Key.Writer)}:*out{sinkBundles}", Resource = MakeBundle(kind, members.Select(m => m.Resource).ToList(), $"*out{sinkBundles++}"), Writer = group.Key.Writer });
            }
        }

        HashSet<(KindSink, KindAlias)> linked = new();
        foreach (KindBlock consumerKind in kinds)
            foreach (KindAlias alias in consumerKind.Aliases)
            {
                List<KindSink> candidates = new();
                foreach (KindBlock other in kinds.Where(k => k != consumerKind))
                    foreach (KindSink sink in other.Sinks.Where(sk => sk.Resource.Backings.Overlaps(alias.Resource.Backings)))
                        candidates.Add(sink);
                var upstream = candidates.Where(c => kinds.First(k => k.Sinks.Contains(c)).Depth <= consumerKind.Depth).ToList();
                foreach (KindSink sink in upstream.Count > 0 ? upstream : candidates)
                    linked.Add((sink, alias));
            }

        foreach (KindBlock kind in kinds)
        {
            foreach (KindAlias a in kind.Aliases)
            {
                System.Drawing.Color rc = ResourceColor(a.Resource.Kind);
                a.Node = new GraphNode { Id = a.Id, Title = a.Resource.PortLabel, HeaderOnly = true, Width = NamedPillWidth(a.Resource.PortLabel), Accent = rc, Tooltip = a.Resource.Tooltip, Pinned = true, Outline = _frameGraphSelection == a.Id ? Style.Selection : null, UserData = new ResourceTarget(a.Consumers[0], a.Resource, kind, false, a.Consumers) };
                Port(a.Node, false, "o", "", rc);
                foreach (KindPass p in a.Consumers)
                {
                    Port(kind.PassNodes[PassId(kind, p)], true, "i:" + a.Resource.Key, a.Resource.PortLabel, rc, a.Resource.Tooltip);
                    Wire(a.Id, "o", PassId(kind, p), "i:" + a.Resource.Key, rc);
                }
            }
            foreach (KindSink sk in kind.Sinks)
            {
                System.Drawing.Color rc = ResourceColor(sk.Resource.Kind);
                sk.Node = new GraphNode { Id = sk.Id, Title = sk.Resource.PortLabel, HeaderOnly = true, Width = NamedPillWidth(sk.Resource.PortLabel), Accent = rc, Tooltip = sk.Resource.Tooltip, Pinned = true, Outline = _frameGraphSelection == sk.Id ? Style.Selection : null, UserData = new ResourceTarget(sk.Writer, sk.Resource, kind, true, new[] { sk.Writer }) };
                Port(sk.Node, true, "i", "", rc);
                Port(kind.PassNodes[PassId(kind, sk.Writer)], false, "o:" + sk.Resource.Key, sk.Resource.PortLabel, rc, sk.Resource.Tooltip);
                Wire(PassId(kind, sk.Writer), "o:" + sk.Resource.Key, sk.Id, "i", rc);
            }
        }

        foreach (var group in linked.GroupBy(l => l.Item2))
        {
            KindAlias alias = group.Key;
            string from = string.Join(", ", group.Select(l => KindLabel(kinds.First(k => k.Sinks.Contains(l.Item1)).Kind)).Distinct());
            alias.Node.Tooltip += "\nfrom " + from;
        }

        List<KindBlock> threaded = kinds.Where(k => k.Passes.Count > 0).OrderBy(k => k.Groups.Min(i => i.Index)).ToList();
        for (int i = 1; i < threaded.Count; i++)
        {
            Port(threaded[i - 1].Exit, false, "out", "", Style.Snapshot);
            Port(threaded[i].Entry, true, "in", "", Style.Snapshot);
            Wire(threaded[i - 1].Exit.Id, "out", threaded[i].Entry.Id, "in", Style.Snapshot, true);
        }

        foreach (KindBlock kind in kinds)
        {
            foreach (KindPass p in kind.Passes)
            {
                GraphNode gn = kind.PassNodes[PassId(kind, p)];
                if (p.Accesses.Any(a => a.Resource == FbKey) && !gn.Inputs.Any(x => x.Id == "i:" + FbKey) && !gn.Outputs.Any(x => x.Id == "o:" + FbKey))
                    gn.Badges.Add(new GraphBadge("frame buffers"));
            }
            kind.Weight = linked.Count(l => kind.Sinks.Contains(l.Item1) || kind.Aliases.Contains(l.Item2)) + kind.Parents.Count;
        }

        Dictionary<KindBlock, int> layers = new();
        int LayerOf(KindBlock kind, int guard = 0)
        {
            if (layers.TryGetValue(kind, out int known))
                return known;
            int layer = 0;
            if (guard < kinds.Count)
            {
                foreach (string parentKind in kind.Parents)
                    if (kinds.FirstOrDefault(k => k.Kind == parentKind) is { } parent && parent.Depth < kind.Depth)
                        layer = Math.Max(layer, LayerOf(parent, guard + 1) + 1);
                foreach (var (sink, alias) in linked.Where(l => kind.Aliases.Contains(l.Item2)))
                    if (kinds.First(k => k.Sinks.Contains(sink)) is { } provider && provider.Depth < kind.Depth)
                        layer = Math.Max(layer, LayerOf(provider, guard + 1) + 1);
            }
            return layers[kind] = layer;
        }
        foreach (KindBlock kind in kinds)
            kind.Layer = LayerOf(kind);
        for (int i = 0; i < threaded.Count; i++)
            threaded[i].Layer = i;

        Dictionary<KindBlock, (List<GraphNode> Nodes, OrigamiGroup Group)> built = new();
        int ordinal = 0;
        foreach (KindBlock kind in kinds)
        {
            int columns = kind.Passes.Count > 0 ? kind.Passes.Max(p => p.Level) + 1 : 0;

            int AliasOrder(KindAlias a) => (a.Resource.Kind == GraphResourceKind.Texture ? 100000 : 0) + kind.Aliases.IndexOf(a);

            List<KindAlias> leftAliases = kind.Aliases.Where(a => a.Consumers[0].Level == 0).OrderBy(AliasOrder).ToList();
            List<KindAlias> topAliases = kind.Aliases.Except(leftAliases).ToList();
            List<KindSink> rightSinks = kind.Sinks.Where(sk => sk.Writer.Level == columns - 1 && !sk.Writer.HasOut).ToList();
            List<KindSink> bottomSinks = kind.Sinks.Except(rightSinks).ToList();

            Dictionary<(string, string), int> rank = new();
            for (int i = 0; i < kind.Aliases.Count; i++)
                foreach (KindPass c in kind.Aliases[i].Consumers)
                {
                    string pid = PassId(kind, c), port = "i:" + kind.Aliases[i].Resource.Key;
                    rank[(pid, port)] = AliasOrder(kind.Aliases[i]);
                }
            foreach (KindSink sink in bottomSinks)
            {
                string pid = PassId(kind, sink.Writer), port = "o:" + sink.Resource.Key;
                rank[(pid, port)] = kind.Sinks.IndexOf(sink);
            }
            foreach (GraphNode node in kind.PassNodes.Values)
            {
                node.Inputs = node.Inputs.OrderBy(x => rank.GetValueOrDefault((node.Id, x.Id), -1)).ToList();
                node.Outputs = node.Outputs.OrderBy(x => rank.GetValueOrDefault((node.Id, x.Id), -1)).ToList();
            }

            float WidthOf(GraphNode n) => NodeGraphPreview.MeasureWidth(n, metrics);
            float leftLane = leftAliases.Count > 0 ? leftAliases.Max(a => WidthOf(a.Node)) + ColumnGap : 0f;
            float rightLane = rightSinks.Count > 0 ? rightSinks.Max(sk => WidthOf(sk.Node)) : 0f;
            float[] passX = new float[Math.Max(columns, 1)];
            for (int c = 0; c < passX.Length; c++)
                passX[c] = KindPad + leftLane + c * (FramePassWidth + ColumnGap);
            float rightLaneX = passX[^1] + FramePassWidth + ColumnGap;
            float width = (rightSinks.Count > 0 ? rightLaneX + rightLane : passX[^1] + FramePassWidth) + KindPad;

            float Height(GraphNode n) => NodeGraphPreview.MeasureHeight(n, metrics);
            float Stack(IEnumerable<GraphNode> items) => items.Sum(n => Height(n) + RowGap) - (items.Any() ? RowGap : 0f);

            List<(GraphNode Node, float Left)> topItems = topAliases.Select(a => (a.Node, passX[Math.Max(a.Consumers.Min(c => c.Level) - 1, 0)])).ToList();
            List<(GraphNode Node, float Left)> bottomItems = bottomSinks.Select(sk => (sk.Node, passX[Math.Min(sk.Writer.Level + 1, passX.Length - 1)])).ToList();

            float BandRow(List<(GraphNode Node, float Left)> items)
                => items.Count > 0 ? items.Max(i => Height(i.Node)) + RowGap : 0f;

            (float Height, float Right) PlaceBand(List<(GraphNode Node, float Left)> items, float y0)
            {
                float rowH = BandRow(items);
                List<float> rights = new();
                float maxRight = 0f;
                foreach (var (node, left) in items.OrderBy(i => i.Left))
                {
                    int row = 0;
                    while (row < rights.Count && left < rights[row] + RowGap)
                        row++;
                    if (row == rights.Count)
                        rights.Add(0f);
                    rights[row] = left + WidthOf(node);
                    maxRight = Math.Max(maxRight, rights[row]);
                    node.Position = new Float2(left, y0 + row * rowH);
                }
                return (rights.Count * rowH, maxRight);
            }

            float topHeight = 0f;

            Dictionary<KindPass, List<GraphNode>> leftOf = kind.Passes.ToDictionary(p => p, p => leftAliases.Where(a => a.Consumers[0] == p).Select(a => a.Node).ToList());
            Dictionary<KindPass, List<GraphNode>> rightOf = kind.Passes.ToDictionary(p => p, p => rightSinks.Where(sk => sk.Writer == p).Select(sk => sk.Node).ToList());
            Dictionary<KindPass, float> rowHeight = kind.Passes.ToDictionary(p => p, p => Math.Max(Height(kind.PassNodes[PassId(kind, p)]), headerH + Math.Max(Stack(leftOf[p]), Stack(rightOf[p]))));

            Dictionary<KindPass, int> lanes = new();
            foreach (KindPass p in kind.Passes.OrderBy(p => p.Level))
            {
                int best = 0;
                float bestCost = float.MaxValue;
                foreach (int candidate in new[] { 0, 1, -1, 2, -2, 3, -3, 4, -4 })
                {
                    float cost = Math.Abs(candidate) * 0.05f;
                    foreach ((KindPass from, KindPass to, string key) in kind.Flows)
                    {
                        if (to == p && lanes.TryGetValue(from, out int fromLane))
                            cost += 0.3f * Math.Abs(candidate - fromLane);
                        if (from.Level >= p.Level || to.Level <= p.Level || !lanes.TryGetValue(from, out int start))
                            continue;
                        int end = lanes.GetValueOrDefault(to);
                        float t = (float)(p.Level - from.Level) / (to.Level - from.Level);
                        float outRow = headerH + 4f + (kind.PassNodes[PassId(kind, from)].Outputs.FindIndex(x => x.Id == "o:" + key) + 0.5f) * metrics.RowHeight;
                        float inRow = headerH + 4f + (kind.PassNodes[PassId(kind, to)].Inputs.FindIndex(x => x.Id == "i:" + key) + 0.5f) * metrics.RowHeight;
                        float wire = start * LaneStep + outRow + (end * LaneStep + inRow - start * LaneStep - outRow) * t, nodeTop = candidate * LaneStep;
                        if (wire > nodeTop - 12f && wire < nodeTop + Height(kind.PassNodes[PassId(kind, p)]) + 12f)
                            cost += 1f;
                    }
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = candidate;
                    }
                }
                lanes[p] = best;
            }
            int minLane = lanes.Count > 0 ? lanes.Values.Min() : 0, maxLane = lanes.Count > 0 ? lanes.Values.Max() : 0;

            float[] columnY = new float[Math.Max(columns, 1)];
            Dictionary<KindPass, float> top = new();
            foreach (KindPass p in kind.Passes)
            {
                top[p] = columnY[p.Level];
                columnY[p.Level] += rowHeight[p] + RowGap;
            }
            float baseHeight = Math.Max(1f, columnY.Max() - RowGap);
            float contentHeight = baseHeight + (maxLane - minLane) * LaneStep;
            foreach (KindPass p in kind.Passes)
            {
                float rowTop = KindTitle + topHeight + top[p] + (lanes[p] - minLane) * LaneStep;
                GraphNode gn = kind.PassNodes[PassId(kind, p)];
                gn.Position = new Float2(passX[p.Level], rowTop);

                float y = rowTop + headerH;
                foreach (GraphNode n in leftOf[p])
                {
                    n.Position = new Float2(passX[p.Level] - ColumnGap - WidthOf(n), y);
                    y += Height(n) + PillGap;
                }
                y = rowTop + headerH;
                foreach (GraphNode n in rightOf[p])
                {
                    n.Position = new Float2(rightLaneX, y);
                    y += Height(n) + PillGap;
                }
            }

            List<GraphNode> fixedNodes = kind.Passes.Select(p => kind.PassNodes[PassId(kind, p)]).Concat(leftAliases.Select(a => a.Node)).Concat(rightSinks.Select(sk => sk.Node)).ToList();
            List<GraphNode> near = new();
            List<(GraphNode Node, float Left)> bandTop = new(), bandBottom = new();

            (float X, float Y, float W, float H) Rect(GraphNode n) => (n.Position.X, n.Position.Y, WidthOf(n), Height(n));

            bool Overlaps(GraphNode n, (float X, float Y, float W, float H) at)
            {
                const float margin = 8f;
                foreach (GraphNode other in fixedNodes.Concat(near))
                {
                    var o = Rect(other);
                    if (at.X < o.X + o.W + margin && at.X + at.W > o.X - margin && at.Y < o.Y + o.H + margin && at.Y + at.H > o.Y - margin)
                        return true;
                }
                return false;
            }

            int Crossings((float X, float Y, float W, float H) at, List<GraphNode> targets, bool input)
            {
                int hits = 0;
                foreach (GraphNode target in targets)
                {
                    var t = Rect(target);
                    float fx = input ? at.X + at.W : t.X + t.W, fy = input ? at.Y + at.H / 2f : t.Y + t.H / 2f;
                    float tx = input ? t.X : at.X, ty = input ? t.Y + t.H / 2f : at.Y + at.H / 2f;
                    foreach (GraphNode other in fixedNodes.Concat(near))
                    {
                        var o = Rect(other);
                        for (int i = 1; i < 16; i++)
                        {
                            float x = fx + (tx - fx) * i / 16f, y = fy + (ty - fy) * i / 16f;
                            if (x > o.X && x < o.X + o.W && y > o.Y && y < o.Y + o.H)
                            {
                                hits++;
                                break;
                            }
                        }
                    }
                }
                return hits;
            }

            bool TryNear(GraphNode node, List<GraphNode> targets, bool input)
            {
                float w = WidthOf(node), h = Height(node);
                float bestCost = float.MaxValue;
                Float2 best = default;
                foreach (GraphNode target in targets)
                {
                    var t = Rect(target);
                    float near1 = input ? t.X - w - ColumnGap * 0.5f : t.X + t.W + ColumnGap * 0.5f;
                    float back = input ? t.X - FramePassWidth - ColumnGap : t.X + FramePassWidth + ColumnGap;
                    float below = t.Y + t.H + RowGap * 0.5f, above = t.Y - h - RowGap * 0.5f, level = t.Y + (t.H - h) / 2f;
                    (float X, float Y)[] spots =
                    {
                        (near1, level),
                        (near1, below),
                        (near1, above),
                        (back, level),
                        (back, below),
                        (back, above),
                        (t.X, below),
                        (t.X, above),
                    };
                    foreach ((float x, float y) in spots)
                    {
                        if (x < 4f || y < KindTitle)
                            continue;
                        var at = (X: x, Y: y, W: w, H: h);
                        if (Overlaps(node, at))
                            continue;
                        int crossings = Crossings(at, targets, input);
                        if (crossings > (targets.Count > 1 ? 1 : 0))
                            continue;
                        float cost = targets.Sum(tg =>
                        {
                            var r = Rect(tg);
                            float portX = input ? r.X : r.X + r.W;
                            float pillX = input ? x + w : x;
                            float run = input ? portX - pillX : pillX - portX;
                            float cost = run < 0f ? 200f - run : run + (run < MinRun ? (MinRun - run) * 2f : 0f);
                            return cost + MathF.Abs(y + h / 2f - (r.Y + r.H / 2f)) * 2f;
                        }) + crossings * 300f;
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            best = new Float2(x, y);
                        }
                    }
                }
                if (bestCost == float.MaxValue)
                    return false;
                node.Position = best;
                near.Add(node);
                return true;
            }

            foreach (KindAlias alias in topAliases)
                if (!TryNear(alias.Node, alias.Consumers.Select(c => kind.PassNodes[PassId(kind, c)]).ToList(), true))
                    bandTop.Add(topItems.First(i => i.Node == alias.Node));
            foreach (KindSink sink in bottomSinks)
                if (!TryNear(sink.Node, new List<GraphNode> { kind.PassNodes[PassId(kind, sink.Writer)] }, false))
                    bandBottom.Add(bottomItems.First(i => i.Node == sink.Node));

            topHeight = bandTop.Count > 0 ? PlaceBand(bandTop, KindTitle).Height : 0f;
            foreach (GraphNode n in fixedNodes.Concat(near))
                n.Position += new Float2(0f, topHeight);

            float contentBottom = Math.Max(KindTitle + topHeight + contentHeight, near.Count > 0 ? near.Max(n => n.Position.Y + Height(n)) : 0f);
            float bottomHeight = bandBottom.Count > 0 ? PlaceBand(bandBottom, contentBottom + RowGap).Height : 0f;
            float bandRight = new[]
            {
                bandTop.Count > 0 ? bandTop.Max(i => i.Left + WidthOf(i.Node)) : 0f,
                bandBottom.Count > 0 ? bandBottom.Max(i => i.Left + WidthOf(i.Node)) : 0f,
                near.Count > 0 ? near.Max(n => n.Position.X + WidthOf(n)) : 0f,
            }.Max();
            width = Math.Max(width, bandRight + KindPad);
            float totalContent = contentBottom - KindTitle + (bottomHeight > 0 ? bottomHeight + RowGap : 0f);

            List<GraphNode> all = new();
            if (kind.Passes.Count > 0)
            {
                float pillY = KindTitle + topHeight - minLane * laneStep + baseHeight / 2f - Height(kind.Entry) / 2f;
                kind.Entry.Position = new Float2(-PillWidth / 2f, pillY);
                kind.Exit.Position = new Float2(width - PillWidth / 2f, pillY);
                all.Add(kind.Entry);
                all.Add(kind.Exit);
            }
            all.AddRange(kind.Aliases.Select(a => a.Node));
            all.AddRange(kind.Passes.Select(p => kind.PassNodes[PassId(kind, p)]));
            all.AddRange(kind.Sinks.Select(sk => sk.Node));
            built[kind] = (all, new OrigamiGroup
            {
                Id = "k:" + kind.Kind,
                Title = kind.Kind == RootFamily ? "Once per execution" : $"Once per {kind.Kind} ({kind.Groups.Count})",
                Position = Float2.Zero,
                Size = new Float2(width, KindTitle + totalContent + KindPad),
                Color = KindColor(ordinal++, kind.Kind == RootFamily),
            });
        }

        float layerX = 0f;
        foreach (var layer in kinds.GroupBy(k => k.Layer).OrderBy(g => g.Key))
        {
            List<KindBlock> ordered = layer.OrderByDescending(k => k.Weight).ThenBy(k => k.Kind).ToList();
            float layerWidth = ordered.Max(k => built[k].Group.Size.X);
            float above = -StackGap / 2f, below = StackGap / 2f;
            for (int i = 0; i < ordered.Count; i++)
            {
                KindBlock kind = ordered[i];
                (List<GraphNode> kindNodes, OrigamiGroup group) = built[kind];
                float y;
                if (ordered.Count == 1)
                    y = -group.Size.Y / 2f;
                else if (i % 2 == 0)
                {
                    y = below;
                    below += group.Size.Y + StackGap;
                }
                else
                {
                    above -= group.Size.Y;
                    y = above;
                    above -= StackGap;
                }
                Float2 offset = new(layerX, y);
                group.Position = offset;
                foreach (GraphNode n in kindNodes)
                    n.Position += offset;
                groups.Add(group);
                nodes.AddRange(kindNodes);
            }
            layerX += layerWidth + KindGap;
        }

        bool fresh = !_graphControllers.TryGetValue(controllerKey, out NodeGraphController? controller);
        if (fresh)
        {
            controller = new NodeGraphController();
            _graphControllers[controllerKey] = controller;
            controller.SetView(new Float2(Metrics.PaddingLarge, Metrics.PaddingLarge), 0.5f);
        }

        if (scope.Count < deep.Groups.Length)
        {
            List<KindBlock> whole = BuildKinds(deep.Groups.ToArray(), deep, model.Index!);
            Dictionary<int, DeepGroup> byIndex = deep.Groups.Where(i => i.Index >= 0).ToDictionary(i => i.Index);
            foreach (KindBlock kind in kinds)
            {
                OrigamiGroup group = built[kind].Group;
                DeepGroup current = kind.Groups[0];
                Dictionary<string, (GraphNode Node, int Target)> connectors = new();
                float y = group.Position.Y + KindTitle;
                foreach (KindAlias alias in kind.Aliases)
                {
                    foreach (KindBlock other in whole.Where(k => k.Kind != kind.Kind && k.Depth <= kind.Depth && k.Resources.Values.Any(r => r.Written && r.Backings.Overlaps(alias.Resource.Backings))))
                    {
                        if (!connectors.TryGetValue(other.Kind, out var connector))
                        {
                            DeepGroup? target = null;
                            for (DeepGroup? walk = current; walk != null; walk = byIndex.GetValueOrDefault(walk.ParentIndex))
                                if (FamilyOf(walk) == other.Kind)
                                {
                                    target = walk;
                                    break;
                                }
                            target ??= other.Groups[0];
                            GraphNode node = new()
                            {
                                Id = $"c:{kind.Kind}:{other.Kind}",
                                Title = target.Name,
                                Width = AliasWidth,
                                Accent = Style.KindDot(NodeKind.Group),
                                Tooltip = "Double click to open " + target.Name,
                                Pinned = true,
                                UserData = target.Index,
                                Position = new Float2(group.Position.X - AliasWidth - 120f, y),
                            };
                            connector = (node, target.Index);
                            connectors[other.Kind] = connector;
                            nodes.Add(node);
                            y += 140f;
                        }
                        Port(connector.Node, false, "o:" + alias.Id, alias.Resource.PortLabel, ResourceColor(alias.Resource.Kind), alias.Resource.Tooltip);
                        Port(alias.Node, true, "x", "", ResourceColor(alias.Resource.Kind));
                        Wire(connector.Node.Id, "o:" + alias.Id, alias.Id, "x", ResourceColor(alias.Resource.Kind));
                        y = Math.Max(y, connector.Node.Position.Y + NodeGraphPreview.MeasureHeight(connector.Node, metrics) + RowGap);
                    }
                }
            }
        }

        Section(title, 0, "Grouped by how often the work runs / click a pass or resource to inspect it / double click any node to open it", w =>
        {
            using (_paper.Row("legend").Height(Metrics.CompactHeight).Gap(Metrics.PaddingLarge).AlignItems(LayoutAlignment.Center).Enter())
            {
                LegendItem("r", ResourceColor(GraphResourceKind.Texture), "Texture");
                LegendItem("b", ResourceColor(GraphResourceKind.Buffer), "Buffer");
                LegendItem("c", Style.Read, "Pass to pass");
                LegendItem("o", Style.Snapshot, "Run order");
                LegendItem("p", Style.KindDot(NodeKind.Pass), "Pass");
            }

            List<Kv> detail = new();
            GraphNode? picked = nodes.FirstOrDefault(n => n.Id == _frameGraphSelection);
            KindPass? selected = picked?.UserData as KindPass;
            if (picked?.UserData is ResourceTarget target)
            {
                KindResource r = target.Resource;
                detail.Add(new("Resource", r.Label));
                detail.Add(new("Type", r.Kind == GraphResourceKind.Buffer ? "Buffer" : "Texture", ResourceColor(r.Kind)));
                detail.Add(new("In", KindLabel(target.Owner.Kind)));
                detail.Add(new(target.Output ? "Written by" : "Read by", string.Join(", ", target.Consumers.Select(c => c.Name).Distinct())));
                List<string> members = r.Members.OrderBy(m => m).ToList();
                detail.Add(new("Members", members.Count.ToString()));
                for (int i = 0; i < members.Count && i < 200; i++)
                    detail.Add(new("#" + (i + 1), members[i]));
                if (members.Count > 200)
                    detail.Add(new("...", $"+{members.Count - 200} more"));
            }
            else if (selected != null)
            {
                KindBlock owner = kinds.First(k => k.Passes.Contains(selected));
                detail.Add(new("Pass", selected.Name));
                detail.Add(new("Runs in", $"{selected.Count} of {owner.Groups.Count} {KindLabel(owner.Kind)} group(s)"));
                detail.Add(new("Reads", string.Join(", ", selected.Accesses.Where(a => !a.IsOutput).Select(a => owner.Resources[a.Resource].PortLabel)) is { Length: > 0 } rd ? rd : "-"));
                detail.Add(new("Writes", string.Join(", ", selected.Accesses.Where(a => a.IsOutput).Select(a => owner.Resources[a.Resource].PortLabel)) is { Length: > 0 } wr ? wr : "-"));
            }
            else
            {
                detail.Add(new("Scopes", kinds.Count.ToString()));
                detail.Add(new("Groups", scope.Count.ToString()));
                detail.Add(new("Passes", kinds.Sum(k => k.Passes.Count).ToString()));
                detail.Add(new("Selection", "None - click a pass or resource"));
            }

            bool stacked = w < 720f;
            float graphWidth = stacked ? w : w - InfoWidth - Metrics.SpacingLarge;
            float infoWidth = stacked ? w : InfoWidth;
            using (_paper.Row("body").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(Metrics.SpacingLarge).WrapContent().LineGap(Metrics.SpacingLarge).Enter())
            {
                Origami.NodeGraph(_paper, "framegraph", graphWidth, FrameGraphHeight)
                    .Nodes(nodes)
                    .Connections(connections)
                    .Groups(groups)
                    .Controller(controller!)
                    .ReadOnly()
                    .Grid()
                    .OnNodeDoubleClick(n =>
                    {
                        if (n.UserData is int instanceIndex)
                        {
                            _selId = "i" + instanceIndex;
                            _tabs[NodeKind.Group] = "Render Graph";
                        }
                        else if ((n.UserData as KindPass ?? (n.UserData as ResourceTarget)?.Pass) is { } pass)
                            _selId = $"i{pass.GroupIndex}p{pass.Pass.Index}";
                    })
                    .OnSelectionChanged(sel => _frameGraphSelection = sel.Nodes.Count > 0 && sel.Nodes[0].UserData is KindPass or ResourceTarget ? sel.Nodes[0].Id : null)
                    .Show();

                using (_paper.Column("detail").Width(infoWidth).Height(UnitValue.Auto).Gap(Metrics.Spacing).Enter())
                {
                    KvGrid(infoWidth, detail, 400f);
                }
            }
        });
    }
}
