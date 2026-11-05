// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Prowl.Rosetta;

/// <summary>
/// Static localization entry point. Call Loc.Configure() once at startup, then Loc.Get() from anywhere.
/// </summary>
public static partial class Loc
{
    private static readonly List<ILocalizationProvider> _providers = [];
    // locale → (key → value)
    private static readonly Dictionary<string, Dictionary<string, string>> _cache = [];
    private static readonly ReaderWriterLockSlim _lock = new();

    private static string _currentLocale = "en";
    private static string _fallbackLocale = "en";
    private static MissingKeyBehavior _missingKeyBehavior = MissingKeyBehavior.ReturnFallback;

    // Hot reload state.
    private static readonly Timer _timer = new(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    private static int _retryCount = 0;
    private const int InitialDelayInMs = 100;
    private const int MaxRetryCount = 3;
    private const int RetryDelayInMs = 400;

    [ThreadStatic] private static string? _scopedLocale;

    /// <summary>The currently active locale.</summary>
    public static string CurrentLocale => _scopedLocale ?? _currentLocale;

    /// <summary>The fallback locale used when a key is missing in the current locale.</summary>
    public static string FallbackLocale => _fallbackLocale;

    /// <summary>Fired when the global locale changes. Args: (oldLocale, newLocale).</summary>
    public static event Action<string, string>? LocaleChanged;

    /// <summary>Fired when a key is not found. Args: (locale, key).</summary>
    public static event Action<string, string>? MissingKey;

    #region Configuration

    /// <summary>
    /// Configure Rosetta. Call once at startup.
    /// </summary>
    /// <remarks> This method is not thread-safe. </remarks>
    public static void Configure(Action<LocConfig> configure)
    {
        // Unbind any previous event handlers.
        foreach (ILocalizationProvider provider in _providers)
            provider.Changed -= OnProviderTranslationsChanged;

        var config = new LocConfig();
        configure(config);

        _cache.Clear();
        LocaleChanged = null;
        MissingKey = null;
        _scopedLocale = null;

        _fallbackLocale = config.Fallback ?? "en";
        _missingKeyBehavior = config.MissingBehavior;
        _currentLocale = config.InitialLocale ?? _fallbackLocale;
        _providers.Clear();
        _providers.AddRange(config.Providers);

        // Pre-load current and fallback
        LoadLocale(_currentLocale);
        if (_currentLocale != _fallbackLocale)
            LoadLocale(_fallbackLocale);

        // Bind event handlers for all providers.
        foreach (ILocalizationProvider provider in _providers)
            provider.Changed += OnProviderTranslationsChanged;
    }

    /// <summary>
    /// Switch the active locale. Loads translations from all providers.
    /// </summary>
    public static void SetLocale(string locale)
    {
        string previousLocale;
        _lock.EnterWriteLock();
        try
        {
            if (_currentLocale == locale) return;
            previousLocale = _currentLocale;
            _currentLocale = locale;
            LoadLocale(locale);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        // Never call user-provided callbacks inside a lock.
        LocaleChanged?.Invoke(previousLocale, locale);
    }

    /// <summary>
    /// Add a provider at runtime (after Configure). Useful for plugins, custom editor
    /// scripts, or game code that wants to register its own translations without
    /// re-configuring the entire system. Later providers override earlier ones on
    /// key conflicts. Automatically reloads the cache.
    /// </summary>
    public static void AddProvider(ILocalizationProvider provider)
    {
        _lock.EnterWriteLock();
        try
        {
            _providers.Add(provider);
            provider.Changed += OnProviderTranslationsChanged;
            ReloadInternal();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Force reload all cached translations for the current locale.
    /// </summary>
    public static void Reload()
    {
        _lock.EnterWriteLock();
        try
        {
            ReloadInternal();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private static void ReloadInternal()
    {
        _cache.Clear();
        LoadLocale(_currentLocale);
        if (_currentLocale != _fallbackLocale)
            LoadLocale(_fallbackLocale);
    }

    /// <summary>
    /// Temporarily override the locale for the current thread.
    /// </summary>
    public static IDisposable BeginScope(string locale)
    {
        _lock.EnterUpgradeableReadLock();
        try
        {
            if (!_cache.ContainsKey(locale))
            {
                _lock.EnterWriteLock();
                try
                {
                    LoadLocale(locale);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }

        return new LocaleScope(locale);
    }

    #endregion

    #region Lookup

    /// <summary>
    /// Get a translated string by key.
    /// </summary>
    public static string Get(string key) => Resolve(key, CurrentLocale);

    /// <summary>
    /// Get a translated string with named interpolation.
    /// Values are taken from the properties of the args object.
    /// </summary>
    public static string Get(string key, object args)
    {
        var raw = Resolve(key, CurrentLocale);
        return Interpolate(raw, args);
    }

    /// <summary>
    /// Get a plural-aware translated string.
    /// The translation file should have sub-keys matching CLDR categories (zero, one, two, few, many, other).
    /// </summary>
    public static string GetPlural(string key, int count)
    {
        var locale = CurrentLocale;
        var category = PluralRules.GetCategory(locale, count);
        var raw = Resolve($"{key}.{category}", locale);
        return Interpolate(raw, new { count });
    }

    /// <summary>
    /// Get a plural-aware translated string with additional interpolation args.
    /// </summary>
    public static string GetPlural(string key, int count, object args)
    {
        var locale = CurrentLocale;
        var category = PluralRules.GetCategory(locale, count);
        var raw = Resolve($"{key}.{category}", locale);
        // Merge count into args via reflection
        var dict = ObjectToDict(args);
        dict["count"] = count.ToString();
        return Interpolate(raw, dict);
    }

    /// <summary>
    /// Get a gender-variant translated string.
    /// The translation file should have sub-keys: male, female, other.
    /// </summary>
    public static string GetGender(string key, Gender gender)
    {
        var suffix = gender switch
        {
            Gender.Male => "male",
            Gender.Female => "female",
            _ => "other"
        };
        return Resolve($"{key}.{suffix}", CurrentLocale);
    }

    /// <summary>
    /// Get a gender-variant translated string with interpolation.
    /// </summary>
    public static string GetGender(string key, Gender gender, object args)
    {
        var suffix = gender switch
        {
            Gender.Male => "male",
            Gender.Female => "female",
            _ => "other"
        };
        var raw = Resolve($"{key}.{suffix}", CurrentLocale);
        return Interpolate(raw, args);
    }

    #endregion

    #region Internals

    [GeneratedRegex(@"\{\{(\w+)\}\}", RegexOptions.Compiled)]
    private static partial Regex GetInterpolationRegex();

    private static string Resolve(string key, string locale)
    {
        string? value = null;

        _lock.EnterReadLock();
        try
        {
            // Try current locale
            if (TryGetInternal(key, locale, out value))
                return value;

            // Try fallback
            if (_missingKeyBehavior == MissingKeyBehavior.ReturnFallback && locale != _fallbackLocale)
                TryGetInternal(key, _fallbackLocale, out value);
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // Never call user-provided callbacks inside a lock.
        MissingKey?.Invoke(locale, key);

        if (value is not null)
            return value;
        if (_missingKeyBehavior == MissingKeyBehavior.Throw)
            throw new KeyNotFoundException($"Localization key '{key}' not found for locale '{locale}'.");
        return key;
    }

    private static bool TryGetInternal(string key, string locale, [MaybeNullWhen(returnValue: false)] out string value)
    {
        if (!_cache.TryGetValue(locale, out Dictionary<string, string>? translations))
        {
            value = null;
            return false;
        }
        return translations.TryGetValue(key, out value);
    }

    private static void LoadLocale(string locale)
    {
        if (_cache.ContainsKey(locale)) return;

        var merged = new Dictionary<string, string>();
        foreach (var provider in _providers)
        {
            if (!provider.SupportsLocale(locale)) continue;
            var entries = provider.Load(locale);
            foreach (var kvp in entries)
                merged[kvp.Key] = kvp.Value; // Later providers override earlier ones
        }
        _cache[locale] = merged;
    }

    private static string Interpolate(string template, object args)
    {
        if (args is Dictionary<string, string> dict)
            return Interpolate(template, dict);

        return Interpolate(template, ObjectToDict(args));
    }

    private static string Interpolate(string template, Dictionary<string, string> values)
    {
        return GetInterpolationRegex().Replace(template, match =>
        {
            string name = match.Groups[1].Value;
            return values.TryGetValue(name, out var val) ? val : match.Value;
        });
    }

    private static Dictionary<string, string> ObjectToDict(object obj)
    {
        var dict = new Dictionary<string, string>();
        foreach (var prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var val = prop.GetValue(obj);
            if (val != null)
                dict[prop.Name] = val.ToString()!;
        }
        return dict;
    }

    private static void OnProviderTranslationsChanged(object? sender, EventArgs e)
    {
        // Because of the way FileSystemWatcher works, this method may be called multiple times in quick succession.
        // Additionally, it may not be safe to reload the translations yet.
        // By triggering a timer, we "debounce" the file system event and additionally we ensure that loading is done
        // off the file system watcher thread.
        _retryCount = 0;
        _timer.Change(InitialDelayInMs, Timeout.Infinite);
    }

    private static void OnTimerElapsed(object? state)
    {
        try
        {
            Reload();
        }
        catch (Exception)
        {
            // If hot-reload fails, try again later.
            if (Interlocked.Increment(ref _retryCount) <= MaxRetryCount)
                _timer.Change(RetryDelayInMs, Timeout.Infinite);
        }
    }

    #endregion

    #region Scoped Locale

    private sealed class LocaleScope : IDisposable
    {
        private readonly string? _previous;

        public LocaleScope(string locale)
        {
            _previous = _scopedLocale;
            _scopedLocale = locale;
        }

        public void Dispose() => _scopedLocale = _previous;
    }

    #endregion
}

/// <summary>
/// Fluent configuration builder for Loc.Configure().
/// </summary>
public class LocConfig
{
    internal List<ILocalizationProvider> Providers { get; } = [];
    internal string? Fallback { get; private set; }
    internal string? InitialLocale { get; private set; }
    internal MissingKeyBehavior MissingBehavior { get; private set; } = MissingKeyBehavior.ReturnFallback;

    public LocConfig SetFallbackLocale(string locale) { Fallback = locale; return this; }
    public LocConfig SetLocale(string locale) { InitialLocale = locale; return this; }
    public LocConfig AddProvider(ILocalizationProvider provider) { Providers.Add(provider); return this; }
    public LocConfig OnMissingKey(MissingKeyBehavior behavior) { MissingBehavior = behavior; return this; }
}
