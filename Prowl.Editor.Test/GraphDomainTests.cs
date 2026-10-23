// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.GraphEditor;
using Prowl.OrigamiUI;
using Prowl.Runtime;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The shared graph editor's edits, checked on a domain with nothing animation about it: two inputs "a"
/// and "b", one input "hidden" the card never shows, and one output.
/// </summary>
public class GraphDomainTests
{
    private sealed class Node
    {
        public string Id = "";
        public string Owner = "";
        public Float2 Position;
        public bool Collapsed;
        public Dictionary<string, string> Wires = [];

        public Node Clone() => new() { Id = Id, Owner = Owner, Position = Position, Collapsed = Collapsed, Wires = new(Wires) };
    }

    private sealed class Domain(AnimationGraph holder) : GraphDomain<Node>(holder)
    {
        public static readonly PortKind Number = new("Number", "Number", System.Drawing.Color.White);

        private readonly List<Node> _nodes = [];
        private int _next;

        public override List<Node> Nodes => _nodes;
        public override List<GraphGroupRecord> Groups => holder.Groups;
        public override List<GraphNoteRecord> Notes => holder.Notes;
        public override EchoObject Snapshot() => throw new NotSupportedException();
        public override bool Restore(EchoObject snapshot, bool compiles) => throw new NotSupportedException();
        public override void Invalidate() { }
        public override string NewId(IdKind kind) => (kind == IdKind.Node ? "n" : "s") + _next++;
        public override string IdOf(Node record) => record.Id;
        public override string TypeOf(Node record) => "node";
        public override string TitleOf(Node record) => record.Id;
        public override string OwnerOf(Node record) => record.Owner;
        public override void SetOwner(Node record, string owner) => record.Owner = owner;
        public override Float2 PositionOf(Node record) => record.Position;
        public override void SetPosition(Node record, Float2 position) => record.Position = position;
        public override bool CollapsedOf(Node record) => record.Collapsed;
        public override void SetCollapsed(Node record, bool collapsed) => record.Collapsed = collapsed;
        public override Node Clone(Node record) => record.Clone();

        public override void RemapIds(Node record, Func<string, string> remap)
        {
            record.Id = remap(record.Id);
            record.Owner = remap(record.Owner);
            foreach (string port in record.Wires.Keys.ToList()) record.Wires[port] = remap(record.Wires[port]);
        }

        public override IReadOnlyList<PortInfo> Inputs(Node record) => [Port("a"), Port("b")];
        public override IReadOnlyList<PortInfo> Outputs(Node record) => [Port("out")];
        public override PortInfo? InputPort(Node record, string portId) => portId is "a" or "b" or "hidden" ? Port(portId) : null;
        public override PortInfo? PortAt(Node record, int index) => null;

        public override IEnumerable<(string Port, WireEnd Source)> AllSources(Node record)
        {
            foreach ((string port, string from) in record.Wires)
                if (from.Length > 0) yield return (port, new WireEnd(from, "out"));
        }

        public override WireEnd? Source(Node record, string inputPort)
            => record.Wires.TryGetValue(inputPort, out string? from) && from.Length > 0 ? new WireEnd(from, "out") : null;

        public override void SetSource(Node record, string inputPort, WireEnd? source) => record.Wires[inputPort] = source?.Node ?? "";

        public override IReadOnlyList<NodeTypeInfo> Catalog => [];

        public override Node? Create(string typeId, Float2 at, string owner)
        {
            var node = new Node { Id = NewId(IdKind.Node), Position = at, Owner = owner };
            _nodes.Add(node);
            return node;
        }

        private static PortInfo Port(string id) => new() { Id = id, Name = id, Label = id, Kind = Number, Optional = true, Index = -1 };
    }

    private static Domain NewDomain() => new(new AnimationGraph());

    private static void Wire(Domain domain, Node from, Node to, string port) => domain.SetSource(to, port, new WireEnd(from.Id, "out"));

    [Fact]
    public void ALoopThroughAHiddenInputIsStillALoop()
    {
        Domain domain = NewDomain();
        Node a = domain.Create("node", default, "")!;
        Node b = domain.Create("node", default, "")!;

        Wire(domain, b, a, "hidden");
        Assert.False(domain.CanConnect(new ConnectionRequest(a.Id, "out", b.Id, "a")));

        Wire(domain, a, b, "a");
        Assert.Contains(domain.FindProblems(""), p => p.Blocking && p.Message.Contains("feeds itself"));
    }

    [Fact]
    public void DeletingANodeClearsHiddenWiresIntoIt()
    {
        Domain domain = NewDomain();
        Node a = domain.Create("node", default, "")!;
        Node b = domain.Create("node", default, "")!;
        Wire(domain, a, b, "hidden");

        domain.Delete([a], []);

        Assert.Null(domain.Source(b, "hidden"));
    }

    [Fact]
    public void PastingRemapsWiresBetweenCopiesAndEmptiesWiresFromOutside()
    {
        Domain domain = NewDomain();
        Node outside = domain.Create("node", default, "")!;
        Node a = domain.Create("node", default, "")!;
        Node b = domain.Create("node", default, "")!;
        Wire(domain, a, b, "a");
        Wire(domain, outside, b, "hidden");

        List<Node> copies = domain.Paste(domain.Capture([a, b]), new Float2(10, 0), "");
        Node aCopy = copies[0], bCopy = copies[1];

        Assert.Equal(aCopy.Id, domain.Source(bCopy, "a")!.Value.Node);
        Assert.Null(domain.Source(bCopy, "hidden"));
        Assert.NotEqual(a.Id, aCopy.Id);
        Assert.Equal(new Float2(10, 0), aCopy.Position);
    }

    [Fact]
    public void PastingMovesWhatIsInsideAnOwnerToTheCopysScope()
    {
        Domain domain = NewDomain();
        Node owner = domain.Create("node", default, "")!;
        Node inside = domain.Create("node", default, owner.Id)!;

        Node copy = domain.Paste(domain.Capture([owner]), default, "").Single();

        Node copiedInside = domain.Nodes.Single(n => n.Owner == copy.Id);
        Assert.NotSame(inside, copiedInside);
        Assert.Equal(4, domain.Nodes.Count);
    }

    [Fact]
    public void DeletingAnOwnerTakesWhatIsInsideIt()
    {
        Domain domain = NewDomain();
        Node owner = domain.Create("node", default, "")!;
        domain.Create("node", default, owner.Id);
        domain.AddNote(default, owner.Id);

        domain.Delete([owner], []);

        Assert.Empty(domain.Nodes);
        Assert.Empty(domain.Notes);
    }
}
