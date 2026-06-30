// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Rosetta.Tests;

public class LocTests
{
    private static string LocalizationPath => Path.Combine(AppContext.BaseDirectory, "Localization", "{locale}.json");

    private void ConfigureEnglish(MissingKeyBehavior behavior = MissingKeyBehavior.ReturnFallback)
    {
        Loc.Configure(cfg => cfg
            .SetFallbackLocale("en")
            .AddProvider(new JsonFileProvider(LocalizationPath))
            .SetLocale("en")
            .OnMissingKey(behavior)
        );
    }

    private void ConfigureFrench()
    {
        Loc.Configure(cfg => cfg
            .SetFallbackLocale("en")
            .AddProvider(new JsonFileProvider(LocalizationPath))
            .SetLocale("fr")
        );
    }

    [Fact]
    public void Get_ReturnsTranslatedString()
    {
        ConfigureEnglish();
        Assert.Equal("Confirm", Loc.Get("ui.button.confirm"));
        Assert.Equal("Cancel", Loc.Get("ui.button.cancel"));
    }

    [Fact]
    public void Get_WithLocaleSwitch_ReturnsCorrectTranslation()
    {
        ConfigureFrench();
        Assert.Equal("Confirmer", Loc.Get("ui.button.confirm"));
        Assert.Equal("Annuler", Loc.Get("ui.button.cancel"));
    }

    [Fact]
    public void Get_WithInterpolation_ReplacesPlaceholders()
    {
        ConfigureEnglish();
        var result = Loc.Get("player.killed", new { name = "Zara", count = 3 });
        Assert.Equal("Zara defeated 3 enemies", result);
    }

    [Fact]
    public void Get_WithInterpolation_SinglePlaceholder()
    {
        ConfigureEnglish();
        var result = Loc.Get("player.greeting", new { name = "Alice" });
        Assert.Equal("Hello, Alice!", result);
    }

    [Fact]
    public void Get_WithInterpolation_MissingPlaceholderLeft_Untouched()
    {
        ConfigureEnglish();
        // Only provide 'name', not 'count'
        var result = Loc.Get("player.killed", new { name = "Zara" });
        Assert.Equal("Zara defeated {{count}} enemies", result);
    }

    [Fact]
    public void GetPlural_English_One()
    {
        ConfigureEnglish();
        Assert.Equal("1 item", Loc.GetPlural("item.collected", count: 1));
    }

    [Fact]
    public void GetPlural_English_Other()
    {
        ConfigureEnglish();
        Assert.Equal("7 items", Loc.GetPlural("item.collected", count: 7));
    }

    [Fact]
    public void GetPlural_English_Zero()
    {
        ConfigureEnglish();
        Assert.Equal("0 items", Loc.GetPlural("item.collected", count: 0));
    }

    [Fact]
    public void GetPlural_WithExtraArgs()
    {
        ConfigureEnglish();
        var result = Loc.GetPlural("enemy.remaining", count: 3, new { zone = "Northgate" });
        Assert.Equal("3 enemies remain in Northgate", result);
    }

    [Fact]
    public void GetPlural_French_ZeroIsSingular()
    {
        ConfigureFrench();
        // French: 0 and 1 use "one" category
        Assert.Equal("0 objet", Loc.GetPlural("item.collected", count: 0));
        Assert.Equal("1 objet", Loc.GetPlural("item.collected", count: 1));
        Assert.Equal("5 objets", Loc.GetPlural("item.collected", count: 5));
    }

    [Fact]
    public void GetGender_Male()
    {
        ConfigureEnglish();
        Assert.Equal("He nods at you.", Loc.GetGender("npc.greeting", Gender.Male));
    }

    [Fact]
    public void GetGender_Female()
    {
        ConfigureEnglish();
        Assert.Equal("She smiles at you.", Loc.GetGender("npc.greeting", Gender.Female));
    }

    [Fact]
    public void GetGender_Other()
    {
        ConfigureEnglish();
        Assert.Equal("They glance over.", Loc.GetGender("npc.greeting", Gender.Other));
    }

