// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Prowl.Echo;

/// <summary>
/// Cached field metadata that avoids per-call attribute reflection.
/// </summary>
internal readonly struct CachedFieldInfo
{
    public readonly FieldInfo Field;
    public readonly string? SerializeIfCondition;
    public readonly bool HasIgnoreOnNull;
    public readonly string[]? FormerNames;

    public CachedFieldInfo(FieldInfo field)
    {
        Field = field;

        var serializeIf = field.GetCustomAttribute<SerializeIfAttribute>();
        SerializeIfCondition = serializeIf?.ConditionMemberName;

        HasIgnoreOnNull = field.IsDefined(typeof(IgnoreOnNullAttribute), false);

        var formerAttrs = field.GetCustomAttributes<FormerlySerializedAsAttribute>();
        string[]? names = null;
        // Avoid LINQ allocation in the common case (no former names)
        foreach (var attr in formerAttrs)
        {
            names ??= CollectFormerNames(formerAttrs);
            break;
        }
        FormerNames = names;
    }

    private static string[] CollectFormerNames(IEnumerable<FormerlySerializedAsAttribute> attrs)
    {
        var list = new List<string>();
        foreach (var attr in attrs)
            list.Add(attr.oldName);
        return list.ToArray();
    }
}

[RequiresUnreferencedCode("These methods use reflection and can't be statically analyzed.")]
public static class ReflectionUtils
{
    // Cache for type lookups
    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();
    // Cache for serializable fields (with pre-computed attribute data)
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, CachedFieldInfo[]> SerializableFieldsCache = new();

    internal static void ClearCache()
    {
        TypeCache.Clear();
        SerializableFieldsCache.Clear();
    }

    internal static Type? FindTypeByName(string qualifiedTypeName)
    {
        return TypeCache.GetOrAdd(qualifiedTypeName, typeName => {
            // First try direct type lookup (works for types in the default AssemblyLoadContext)
            Type? t = Type.GetType(typeName);
            if (t != null)
                return t;

            // Strip assembly qualifier to get just the namespace-qualified type name.
            // Assembly-qualified names look like "Namespace.Type, AssemblyName, Version=..."
            // asm.GetType() needs just "Namespace.Type" to search within a specific assembly.
            string typeNameOnly = typeName;
            int commaIdx = typeName.IndexOf(',');
            if (commaIdx >= 0)
                typeNameOnly = typeName.Substring(0, commaIdx).Trim();

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly asm in assemblies)
            {
                // Try full name lookup (handles non-assembly-qualified names)
                t = asm.GetType(typeName);
                if (t != null)
                    return t;

                // Try with stripped assembly qualifier (finds types in custom AssemblyLoadContexts
                // where the assembly-qualified lookup fails)
                if (typeNameOnly != typeName)
                {
                    t = asm.GetType(typeNameOnly);
                    if (t != null)
                        return t;
                }

                // Try name-only lookup (case insensitive) while ignoring load failures
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(type => type != null).Cast<Type>().ToArray();
                }

                t = types.FirstOrDefault(type => type.Name.Equals(typeNameOnly, StringComparison.OrdinalIgnoreCase));
                if (t != null)
                    return t;
            }

            // Former-name fallback: the serialized type may have been renamed. Look for a loaded type
            // carrying [FormerlySerializedAs(oldName)] whose old name matches the (now-missing) requested
            // name - either the old namespace-qualified name or the old short name.
            return FindTypeByFormerName(typeNameOnly);
        });
    }

    private static Type? FindTypeByFormerName(string typeNameOnly)
    {
        string shortName = typeNameOnly;
        int dot = typeNameOnly.LastIndexOf('.');
        if (dot >= 0) shortName = typeNameOnly[(dot + 1)..];

        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(type => type != null).Cast<Type>().ToArray(); }

            foreach (Type type in types)
            {
                foreach (var attr in type.GetCustomAttributes<FormerlySerializedAsAttribute>(false))
                {
                    if (attr.oldName == typeNameOnly || attr.oldName == shortName)
                        return type;
                }
            }
        }
        return null;
    }

    internal static CachedFieldInfo[] GetSerializableFields(this object target)
    {
        Type targetType = target.GetType();
        return SerializableFieldsCache.GetOrAdd(targetType.TypeHandle, _ => {
            const BindingFlags flags = BindingFlags.Public |
                                     BindingFlags.NonPublic |
                                     BindingFlags.Instance |
                                     BindingFlags.DeclaredOnly;

            // Start with the current type
            List<CachedFieldInfo> fields = new List<CachedFieldInfo>();
            Type? currentType = targetType;

            // Walk up the inheritance hierarchy to collect fields from all base types
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(flags))
                {
                    if (IsFieldSerializable(field))
                        fields.Add(new CachedFieldInfo(field));
                }

                currentType = currentType.BaseType;
            }

            return fields.ToArray();
        });
    }

    private static bool IsFieldSerializable(FieldInfo field)
    {
        // Check if field should be serialized
        bool shouldSerialize = field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null;
        if (!shouldSerialize)
            return false;
        // Check if field should be ignored
        bool shouldIgnore = field.GetCustomAttribute<SerializeIgnoreAttribute>() != null ||
                            field.GetCustomAttribute<NonSerializedAttribute>() != null;
        if (shouldIgnore)
            return false;
        return true;
    }
}
