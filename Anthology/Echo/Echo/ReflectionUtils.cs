// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Prowl.Echo;

[RequiresUnreferencedCode("These methods use reflection and can't be statically analyzed.")]
internal static class ReflectionUtils
{
    internal static Type? FindType(string qualifiedTypeName)
    {
        Type? t = Type.GetType(qualifiedTypeName);

        if (t != null)
        {
            return t;
        }
        else
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(qualifiedTypeName);
                if (t != null)
                    return t;

                // If not found, try to find by name without namespace
                t = asm.GetTypes().FirstOrDefault(t => t.Name.Equals(qualifiedTypeName, StringComparison.OrdinalIgnoreCase));
                if (t != null)
                    return t;
            }
            return null;
        }
    }

    internal static FieldInfo[] GetSerializableFields(this object target)
    {
        FieldInfo[] fields = GetAllFields(target.GetType()).ToArray();
        // Only allow Publics or ones with SerializeField
        fields = fields.Where(field => (field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null) && field.GetCustomAttribute<SerializeIgnoreAttribute>() == null).ToArray();
        // Remove Public NonSerialized fields
        fields = fields.Where(field => !field.IsPublic || field.GetCustomAttribute<NonSerializedAttribute>() == null).ToArray();
        return fields;
    }

    internal static IEnumerable<FieldInfo> GetAllFields(Type? t)
    {
        if (t == null)
            return Enumerable.Empty<FieldInfo>();

        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                             BindingFlags.Instance | BindingFlags.DeclaredOnly;

        return t.GetFields(flags).Concat(GetAllFields(t.BaseType));
    }
}
