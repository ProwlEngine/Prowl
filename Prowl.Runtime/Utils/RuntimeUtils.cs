// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

public static class RuntimeUtils
{
    private static readonly Dictionary<TypeInfo, bool> s_deepCopyByAssignmentCache = [];

    [OnAssemblyUnload]
    public static void ClearCache()
    {
        s_deepCopyByAssignmentCache.Clear();
    }

    public static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsFreeBSD() => RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD);
    public static bool IsMac() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static Type? FindType(string qualifiedTypeName)
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
            }
            return null;
        }
    }

    /// <summary>
    /// Determines whether two <see cref="MemberInfo"/> instances refer to the same member,
    /// regardless of the context in which each instance was obtained.
    /// </summary>
    public static bool IsEquivalent(this MemberInfo first, MemberInfo second)
    {
        if (first == second)
            return true;

        // Early-out the basic stuff
        if (first.DeclaringType != second.DeclaringType)
            return false;
        if (first.Module != second.Module)
            return false;
        if (first.Name != second.Name)
            return false;

        // Check if they're both the same kind of member
        if (first is FieldInfo)
        {
            if (second is not FieldInfo)
                return false;
        }
        else if (first is PropertyInfo)
        {
            if (second is not PropertyInfo)
                return false;
        }
        else if (first is MethodInfo)
        {
            if (second is not MethodInfo)
                return false;
        }
        else if (first is ConstructorInfo)
        {
            if (second is not ConstructorInfo)
                return false;
        }
        else if (first is EventInfo)
        {
            if (second is not EventInfo)
                return false;
        }
        else if (first is TypeInfo)
        {
            if (second is not TypeInfo)
                return false;
        }

        if (first is MethodBase)
        {
            MethodBase firstMethod = first as MethodBase;
            MethodBase secondMethod = second as MethodBase;

            // If its a generic method, check its generic Type parameters
            if (firstMethod.IsGenericMethod)
            {
                if (!secondMethod.IsGenericMethod)
                    return false;

                Type[] firstGenArgs = firstMethod.GetGenericArguments();
                Type[] secondGenArgs = secondMethod.GetGenericArguments();
                for (int i = 0; i < secondGenArgs.Length; i++)
                {
                    if (firstGenArgs[i] != secondGenArgs[i])
                        return false;
                }
            }

            // Check its parameters
            ParameterInfo[] firstParams = firstMethod.GetParameters();
            ParameterInfo[] secondParams = secondMethod.GetParameters();
            if (firstParams.Length != secondParams.Length)
                return false;

            for (int i = 0; i < firstParams.Length; i++)
            {
                if (firstParams[i].ParameterType != secondParams[i].ParameterType)
                    return false;
                if (firstParams[i].IsOut != secondParams[i].IsOut)
                    return false;
                if (firstParams[i].IsIn != secondParams[i].IsIn)
                    return false;
                if (firstParams[i].IsRetval != secondParams[i].IsRetval)
                    return false;
            }
        }

        return true;
    }

    public static FieldInfo[] GetSerializableFields(this object target)
    {
        FieldInfo[] fields = GetAllFields(target.GetType()).ToArray();
        // Only allow Publics or ones with SerializeField
        fields = fields.Where(field => (field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null) && field.GetCustomAttribute<SerializeIgnoreAttribute>() == null).ToArray();
        // Remove Public NonSerialized fields
        fields = fields.Where(field => !field.IsPublic || field.GetCustomAttribute<NonSerializedAttribute>() == null).ToArray();
        return fields;
    }

    public static IEnumerable<FieldInfo> GetAllFields(Type? t)
    {
        if (t == null)
            return [];

        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                             BindingFlags.Instance | BindingFlags.DeclaredOnly;

        return t.GetFields(flags).Concat(GetAllFields(t.BaseType));
    }

    public static object? GetValue(this MemberInfo member, object? target)
    {
        if (member is PropertyInfo prop)
            return prop.GetValue(target);
        else if (member is FieldInfo field)
            return field.GetValue(target);
        else
            return null;
    }

    public static void SetValue(this MemberInfo member, object? target, object? value)
    {
        if (member is PropertyInfo prop)
            prop.SetValue(target, value);
        else if (member is FieldInfo field)
            field.SetValue(target, value);
    }

    public static string Prettify(string label)
    {
        if (label.StartsWith('_'))
            label = label.Substring(1);

        // Use a StringBuilder to avoid modifying the original string in the loop
        StringBuilder result = new StringBuilder(label.Length * 2);
        result.Append(char.ToUpper(label[0]));

        // Add space before each Capital letter (except the first)
        for (int i = 1; i < label.Length; i++)
        {
            if (char.IsUpper(label[i]) && !char.IsUpper(label[i - 1]))
            {
                result.Append(' ');      // Add space
                result.Append(label[i]); // Append the current uppercase character
            }
            else if (label[i] == '_')
            {
                continue;
            }
            else
            {
                result.Append(label[i]);  // Append the current character
            }
        }

        return Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(result.ToString());
    }

    public static IEnumerable<Type> GetTypesWithAttribute<T>()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
            foreach (var type in assembly.GetTypes())
                if (type.GetCustomAttributes(typeof(T), true).Length > 0)
                    yield return type;
    }

    public static List<Type> FindTypesImplementing(Type propertyType, bool ignoreGenerics = false)
    {
        List<Type> types = [];
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (Type type in asm.GetTypes())
            {
                if (!propertyType.IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                    continue;

                if (ignoreGenerics && type.IsGenericType)
                    continue;

                types.Add(type);
            }
        }
        return types;
    }

    public static string GetUniquePath(string target)
    {
        if (!System.IO.File.Exists(target))
            return target;

        string path = System.IO.Path.GetDirectoryName(target);
        string name = System.IO.Path.GetFileNameWithoutExtension(target);
        string ext = System.IO.Path.GetExtension(target);

        for (int i = 1; ; i++)
        {
            string temp = System.IO.Path.Combine(path, $"{name} ({i}){ext}");
            if (!System.IO.File.Exists(temp))
                return temp;
        }
    }

    public static bool HasAttribute<T>(this MemberInfo member) where T : Attribute => member.GetCustomAttributes(typeof(T), true).Length > 0;
    public static IEnumerable<T> GetAttributes<T>(this MemberInfo member) where T : Attribute => member.GetCustomAttributes(typeof(T), true).Cast<T>();

    /// <summary>
    /// Returns whether the specified type is a primitive, enum, string, decimal, or struct that
    /// consists only of those types, allowing to do a deep-copy by simply assigning it.
    /// </summary>
    /// Source: https://github.com/AdamsLair/duality/blob/4156e696b381e508df409ca4741d1eae88223287/Source/Core/Duality/Utility/ReflectionHelper.cs#L470
    public static bool IsDeepCopyByAssignment(this TypeInfo typeInfo)
    {
        // Early-out for some obvious cases
        if (typeInfo.IsArray) return false;
        if (typeInfo.IsPrimitive) return true;
        if (typeInfo.IsEnum) return true;

        // Special cases for some well-known classes
        Type type = typeInfo.AsType();
        if (type == typeof(string)) return true;
        if (type == typeof(decimal)) return true;

        // Otherwise, any class is not plain old data
        if (typeInfo.IsClass) return false;
        if (typeInfo.IsInterface) return false;

        lock (s_deepCopyByAssignmentCache)
        {
            // If we have no evidence so far, check the cache and iterate fields
            bool isPlainOldData;
            if (s_deepCopyByAssignmentCache.TryGetValue(typeInfo, out isPlainOldData))
            {
                return isPlainOldData;
            }
            else
            {
                isPlainOldData = true;
                foreach (FieldInfo field in typeInfo.DeclaredFieldsDeep())
                {
                    if (field.IsStatic) continue;
                    TypeInfo fieldTypeInfo = field.FieldType.GetTypeInfo();
                    if (!IsDeepCopyByAssignment(fieldTypeInfo))
                    {
                        isPlainOldData = false;
                        break;
                    }
                }

                s_deepCopyByAssignmentCache[typeInfo] = isPlainOldData;
                return isPlainOldData;
            }
        }
    }

    /// <summary>
    /// Returns a TypeInfos BaseType as a TypeInfo, or null if it was null.
    /// </summary>
    /// <param name="type"></param>
    /// Source: https://github.com/AdamsLair/duality/blob/4156e696b381e508df409ca4741d1eae88223287/Source/Core/Duality/Utility/Extensions/ExtMethodsTypeInfo.cs#L45
    public static TypeInfo GetBaseTypeInfo(this TypeInfo type)
    {
        return type.BaseType != null ? type.BaseType.GetTypeInfo() : null;
    }

    /// <summary>
    /// Returns a Types inheritance level. The <c>object</c>-Type has an inheritance level of
    /// zero, each subsequent inheritance increases it by one.
    /// </summary>
    /// Source: https://github.com/AdamsLair/duality/blob/4156e696b381e508df409ca4741d1eae88223287/Source/Core/Duality/Utility/Extensions/ExtMethodsTypeInfo.cs#L45
    public static int GetInheritanceDepth(this TypeInfo type)
    {
        int level = 0;
        while (type.BaseType != null)
        {
            type = type.BaseType.GetTypeInfo();
            level++;
        }
        return level;
    }

    /// <summary>
    /// Returns all fields that are declared within this Type, or any of its base Types.
    /// Includes public, non-public, static and instance fields.
    /// </summary>
    /// Source: https://github.com/AdamsLair/duality/blob/4156e696b381e508df409ca4741d1eae88223287/Source/Core/Duality/Utility/Extensions/ExtMethodsTypeInfo.cs#L45
    public static IEnumerable<FieldInfo> DeclaredFieldsDeep(this TypeInfo type)
    {
        IEnumerable<FieldInfo> result = Enumerable.Empty<FieldInfo>();

        while (type != null)
        {
            result = result.Concat(type.DeclaredFields);
            type = type.GetBaseTypeInfo();
        }

        return result;
    }

    /// <summary>
    /// Returns all properties that are declared within this Type, or any of its base Types.
    /// Includes public, non-public, static and instance properties.
    /// </summary>
    /// Source: https://github.com/AdamsLair/duality/blob/4156e696b381e508df409ca4741d1eae88223287/Source/Core/Duality/Utility/Extensions/ExtMethodsTypeInfo.cs#L45
    public static IEnumerable<PropertyInfo> DeclaredPropertiesDeep(this TypeInfo type)
    {
        IEnumerable<PropertyInfo> result = Enumerable.Empty<PropertyInfo>();

        while (type != null)
        {
            result = result.Concat(type.DeclaredProperties);
            type = type.GetBaseTypeInfo();
        }

        return result;
    }

    /// <summary>
    /// Returns all members that are declared within this Type, or any of its base Types.
    /// Includes public, non-public, static and instance fields.
    /// </summary>
    /// Source: https://github.com/AdamsLair/duality/blob/4156e696b381e508df409ca4741d1eae88223287/Source/Core/Duality/Utility/Extensions/ExtMethodsTypeInfo.cs#L45
    public static IEnumerable<MemberInfo> DeclaredMembersDeep(this TypeInfo type)
    {
        IEnumerable<MemberInfo> result = Enumerable.Empty<MemberInfo>();

        while (type != null)
        {
            result = result.Concat(type.DeclaredMembers);
            type = type.GetBaseTypeInfo();
        }

        return result;
    }
}
