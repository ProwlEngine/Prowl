// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;

namespace Prowl.Echo;

/// <summary>
/// Manages compact and full type name resolution for serialization.
/// </summary>
public static class TypeNameRegistry
{
    private static readonly ConcurrentDictionary<Type, string> _compactNames = new();
    private static readonly ConcurrentDictionary<string, Type?> _compactNameLookup = new();
    private static readonly ConcurrentDictionary<Type, string> _fullNames = new();
    private static readonly ConcurrentDictionary<string, Type?> _fullNameLookup = new();

    // Predefined compact names for common types
    private static readonly Dictionary<Type, string> _predefinedCompactNames = new()
    {
        { typeof(int), "i" },
        { typeof(string), "s" },
        { typeof(bool), "b" },
        { typeof(float), "f" },
        { typeof(double), "d" },
        { typeof(long), "l" },
        { typeof(byte), "y" },
        { typeof(sbyte), "Y" },
        { typeof(short), "h" },
        { typeof(ushort), "H" },
        { typeof(uint), "I" },
        { typeof(ulong), "L" },
        { typeof(decimal), "m" },
        { typeof(char), "c" },
        { typeof(DateTime), "dt" },
        { typeof(Guid), "g" },
    };

    private static readonly Dictionary<string, Type> _predefinedTypeLookup;

    static TypeNameRegistry()
    {
        _predefinedTypeLookup = _predefinedCompactNames.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
    }

    public static void ClearCache()
    {
        _compactNames.Clear();
        _compactNameLookup.Clear();
        _fullNames.Clear();
        _fullNameLookup.Clear();
    }

    public static string GetCompactTypeName(Type type)
    {
        return _compactNames.GetOrAdd(type, t => {
            // Check predefined compact names first
            if (_predefinedCompactNames.TryGetValue(t, out var predefined))
                return predefined;

            // For enums, use enum name
            if (t.IsEnum)
                return $"e:{t.Name}";

            // For arrays, use special notation
            if (t.IsArray)
            {
                var elementType = t.GetElementType()!;
                var elementName = GetCompactTypeName(elementType);
                return t.GetArrayRank() == 1 ? $"{elementName}[]" : $"{elementName}[{new string(',', t.GetArrayRank() - 1)}]";
            }

            // For generic types, use compact generic notation
            if (t.IsGenericType)
                return GetCompactGenericTypeName(t);

            // Fallback to simple name
            return t.Name;
        });
    }

    public static Type? ResolveCompactTypeName(string name)
    {
        return _compactNameLookup.GetOrAdd(name, n => {
            // Check predefined types first
            if (_predefinedTypeLookup.TryGetValue(n, out var predefined))
                return predefined;

            // Handle enum notation
            if (n.StartsWith("e:"))
                return ResolveEnumType(n[2..]);

            // Handle array notation
            if (n.Contains("[]") || n.Contains("[,"))
                return ResolveArrayType(n);

            // Handle generic notation
            if (n.Contains('<') && n.Contains('>'))
                return ResolveGenericType(n);

            // Fallback to full type resolution
            return ReflectionUtils.FindTypeByName(n);
        });
    }

    public static string GetFullTypeName(Type type)
    {
        return _fullNames.GetOrAdd(type, t => {
            // Use assembly qualified name for better resolution
            return t.AssemblyQualifiedName ?? t.FullName ?? t.Name;
        });
    }

    public static Type? ResolveFullTypeName(string name)
    {
        return _fullNameLookup.GetOrAdd(name, n => {
            try
            {
                // Try direct type resolution first
                var type = Type.GetType(n);
                if (type != null) return type;

                // Fallback to reflection utils
                return ReflectionUtils.FindTypeByName(n);
            }
            catch
            {
                return null;
            }
        });
    }

    private static string GetCompactGenericTypeName(Type type)
    {
        var definition = type.GetGenericTypeDefinition();
        var args = type.GetGenericArguments();
        var argNames = args.Select(GetCompactTypeName);

        var baseName = definition.Name;
        // Remove the `1, `2 etc. from generic type names
        var backtickIndex = baseName.IndexOf('`');
        if (backtickIndex > 0)
            baseName = baseName[..backtickIndex];

        return $"{baseName}<{string.Join(",", argNames)}>";
    }

    private static Type? ResolveEnumType(string enumName)
    {
        // Simple enum resolution - could be enhanced
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.IsEnum && t.Name == enumName);
    }

    private static Type? ResolveArrayType(string arrayName)
    {
        try
        {
            // Parse array notation like "i[]" or "s[,]"
            var bracketIndex = arrayName.IndexOf('[');
            if (bracketIndex == -1) return null;

            var elementTypeName = arrayName[..bracketIndex];
            var rankInfo = arrayName[bracketIndex..];

            var elementType = ResolveCompactTypeName(elementTypeName);
            if (elementType == null) return null;

            // Count commas to determine rank
            var rank = rankInfo.Count(c => c == ',') + 1;

            return rank == 1 ? elementType.MakeArrayType() : elementType.MakeArrayType(rank);
        }
        catch
        {
            return null;
        }
    }

    private static Type? ResolveGenericType(string genericName)
    {
        try
        {
            // Parse generic notation like "List<i>" or "Dictionary<s,i>"
            var openIndex = genericName.IndexOf('<');
            var closeIndex = genericName.LastIndexOf('>');

            if (openIndex == -1 || closeIndex == -1) return null;

            var baseName = genericName[..openIndex];
            var argsString = genericName[(openIndex + 1)..closeIndex];

            // Split arguments - Is this good enough? Should be?
            var argTypeNames = argsString.Split(',').Select(s => s.Trim()).ToArray();
            var argTypes = argTypeNames.Select(ResolveCompactTypeName).ToArray();

            if (argTypes.Any(t => t == null)) return null;

            // Find the generic type definition
            var genericDefinition = FindGenericTypeDefinition(baseName, argTypes.Length);
            if (genericDefinition == null) return null;

            return genericDefinition.MakeGenericType(argTypes!);
        }
        catch
        {
            return null;
        }
    }

    private static Type? FindGenericTypeDefinition(string name, int arity)
    {
        var fullName = $"{name}`{arity}";

        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.IsGenericTypeDefinition && t.Name == fullName);
    }
}