    [Fact]
    public void GetGender_French()
    {
        ConfigureFrench();
        Assert.Equal("Elle vous sourit.", Loc.GetGender("npc.greeting", Gender.Female));
    }

    [Fact]
    public void SetLocale_ChangesCurrentLocale()
    {
        ConfigureEnglish();
        Assert.Equal("en", Loc.CurrentLocale);
        Loc.SetLocale("fr");
        Assert.Equal("fr", Loc.CurrentLocale);
        Assert.Equal("Confirmer", Loc.Get("ui.button.confirm"));
    }

    [Fact]
    public void SetLocale_FiresLocaleChangedEvent()
    {
        ConfigureEnglish();
        string? oldLocale = null, newLocale = null;
        Loc.LocaleChanged += (o, n) => { oldLocale = o; newLocale = n; };
        Loc.SetLocale("fr");
        Assert.Equal("en", oldLocale);
        Assert.Equal("fr", newLocale);
    }

    [Fact]
    public void SetLocale_SameLocale_DoesNotFireEvent()
    {
        ConfigureEnglish();
        bool fired = false;
        Loc.LocaleChanged += (_, _) => fired = true;
        Loc.SetLocale("en");
        Assert.False(fired);
    }

    [Fact]
    public void MissingKey_ReturnFallback_TriesFallbackLocale()
    {
        ConfigureFrench();
        // "only.in.english" exists in en but not fr
        Assert.Equal("English only", Loc.Get("only.in.english"));
    }

    [Fact]
    public void MissingKey_ReturnKey_ReturnsKeyString()
    {
        ConfigureEnglish(MissingKeyBehavior.ReturnKey);
        Assert.Equal("nonexistent.key", Loc.Get("nonexistent.key"));
    }

    [Fact]
    public void MissingKey_Throw_ThrowsException()
    {
        ConfigureEnglish(MissingKeyBehavior.Throw);
        Assert.Throws<KeyNotFoundException>(() => Loc.Get("nonexistent.key"));
    }

    [Fact]
    public void MissingKey_EventFired()
    {
        ConfigureEnglish(MissingKeyBehavior.ReturnKey);
        string? missingLocale = null, missingKey = null;
        Loc.MissingKey += (l, k) => { missingLocale = l; missingKey = k; };
        Loc.Get("nonexistent.key");
        Assert.Equal("en", missingLocale);
        Assert.Equal("nonexistent.key", missingKey);
    }

    [Fact]
    public void BeginScope_OverridesLocaleTemporarily()
    {
        ConfigureEnglish();
        Assert.Equal("Confirm", Loc.Get("ui.button.confirm"));

        using (Loc.BeginScope("de"))
        {
            Assert.Equal("de", Loc.CurrentLocale);
            Assert.Equal("Bestätigen", Loc.Get("ui.button.confirm"));
        }

        Assert.Equal("en", Loc.CurrentLocale);
        Assert.Equal("Confirm", Loc.Get("ui.button.confirm"));
    }

    [Fact]
    public void BeginScope_NestedScopes()
    {
        ConfigureEnglish();

        using (Loc.BeginScope("fr"))
        {
            Assert.Equal("Confirmer", Loc.Get("ui.button.confirm"));

            using (Loc.BeginScope("de"))
            {
                Assert.Equal("Bestätigen", Loc.Get("ui.button.confirm"));
            }

            Assert.Equal("Confirmer", Loc.Get("ui.button.confirm"));
        }

        Assert.Equal("Confirm", Loc.Get("ui.button.confirm"));
    }

    [Fact]
    public void Reload_ClearsAndReloadsCache()
    {
        ConfigureEnglish();
        Assert.Equal("Confirm", Loc.Get("ui.button.confirm"));
        // Reload should not break anything
        Loc.Reload();
        Assert.Equal("Confirm", Loc.Get("ui.button.confirm"));
    }

    [Fact]
    public void Configure_CanBeCalledMultipleTimes()
    {
        ConfigureEnglish();
        Assert.Equal("Confirm", Loc.Get("ui.button.confirm"));

        // Reconfigure with French
        ConfigureFrench();
        Assert.Equal("Confirmer", Loc.Get("ui.button.confirm"));
    }
}
