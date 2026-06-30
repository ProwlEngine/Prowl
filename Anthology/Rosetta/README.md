# Rosetta

**Rosetta** is a lightweight, backend-agnostic localization library for .NET. It works anywhere — Unity, custom engines, desktop apps, and games.

---

## Installation

```bash
dotnet add package Prowl.Rosetta
```

---

## Quick Start

```csharp
// 1. Configure once at startup
Loc.Configure(cfg => cfg
    .SetFallbackLocale("en")
    .AddProvider(new JsonFileProvider("Localization/{locale}.json"))
    .SetLocale("fr")
);

// 2. Look up strings anywhere
string text = Loc.Get("ui.button.confirm");
```

That's it. `Loc.Get` is a static accessor so you can call it from anywhere without dependency injection.

---

## Core API

### Basic lookup

```csharp
string text = Loc.Get("ui.button.confirm");
// → "Confirm"
```

### Interpolation

Named placeholders — not positional `{0}` — so translators always have context.

```csharp
string text = Loc.Get("player.killed", new { name = "Zara", count = 3 });
// → "Zara defeated 3 enemies"
```

### Pluralization

Rosetta follows [Unicode CLDR](https://cldr.unicode.org/index/cldr-spec/plural-rules) plural rules, so languages with complex plural forms (Russian, Arabic, Polish) work correctly out of the box.

```csharp
string text = Loc.GetPlural("item.collected", count: 1);  // → "1 item"
string text = Loc.GetPlural("item.collected", count: 7);  // → "7 items"

// With interpolation
string text = Loc.GetPlural("enemy.remaining", count: 3, new { zone = "Northgate" });
// → "3 enemies remain in Northgate"
```

### Gender

```csharp
string text = Loc.GetGender("npc.greeting", gender: Gender.Female);
// → "She smiles at you."
```

---

## Translation File Format

Rosetta uses flat JSON by default. Keys use dot notation. Pluralization and gender are expressed as nested objects.

```json
{
  "ui.button.confirm": "Confirm",

  "player.killed": "{{name}} defeated {{count}} enemies",

  "item.collected": {
    "one":   "{{count}} item",
    "other": "{{count}} items"
  },

  "npc.greeting": {
    "male":   "He nods at you.",
    "female": "She smiles at you.",
    "other":  "They glance over."
  }
}
```

---

## Providers

Providers are the source of truth for translations. They are tried **in order**, with later providers overriding earlier ones on key conflicts. This lets you ship bundled strings and override them from a remote backend.

Rosetta ships with the following built-in providers:

| Provider | Description |
|---|---|
| `JsonFileProvider` | Loads `.json` files from disk. Path supports `{locale}` token. |
| `EmbeddedResourceProvider` | Loads from embedded assembly resources. Good for libraries and engines. |
| `InMemoryProvider` | Loads from a `Dictionary<string, string>`. Useful for testing. |

### Configuration examples

**File-based:**

```csharp
Loc.Configure(cfg => cfg
    .SetFallbackLocale("en")
    .AddProvider(new JsonFileProvider("Localization/{locale}.json"))
    .SetLocale("fr")
);
```

**Prowl Engine (embedded resources):**

```csharp
Loc.Configure(cfg => cfg
    .SetFallbackLocale("en")
    .AddProvider(new EmbeddedResourceProvider(Assembly.GetExecutingAssembly()))
    .SetLocale("en")
    .OnMissingKey(MissingKeyBehavior.ReturnKey) // never crash in editor
);
```

**Hybrid — local fallback with remote overrides:**

```csharp
Loc.Configure(cfg => cfg
    .SetFallbackLocale("en")
    .AddProvider(new JsonFileProvider("Localization/{locale}.json")) // bundled baseline
    .AddProvider(new MyRemoteProvider())                             // remote overrides
);
```

---

## Locale Switching

```csharp
// Switch at runtime — reloads and fires LocaleChanged
Loc.SetLocale("ja");

// Subscribe to locale changes for UI refresh
Loc.LocaleChanged += (oldLocale, newLocale) => RefreshAllUI();
```

### Scoped locale

Temporarily override the locale without changing the global setting — useful for previewing translations or localizing a specific NPC.

```csharp
using (var scope = Loc.BeginScope("de"))
{
    string preview = Loc.Get("ui.title"); // German, inside the scope
}
// Global locale restored outside the using block
```

---

## Missing Keys

Control what happens when a key is not found.

```csharp
.OnMissingKey(MissingKeyBehavior.ReturnKey)       // returns "ui.button.confirm" — safe for editors
.OnMissingKey(MissingKeyBehavior.ReturnFallback)  // tries the fallback locale first
.OnMissingKey(MissingKeyBehavior.Throw)           // throws KeyNotFoundException — strict mode
```

You can also hook into missing key events to track untranslated strings:

```csharp
Loc.MissingKey += (locale, key) =>
{
    Analytics.Track("missing_translation", new { locale, key });
};
```

---

## Custom Providers

Implementing your own provider requires a single interface:

```csharp
public class MyCustomProvider : ILocalizationProvider
{
    public IReadOnlyDictionary<string, string> Load(string locale)
    {
        // Return a flat key → value dictionary for this locale
        return FetchFromMyBackend(locale);
    }

    public bool SupportsLocale(string locale) =>
        _supportedLocales.Contains(locale);
}
```

Then register it like any built-in provider:

```csharp
.AddProvider(new MyCustomProvider())
```

---

## Reload

To force a reload of all cached translations — for example after a live content update:

```csharp
Loc.Reload();
```

---

## Design Philosophy

- **One static entry point.** `Loc.Get` works from anywhere — no service locator, no constructor injection required.
- **Provider order is override order.** Bundle your baseline translations and let later providers win on conflict.
- **Named placeholders only.** `{{name}}` instead of `{0}` keeps translation files readable for non-developers.
- **CLDR pluralization.** Plural rules are locale-aware from the start. You don't bolt this on later.
- **Never throws in production.** The default missing key behaviour is `ReturnFallback` then `ReturnKey`. Your game keeps running.

---
