using System;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>Where a lambda method physically lives, which is what identifies its owning user type.</summary>
internal enum LambdaHome
{
    Unknown,

    /// <summary>Directly on the user type. A local function, or a lambda that captures only the instance.</summary>
    UserType,

    /// <summary>On the singleton cache class. Captures nothing.</summary>
    CacheClass,

    /// <summary>On a closure. Captures locals.</summary>
    DisplayClass,
}

/// <summary>What a rebuilt delegate needs for its target.</summary>
internal enum CaptureMode
{
    Unknown,

    /// <summary>Nothing captured. The target is either null or the cache class singleton.</summary>
    None,

    /// <summary>Only the declaring instance was captured.</summary>
    Instance,

    /// <summary>Locals were captured into a closure object.</summary>
    DisplayClass,
}

internal readonly record struct LambdaIdentity(
    Type UserType, LambdaHome Home, CaptureMode Capture, string? ScopeName, int ScopeOrdinal, int LambdaOrdinal)
{
    public bool IsValid => UserType != null && Home != LambdaHome.Unknown;
}

/// <summary>
/// Matches a compiler generated lambda or local function to its counterpart after a reload, by the ordinal of
/// the method it was written inside rather than by its own name, which is not stable across an edit.
/// </summary>
internal sealed class LambdaMatcher
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private readonly TypeMap _types;
    private readonly MemberMap _members;
    private readonly SyntheticIndexes _indexes;
    private readonly ReportBuilder _report;

    public LambdaMatcher(TypeMap types, MemberMap members, SyntheticIndexes indexes, ReportBuilder report)
    {
        _types = types;
        _members = members;
        _indexes = indexes;
        _report = report;
    }

    public static bool IsSynthetic(MethodBase method)
        => SyntheticName.TryParse(method.Name, out var name) && name.IsLambdaLike;

    /// <summary>
    /// Reads a lambda method's identity: which user type it belongs to, how it captures, and its position
    /// among the lambdas of its scope method.
    /// </summary>
    public static LambdaIdentity Identify(MethodInfo lambda)
    {
        if (lambda.DeclaringType is not { } declaring) return default;
        if (!SyntheticName.TryParse(lambda.Name, out var name) || !name.IsLambdaLike) return default;
        if (name.Ordinal < 0) return default;

        var home = HomeOf(declaring);

        switch (home)
        {
            case LambdaHome.DisplayClass:
            {
                // The display class name carries the scope ordinal; the method name carries only its own.
                if (!SyntheticName.TryParse(declaring.Name, out var closure)) return default;
                if (closure.Ordinal < 0 || closure.SubOrdinal < 0) return default;
                if (declaring.DeclaringType is not { } userType) return default;

                return new LambdaIdentity(OpenOwner(userType, declaring), home, CaptureMode.DisplayClass,
                    name.Scope, closure.Ordinal, name.Ordinal);
            }

            case LambdaHome.CacheClass:
            {
                if (name.SubOrdinal < 0) return default;
                if (declaring.DeclaringType is not { } userType) return default;

                return new LambdaIdentity(OpenOwner(userType, declaring), home, CaptureMode.None,
                    name.Scope, name.Ordinal, name.SubOrdinal);
            }

            case LambdaHome.UserType:
            {
                if (name.SubOrdinal < 0) return default;

                // A static local function needs no target; an instance lambda captures "this".
                var capture = lambda.IsStatic ? CaptureMode.None : CaptureMode.Instance;
                return new LambdaIdentity(declaring, home, capture, name.Scope, name.Ordinal, name.SubOrdinal);
            }

            default:
                return default;
        }
    }

    private static LambdaHome HomeOf(Type declaring)
    {
        if (!SyntheticName.TryParse(declaring.Name, out var name)) return LambdaHome.UserType;
        if (name.Kind != SyntheticKind.LambdaDisplayClass) return LambdaHome.UserType;
        return name.Suffix == "DisplayClass" ? LambdaHome.DisplayClass : LambdaHome.CacheClass;
    }

    // A lambda in a generic scope gets a generic closure carrying the same type arguments, so the user type is
    // reconstructed closed over them.
    private static Type OpenOwner(Type userType, Type closure)
    {
        if (!closure.IsConstructedGenericType) return userType;
        if (!userType.IsGenericTypeDefinition) return userType;

        var arguments = closure.GenericTypeArguments;
        return arguments.Length == userType.GetGenericArguments().Length
            ? userType.MakeGenericType(arguments)
            : userType;
    }

    /// <summary>The counterpart of a lambda method after the reload, or null when there is none.</summary>
    public MethodInfo? Match(MethodInfo previous)
    {
        var identity = Identify(previous);
        if (!identity.IsValid) return null;

        var userType = _types.Resolve(identity.UserType);
        if (userType.Target is not { } currentUserType) return null;

        // A lambda in an untouched, non generic type is still itself.
        if (userType.IsUnchanged && !previous.IsGenericMethod && !previous.DeclaringType!.IsGenericType)
            return previous;

        var scope = _indexes.For(identity.UserType.Assembly)
            .FindScopeMethod(Definition(identity.UserType), identity.ScopeName, identity.ScopeOrdinal);

        if (scope == null)
        {
            _report.Report(ReloadCode.LambdaScopeUnresolved, ReloadSeverity.Warning,
                $"No method with lambda scope ordinal {identity.ScopeOrdinal} was found.",
                identity.UserType.FullName);
            return null;
        }

        if (_members.ResolveMethod(scope) is not { } currentScope) return null;

        var index = _indexes.For(currentUserType.Assembly);
        var key = new LambdaKey(
            Definition(currentUserType).FullName ?? currentUserType.Name,
            identity.ScopeName,
            index.ScopeOrdinalOf(currentScope),
            identity.LambdaOrdinal);

        if (!index.TryGetLambda(key, out var entry)) return null;

        return Close(entry, previous, currentUserType);
    }

    private static Type Definition(Type type) => type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;

    /// <summary>
    /// The index stores open definitions, so a generic container has to be closed before a method can be bound
    /// on it. The arguments come from the container the previous lambda actually lived on, which is the only
    /// thing that knows them: a display class is generic when its <em>scope method</em> is generic just as much
    /// as when its declaring type is, and only the former case leaves the user type non generic.
    /// </summary>
    private MethodInfo? Close(in LambdaEntry entry, MethodInfo previous, Type currentUserType)
    {
        var container = entry.DeclaringType;
        if (!container.IsGenericTypeDefinition) return container.GetMethod(entry.MethodName, AllDeclared);

        Type[] mapped;

        if (previous.DeclaringType is { IsConstructedGenericType: true } source)
        {
            var arguments = source.GenericTypeArguments;
            mapped = new Type[arguments.Length];

            for (int i = 0; i < arguments.Length; i++)
            {
                if (_types.Resolve(arguments[i]).Target is not { } target) return null;
                mapped[i] = target;
            }
        }
        else if (currentUserType.IsConstructedGenericType)
        {
            mapped = currentUserType.GenericTypeArguments;
        }
        else
        {
            return null;
        }

        if (container.GetGenericArguments().Length != mapped.Length) return null;

        try { container = container.MakeGenericType(mapped); }
        catch (ArgumentException) { return null; }

        return container.GetMethod(entry.MethodName, AllDeclared);
    }
}
