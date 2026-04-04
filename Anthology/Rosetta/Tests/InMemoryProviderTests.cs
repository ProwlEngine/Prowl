// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Rosetta.Tests;

public class InMemoryProviderTests
{
    [Fact]
    public void BasicLookup()
    {
        var provider = new InMemoryProvider("en", new Dictionary<string, string>
        {
            ["hello"] = "Hello",
            ["goodbye"] = "Goodbye"
        });

        Loc.Configure(cfg => cfg
            .SetFallbackLocale("en")
            .AddProvider(provider)
            .SetLocale("en")
        );

        Assert.Equal("Hello", Loc.Get("hello"));
        Assert.Equal("Goodbye", Loc.Get("goodbye"));
    }

    [Fact]
    public void MultipleLocales()
    {
        var provider = new InMemoryProvider("en", new Dictionary<string, string>
        {
            ["greeting"] = "Hello"
        }).AddLocale("es", new Dictionary<string, string>
        {
            ["greeting"] = "Hola"
        });

        Loc.Configure(cfg => cfg
            .SetFallbackLocale("en")
            .AddProvider(provider)
            .SetLocale("en")
        );

        Assert.Equal("Hello", Loc.Get("greeting"));
        Loc.SetLocale("es");
        Assert.Equal("Hola", Loc.Get("greeting"));
    }

    [Fact]
    public void ProviderOverrideOrder()
    {
        var baseline = new InMemoryProvider("en", new Dictionary<string, string>
        {
            ["key"] = "baseline",
            ["other"] = "from baseline"
        });
        var overrides = new InMemoryProvider("en", new Dictionary<string, string>
        {
            ["key"] = "overridden"
        });

        Loc.Configure(cfg => cfg
            .SetFallbackLocale("en")
            .AddProvider(baseline)
            .AddProvider(overrides)
            .SetLocale("en")
        );

        Assert.Equal("overridden", Loc.Get("key"));
        Assert.Equal("from baseline", Loc.Get("other"));
    }

    [Fact]
    public void SupportsLocale_ReturnsFalseForUnknown()
    {
        var provider = new InMemoryProvider("en", new Dictionary<string, string>
        {
            ["key"] = "value"
        });

        Assert.True(provider.SupportsLocale("en"));
        Assert.False(provider.SupportsLocale("fr"));
    }
}
