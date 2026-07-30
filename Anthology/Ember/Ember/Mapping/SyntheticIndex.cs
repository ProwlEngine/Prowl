using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Prowl.Ember;

/// <summary>Identifies a compiler generated nested type by what it is, rather than by its unstable name.</summary>
internal readonly record struct SyntheticTypeKey(
    string DeclaringTypeName, SyntheticKind Kind, string? Suffix, int ScopeOrdinal, int SubOrdinal);

/// <summary>Identifies a lambda or local function by its scope method and its position within it.</summary>
internal readonly record struct LambdaKey(
    string DeclaringTypeName, string? ScopeName, int ScopeOrdinal, int LambdaOrdinal);

/// <summary>Where a lambda method lives. The declaring type may be a generic definition to be closed later.</summary>
internal readonly record struct LambdaEntry(Type DeclaringType, string MethodName);

/// <summary>
/// Everything compiler generated in one assembly, indexed once. Matching a display class, state machine, or
/// lambda across a reload then costs a dictionary lookup instead of a search that re-decodes method bodies.
/// </summary>
internal sealed class SyntheticIndex
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly ConditionalWeakTable<Assembly, SyntheticIndex> s_cache = new();

    private readonly Dictionary<SyntheticTypeKey, Type> _types = new();
    private readonly Dictionary<LambdaKey, LambdaEntry> _lambdas = new();
    private readonly Dictionary<string, Type> _anonymous = new(StringComparer.Ordinal);
    private readonly Dictionary<(Type, string?, int), MethodBase?> _scopeMethods = new();

    private readonly Assembly _assembly;
    private AssemblyMetadata? _metadata;

    private SyntheticIndex(Assembly assembly)
    {
        _assembly = assembly;
        Build();
    }

    /// <summary>
    /// Indexes are cached per assembly across reloads. An assembly's metadata cannot change while it is
    /// loaded, so the index stays valid; the weak table keeps an unloaded assembly from being pinned.
    /// </summary>
    public static SyntheticIndex For(Assembly assembly)
        => s_cache.GetValue(assembly, static a => new SyntheticIndex(a));

    /// <summary>
    /// The metadata reader is supplied per reload, because it depends on the caller's byte resolver. Only the
    /// scope ordinal lookups need it, and those are cached on the reader.
    /// </summary>
    public void UseMetadata(AssemblyMetadata? metadata)
    {
        if (ReferenceEquals(_metadata, metadata)) return;
        _metadata = metadata;
        _scopeMethods.Clear();
    }

    public bool TryGetType(in SyntheticTypeKey key, out Type type) => _types.TryGetValue(key, out type!);
    public bool TryGetLambda(in LambdaKey key, out LambdaEntry entry) => _lambdas.TryGetValue(key, out entry);

    public Type? FindAnonymous(string[] propertyNames)
        => _anonymous.TryGetValue(AnonymousKey(propertyNames), out var type) ? type : null;

    // Property names are identifiers, so a comma cannot appear inside one and distinct lists cannot collide.
    private static string AnonymousKey(string[] propertyNames) => string.Join(',', propertyNames);

    /// <summary>The method a lambda was declared inside, found by the ordinal Roslyn gave it.</summary>
    public MethodBase? FindScopeMethod(Type declaringType, string? scopeName, int scopeOrdinal)
    {
        if (scopeOrdinal < 0) return null;

        var key = (declaringType, scopeName, scopeOrdinal);
        if (_scopeMethods.TryGetValue(key, out var cached)) return cached;

        MethodBase? found = null;
        foreach (var candidate in CandidateScopeMethods(declaringType, scopeName))
        {
            if (ScopeOrdinalOf(candidate) != scopeOrdinal) continue;
            found = candidate;
            break;
        }

        return _scopeMethods[key] = found;
    }

    public int ScopeOrdinalOf(MethodBase method) => _metadata?.GetLambdaScopeOrdinal(method) ?? -1;

    private static IEnumerable<MethodBase> CandidateScopeMethods(Type type, string? name) => name switch
    {
        null => type.GetConstructors(AllDeclared).Cast<MethodBase>().Concat(type.GetMethods(AllDeclared)),
        ".ctor" => type.GetConstructors(AllDeclared | BindingFlags.Instance),
        ".cctor" => type.GetConstructors(AllDeclared | BindingFlags.Static),
        _ => type.GetMethods(AllDeclared).Where(x => x.Name == name),
    };

    private void Build()
    {
        foreach (var type in SafeTypes(_assembly))
        {
            if (!SyntheticName.TryParse(type.Name, out var name))
            {
                IndexLambdasOn(type, type, scopeOrdinalFromDisplayClass: -1);
                continue;
            }

            switch (name.Kind)
            {
                case SyntheticKind.AnonymousType:
                    IndexAnonymous(type);
                    break;

                case SyntheticKind.LambdaDisplayClass:
                case SyntheticKind.StateMachine:
                    IndexSyntheticType(type, name);
                    break;
            }
        }
    }

    private void IndexSyntheticType(Type type, in SyntheticName name)
    {
        var declaring = type.DeclaringType;
        if (declaring?.FullName == null) return;

        if (name.Ordinal >= 0)
            _types[new SyntheticTypeKey(declaring.FullName, name.Kind, name.Suffix, name.Ordinal, name.SubOrdinal)] = type;

        if (name.Kind != SyntheticKind.LambdaDisplayClass) return;

        // A display class carries the scope ordinal. A plain "<>c" cache class does not, and its methods carry
        // both ordinals themselves.
        IndexLambdasOn(type, declaring, name.Suffix == "DisplayClass" ? name.Ordinal : -1);
    }

    private void IndexLambdasOn(Type container, Type declaring, int scopeOrdinalFromDisplayClass)
    {
        if (declaring.FullName is not { } declaringName) return;

        foreach (var method in container.GetMethods(AllDeclared))
        {
            if (!SyntheticName.TryParse(method.Name, out var name) || !name.IsLambdaLike) continue;
            if (name.Ordinal < 0) continue;

            int scopeOrdinal, lambdaOrdinal;

            if (scopeOrdinalFromDisplayClass >= 0)
            {
                // On a display class the method name carries only its own ordinal.
                if (name.SubOrdinal >= 0) continue;
                scopeOrdinal = scopeOrdinalFromDisplayClass;
                lambdaOrdinal = name.Ordinal;
            }
            else
            {
                if (name.SubOrdinal < 0) continue;
                scopeOrdinal = name.Ordinal;
                lambdaOrdinal = name.SubOrdinal;
            }

            _lambdas[new LambdaKey(declaringName, name.Scope, scopeOrdinal, lambdaOrdinal)]
                = new LambdaEntry(container, method.Name);
        }
    }

    // An anonymous type is a generic definition whose parameters are named "<PropertyName>j__TPar". The
    // compiler reuses one definition per ordered property list, so that list is the identity.
    private void IndexAnonymous(Type type)
    {
        if (!type.IsGenericTypeDefinition) return;
        if (!type.Name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal)) return;

        var parameters = type.GetGenericArguments();
        if (parameters.Length == 0) return;

        var names = new string[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (!TryReadAnonymousPropertyName(parameters[i].Name, out var propertyName)) return;
            names[i] = propertyName;
        }

        _anonymous[AnonymousKey(names)] = type;
    }

    private static bool TryReadAnonymousPropertyName(string parameterName, out string propertyName)
    {
        const string suffix = ">j__TPar";
        propertyName = string.Empty;

        if (parameterName.Length <= suffix.Length + 1) return false;
        if (parameterName[0] != '<') return false;
        if (!parameterName.EndsWith(suffix, StringComparison.Ordinal)) return false;

        propertyName = parameterName[1..^suffix.Length];
        return propertyName.Length > 0 && !propertyName.Contains('>');
    }

    internal static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
    }
}
