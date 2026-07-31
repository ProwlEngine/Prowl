using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Ember;

public enum TypeResolutionKind
{
    /// <summary>The type carries over as itself.</summary>
    Unchanged,

    /// <summary>The type has a counterpart in a replacement assembly. <c>Target</c> is it.</summary>
    Substituted,

    /// <summary>The type no longer exists. References to its instances become null.</summary>
    Removed,
}

public readonly record struct TypeResolution(TypeResolutionKind Kind, Type? Target)
{
    public bool IsUnchanged => Kind == TypeResolutionKind.Unchanged;
    public bool IsSubstituted => Kind == TypeResolutionKind.Substituted;
    public bool IsRemoved => Kind == TypeResolutionKind.Removed;

    internal static TypeResolution Unchanged(Type type) => new(TypeResolutionKind.Unchanged, type);
    internal static TypeResolution Substituted(Type type) => new(TypeResolutionKind.Substituted, type);
    internal static readonly TypeResolution Removed = new(TypeResolutionKind.Removed, null);
}

/// <summary>
/// What a previous side type becomes after the reload. The tri state answer is deliberate: a null target on
/// its own cannot distinguish "deleted" from "we failed to match it", and callers need to tell those apart.
/// </summary>
public sealed class TypeMap
{
    private const BindingFlags AnyNested = BindingFlags.Public | BindingFlags.NonPublic;

    private readonly AssemblyMap _assemblies;
    private readonly SyntheticIndexes _indexes;
    private readonly ReportBuilder _report;
    private readonly Dictionary<Type, TypeResolution> _cache = new();
    private readonly HashSet<Type> _resolving = new();

    private MemberMap _members = null!;

    internal TypeMap(AssemblyMap assemblies, SyntheticIndexes indexes, ReportBuilder report)
    {
        _assemblies = assemblies;
        _indexes = indexes;
        _report = report;
    }

    internal void UseMembers(MemberMap members) => _members = members;

    public bool IsSubstituted(Type type) => Resolve(type).IsSubstituted;

    /// <summary>The resolved type, or null when it was removed. Prefer <see cref="Resolve"/> where it matters.</summary>
    public Type? Target(Type type) => Resolve(type).Target;

    public TypeResolution Resolve(Type type)
    {
        if (_cache.TryGetValue(type, out var cached)) return cached;

        // A type whose resolution depends on its own resolution. Answering "unchanged" would be silently wrong
        // for a replaced type, so the answer is only assumed where it cannot be wrong, and reported otherwise.
        if (!_resolving.Add(type)) return ReenteredResolution(type);

        try
        {
            var resolution = ResolveUncached(type);
            _cache[type] = resolution;
            return resolution;
        }
        finally
        {
            _resolving.Remove(type);
        }
    }

    private TypeResolution ReenteredResolution(Type type)
    {
        if (!_assemblies.IsSubstituted(type.Assembly))
            return TypeResolution.Unchanged(type);

        _report.Report(ReloadCode.ResolutionCycle, ReloadSeverity.Error,
            "Resolving this type requires resolving itself, so it cannot be matched. Treated as removed.",
            type.FullName ?? type.Name);

        return TypeResolution.Removed;
    }

    private TypeResolution ResolveUncached(Type type)
    {
        if (type.IsArray) return ResolveArray(type);
        if (type.IsByRef) return ResolveWrapped(type, static t => t.MakeByRefType());
        if (type.IsPointer) return ResolveWrapped(type, static t => t.MakePointerType());
        if (type.IsConstructedGenericType) return ResolveConstructedGeneric(type);

        var assembly = _assemblies.Resolve(type.Assembly);

        return assembly.Kind switch
        {
            AssemblyResolutionKind.Removed => Removed(type),
            AssemblyResolutionKind.Substituted => Substitute(type, assembly.Target!),
            _ => Validate(type) ? TypeResolution.Unchanged(type) : TypeResolution.Removed,
        };
    }

