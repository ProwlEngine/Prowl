using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// What a previous side reflection handle becomes after the reload. Members are matched on the resolved
/// declaring type by name, member kind, and corresponding signature.
/// </summary>
public sealed class MemberMap
{
    private const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private readonly TypeMap _types;
    private readonly ReportBuilder _report;
    private readonly Dictionary<MemberInfo, MemberInfo?> _cache = new();
    private readonly HashSet<MemberInfo> _resolving = new();

    private LambdaMatcher _lambdas = null!;

    internal MemberMap(TypeMap types, ReportBuilder report)
    {
        _types = types;
        _report = report;
    }

    internal void UseLambdas(LambdaMatcher lambdas) => _lambdas = lambdas;

    public MethodBase? ResolveMethod(MethodBase method) => Resolve(method) as MethodBase;
    public FieldInfo? ResolveField(FieldInfo field) => Resolve(field) as FieldInfo;
    public PropertyInfo? ResolveProperty(PropertyInfo property) => Resolve(property) as PropertyInfo;

    public ParameterInfo? ResolveParameter(ParameterInfo parameter)
    {
        var member = Resolve(parameter.Member);
        if (ReferenceEquals(member, parameter.Member)) return parameter;
        if (member == null) return null;

        if (member is not MethodBase method) return parameter;

        var parameters = method.GetParameters();
        if (parameter.Position < parameters.Length && parameters[parameter.Position].Name == parameter.Name)
            return parameters[parameter.Position];

        return parameters.FirstOrDefault(x => x.Name == parameter.Name);
    }

    public MemberInfo? Resolve(MemberInfo member)
    {
        if (member is Type type) return _types.Resolve(type).Target;

        if (_cache.TryGetValue(member, out var cached)) return cached;

        // A member whose resolution depends on its own. Handing back the previous side member would be
        // silently wrong once its declaring type moved, so that is only assumed where it cannot be.
        if (!_resolving.Add(member)) return Reentered(member);

        try
        {
            var resolved = ResolveUncached(member);
            _cache[member] = resolved;
            return resolved;
        }
        finally
        {
            _resolving.Remove(member);
        }
    }

    private MemberInfo? Reentered(MemberInfo member)
    {
        if (member.DeclaringType is { } declaring && !_types.IsSubstituted(declaring))
            return member;

        _report.Report(ReloadCode.ResolutionCycle, ReloadSeverity.Error,
            "Resolving this member requires resolving itself, so it cannot be matched.",
            $"{member.DeclaringType?.FullName}.{member.Name}");

        return null;
    }

    private MemberInfo? ResolveUncached(MemberInfo member)
    {
        if (member.DeclaringType is not { } declaring) return member;

        // A lambda or local function is matched structurally, not by name.
        if (member is MethodInfo lambda && LambdaMatcher.IsSynthetic(lambda))
            return _lambdas.Match(lambda);

        var target = _types.Resolve(declaring);

        // Nothing moved, and the handle is not a closed generic method that needs rebuilding.
        if (target.IsUnchanged && member is not MethodInfo { IsConstructedGenericMethod: true })
            return member;

        if (target.Target is not { } currentType) return null;

        var match = currentType.GetMembers(AllDeclared)
            .FirstOrDefault(candidate => SignatureComparer.Corresponds(member, candidate, _types));

        if (match == null)
        {
            _report.Report(ReloadCode.MemberUnmatched, ReloadSeverity.Warning,
                "No counterpart after the reload, so the stored handle becomes null.",
                $"{declaring.FullName}.{member.Name}");
            return null;
        }

        if (member is MethodInfo { IsConstructedGenericMethod: true } constructed
            && match is MethodInfo { IsGenericMethodDefinition: true } definition)
            return CloseGenericMethod(constructed, definition);

        return match;
    }

    private MethodInfo? CloseGenericMethod(MethodInfo previous, MethodInfo definition)
    {
        var arguments = previous.GetGenericArguments();
        var mapped = new Type[arguments.Length];

        for (int i = 0; i < arguments.Length; i++)
        {
            if (_types.Resolve(arguments[i]).Target is not { } target) return null;
            mapped[i] = target;
        }

        try { return definition.MakeGenericMethod(mapped); }
        catch (ArgumentException) { return null; }
    }
}
