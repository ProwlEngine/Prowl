// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Runtime.Events;

/// <summary>
/// Builds and caches a mapping from each <typeparamref name="T"/> enum value
/// to the <see cref="Type"/> declared by its <see cref="EventArgsAttribute"/>
/// (if any). Evaluated once per closed generic <c>T</c>.
/// </summary>
internal static class EventArgsContract<T> where T : struct, Enum
{
    /// <summary>
    /// Maps each enum value to its declared <c>TArgs</c> type, or <c>null</c>
    /// if the value has no <see cref="EventArgsAttribute"/>.
    /// </summary>
    private static readonly Dictionary<T, Type?> s_declaredArgs = Build();

    private static Dictionary<T, Type?> Build()
    {
        Dictionary<T, Type?> map = new Dictionary<T, Type?>();
        Type enumType = typeof(T);

        foreach (T value in Enum.GetValues<T>())
        {
            string name = Enum.GetName(value)!;
            FieldInfo? field = enumType.GetField(name);
            EventArgsAttribute? attr = field?.GetCustomAttribute<EventArgsAttribute>();
            map[value] = attr?.ArgsType;
        }

        return map;
    }

    /// <summary>
    /// Returns <c>true</c> when <typeparamref name="TArgs"/> is compatible
    /// with the contract declared on <paramref name="eventType"/>.
    /// Returns <c>true</c> when no attribute is present (opt-in model).
    /// </summary>
    public static bool IsValid<TArgs>(T eventType)
    {
        if (!s_declaredArgs.TryGetValue(eventType, out Type? declared) || declared is null)
            return true;

        return declared == typeof(TArgs);
    }

    /// <summary>
    /// Returns the declared type name for diagnostics, or <c>"(none)"</c>.
    /// </summary>
    public static string GetDeclaredName(T eventType)
    {
        if (s_declaredArgs.TryGetValue(eventType, out Type? declared) && declared is not null)
            return declared.Name;
        return "(none)";
    }
}