    private TypeResolution Removed(Type type)
    {
        _report.Report(ReloadCode.TypeRemoved, ReloadSeverity.Info,
            "Type no longer exists. References to its instances become null.", type.FullName ?? type.Name);
        return TypeResolution.Removed;
    }

    private TypeResolution ResolveArray(Type type)
    {
        var element = Resolve(type.GetElementType()!);
        if (element.IsRemoved) return TypeResolution.Removed;
        if (element.IsUnchanged) return TypeResolution.Unchanged(type);

        int rank = type.GetArrayRank();
        return TypeResolution.Substituted(rank == 1 ? element.Target!.MakeArrayType() : element.Target!.MakeArrayType(rank));
    }

    private TypeResolution ResolveWrapped(Type type, Func<Type, Type> wrap)
    {
        var element = Resolve(type.GetElementType()!);
        if (element.IsRemoved) return TypeResolution.Removed;
        return element.IsUnchanged ? TypeResolution.Unchanged(type) : TypeResolution.Substituted(wrap(element.Target!));
    }

    private TypeResolution ResolveConstructedGeneric(Type type)
    {
        var definition = Resolve(type.GetGenericTypeDefinition());
        if (definition.IsRemoved) return TypeResolution.Removed;

        var arguments = type.GetGenericArguments();
        bool changed = definition.IsSubstituted;

        for (int i = 0; i < arguments.Length; i++)
        {
            var argument = Resolve(arguments[i]);
            if (argument.IsRemoved) return TypeResolution.Removed;

            changed |= argument.IsSubstituted;
            arguments[i] = argument.Target!;
        }

        return changed
            ? TypeResolution.Substituted(definition.Target!.MakeGenericType(arguments))
            : TypeResolution.Unchanged(type);
    }

    private TypeResolution Substitute(Type type, Assembly target)
    {
        if (SyntheticName.TryParse(type.Name, out var name))
            return SubstituteSynthetic(type, target, name);

        if (type.FullName == null)
            return SubstituteUnnamed(type);

        var candidate = target.GetType(type.FullName);
        if (candidate == null) return Removed(type);

        return Validate(candidate) ? TypeResolution.Substituted(candidate) : TypeResolution.Removed;
    }

    private TypeResolution SubstituteSynthetic(Type type, Assembly target, in SyntheticName name)
    {
        switch (name.Kind)
        {
            case SyntheticKind.LambdaDisplayClass:
            case SyntheticKind.StateMachine:
                return SubstituteNested(type, name);

            case SyntheticKind.AnonymousType:
                return SubstituteAnonymous(type, target);

            case SyntheticKind.ReadOnlyList:
            case SyntheticKind.InlineArray:
                return type.FullName is { } fullName && target.GetType(fullName) is { } found
                    ? TypeResolution.Substituted(found)
                    : TypeResolution.Removed;

            default:
                // Other synthetic kinds are not structurally matched, so they cannot be carried over.
                return TypeResolution.Removed;
        }
    }

    // A display class or state machine is matched by the ordinal of the method it was declared inside, which
    // is stable across an edit in a way its own name is not.
    private TypeResolution SubstituteNested(Type type, in SyntheticName name)
    {
        var declaring = type.DeclaringType;
        if (declaring == null) return TypeResolution.Removed;

        var newDeclaring = Resolve(declaring);
        if (newDeclaring.Target is not { } target) return TypeResolution.Removed;

        if (name.Ordinal < 0 && name.SubOrdinal < 0)
        {
            var byName = target.GetNestedType(type.Name, AnyNested);
            return byName == null ? TypeResolution.Removed : TypeResolution.Substituted(byName);
        }

        var previousIndex = _indexes.For(declaring.Assembly);
        var scope = previousIndex.FindScopeMethod(declaring, null, name.Ordinal);

        if (scope == null)
        {
            _report.Report(ReloadCode.ScopeMethodUnmatched, ReloadSeverity.Warning,
                $"No method with lambda scope ordinal {name.Ordinal} was found, so its {name.Kind} cannot be matched.",
                declaring.FullName);
            return TypeResolution.Removed;
        }

        if (_members.ResolveMethod(scope) is not { } newScope) return TypeResolution.Removed;

        var currentIndex = _indexes.For(target.Assembly);
        var key = new SyntheticTypeKey(
            target.FullName ?? target.Name, name.Kind, name.Suffix,
            currentIndex.ScopeOrdinalOf(newScope), name.SubOrdinal);

        if (currentIndex.TryGetType(key, out var matched))
            return TypeResolution.Substituted(matched);

        _report.Report(ReloadCode.SyntheticTypeUnmatched, ReloadSeverity.Warning,
            $"No counterpart for this {name.Kind}. Anything it held is dropped.", type.FullName ?? type.Name);
        return TypeResolution.Removed;
    }

