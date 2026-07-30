using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>Migration tests for collections: lists, dictionaries, sets, queues, immutable/frozen/concurrent variants, subclasses, and custom implementations.</summary>
[Trait("Category", "Build")]
public class CollectionMigrationTests : MigrationTestBase
{
    private static object[] Items(object collection) => ((IEnumerable)collection).Cast<object>().ToArray();
    private static int Id(object e) => (int)e.GetType().GetField("Id")!.GetValue(e)!;

    private const string EDef = "public class E { public int Id; }";
    private const string EDef2 = "public class E { public int Id; public int Extra; }";

    [Fact]
    public void Migrate_DictionarySubclass_SwappedKey_StillResolves()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " + EDef +
            "public class MyDict : Dictionary<E,int> {} " +
            "public static class H { public static E First; public static MyDict M = new(); " +
            "public static void Setup(){ First = new E{Id=1}; M[First]=10; M[new E{Id=2}]=20; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; " + EDef2 +
            "public class MyDict : Dictionary<E,int> {} " +
            "public static class H { public static E First; public static MyDict M = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object first = hV2.GetField("First")!.GetValue(null)!;
        var dict = (IDictionary)hV2.GetField("M")!.GetValue(null)!;
        Assert.Equal(2, dict.Count);
        Assert.True(dict.Contains(first), "migrated key must still resolve in the dictionary subclass");
        Assert.Equal(10, dict[first]);
    }

    [Fact]
    public void Migrate_HashSetSubclass_SwappedElement_StillContains()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " + EDef +
            "public class MySet : HashSet<E> {} " +
            "public static class H { public static E Shared; public static MySet S = new(); " +
            "public static void Setup(){ Shared = new E{Id=1}; S.Add(Shared); S.Add(new E{Id=2}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; " + EDef2 +
            "public class MySet : HashSet<E> {} " +
            "public static class H { public static E Shared; public static MySet S = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var set = hV2.GetField("S")!.GetValue(null)!;
        Assert.Equal(2, Items(set).Length);
        bool contains = (bool)set.GetType().GetMethod("Contains")!.Invoke(set, new[] { shared })!;
        Assert.True(contains, "migrated element must still be found in the HashSet subclass");
    }

    [Fact]
    public void Migrate_ImmutableList_OfSwappedType()
    {
        Assembly v1 = Compile("using System.Collections.Immutable; " + EDef +
            "public static class H { public static E Shared; public static ImmutableList<E> L; " +
            "public static void Setup(){ Shared = new E{Id=1}; L = ImmutableList.Create(Shared, new E{Id=2}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Immutable; " + EDef2 +
            "public static class H { public static E Shared; public static ImmutableList<E> L; public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var items = Items(hV2.GetField("L")!.GetValue(null)!);
        Assert.Equal(2, items.Length);
        Assert.Same(eV2, items[0].GetType());
        Assert.Equal(new[] { 1, 2 }, items.Select(Id));
        Assert.Same(shared, items[0]);
    }

    [Fact]
    public void Migrate_ImmutableDictionary_SwappedKey_StillResolves()
    {
        Assembly v1 = Compile("using System.Collections.Immutable; " + EDef +
            "public static class H { public static E First; public static ImmutableDictionary<E,int> M; " +
            "public static void Setup(){ First = new E{Id=1}; M = ImmutableDictionary<E,int>.Empty.Add(First,10).Add(new E{Id=2},20); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Immutable; " + EDef2 +
            "public static class H { public static E First; public static ImmutableDictionary<E,int> M; public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object first = hV2.GetField("First")!.GetValue(null)!;
        object dict = hV2.GetField("M")!.GetValue(null)!;
        var containsKey = (bool)dict.GetType().GetMethod("ContainsKey")!.Invoke(dict, new[] { first })!;
        Assert.True(containsKey, "migrated key must resolve in the ImmutableDictionary");
    }

    [Fact]
    public void Migrate_ImmutableArray_OfSwappedType()
    {
        Assembly v1 = Compile("using System.Collections.Immutable; " + EDef +
            "public static class H { public static E Shared; public static ImmutableArray<E> A; " +
            "public static void Setup(){ Shared = new E{Id=1}; A = ImmutableArray.Create(Shared, new E{Id=2}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Immutable; " + EDef2 +
            "public static class H { public static E Shared; public static ImmutableArray<E> A; public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var items = Items(hV2.GetField("A")!.GetValue(null)!);
        Assert.Equal(2, items.Length);
        Assert.Same(eV2, items[0].GetType());
        Assert.Same(shared, items[0]);
    }

    [Fact]
    public void Migrate_ImmutableHashSet_SwappedElement_StillContains()
    {
        Assembly v1 = Compile("using System.Collections.Immutable; " + EDef +
            "public static class H { public static E Shared; public static ImmutableHashSet<E> S; " +
            "public static void Setup(){ Shared = new E{Id=1}; S = ImmutableHashSet<E>.Empty.Add(Shared).Add(new E{Id=2}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Immutable; " + EDef2 +
            "public static class H { public static E Shared; public static ImmutableHashSet<E> S; public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object set = hV2.GetField("S")!.GetValue(null)!;
        var contains = (bool)set.GetType().GetMethod("Contains")!.Invoke(set, new[] { shared })!;
        Assert.True(contains, "migrated element must resolve in the ImmutableHashSet");
    }

    [Fact]
    public void Migrate_FrozenDictionary_SwappedKey_StillResolves()
    {
        Assembly v1 = Compile("using System.Collections.Generic; using System.Collections.Frozen; " + EDef +
            "public static class H { public static E First; public static FrozenDictionary<E,int> M; " +
            "public static void Setup(){ First = new E{Id=1}; var d = new Dictionary<E,int>{ {First,10}, {new E{Id=2},20} }; M = d.ToFrozenDictionary(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; using System.Collections.Frozen; " + EDef2 +
            "public static class H { public static E First; public static FrozenDictionary<E,int> M; public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object first = hV2.GetField("First")!.GetValue(null)!;
        object dict = hV2.GetField("M")!.GetValue(null)!;
        var containsKey = (bool)dict.GetType().GetMethod("ContainsKey")!.Invoke(dict, new[] { first })!;
        Assert.True(containsKey, "migrated key must resolve in the FrozenDictionary");
    }

    [Fact]
    public void Migrate_FrozenSet_SwappedElement_StillContains()
    {
        Assembly v1 = Compile("using System.Collections.Generic; using System.Collections.Frozen; " + EDef +
            "public static class H { public static E Shared; public static FrozenSet<E> S; " +
            "public static void Setup(){ Shared = new E{Id=1}; S = new HashSet<E>{ Shared, new E{Id=2} }.ToFrozenSet(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; using System.Collections.Frozen; " + EDef2 +
            "public static class H { public static E Shared; public static FrozenSet<E> S; public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object set = hV2.GetField("S")!.GetValue(null)!;
        var contains = (bool)set.GetType().GetMethod("Contains")!.Invoke(set, new[] { shared })!;
        Assert.True(contains, "migrated element must resolve in the FrozenSet");
    }

    [Fact]
    public void Migrate_ConcurrentStack_OfSwappedType()
    {
        Assembly v1 = Compile("using System.Collections.Concurrent; " + EDef +
            "public static class H { public static E Shared; public static ConcurrentStack<E> S = new(); " +
            "public static void Setup(){ Shared = new E{Id=1}; S.Push(new E{Id=2}); S.Push(Shared); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Concurrent; " + EDef2 +
            "public static class H { public static E Shared; public static ConcurrentStack<E> S = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var items = Items(hV2.GetField("S")!.GetValue(null)!);
        Assert.Equal(2, items.Length);
        Assert.Same(eV2, items[0].GetType());
        Assert.Equal(new[] { 1, 2 }, items.Select(Id)); // stack enumerates top (last pushed) first
        Assert.Same(shared, items[0]);
    }

    [Fact]
    public void Migrate_ConcurrentBag_OfSwappedType()
    {
        Assembly v1 = Compile("using System.Collections.Concurrent; " + EDef +
            "public static class H { public static E Shared; public static ConcurrentBag<E> B = new(); " +
            "public static void Setup(){ Shared = new E{Id=1}; B.Add(Shared); B.Add(new E{Id=2}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Concurrent; " + EDef2 +
            "public static class H { public static E Shared; public static ConcurrentBag<E> B = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var items = Items(hV2.GetField("B")!.GetValue(null)!);
        Assert.Equal(2, items.Length);
        Assert.All(items, x => Assert.Same(eV2, x.GetType()));
        Assert.Contains(items, x => ReferenceEquals(x, shared));
        Assert.Equal(new[] { 1, 2 }, items.Select(Id).OrderBy(x => x));
    }

    [Fact]
    public void Migrate_KeyedCollection_OfSwappedType_StillResolvesByKey()
    {
        const string kc = "public class ById : System.Collections.ObjectModel.KeyedCollection<int,E> { protected override int GetKeyForItem(E e) => e.Id; }";
        Assembly v1 = Compile(EDef + kc +
            "public static class H { public static E Shared; public static ById C = new(); " +
            "public static void Setup(){ Shared = new E{Id=1}; C.Add(Shared); C.Add(new E{Id=2}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(EDef2 + kc +
            "public static class H { public static E Shared; public static ById C = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object coll = hV2.GetField("C")!.GetValue(null)!;
        Assert.Equal(2, Items(coll).Length);
        object byKey = coll.GetType().GetProperty("Item", new[] { typeof(int) })!.GetValue(coll, new object[] { 1 })!;
        Assert.Same(eV2, byKey.GetType());
        Assert.Same(shared, byKey);
    }

    [Fact]
    public void Migrate_ObservableCollection_OfSwappedType()
    {
        Assembly v1 = Compile("using System.Collections.ObjectModel; " + EDef +
            "public static class H { public static E Shared; public static ObservableCollection<E> C = new(); " +
            "public static void Setup(){ Shared = new E{Id=1}; C.Add(Shared); C.Add(new E{Id=2}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.ObjectModel; " + EDef2 +
            "public static class H { public static E Shared; public static ObservableCollection<E> C = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var items = Items(hV2.GetField("C")!.GetValue(null)!);
        Assert.Equal(2, items.Length);
        Assert.Same(eV2, items[0].GetType());
        Assert.Same(shared, items[0]);
    }

    [Fact]
    public void Migrate_DictionaryWithCustomComparer_SwappedKey_StillResolves()
    {
        const string cmp = "public class ByIdCmp : System.Collections.Generic.IEqualityComparer<E> { public bool Equals(E a, E b) => a.Id == b.Id; public int GetHashCode(E e) => e.Id; }";
        Assembly v1 = Compile("using System.Collections.Generic; " + EDef + cmp +
            "public static class H { public static E First; public static Dictionary<E,int> M = new(new ByIdCmp()); " +
            "public static void Setup(){ First = new E{Id=1}; M[First]=10; M[new E{Id=2}]=20; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; " + EDef2 + cmp +
            "public static class H { public static E First; public static Dictionary<E,int> M = new(new ByIdCmp()); public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object first = hV2.GetField("First")!.GetValue(null)!;
        var dict = (IDictionary)hV2.GetField("M")!.GetValue(null)!;
        Assert.Equal(2, dict.Count);
        Assert.True(dict.Contains(first));
        Assert.Equal(10, dict[first]);
    }

    [Fact]
    public void Migrate_DictionaryWithSwappedEnumKey_StillResolves()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public enum Team { Red, Blue, Green } " + EDef +
            "public static class H { public static Dictionary<Team,E> M = new(); public static E Blue; " +
            "public static void Setup(){ Blue = new E{Id=1}; M[Team.Blue]=Blue; M[Team.Red]=new E{Id=2}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; public enum Team { Red, Blue, Green } " + EDef2 +
            "public static class H { public static Dictionary<Team,E> M = new(); public static E Blue; public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        Type teamV2 = v2.GetType("Team")!;
        object blue = hV2.GetField("Blue")!.GetValue(null)!;
        var dict = (IDictionary)hV2.GetField("M")!.GetValue(null)!;
        Assert.Equal(2, dict.Count);
        object key = Enum.Parse(teamV2, "Blue");
        Assert.Same(blue, dict[key]);
    }

    [Fact]
    public void Migrate_MultidimensionalArray_OfSwappedType()
    {
        Assembly v1 = Compile(EDef +
            "public static class H { public static E Shared; public static E[,] Grid = new E[2,2]; " +
            "public static void Setup(){ Shared = new E{Id=1}; Grid[0,0]=Shared; Grid[1,1]=new E{Id=4}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(EDef2 +
            "public static class H { public static E Shared; public static E[,] Grid = new E[2,2]; public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var grid = (Array)hV2.GetField("Grid")!.GetValue(null)!;
        object cell = grid.GetValue(0, 0)!;
        Assert.Same(eV2, cell.GetType());
        Assert.Same(shared, cell);
        Assert.Equal(4, Id(grid.GetValue(1, 1)!));
    }

    [Fact]
    public void Migrate_JaggedArray_OfSwappedType()
    {
        Assembly v1 = Compile(EDef +
            "public static class H { public static E Shared; public static E[][] J; " +
            "public static void Setup(){ Shared = new E{Id=1}; J = new E[][]{ new E[]{ Shared, new E{Id=2} }, new E[]{ new E{Id=3} } }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(EDef2 +
            "public static class H { public static E Shared; public static E[][] J; public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var jagged = (Array)hV2.GetField("J")!.GetValue(null)!;
        var row0 = (Array)jagged.GetValue(0)!;
        Assert.Same(eV2, row0.GetValue(0)!.GetType());
        Assert.Same(shared, row0.GetValue(0));
        Assert.Equal(3, Id(((Array)jagged.GetValue(1)!).GetValue(0)!));
    }

    [Fact]
    public void Migrate_CustomCollectionSubclass_OfSwappedType()
    {
        Assembly v1 = Compile("using System.Collections.ObjectModel; " + EDef +
            "public class Bag : Collection<E> {} " +
            "public static class H { public static E Shared; public static Bag B = new(); public static void Setup(){ Shared = new E{Id=1}; B.Add(Shared); B.Add(new E{Id=2}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.ObjectModel; " + EDef2 +
            "public class Bag : Collection<E> {} public static class H { public static E Shared; public static Bag B = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var items = ((IEnumerable)hV2.GetField("B")!.GetValue(null)!).Cast<object>().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Same(eV2, items[0].GetType());
        Assert.Same(shared, items[0]);
    }

    [Fact]
    public void Migrate_CustomIListImplementation_OfSwappedType()
    {
        const string list = "public class MyList : System.Collections.Generic.IList<E> { " +
            "public E[] Data = new E[4]; public int N; " +
            "public E this[int i] { get => Data[i]; set => Data[i] = value; } public int Count => N; public bool IsReadOnly => false; " +
            "public void Add(E e){ Data[N++] = e; } public void Clear(){ N=0; } public bool Contains(E e)=>System.Array.IndexOf(Data,e)>=0; " +
            "public void CopyTo(E[] a,int i){} public int IndexOf(E e)=>System.Array.IndexOf(Data,e); public void Insert(int i,E e){} public bool Remove(E e)=>false; public void RemoveAt(int i){} " +
            "public System.Collections.Generic.IEnumerator<E> GetEnumerator(){ for(int i=0;i<N;i++) yield return Data[i]; } " +
            "System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator(); }";
        Assembly v1 = Compile(EDef + list +
            "public static class H { public static E Shared; public static MyList L = new(); public static void Setup(){ Shared = new E{Id=1}; L.Add(Shared); L.Add(new E{Id=2}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(EDef2 + list +
            "public static class H { public static E Shared; public static MyList L = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object myList = hV2.GetField("L")!.GetValue(null)!;
        object item0 = myList.GetType().GetProperty("Item")!.GetValue(myList, new object[] { 0 })!;
        Assert.Same(eV2, item0.GetType());
        Assert.Same(shared, item0);
    }

    [Fact]
    public void Migrate_DictionarySubclass_IntKey_StillResolves()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " + EDef +
            "public class MyMap : Dictionary<int,E> {} " +
            "public static class H { public static E Shared; public static MyMap M = new(); public static void Setup(){ Shared = new E{Id=1}; M[1]=Shared; M[2]=new E{Id=2}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; " + EDef2 +
            "public class MyMap : Dictionary<int,E> {} public static class H { public static E Shared; public static MyMap M = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var dict = (IDictionary)hV2.GetField("M")!.GetValue(null)!;
        Assert.Same(shared, dict[1]); // int keys hash stably, so the subclass still resolves
    }

    [Fact]
    public void UpdateReferences_MigratesListAndArrayElements_ToNewType()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; " +
            "public static class Store { public static List<Item> Items; public static Item[] Extras; public static Item First; } " +
            "public class Item { public int Id; }");

        Type itemV1 = v1.GetType("Item")!;
        Type storeV1 = v1.GetType("Store")!;
        object i1 = Activator.CreateInstance(itemV1)!;
        object i2 = Activator.CreateInstance(itemV1)!;
        itemV1.GetField("Id")!.SetValue(i1, 1);
        itemV1.GetField("Id")!.SetValue(i2, 2);

        Type listV1 = typeof(System.Collections.Generic.List<>).MakeGenericType(itemV1);
        var listInstance = (System.Collections.IList)Activator.CreateInstance(listV1)!;
        listInstance.Add(i1);
        listInstance.Add(i2);
        storeV1.GetField("Items")!.SetValue(null, listInstance);

        Array arrV1 = Array.CreateInstance(itemV1, 1);
        arrV1.SetValue(i1, 0);
        storeV1.GetField("Extras")!.SetValue(null, arrV1);
        storeV1.GetField("First")!.SetValue(null, i1);

        Assembly v2 = Compile(
            "using System.Collections.Generic; " +
            "public static class Store { public static List<Item> Items; public static Item[] Extras; public static Item First; } " +
            "public class Item { public int Id; public int Rank; }");

        Migrate(v1, v2);

        Type itemV2 = v2.GetType("Item")!;
        Type storeV2 = v2.GetType("Store")!;
        var newList = (System.Collections.IList)storeV2.GetField("Items")!.GetValue(null)!;
        var newArr = (Array)storeV2.GetField("Extras")!.GetValue(null)!;
        object newFirst = storeV2.GetField("First")!.GetValue(null)!;

        Assert.Equal(2, newList.Count);
        Assert.Same(itemV2, newList[0]!.GetType());
        Assert.Equal(1, itemV2.GetField("Id")!.GetValue(newList[0]));
        Assert.Equal(2, itemV2.GetField("Id")!.GetValue(newList[1]));
        Assert.Same(itemV2, newArr.GetType().GetElementType());
        Assert.Same(newList[0], newArr.GetValue(0));
        Assert.Same(newList[0], newFirst);
    }

    [Fact]
    public void UpdateReferences_RebuildsDictionary_OntoNewValueType()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; " +
            "public static class Reg { public static Dictionary<string, Item> Items; public static Item Special; } " +
            "public class Item { public int Id; }");

        Type itemV1 = v1.GetType("Item")!;
        Type regV1 = v1.GetType("Reg")!;
        object a = Activator.CreateInstance(itemV1)!;
        object b = Activator.CreateInstance(itemV1)!;
        itemV1.GetField("Id")!.SetValue(a, 10);
        itemV1.GetField("Id")!.SetValue(b, 20);

        Type dictV1 = typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(typeof(string), itemV1);
        var dict = (System.Collections.IDictionary)Activator.CreateInstance(dictV1)!;
        dict["a"] = a;
        dict["b"] = b;
        regV1.GetField("Items")!.SetValue(null, dict);
        regV1.GetField("Special")!.SetValue(null, b);

        Assembly v2 = Compile(
            "using System.Collections.Generic; " +
            "public static class Reg { public static Dictionary<string, Item> Items; public static Item Special; } " +
            "public class Item { public int Id; public bool Flag; }");

        Migrate(v1, v2);

        Type itemV2 = v2.GetType("Item")!;
        Type regV2 = v2.GetType("Reg")!;
        var newDict = (System.Collections.IDictionary)regV2.GetField("Items")!.GetValue(null)!;
        object newSpecial = regV2.GetField("Special")!.GetValue(null)!;

        Assert.Equal(2, newDict.Count);
        Assert.Same(itemV2, newDict["a"]!.GetType());
        Assert.Equal(10, itemV2.GetField("Id")!.GetValue(newDict["a"]));
        Assert.Equal(20, itemV2.GetField("Id")!.GetValue(newDict["b"]));
        Assert.Same(newDict["b"], newSpecial);
    }

    [Fact]
    public void Migrate_QueueStackLinkedList_OfSwappedType()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static E Shared; public static Queue<E> Q = new(); public static Stack<E> S = new(); public static LinkedList<E> L = new(); " +
            "public static void Setup() { Shared = new E{Id=1}; Q.Enqueue(Shared); Q.Enqueue(new E{Id=2}); S.Push(new E{Id=3}); S.Push(Shared); L.AddLast(Shared); L.AddLast(new E{Id=4}); } }");
        Type hV1 = v1.GetType("H")!;
        hV1.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static E Shared; public static Queue<E> Q = new(); public static Stack<E> S = new(); public static LinkedList<E> L = new(); public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        FieldInfo id = eV2.GetField("Id")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;

        var q = Items(hV2.GetField("Q")!.GetValue(null)!);
        var s = Items(hV2.GetField("S")!.GetValue(null)!);
        var l = Items(hV2.GetField("L")!.GetValue(null)!);

        Assert.Same(eV2, q[0].GetType());
        Assert.Equal(new[] { 1, 2 }, q.Select(x => (int)id.GetValue(x)!));
        Assert.Equal(new[] { 1, 3 }, s.Select(x => (int)id.GetValue(x)!)); // stack pops last-in-first, enumerates top first
        Assert.Equal(new[] { 1, 4 }, l.Select(x => (int)id.GetValue(x)!));
        Assert.Same(shared, q[0]);
        Assert.Same(shared, s[0]);
        Assert.Same(shared, l[0]);
    }

    [Fact]
    public void Migrate_ConcurrentQueue_OfSwappedType()
    {
        Assembly v1 = Compile(
            "using System.Collections.Concurrent; public class E { public int Id; } " +
            "public static class H { public static E Shared; public static ConcurrentQueue<E> Q = new(); " +
            "public static void Setup() { Shared = new E{Id=1}; Q.Enqueue(Shared); Q.Enqueue(new E{Id=2}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System.Collections.Concurrent; public class E { public int Id; public int Extra; } " +
            "public static class H { public static E Shared; public static ConcurrentQueue<E> Q = new(); public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        var q = Items(hV2.GetField("Q")!.GetValue(null)!);
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        FieldInfo id = eV2.GetField("Id")!;

        Assert.Same(eV2, q[0].GetType());
        Assert.Equal(new[] { 1, 2 }, q.Select(x => (int)id.GetValue(x)!));
        Assert.Same(shared, q[0]);
    }

    [Fact]
    public void Migrate_DictionaryWithSwappedKeyType()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; public class K { public int N; } " +
            "public static class H { public static K First; public static Dictionary<K,string> M = new(); " +
            "public static void Setup() { First = new K{N=1}; M[First] = \"one\"; M[new K{N=2}] = \"two\"; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System.Collections.Generic; public class K { public int N; public int Extra; } " +
            "public static class H { public static K First; public static Dictionary<K,string> M = new(); public static void Setup() { } }");
        Migrate(v1, v2);

        Type kV2 = v2.GetType("K")!;
        Type hV2 = v2.GetType("H")!;
        object first = hV2.GetField("First")!.GetValue(null)!;
        var dict = (IDictionary)hV2.GetField("M")!.GetValue(null)!;

        Assert.Equal(2, dict.Count);
        Assert.Same(kV2, first.GetType());
        Assert.Equal("one", dict[first]);
    }

    [Fact]
    public void Migrate_NestedGenericCollections()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static E Shared; public static List<List<E>> LL = new(); public static Dictionary<string,List<E>> DL = new(); " +
            "public static void Setup() { Shared = new E{Id=1}; LL.Add(new List<E>{ Shared, new E{Id=2} }); DL[\"k\"] = new List<E>{ Shared }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static E Shared; public static List<List<E>> LL = new(); public static Dictionary<string,List<E>> DL = new(); public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;

        var ll = (IList)hV2.GetField("LL")!.GetValue(null)!;
        var inner = (IList)ll[0]!;
        Assert.Same(eV2, inner[0]!.GetType());
        Assert.Same(shared, inner[0]);

        var dl = (IDictionary)hV2.GetField("DL")!.GetValue(null)!;
        var dlInner = (IList)dl["k"]!;
        Assert.Same(shared, dlInner[0]);
    }

    [Fact]
    public void Migrate_SortedSetAndSortedList_OfSwappedType()
    {
        Assembly v1 = Compile(
            "using System; using System.Collections.Generic; " +
            "public class Item : IComparable<Item> { public int Id; public int CompareTo(Item o) => Id.CompareTo(o.Id); } " +
            "public static class H { public static Item Shared; public static SortedSet<Item> SS = new(); public static SortedList<int,Item> SL = new(); " +
            "public static void Setup() { Shared = new Item{Id=2}; SS.Add(new Item{Id=3}); SS.Add(Shared); SS.Add(new Item{Id=1}); SL[5] = Shared; SL[1] = new Item{Id=9}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; using System.Collections.Generic; " +
            "public class Item : IComparable<Item> { public int Id; public int Extra; public int CompareTo(Item o) => Id.CompareTo(o.Id); } " +
            "public static class H { public static Item Shared; public static SortedSet<Item> SS = new(); public static SortedList<int,Item> SL = new(); public static void Setup() { } }");
        Migrate(v1, v2);

        Type itemV2 = v2.GetType("Item")!;
        Type hV2 = v2.GetType("H")!;
        FieldInfo id = itemV2.GetField("Id")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;

        var ss = Items(hV2.GetField("SS")!.GetValue(null)!);
        Assert.Same(itemV2, ss[0].GetType());
        Assert.Equal(new[] { 1, 2, 3 }, ss.Select(x => (int)id.GetValue(x)!));
        Assert.Same(shared, ss[1]);

        var sl = (IDictionary)hV2.GetField("SL")!.GetValue(null)!;
        Assert.Same(shared, sl[5]);
    }

    [Fact]
    public void Migrate_RemovedTypeCollection_DoesNotCrash_AndSiblingsSurvive()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; public class Enemy { public int Id; } public class Deleted { public int X; } " +
            "public static class H { public static List<object> Stuff = new(); public static object Dict; " +
            "public static void Setup() { Stuff.Add(new Enemy{Id=1}); Stuff.Add(new List<Deleted>{ new Deleted{X=9} }); Stuff.Add(new Enemy{Id=2}); Dict = new Dictionary<Deleted,int>{ { new Deleted(), 1 } }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System.Collections.Generic; public class Enemy { public int Id; public int Extra; } " +
            "public static class H { public static List<object> Stuff = new(); public static object Dict; public static void Setup() { } }");
        Migrate(v1, v2);

        Type enemyV2 = v2.GetType("Enemy")!;
        Type hV2 = v2.GetType("H")!;
        var stuff = (IList)hV2.GetField("Stuff")!.GetValue(null)!;

        Assert.Equal(3, stuff.Count);
        Assert.Same(enemyV2, stuff[0]!.GetType());
        Assert.Equal(1, enemyV2.GetField("Id")!.GetValue(stuff[0]));
        Assert.Null(stuff[1]);
        Assert.Same(enemyV2, stuff[2]!.GetType());
        Assert.Equal(2, enemyV2.GetField("Id")!.GetValue(stuff[2]));
        Assert.Null(hV2.GetField("Dict")!.GetValue(null));
    }

    private const string ValueHashKey =
        "public class Key { public int Id; " +
        "  public override int GetHashCode() => Id; " +
        "  public override bool Equals(object o) => o is Key k && k.Id == Id; } ";

    private const string ValueHashKeyV2 =
        "public class Key { public int Id; public int Extra; " +
        "  public override int GetHashCode() => Id; " +
        "  public override bool Equals(object o) => o is Key k && k.Id == Id; } ";

    // The same shape through the mutable path, which defers re-insertion until the keys are complete.
    [Fact]
    public void Dictionary_ValueHashedKey_StillResolves()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " + ValueHashKey +
            "public static class H { public static Dictionary<Key, int> M = new(); " +
            "  public static void Setup() { M[new Key { Id = 5 }] = 50; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; " + ValueHashKeyV2 +
            "public static class H { public static Dictionary<Key, int> M = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        Type keyV2 = v2.GetType("Key")!;
        object probe = Activator.CreateInstance(keyV2)!;
        keyV2.GetField("Id")!.SetValue(probe, 5);

        var map = (IDictionary)v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        Assert.True(map.Contains(probe), "the migrated key could not be found by an equal key");
    }

    [Fact]
    public void ConcurrentDictionary_ValueHashedKey_StillResolves()
    {
        Assembly v1 = Compile("using System.Collections.Concurrent; " + ValueHashKey +
            "public static class H { public static ConcurrentDictionary<Key, int> M = new(); " +
            "  public static void Setup() { M[new Key { Id = 5 }] = 50; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Concurrent; " + ValueHashKeyV2 +
            "public static class H { public static ConcurrentDictionary<Key, int> M = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        Type keyV2 = v2.GetType("Key")!;
        object probe = Activator.CreateInstance(keyV2)!;
        keyV2.GetField("Id")!.SetValue(probe, 5);

        var map = (IDictionary)v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        Assert.True(map.Contains(probe));
    }

    [Fact]
    public void StructKey_WithValueHash_StillResolves()
    {
        const string key =
            "public struct SKey { public int Id; public string Tag; " +
            "  public override int GetHashCode() => Id; " +
            "  public override bool Equals(object o) => o is SKey k && k.Id == Id; } ";

        Assembly v1 = Compile("using System.Collections.Generic; " + key +
            "public static class H { public static Dictionary<SKey, int> M = new(); " +
            "  public static void Setup() { M[new SKey { Id = 5, Tag = \"t\" }] = 50; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; " +
            key.Replace("public string Tag;", "public string Tag; public int Extra;") +
            "public static class H { public static Dictionary<SKey, int> M = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        Type keyV2 = v2.GetType("SKey")!;
        object probe = Activator.CreateInstance(keyV2)!;
        keyV2.GetField("Id")!.SetValue(probe, 5);

        var map = (IDictionary)v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        Assert.True(map.Contains(probe));
    }

    [Fact]
    public void ReadOnlyStaticDictionary_RehashedInPlace()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " + ValueHashKey +
            "public static class H { public static readonly Dictionary<Key, int> M = new(); " +
            "  public static void Setup() { M[new Key { Id = 5 }] = 50; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; " + ValueHashKeyV2 +
            "public static class H { public static readonly Dictionary<Key, int> M = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        Type keyV2 = v2.GetType("Key")!;
        object probe = Activator.CreateInstance(keyV2)!;
        keyV2.GetField("Id")!.SetValue(probe, 5);

        var map = (IDictionary)v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        Assert.Single(map);
        Assert.True(map.Contains(probe));
    }

    // A container that contains itself. The identity map has to close the loop through the rebuild queue.
    [Fact]
    public void DictionaryContainingItself_Terminates()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static Dictionary<string, object> M = new(); " +
            "  public static void Setup() { M[\"self\"] = M; M[\"e\"] = new E { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static Dictionary<string, object> M = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        var map = (IDictionary)v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        Assert.Same(map, map["self"]);
        Assert.Same(v2.GetType("E"), map["e"]!.GetType());
    }

    // The same instance used as both a key and a value has to stay one object.
    [Fact]
    public void SameInstanceAsKeyAndValue_KeepsOneIdentity()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static Dictionary<E, E> M = new(); " +
            "  public static void Setup() { var e = new E { Id = 1 }; M[e] = e; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static Dictionary<E, E> M = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        var map = (IDictionary)v2.GetType("H")!.GetField("M")!.GetValue(null)!;

        var entries = map.GetEnumerator();
        Assert.True(entries.MoveNext());
        Assert.Same(entries.Key, entries.Value);
        Assert.False(entries.MoveNext());
    }

    [Fact]
    public void NestedGenericContainer_OfSwappedType()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static Dictionary<string, List<E>> M = new(); " +
            "  public static void Setup() { M[\"a\"] = new List<E> { new E { Id = 4 } }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static Dictionary<string, List<E>> M = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        var map = (IDictionary)v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        var list = (IList)map["a"]!;

        Assert.Single(list);
        Assert.Same(v2.GetType("E"), list[0]!.GetType());
    }

    // A key hashed before its fields are populated lands in the wrong bucket and can never be found again.
    [Fact]
    public void ImmutableDictionary_ValueHashedKey_StillResolves()
    {
        Assembly v1 = Compile("using System.Collections.Immutable; " + ValueHashKey +
            "public static class H { public static ImmutableDictionary<Key, int> M; " +
            "  public static void Setup() { M = ImmutableDictionary<Key, int>.Empty.Add(new Key { Id = 5 }, 50); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Immutable; " + ValueHashKeyV2 +
            "public static class H { public static ImmutableDictionary<Key, int> M; public static void Setup() { } }");

        Migrate(v1, v2);

        Type keyV2 = v2.GetType("Key")!;
        object probe = Activator.CreateInstance(keyV2)!;
        keyV2.GetField("Id")!.SetValue(probe, 5);

        var map = (IDictionary)v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        Assert.True(map.Contains(probe), "the migrated key could not be found by an equal key");
    }

    [Fact]
    public void ImmutableHashSet_ValueHashedElement_StillContains()
    {
        Assembly v1 = Compile("using System.Collections.Immutable; " + ValueHashKey +
            "public static class H { public static ImmutableHashSet<Key> S; " +
            "  public static void Setup() { S = ImmutableHashSet<Key>.Empty.Add(new Key { Id = 5 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Immutable; " + ValueHashKeyV2 +
            "public static class H { public static ImmutableHashSet<Key> S; public static void Setup() { } }");

        Migrate(v1, v2);

        Type keyV2 = v2.GetType("Key")!;
        object probe = Activator.CreateInstance(keyV2)!;
        keyV2.GetField("Id")!.SetValue(probe, 5);

        object set = v2.GetType("H")!.GetField("S")!.GetValue(null)!;
        bool contains = (bool)set.GetType().GetMethod("Contains")!.Invoke(set, new[] { probe })!;

        Assert.True(contains, "the migrated element could not be found by an equal one");
    }

    [Fact]
    public void EmptyImmutableDictionary_StaysUsable()
    {
        Assembly v1 = Compile("using System.Collections.Immutable; public class E { public int Id; } " +
            "public static class H { public static ImmutableDictionary<string, E> M = ImmutableDictionary<string, E>.Empty; }");
        _ = v1.GetType("H")!.GetField("M")!.GetValue(null);

        Assembly v2 = Compile("using System.Collections.Immutable; public class E { public int Id; public int Extra; } " +
            "public static class H { public static ImmutableDictionary<string, E> M = ImmutableDictionary<string, E>.Empty; }");

        Migrate(v1, v2);

        object map = v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        Assert.Equal(0, map.GetType().GetProperty("Count")!.GetValue(map));

        // Adding has to work on the migrated instance, not just reading from it.
        object grown = map.GetType().GetMethod("Add")!
            .Invoke(map, new object?[] { "k", Activator.CreateInstance(v2.GetType("E")!) })!;

        Assert.Equal(1, grown.GetType().GetProperty("Count")!.GetValue(grown));
    }

    [Fact]
    public void ImmutableDictionary_KeepsIdentityAcrossReferences()
    {
        Assembly v1 = Compile("using System.Collections.Immutable; public class E { public int Id; } " +
            "public static class H { public static ImmutableDictionary<string, E> A; public static ImmutableDictionary<string, E> B; " +
            "  public static void Setup() { A = ImmutableDictionary<string, E>.Empty.Add(\"k\", new E { Id = 1 }); B = A; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Immutable; public class E { public int Id; public int Extra; } " +
            "public static class H { public static ImmutableDictionary<string, E> A; public static ImmutableDictionary<string, E> B; " +
            "  public static void Setup() { } }");

        Migrate(v1, v2);

        object a = v2.GetType("H")!.GetField("A")!.GetValue(null)!;
        object b = v2.GetType("H")!.GetField("B")!.GetValue(null)!;

        Assert.Same(a, b);

        var map = (IDictionary)a;
        Assert.Single(map);
        Assert.Same(v2.GetType("E"), map["k"]!.GetType());
    }

    [Fact]
    public void Queue_OfSwappedType_KeepsOrder()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static Queue<E> Q = new(); " +
            "  public static void Setup() { Q.Enqueue(new E { Id = 1 }); Q.Enqueue(new E { Id = 2 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static Queue<E> Q = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        var queue = (IEnumerable)v2.GetType("H")!.GetField("Q")!.GetValue(null)!;
        Type eV2 = v2.GetType("E")!;
        var ids = queue.Cast<object>().Select(x => (int)eV2.GetField("Id")!.GetValue(x)!).ToArray();

        Assert.Equal(new[] { 1, 2 }, ids);
    }

    [Fact]
    public void Stack_OfSwappedType_KeepsOrder()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static Stack<E> S = new(); " +
            "  public static void Setup() { S.Push(new E { Id = 1 }); S.Push(new E { Id = 2 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static Stack<E> S = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        var stack = (IEnumerable)v2.GetType("H")!.GetField("S")!.GetValue(null)!;
        Type eV2 = v2.GetType("E")!;
        var ids = stack.Cast<object>().Select(x => (int)eV2.GetField("Id")!.GetValue(x)!).ToArray();

        Assert.Equal(new[] { 2, 1 }, ids); // a stack enumerates top first
    }

    [Fact]
    public void JaggedArray_OfSwappedType()
    {
        Assembly v1 = Compile("public class E { public int Id; } " +
            "public static class H { public static E[][] Grid; " +
            "  public static void Setup() { Grid = new E[][] { new E[] { new E { Id = 1 } } }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public class E { public int Id; public int Extra; } " +
            "public static class H { public static E[][] Grid; public static void Setup() { } }");

        Migrate(v1, v2);

        var grid = (Array)v2.GetType("H")!.GetField("Grid")!.GetValue(null)!;
        var row = (Array)grid.GetValue(0)!;

        Assert.Same(v2.GetType("E"), row.GetValue(0)!.GetType());
    }

    [Fact]
    public void ConditionalWeakTable_WithSwappedKey()
    {
        Assembly v1 = Compile("using System.Runtime.CompilerServices; public class E { public int Id; } " +
            "public class Note { public string Text; } " +
            "public static class H { public static ConditionalWeakTable<E, Note> T = new(); public static E Key; " +
            "  public static void Setup() { Key = new E { Id = 1 }; T.Add(Key, new Note { Text = \"n\" }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Runtime.CompilerServices; public class E { public int Id; public int Extra; } " +
            "public class Note { public string Text; } " +
            "public static class H { public static ConditionalWeakTable<E, Note> T = new(); public static E Key; " +
            "  public static void Setup() { } }");

        Migrate(v1, v2);

        object table = v2.GetType("H")!.GetField("T")!.GetValue(null)!;
        object key = v2.GetType("H")!.GetField("Key")!.GetValue(null)!;

        var arguments = new object?[] { key, null };
        bool found = (bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, arguments)!;

        Assert.True(found, "the migrated key no longer resolves in the weak table");
    }

    [Fact]
    public void WeakReferenceOfSwappedType_RepointedNotCleared()
    {
        Assembly v1 = Compile("using System; public class E { public int Id; } " +
            "public static class H { public static E Strong; public static WeakReference<E> Weak; " +
            "  public static void Setup() { Strong = new E { Id = 6 }; Weak = new WeakReference<E>(Strong); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System; public class E { public int Id; public int Extra; } " +
            "public static class H { public static E Strong; public static WeakReference<E> Weak; public static void Setup() { } }");

        Migrate(v1, v2);

        object weak = v2.GetType("H")!.GetField("Weak")!.GetValue(null)!;
        object strong = v2.GetType("H")!.GetField("Strong")!.GetValue(null)!;

        var arguments = new object?[1];
        bool alive = (bool)weak.GetType().GetMethod("TryGetTarget")!.Invoke(weak, arguments)!;

        Assert.True(alive, "the weak reference lost its target across the reload");
        Assert.Same(strong, arguments[0]);
    }

    [Fact]
    public void CustomComparer_IsStillCarriedAcross()
    {
        const string comparer =
            "public class Ci : System.Collections.Generic.IEqualityComparer<string> { " +
            "  public bool Equals(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase); " +
            "  public int GetHashCode(string s) => s.ToLowerInvariant().GetHashCode(); } ";

        Assembly v1 = Compile("using System.Collections.Generic; " + comparer +
            "public class E { public int Id; } " +
            "public static class H { public static Dictionary<string, E> M = new(new Ci()); " +
            "  public static void Setup() { M[\"Key\"] = new E { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; " + comparer +
            "public class E { public int Id; public int Extra; } " +
            "public static class H { public static Dictionary<string, E> M = new(new Ci()); public static void Setup() { } }");

        Migrate(v1, v2);

        var map = (IDictionary)v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        Assert.True(map.Contains("KEY"), "the case insensitive comparer did not survive the reload");
    }

    // The default comparer is a framework singleton, not something to migrate into a fabricated stand-in.
    [Fact]
    public void DefaultComparer_IsNotFabricated()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static Dictionary<string, E> M = new(); " +
            "  public static void Setup() { M[\"k\"] = new E { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static Dictionary<string, E> M = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        var map = v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        object actual = map.GetType().GetProperty("Comparer")!.GetValue(map)!;

        Assert.Same(EqualityComparer<string>.Default, actual);
    }

    [Fact]
    public void DictionaryWithSwappedKey_UsesTheCurrentDefaultComparer()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class K { public int Id; } " +
            "public static class H { public static Dictionary<K, int> M = new(); public static K Key; " +
            "  public static void Setup() { Key = new K { Id = 1 }; M[Key] = 9; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class K { public int Id; public int Extra; } " +
            "public static class H { public static Dictionary<K, int> M = new(); public static K Key; public static void Setup() { } }");

        Migrate(v1, v2);

        object map = v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        object comparer = map.GetType().GetProperty("Comparer")!.GetValue(map)!;

        Type expected = typeof(EqualityComparer<>).MakeGenericType(v2.GetType("K")!);
        object current = expected.GetProperty("Default", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;

        Assert.Same(current, comparer);
    }
}
