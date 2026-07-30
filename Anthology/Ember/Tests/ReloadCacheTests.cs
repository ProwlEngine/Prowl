using System;
using System.Collections.Generic;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>
/// The self-clearing cache, on both paths that matter: living outside the reload, which is the usual case, and
/// living in the reload itself, which is what happens once a type argument is one of the replaced types.
/// </summary>
[Trait("Category", "Build")]
public class ReloadCacheTests : MigrationTestBase
{
    [Fact]
    public void Preserved_CacheEmptiesItself()
    {
        Assembly v1 = Compile("public class E { public int Id; }");
        Assembly v2 = Compile("public class E { public int Id; public int Extra; }");

        var cache = new ReloadCache<Type, int>();
        cache.Set(typeof(string), 42);
        Assert.True(cache.TryGetValue(typeof(string), out _));

        Reload(o => o.Scope.Include(v1), b => b.Replace(v1, v2).Root(cache));

        Assert.False(cache.TryGetValue(typeof(string), out _));
    }

    [Fact]
    public void Preserved_SeedEntriesComeBack()
    {
        Assembly v1 = Compile("public class E { public int Id; }");
        Assembly v2 = Compile("public class E { public int Id; public int Extra; }");

        var cache = new ReloadCache<string, int>(new KeyValuePair<string, int>("seed", 1));
        cache.Set("scratch", 2);

        Reload(o => o.Scope.Include(v1), b => b.Replace(v1, v2).Root(cache));

        Assert.True(cache.TryGetValue("seed", out int seeded));
        Assert.Equal(1, seeded);
        Assert.False(cache.TryGetValue("scratch", out _));
    }

    [Fact]
    public void Preserved_FactoryStillWorksAfterTheReload()
    {
        Assembly v1 = Compile("public class E { public int Id; }");
        Assembly v2 = Compile("public class E { public int Id; public int Extra; }");

        var cache = new ReloadCache<int, string>(key => $"v{key}");
        Assert.Equal("v1", cache[1]);

        Reload(o => o.Scope.Include(v1), b => b.Replace(v1, v2).Root(cache));

        Assert.Equal(0, cache.Count);
        Assert.Equal("v2", cache[2]); // the factory carried over and the map is usable again
    }

    // A cache whose key type is one of the replaced types is itself replaced, so it takes the attach path.
    [Fact]
    public void Replaced_CacheIsUsableAndEmpty()
    {
        Assembly v1 = Compile("using System; using System.Collections.Generic; using Prowl.Ember; " +
            "public class K { public int Id; } " +
            "public static class H { public static ReloadCache<K, int> C = new(); public static K Key; " +
            "  public static void Setup() { Key = new K { Id = 1 }; C.Set(Key, 5); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System; using System.Collections.Generic; using Prowl.Ember; " +
            "public class K { public int Id; public int Extra; } " +
            "public static class H { public static ReloadCache<K, int> C = new(); public static K Key; " +
            "  public static void Setup() { } }");

        var report = Migrate(v1, v2);

        object cache = v2.GetType("H")!.GetField("C")!.GetValue(null)!;
        object key = v2.GetType("H")!.GetField("Key")!.GetValue(null)!;

        Assert.Equal(0, cache.GetType().GetProperty("Count")!.GetValue(cache));

        // The map has to be a live instance, not the null an opted-out field used to arrive as.
        var arguments = new object?[] { key, 7 };
        bool added = (bool)cache.GetType().GetMethod("TryAdd")!.Invoke(cache, arguments)!;

        Assert.True(added);
        Assert.True(report.Succeeded, string.Join(" | ", report.Errors));
    }

    [Fact]
    public void WithoutFactory_IndexerExplainsItself()
    {
        var cache = new ReloadCache<string, int>();
        var thrown = Assert.Throws<InvalidOperationException>(() => _ = cache["missing"]);

        Assert.Contains("factory", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }
}
