// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Xunit;

using static Prowl.Echo.Test.RoundtripTestHelpers;

namespace Prowl.Echo.Test;

// Collection round-trip edge cases: non-string dictionary keys, reserved/empty keys, custom comparers,
// stack/queue ordering, multi-dimensional and jagged arrays, null-vs-empty, and read-only collections.
public class CollectionEdgeCase_Tests
{
    private enum Key : byte { X = 1, Y = 200 }

    [Fact]
    public void Dictionary_EnumKey_Roundtrips()
    {
        var d = new Dictionary<Key, int> { [Key.X] = 1, [Key.Y] = 2 };
        var back = Roundtrip(d);
        Assert.Equal(2, back.Count);
        Assert.Equal(1, back[Key.X]);
        Assert.Equal(2, back[Key.Y]);
    }

    [Fact]
    public void Dictionary_IntKey_Roundtrips()
    {
        var d = new Dictionary<int, string> { [1] = "a", [2] = "b" };
        var back = Roundtrip(d);
        Assert.Equal("a", back[1]);
        Assert.Equal("b", back[2]);
    }

    [Fact]
    public void Dictionary_GuidKey_Roundtrips()
    {
        var g = Guid.NewGuid();
        var d = new Dictionary<Guid, int> { [g] = 42 };
        Assert.Equal(42, Roundtrip(d)[g]);
    }

    [Fact]
    public void Dictionary_ReservedNameStringKeys()
    {
        var d = new Dictionary<string, int> { ["$id"] = 1, ["$type"] = 2, ["array"] = 3 };
        var back = Roundtrip(d);
        Assert.Equal(1, back["$id"]);
        Assert.Equal(2, back["$type"]);
        Assert.Equal(3, back["array"]);
    }

    [Fact]
    public void Dictionary_EmptyStringKey()
    {
        var d = new Dictionary<string, int> { [""] = 7 };
        Assert.Equal(7, Roundtrip(d)[""]);
    }

    [Fact]
    public void Dictionary_CustomComparer_Preserved()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Hello"] = 1 };
        Assert.True(Roundtrip(d).ContainsKey("HELLO"));
    }

    [Fact]
    public void HashSet_CustomComparer_Preserved()
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Hello" };
        Assert.Contains("HELLO", Roundtrip(s));
    }

    [Fact]
    public void Stack_PreservesOrder()
    {
        var s = new Stack<int>();
        s.Push(1); s.Push(2); s.Push(3);
        Assert.Equal(new[] { 3, 2, 1 }, Roundtrip(s).ToArray());
    }

    [Fact]
    public void Queue_PreservesOrder()
    {
        var q = new Queue<int>();
        q.Enqueue(1); q.Enqueue(2); q.Enqueue(3);
        Assert.Equal(new[] { 1, 2, 3 }, Roundtrip(q).ToArray());
    }

    [Fact]
    public void Array2D_Roundtrips()
    {
        var a = new int[,] { { 1, 2 }, { 3, 4 } };
        Assert.Equal(a, Roundtrip(a));
    }

    [Fact]
    public void JaggedArray_Roundtrips()
    {
        var a = new int[][] { new[] { 1 }, new[] { 2, 3 } };
        var back = Roundtrip(a);
        Assert.Single(back[0]);
        Assert.Equal(new[] { 2, 3 }, back[1]);
    }

    [Fact]
    public void Array_WithNullElements()
    {
        var a = new string?[] { "a", null, "c" };
        var back = Roundtrip(a);
        Assert.Equal("a", back[0]);
        Assert.Null(back[1]);
        Assert.Equal("c", back[2]);
    }

    [Fact]
    public void EmptyArray_Roundtrips() => Assert.Empty(Roundtrip(new int[0]));

    [Fact]
    public void NullList_StaysNull() => Assert.Null(Roundtrip(new ListHolder { Items = null }).Items);

    [Fact]
    public void EmptyList_StaysEmptyNotNull()
    {
        var back = Roundtrip(new ListHolder { Items = new() });
        Assert.NotNull(back.Items);
        Assert.Empty(back.Items!);
    }

    [Fact]
    public void ReadOnlyCollection_Roundtrips()
    {
        var col = new System.Collections.ObjectModel.ReadOnlyCollection<int>(new[] { 1, 2, 3 });
        Assert.Equal(new[] { 1, 2, 3 }, Roundtrip(col));
    }

    [Fact]
    public void NestedGenerics_Roundtrip()
    {
        var d = new Dictionary<string, List<int>> { ["a"] = new() { 1, 2 }, ["b"] = new() { 3 } };
        var back = Roundtrip(d);
        Assert.Equal(new[] { 1, 2 }, back["a"]);
        Assert.Equal(new[] { 3 }, back["b"]);
    }

    public class ListHolder { public List<int>? Items; }
}
