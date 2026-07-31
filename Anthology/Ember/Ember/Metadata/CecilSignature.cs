using System;
using System.Linq;

using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Prowl.Ember;

/// <summary>Matches Cecil type references against live runtime types, so a runtime member can be found in metadata.</summary>
internal static class CecilSignature
{
    /// <summary>
    /// Whether a Cecil type reference denotes the same type as a runtime type. Handles modifiers, arrays,
    /// byref, generic parameters, nesting, constructed generics, and generic definitions.
    /// </summary>
    public static bool Matches(TypeReference? reference, Type? type)
    {
        if (reference == null && type == null) return true;
        if (reference == null || type == null) return false;

        if (reference is RequiredModifierType required)
            reference = required.ElementType;

        if (type.IsArray)
        {
            if (reference is not ArrayType array) return false;
            if (type.GetArrayRank() != array.Rank) return false;
            return Matches(array.ElementType, type.GetElementType());
        }
        if (reference.IsArray) return false;

        if (type.IsByRef)
        {
            if (reference is not ByReferenceType byRef) return false;
            return Matches(byRef.ElementType, type.GetElementType());
        }
        if (reference.IsByReference) return false;

        if (type.IsGenericParameter) return MatchesGenericParameter(reference, type);

        if (type.Name != reference.Name) return false;

        if (type.DeclaringType != null)
        {
            if (!Matches(reference.DeclaringType, type.DeclaringType)) return false;
        }
        else if (reference.DeclaringType != null)
        {
            return false;
        }
        else if ((type.Namespace ?? string.Empty) != (reference.Namespace ?? string.Empty))
        {
            return false;
        }

        if (type.IsConstructedGenericType)
        {
            if (reference is not GenericInstanceType instance) return false;

            var arguments = type.GenericTypeArguments;
            if (arguments.Length != instance.GenericArguments.Count) return false;

            for (int i = 0; i < arguments.Length; i++)
                if (!Matches(instance.GenericArguments[i], arguments[i]))
                    return false;
        }
        else if (reference.IsGenericInstance)
        {
            return false;
        }

        if (type.IsGenericTypeDefinition)
        {
            if (!reference.HasGenericParameters) return false;
            if (type.GetGenericArguments().Length != reference.GenericParameters.Count) return false;
        }

        return true;
    }

    private static bool MatchesGenericParameter(TypeReference reference, Type type)
    {
        if (reference is not GenericParameter parameter) return false;
        if (parameter.Position != type.GenericParameterPosition) return false;

        if (type.DeclaringMethod != null)
        {
            if (type.DeclaringMethod.Name != parameter.DeclaringMethod?.Name) return false;

            // A generic method on a constructed generic type compares against the open declaring type.
            if (parameter.DeclaringMethod.DeclaringType.IsGenericInstance
                && (type.DeclaringMethod.DeclaringType?.IsGenericTypeDefinition ?? false))
                return Matches(parameter.DeclaringMethod.DeclaringType.GetElementType(), type.DeclaringMethod.DeclaringType);

            return Matches(parameter.DeclaringMethod.DeclaringType, type.DeclaringMethod.DeclaringType);
        }

        return type.DeclaringType != null && Matches(parameter.DeclaringType, type.DeclaringType);
    }

    /// <summary>
    /// Substitutes a method reference's generic arguments into a type reference, so parameter types compare
    /// correctly for a constructed generic method.
    /// </summary>
    public static TypeReference Substitute(MethodReference method, TypeReference type)
    {
        if (!type.ContainsGenericParameter) return type;

        switch (type)
        {
            case GenericParameter parameter:
                return SubstituteParameter(method, parameter);

            case ArrayType array:
                var element = Substitute(method, array.ElementType);
                return array.Rank == 1 ? element.MakeArrayType() : element.MakeArrayType(array.Rank);

            case ByReferenceType byRef:
                return Substitute(method, byRef.ElementType).MakeByReferenceType();

            case GenericInstanceType instance:
                var definition = Substitute(method, instance.ElementType);
                var arguments = instance.GenericArguments.Select(x => Substitute(method, x)).ToArray();
                return definition.MakeGenericInstanceType(arguments);

            default:
                return type;
        }
    }

    private static TypeReference SubstituteParameter(MethodReference method, GenericParameter parameter)
    {
        if (parameter.DeclaringMethod != null)
        {
            if (method is GenericInstanceMethod generic
                && generic.GetElementMethod().FullName == parameter.DeclaringMethod.FullName)
                return generic.GenericArguments[parameter.Position];

            return parameter;
        }

        if (parameter.DeclaringType == null) return parameter;

        for (var declaring = method.DeclaringType; declaring != null; declaring = declaring.DeclaringType)
            if (declaring is GenericInstanceType instance
                && instance.GetElementType().FullName == parameter.DeclaringType.FullName)
                return instance.GenericArguments[parameter.Position];

        return parameter;
    }
}