    private TypeResolution SubstituteAnonymous(Type type, Assembly target)
    {
        if (!type.IsGenericTypeDefinition) return TypeResolution.Unchanged(type);

        var properties = AnonymousPropertyNames(type);
        if (properties == null) return TypeResolution.Removed;

        var matched = _indexes.For(target).FindAnonymous(properties);
        if (matched != null) return TypeResolution.Substituted(matched);

        _report.Report(ReloadCode.AnonymousTypeUnmatched, ReloadSeverity.Warning,
            $"No anonymous type with properties {{ {string.Join(", ", properties)} }} exists after the reload.", null);
        return TypeResolution.Removed;
    }

    private static string[]? AnonymousPropertyNames(Type type)
    {
        var parameters = type.GetGenericArguments();
        if (parameters.Length == 0) return null;

        var names = new string[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var name = parameters[i].Name;
            const string suffix = ">j__TPar";
            if (name.Length <= suffix.Length + 1 || name[0] != '<' || !name.EndsWith(suffix, StringComparison.Ordinal))
                return null;

            names[i] = name[1..^suffix.Length];
        }
        return names;
    }

    // Generic parameters and some nested types have no full name, so they are located through their owner.
    private TypeResolution SubstituteUnnamed(Type type)
    {
        if (type.DeclaringType == null) return TypeResolution.Removed;

        if (type.IsGenericMethodParameter)
        {
            var method = _members.ResolveMethod(type.DeclaringMethod!);
            var arguments = method?.GetGenericArguments();
            return arguments != null && type.GenericParameterPosition < arguments.Length
                ? TypeResolution.Substituted(arguments[type.GenericParameterPosition])
                : TypeResolution.Removed;
        }

        var declaring = Resolve(type.DeclaringType);
        if (declaring.Target is not { } target) return TypeResolution.Removed;

        if (type.IsGenericTypeParameter)
        {
            var arguments = target.GetGenericArguments();
            return type.GenericParameterPosition < arguments.Length
                ? TypeResolution.Substituted(arguments[type.GenericParameterPosition])
                : TypeResolution.Removed;
        }

        var nested = target.GetNestedType(type.Name, AnyNested);
        return nested == null ? TypeResolution.Removed : TypeResolution.Substituted(nested);
    }

    /// <summary>
    /// A candidate is only usable if it does not itself inherit from, or implement, something defined in an
    /// assembly this reload is replacing. That happens when assembly B references A and only A was swapped.
    /// </summary>
    private bool Validate(Type candidate)
    {
        foreach (var contract in candidate.GetInterfaces())
            if (_assemblies.IsSubstituted(contract.Assembly))
                return RejectSubstitution(candidate, contract.Assembly);

        for (var baseType = candidate; baseType != null; baseType = baseType.BaseType)
            if (_assemblies.IsSubstituted(baseType.Assembly))
                return RejectSubstitution(candidate, baseType.Assembly);

        return true;
    }

    private bool RejectSubstitution(Type candidate, Assembly offending)
    {
        _report.Report(ReloadCode.TypeSubstitutionRejected, ReloadSeverity.Warning,
            $"Cannot be used without referencing {offending.GetName().Name}, which this reload replaces. Treated as removed.",
            candidate.FullName ?? candidate.Name);
        return false;
    }
}
