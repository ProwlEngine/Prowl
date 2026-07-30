using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Mono.Cecil;

// Prowl has its own AssemblyDefinition for .asmdef files, so Cecil's is referenced under an alias.
using CecilAssembly = Mono.Cecil.AssemblyDefinition;

namespace Prowl.Ember;

/// <summary>
/// Cecil access for the assemblies taking part in one reload. The only place in the engine that names a Cecil
/// type, along with <see cref="AssemblyMetadata"/> and <see cref="ILTranslator"/>.
/// </summary>
internal sealed class MetadataCache : IDisposable
{
    private readonly Func<Assembly, byte[]?>? _bytes;
    private readonly ReportBuilder _report;
    private readonly Dictionary<Assembly, AssemblyMetadata?> _assemblies = new();

    public MetadataCache(Func<Assembly, byte[]?>? bytes, ReportBuilder report)
    {
        _bytes = bytes;
        _report = report;
    }

    public AssemblyMetadata? For(Assembly assembly)
    {
        if (_assemblies.TryGetValue(assembly, out var cached)) return cached;

        AssemblyMetadata? metadata = null;
        var bytes = _bytes?.Invoke(assembly);

        if (bytes == null)
        {
            _report.Report(ReloadCode.NoAssemblyBytes, ReloadSeverity.Warning,
                "No IL available. Field initializer replay and closure matching are unavailable for this assembly.",
                assembly.GetName().Name);
        }
        else
        {
            try
            {
                var definition = CecilAssembly.ReadAssembly(new MemoryStream(bytes), new ReaderParameters { ReadSymbols = false });
                metadata = new AssemblyMetadata(this, definition, _report);
            }
            catch (Exception e)
            {
                _report.Report(ReloadCode.MetadataReadFailed, e, assembly.GetName().Name);
            }
        }

        _assemblies[assembly] = metadata;
        return metadata;
    }

    public TypeDefinition? FindType(Type type)
    {
        if (type.IsConstructedGenericType)
            type = type.GetGenericTypeDefinition();

        if (!type.IsNested)
            return For(type.Assembly)?.FindTopLevelType(type);

        var declaring = FindType(type.DeclaringType!);
        return declaring?.NestedTypes.SingleOrDefault(x => x.Name == type.Name);
    }

    public MethodDefinition? FindMethod(MethodBase method)
    {
        if (method is MethodInfo { IsConstructedGenericMethod: true } constructed)
            method = constructed.GetGenericMethodDefinition();

        var declaring = FindType(method.DeclaringType!);
        if (declaring == null) return null;

        var parameters = method.GetParameters();

        foreach (var candidate in declaring.Methods)
        {
            if (candidate.Name != method.Name) continue;
            if (candidate.Parameters.Count != parameters.Length) continue;

            bool matches = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                var parameterType = CecilSignature.Substitute(candidate, candidate.Parameters[i].ParameterType);
                if (!CecilSignature.Matches(parameterType, parameters[i].ParameterType))
                {
                    matches = false;
                    break;
                }
            }

            if (matches) return candidate;
        }

        _report.Report(ReloadCode.MetadataResolveFailed, ReloadSeverity.Warning,
            "No metadata definition found for this method.",
            $"{method.DeclaringType?.FullName}.{method.Name}");
        return null;
    }

    public void Dispose()
    {
        foreach (var metadata in _assemblies.Values)
            metadata?.Dispose();
        _assemblies.Clear();
    }
}

/// <summary>One loaded assembly's metadata, and the per assembly caches derived from it.</summary>
internal sealed class AssemblyMetadata : IDisposable
{
    private readonly MetadataCache _owner;
    private readonly CecilAssembly _definition;
    private readonly ReportBuilder _report;

    private readonly Dictionary<MethodBase, int> _scopeOrdinals = new();
    private readonly Dictionary<FieldInfo, Func<object?>?> _fieldInitializers = new();

    public AssemblyMetadata(MetadataCache owner, CecilAssembly definition, ReportBuilder report)
    {
        _owner = owner;
        _definition = definition;
        _report = report;
    }

    public TypeDefinition? FindTopLevelType(Type type) => _definition.MainModule.GetType(type.FullName);

    /// <summary>
    /// The value a field would have received from its field initializer, as a factory evaluated once per
    /// instance so that <c>= new List<T>()</c> produces a fresh list each time. False when the field has
    /// no independent initializer, or its expression could not be translated.
    /// </summary>
    public bool TryGetFieldInitializer(FieldInfo field, out Func<object?> factory)
    {
        if (!_fieldInitializers.TryGetValue(field, out var cached))
            _fieldInitializers[field] = cached = FieldInitializerReader.Build(_owner, field, _report);

        factory = cached!;
        return cached != null;
    }

    /// <summary>
    /// The ordinal Roslyn assigned this method among the lambda bearing methods of its type. Recovered from
    /// the state machine attribute when there is one, otherwise by reading the body for the lambdas it
    /// defines. Minus one when the method bears no lambda.
    /// </summary>
    public int GetLambdaScopeOrdinal(MethodBase method)
    {
        if (_scopeOrdinals.TryGetValue(method, out var cached)) return cached;

        // An async or iterator method carries its state machine type; its ordinal is that type's ordinal.
        if (method.GetCustomAttribute<StateMachineAttribute>() is { } stateMachine
            && SyntheticName.TryParse(stateMachine.StateMachineType.Name, out var machineName)
            && machineName.Kind == SyntheticKind.StateMachine
            && machineName.Ordinal >= 0)
            return _scopeOrdinals[method] = machineName.Ordinal;

        int ordinal = -1;
        var definition = _owner.FindMethod(method);

        if (definition is { HasBody: true })
        {
            foreach (var instruction in definition.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference reference) continue;
                if (!SyntheticName.TryParse(reference.Name, out var name) || !name.IsLambdaLike) continue;

                // Case 3 puts the scope ordinal on the display class, not on the lambda method.
                int candidate = name.SubOrdinal >= 0
                    ? name.Ordinal
                    : DisplayClassScopeOrdinal(reference);

                if (candidate >= 0) ordinal = candidate;
            }
        }

        return _scopeOrdinals[method] = ordinal;
    }

    private static int DisplayClassScopeOrdinal(MethodReference lambda)
        => SyntheticName.TryParse(lambda.DeclaringType.Name, out var displayClass)
           && displayClass.Kind == SyntheticKind.LambdaDisplayClass
           && displayClass.Suffix == "DisplayClass"
           && displayClass.SubOrdinal >= 0
            ? displayClass.Ordinal
            : -1;

    public void Dispose() => _definition.Dispose();
}
