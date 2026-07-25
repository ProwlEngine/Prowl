// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using static Prowl.Echo.Test.RoundtripTestHelpers;

namespace Prowl.Echo.Test;

// Reference tracking: cycles, self-references, and shared instance identity for both objects and
// collections, including across text/binary formats and the $id:0 edge case.
public class ReferenceTracking_Tests
{
    public class Node { public string? Name; public Node? Next; }
    public class TwoRefs { public Node? First; public Node? Second; }
    public class ListPair { public List<int>? A; public List<int>? B; }
    public class ArrayPair { public int[]? A; public int[]? B; }

    [Fact]
    public void SelfReference_Roundtrips()
    {
        var n = new Node { Name = "self" };
        n.Next = n;
        var back = Roundtrip(n);
        Assert.Same(back, back.Next);
    }

    [Fact]
    public void Cycle_TwoNodes_Roundtrips()
    {
        var a = new Node { Name = "a" };
        var b = new Node { Name = "b" };
        a.Next = b; b.Next = a;
        var back = Roundtrip(a);
        Assert.Same(back, back.Next!.Next);
    }

    [Fact]
    public void SharedReference_PreservesIdentity()
    {
        var shared = new Node { Name = "shared" };
        var back = Roundtrip(new TwoRefs { First = shared, Second = shared });
        Assert.Same(back.First, back.Second);
    }

    [Fact]
    public void SharedReference_InList_PreservesIdentity()
    {
        var shared = new Node { Name = "s" };
        var back = Roundtrip(new List<Node> { shared, shared });
        Assert.Same(back[0], back[1]);
    }

    [Fact]
    public void SharedReference_SurvivesText()
    {
        var shared = new Node { Name = "s" };
        var back = RoundtripText(new TwoRefs { First = shared, Second = shared });
        Assert.Same(back.First, back.Second);
    }

    [Fact]
    public void SharedReference_SurvivesBinary()
    {
        var shared = new Node { Name = "s" };
        var back = RoundtripBinary(new TwoRefs { First = shared, Second = shared });
        Assert.Same(back.First, back.Second);
    }

    [Fact]
    public void SharedCollectionInstance_KeepsIdentity()
    {
        var shared = new List<int> { 1, 2, 3 };
        var back = Roundtrip(new ListPair { A = shared, B = shared });
        Assert.Same(back.A, back.B);
    }

    [Fact]
    public void SharedArrayInstance_KeepsIdentity()
    {
        var shared = new[] { 1, 2, 3 };
        var back = Roundtrip(new ArrayPair { A = shared, B = shared });
        Assert.Same(back.A, back.B);
    }

    [Fact]
    public void SharedHashSetInstance_KeepsIdentity()
    {
        var shared = new HashSet<int> { 1, 2, 3 };
        var back = Roundtrip((shared, shared));
        Assert.Same(back.Item1, back.Item2);
    }

    [Fact]
    public void SharedDictionaryInstance_KeepsIdentity()
    {
        var shared = new Dictionary<string, int> { ["a"] = 1 };
        var back = Roundtrip((shared, shared));
        Assert.Same(back.Item1, back.Item2);
    }

    [Fact]
    public void SelfContainingList_Roundtrips()
    {
        var a = new List<object>();
        a.Add(a);
        var back = Roundtrip(a);
        Assert.Single(back);
        Assert.Same(back, back[0]);
    }

    [Fact]
    public void SelfContainingDictionary_Roundtrips()
    {
        var d = new Dictionary<string, object>();
        d["self"] = d;
        var back = Roundtrip(d);
        Assert.Same(back, back["self"]);
    }

    [Fact]
    public void DefinitionWithIdZero_NotSwallowedBySentinel()
    {
        var def = EchoObject.NewCompound();
        def["$id"] = new EchoObject(EchoType.Int, 0);
        def["Name"] = new EchoObject(EchoType.String, "Hello");
        var node = Serializer.Deserialize(def, typeof(Node), new SerializationContext()) as Node;
        Assert.NotNull(node);
        Assert.Equal("Hello", node!.Name);
    }
}
