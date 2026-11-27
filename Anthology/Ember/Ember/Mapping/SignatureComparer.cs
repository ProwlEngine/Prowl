using System;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// Type and signature comparison, split by which sides of the reload are involved. Keeping the two cases
/// apart matters: comparing two current side types is plain structural equality, while comparing a previous
/// side type against a current side one has to go through <see cref="TypeMap"/> and must never try to resolve
/// a generic parameter.
/// </summary>
internal static class SignatureComparer
{
    /// <summary>Structural equality between two types on the same side of the reload.</summary>
    public static bool SameShape(Type? a, Type? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;

        if (a.IsArray && b.IsArray)
        {
            if (a.GetArrayRank() != b.GetArrayRank()) return false;
            return SameShape(a.GetElementType(), b.GetElementType());
        }

        if (a.IsByRef && b.IsByRef)
            return SameShape(a.GetElementType(), b.GetElementType());

        if (a.IsGenericType && b.IsGenericType)
        {
            if (!SameShape(a.GetGenericTypeDefinition(), b.GetGenericTypeDefinition())) return false;

            var left = a.GetGenericArguments();
            var right = b.GetGenericArguments();
            if (left.Length != right.Length) return false;

            for (int i = 0; i < left.Length; i++)
                if (!SameShape(left[i], right[i])) return false;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a previous side type denotes the same type as a current side one.
    /// </summary>
    /// <remarks>
    /// Generic parameters compare by position only. Resolving them instead would recurse forever when matching
    /// a generic method by its own type parameter typed parameters, which is the whole reason the position
    /// rule exists.
    /// </remarks>
    public static bool Corresponds(Type? previous, Type? current, TypeMap map)
    {
        if (ReferenceEquals(previous, current)) return true;
        if (previous == null || current == null) return false;

        if (previous.IsArray != current.IsArray) return false;
        if (previous.IsArray)
        {
            if (previous.GetArrayRank() != current.GetArrayRank()) return false;
            return Corresponds(previous.GetElementType(), current.GetElementType(), map);
        }

        if (previous.IsByRef != current.IsByRef) return false;
        if (previous.IsByRef)
            return Corresponds(previous.GetElementType(), current.GetElementType(), map);

        if (previous.IsConstructedGenericType != current.IsConstructedGenericType) return false;
        if (previous.IsConstructedGenericType)
        {
            if (!Corresponds(previous.GetGenericTypeDefinition(), current.GetGenericTypeDefinition(), map)) return false;

            var left = previous.GetGenericArguments();
            var right = current.GetGenericArguments();
            if (left.Length != right.Length) return false;

            for (int i = 0; i < left.Length; i++)
                if (!Corresponds(left[i], right[i], map)) return false;

            return true;
        }

        if (previous.IsGenericMethodParameter != current.IsGenericMethodParameter) return false;
        if (previous.IsGenericMethodParameter)
            return previous.GenericParameterPosition == current.GenericParameterPosition;

        if (previous.IsGenericTypeParameter != current.IsGenericTypeParameter) return false;
        if (previous.IsGenericTypeParameter)
            return previous.GenericParameterPosition == current.GenericParameterPosition;

        var resolved = map.Target(previous);
        return !ReferenceEquals(resolved, previous) && SameShape(resolved, current);
    }

    /// <summary>Whether a previous side method matches a current side one by name, kind, and signature.</summary>
    public static bool Corresponds(MemberInfo previous, MemberInfo current, TypeMap map)
    {
        if (previous.Name != current.Name) return false;
        if (previous.GetType() != current.GetType()) return false;

        if (previous is not MethodBase previousMethod) return true;
        var currentMethod = (MethodBase)current;

        if (previousMethod is MethodInfo { IsConstructedGenericMethod: true } constructed)
            previousMethod = constructed.GetGenericMethodDefinition();

        var left = previousMethod.GetParameters();
        var right = currentMethod.GetParameters();
        if (left.Length != right.Length) return false;

        for (int i = 0; i < left.Length; i++)
            if (!Corresponds(left[i].ParameterType, right[i].ParameterType, map))
                return false;

        return previousMethod.IsGenericMethodDefinition || previousMethod.IsConstructedGenericMethod
            ? currentMethod.IsGenericMethodDefinition
            : !currentMethod.IsGenericMethodDefinition;
    }
}
