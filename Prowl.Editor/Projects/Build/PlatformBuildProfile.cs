// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Editor.Build;

/// <summary>
/// Per-platform scripting define symbols and any future platform-specific build knobs.
/// Serialised as part of the project's build settings.
/// </summary>
/// <remarks>
/// Deliberately has no drawing code. A profile is data, so a CI build can construct one without a UI
/// toolkit present. The editor renders it through a registered drawer keyed on the profile type.
/// </remarks>
public class PlatformBuildProfile
{
    /// <summary>
    /// Scripting define symbols passed to the compiler when building for this platform.
    /// </summary>
    public List<string> ScriptingDefineSymbols { get; set; } = [];

    /// <summary> Returns the type of build pipeline used for this platform, or null if none is configured. </summary>
    public virtual Type? GetPipelineType() => null;

    /// <summary> Resets all profile properties to their default values. </summary>
    public virtual void ToDefault() { }

    public virtual void ApplyOverrides(PlatformOverrides overrides) { }

    /// <summary> Adds platform-specific scripting define symbols to the given list. </summary>
    public virtual void ModifyDefines(List<string> defines) { }
}


public sealed class PlatformOverrides
{
    private static Dictionary<Type, Func<string, object>> _parsers = new()
    {
        { typeof(bool), (x) => bool.Parse(x) },
        { typeof(int), (x) => int.Parse(x) },
        { typeof(float), (x) => float.Parse(x) },
    };

    private Dictionary<string, string> _args;


    public PlatformOverrides(List<string> values)
    {
        _args = [];

        foreach (string s in values)
        {
            string[] vals = s.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (vals.Length != 2)
                throw new ArgumentException($"Improperly formatted selector: {s}");

            _args[vals[0]] = vals[1];
        }
    }


    public T GetArg<T>(string key, T defaultValue) where T : struct
    {
        if (!_args.TryGetValue(key, out string value))
            return defaultValue;

        _args.Remove(key);

        try
        {
            if (typeof(T).IsEnum)
                return Enum.Parse<T>(value, true);

            if (_parsers.TryGetValue(typeof(T), out Func<string, object>? parser))
                return (T)parser.Invoke(value);
        }
        catch (Exception e)
        {
            throw new ArgumentException($"Invalid value '{value}' for {key}", e);
        }

        // we should probably report if we hit an unknown generic but wtv
        return defaultValue;
    }


    public bool TryGetTarget(string family, out PlatformTarget? target)
    {
        target = default;
        if (!_args.Remove("target", out string value))
            return false;

        target = TargetRegistry.Shared.ByFamily(family).FirstOrDefault(x => x.Id.Equals(value, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown {family} target '{value}'. Options: {string.Join(", ", TargetRegistry.Shared.ByFamily(family).Select(x => x.Id))}");
        return true;
    }


    public void ThrowIfUnknown()
    {
        if (_args.Count > 0)
            throw new ArgumentException($"Unknown keys: {string.Join(", ", _args.Keys)}");
    }
}