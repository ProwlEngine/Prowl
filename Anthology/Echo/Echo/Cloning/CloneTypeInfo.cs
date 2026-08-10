// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Prowl.Echo.Cloning;

internal readonly struct CloneFieldInfo
{
    public readonly FieldInfo Field;
    public readonly CloneFieldFlags Flags;
    public readonly CloneBehavior Behavior;
    public readonly Type? BehaviorTarget;

    public CloneFieldInfo(FieldInfo field, CloneFieldFlags flags, CloneBehaviorAttribute? behavior)
    {
        Field = field;
        Flags = flags;
        Behavior = behavior?.Behavior ?? CloneBehavior.Default;
        BehaviorTarget = behavior?.TargetType;
    }
}

/// <summary>
/// Cloning rules for a type, worked out once and cached.
/// </summary>
[RequiresUnreferencedCode("Cloning reflects over every instance field and cannot be statically analyzed.")]
internal sealed class CloneTypeInfo
{
    private static readonly Dictionary<Type, CloneTypeInfo> _cache = new();
    private static readonly object _cacheLock = new();

    public readonly Type Type;

    /// <summary>Immutable or plain data. Assigning it across is a complete copy.</summary>
    public readonly bool CopyByAssignment;

    public readonly CloneBehavior Behavior;
    public readonly bool IsArray;
    public readonly ICloneFormat? Format;
    public readonly bool RequiresMerge;

    /// <summary>False only when nothing reachable through this type can be owned, which prunes the setup pass.</summary>
    public bool InvestigateOwnership;

    public CloneTypeInfo? ElementType;
    public CloneFieldInfo[] Fields = [];

    public static CloneTypeInfo Get(Type type)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(type, out CloneTypeInfo? cached))
                return cached;

            var info = new CloneTypeInfo(type);

            // Published before it is filled in, since a type's fields can lead back to it. The lock is
            // reentrant, so the recursive lookup finds the partial entry rather than looping.
            _cache[type] = info;
            try
            {
                info.Init();
            }
            catch
            {
                _cache.Remove(type);
                throw;
            }

            return info;
        }
    }

    public static void ClearCache()
    {
        lock (_cacheLock)
            _cache.Clear();
    }

    private CloneTypeInfo(Type type)
    {
        Type = type;
        CopyByAssignment = IsDeepCopyByAssignment(type);
        InvestigateOwnership = !CopyByAssignment;
        IsArray = type.IsArray;
        Behavior = ResolveTypeBehavior(type);

        if (!CopyByAssignment)
        {
            Format = CloneFormats.For(type);
            RequiresMerge = Format is ICloneLateFormat { RequiresMerge: true };
        }
    }

    private void Init()
    {
        if (CopyByAssignment) return;

        if (IsArray)
        {
            if (Type.GetArrayRank() > 1)
                throw new NotSupportedException(
                    $"Cloning multidimensional arrays is not supported ({Type}). " +
                    "Skip the field with [CloneField(CloneFieldFlags.Skip)] or use a jagged array.");

            ElementType = Get(Type.GetElementType()!);
            InvestigateOwnership = !ElementType.CopyByAssignment;
            return;
        }

        if (Format != null) return;

        var fields = new List<CloneFieldInfo>();
        Type? current = Type;
        while (current != null && current != typeof(object))
        {
            bool typeIsManual = current.IsDefined(typeof(ManuallyClonedAttribute), false);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (FieldInfo field in current.GetFields(flags))
            {
                if (typeIsManual || field.IsDefined(typeof(ManuallyClonedAttribute), false)) continue;

                CloneFieldFlags fieldFlags = field.GetCustomAttribute<CloneFieldAttribute>()?.Flags ?? CloneFieldFlags.None;
                if ((fieldFlags & CloneFieldFlags.Skip) != 0) continue;

                // Serialization exclusions apply unless the field opts back in.
                if ((fieldFlags & CloneFieldFlags.DontSkip) == 0 && IsExcludedFromSerialization(field))
                    continue;

                fields.Add(new CloneFieldInfo(field, fieldFlags, field.GetCustomAttribute<CloneBehaviorAttribute>()));
            }

            current = current.BaseType;
        }

        Fields = fields.ToArray();
    }

    private static bool IsExcludedFromSerialization(FieldInfo field)
        => field.IsDefined(typeof(SerializeIgnoreAttribute), false) ||
           field.IsDefined(typeof(NonSerializedAttribute), false);

    private static CloneBehavior ResolveTypeBehavior(Type type)
    {
        var attribute = type.GetCustomAttribute<CloneBehaviorAttribute>(inherit: true);
        if (attribute != null && attribute.Behavior != CloneBehavior.Default)
            return attribute.Behavior;

        foreach (Type contract in type.GetInterfaces())
        {
            var fromInterface = contract.GetCustomAttribute<CloneBehaviorAttribute>(inherit: false);
            if (fromInterface != null && fromInterface.Behavior != CloneBehavior.Default)
                return fromInterface.Behavior;
        }

        return CloneBehavior.ChildObject;
    }

    private static bool IsDeepCopyByAssignment(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type.IsPointer) return true;
        if (type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid) ||
            type == typeof(Uri) || type == typeof(Version))
            return true;
        if (typeof(MemberInfo).IsAssignableFrom(type)) return true;

        // Framework types that are effectively immutable and whose innards must not be walked. Anything
        // else opaque needs [CloneBehavior(CloneBehavior.Reference)] on the field that holds it.
        if (typeof(System.Text.RegularExpressions.Regex).IsAssignableFrom(type) ||
            typeof(System.Globalization.CultureInfo).IsAssignableFrom(type) ||
            typeof(System.Text.Encoding).IsAssignableFrom(type) ||
            typeof(TimeZoneInfo).IsAssignableFrom(type))
            return true;

        if (!type.IsValueType) return false;

        // A struct is plain data when everything in it is. Structs cannot contain themselves, so this ends.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (FieldInfo field in type.GetFields(flags))
            if (!IsDeepCopyByAssignment(field.FieldType))
                return false;

        return true;
    }
}
