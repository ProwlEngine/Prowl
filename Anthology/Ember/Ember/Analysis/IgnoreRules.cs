using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Prowl.Ember;

/// <summary>Where <see cref="ReloadIgnoreAttribute"/> applies, including the places it applies indirectly.</summary>
internal static class IgnoreRules
{
    public static bool Applies(Type type) => type.GetCustomAttribute<ReloadIgnoreAttribute>() != null;

    /// <summary>
    /// True when the field carries the attribute, or when it is the backing field of an auto property that
    /// carries it, so marking a property protects the storage behind it.
    /// </summary>
    public static bool Applies(FieldInfo field)
    {
        if (field.GetCustomAttribute<ReloadIgnoreAttribute>() != null) return true;

        if (!field.IsPrivate || field.GetCustomAttribute<CompilerGeneratedAttribute>() == null) return false;

        if (!SyntheticName.TryParse(field.Name, out var name)
            || name.Kind != SyntheticKind.AutoPropertyBackingField
            || name.Scope == null)
            return false;

        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                  | (field.IsStatic ? BindingFlags.Static : BindingFlags.Instance);

        return field.DeclaringType?.GetProperty(name.Scope, flags)?.GetCustomAttribute<ReloadIgnoreAttribute>() != null;
    }
}
