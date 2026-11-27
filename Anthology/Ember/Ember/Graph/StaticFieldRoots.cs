using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// Every static field the reload should walk. A static of a replaced type is read from the previous side field
/// and written to the current side one, which are different members on different types, so the two slots of a
/// <see cref="Root"/> differ here.
/// </summary>
public sealed class StaticFieldRoots : IRootProvider
{
    private const BindingFlags Statics =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public IEnumerable<Root> Enumerate(RootContext context)
    {
        foreach (var assembly in AssembliesToWalk(context))
        {
            if (context.Scope.IsExcluded(assembly)) continue;

            if (!context.Assemblies.IsSubstituted(assembly)
                && !context.Assemblies.Added.Contains(assembly)
                && context.Assemblies.ReferencesSubstituted(assembly, out var reference))
            {
                context.Report(ReloadCode.ScopeAssemblySkipped, ReloadSeverity.Info,
                    $"References {reference.Name}, which this reload replaces, so its statics were compiled against the previous types.",
                    assembly.GetName().Name);
                continue;
            }

            foreach (var type in SyntheticIndex.SafeTypes(assembly))
            {
                if (!context.Scope.Accepts(type)) continue;
                if (!ShouldWalk(type, context)) continue;

                foreach (var root in RootsOf(type, context))
                    yield return root;
            }
        }
    }

    private static IEnumerable<Assembly> AssembliesToWalk(RootContext context)
        => context.Scope.Included
            .Union(context.Assemblies.Previous)
            .Union(context.Assemblies.Added);

    private static bool ShouldWalk(Type type, RootContext context)
    {
        // The statics of an open generic type cannot be enumerated at all. Analyzer rule EMBA001 warns about
        // declaring them.
        if (type.ContainsGenericParameters) return false;
        if (IgnoreRules.Applies(type)) return false;
        if (type.Name == "<PrivateImplementationDetails>") return false;

        // A sealed compiler generated type from a replaced assembly holds only closure state, reached through
        // the delegates that own it rather than through its own statics.
        if (type.IsSealed && context.Assemblies.IsSubstituted(type.Assembly) && SyntheticName.TryParse(type.Name, out _))
            return false;

        return true;
    }

    private static IEnumerable<Root> RootsOf(Type type, RootContext context)
    {
        var resolution = context.Types.Resolve(type);
        if (resolution.Target is not { } currentType) yield break;

        bool moved = !resolution.IsUnchanged;

        foreach (var field in type.GetFields(Statics))
        {
            if (field.IsLiteral) continue;
            if (IgnoreRules.Applies(field)) continue;

            var destination = moved ? currentType.GetField(field.Name, Statics) : field;
            if (destination == null) continue;
            if (destination.IsLiteral) continue;
            if (IgnoreRules.Applies(destination)) continue;

            // Touching any static runs the type initializer. If it throws, nothing else on this type is
            // readable either, so the whole type is abandoned rather than reported once per field.
            if (!CanRead(field, type, context, out bool typeIsUnusable))
            {
                if (typeIsUnusable) yield break;
                continue;
            }

            yield return Root.At(ValueSlot.StaticField(field), ValueSlot.StaticField(destination));
        }
    }

    private static bool CanRead(FieldInfo field, Type type, RootContext context, out bool typeIsUnusable)
    {
        typeIsUnusable = false;

        try
        {
            field.GetValue(null);
            return true;
        }
        catch (TargetInvocationException e) when (e.InnerException is TypeInitializationException inner)
        {
            context.Report(ReloadCode.StaticInitializerThrew, ReloadSeverity.Warning,
                $"{inner.Message} None of this type's statics can be migrated.", type.FullName);
            typeIsUnusable = true;
            return false;
        }
        catch (TypeInitializationException e)
        {
            context.Report(ReloadCode.StaticInitializerThrew, ReloadSeverity.Warning,
                $"{e.Message} None of this type's statics can be migrated.", type.FullName);
            typeIsUnusable = true;
            return false;
        }
        catch (Exception e)
        {
            context.Report(ReloadCode.StaticInitializerThrew, ReloadSeverity.Warning,
                e.Message, $"{type.FullName}.{field.Name}");
            return false;
        }
    }
}
