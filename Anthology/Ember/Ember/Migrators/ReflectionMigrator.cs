using System;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// Repoints stored reflection handles. A handle denotes its target by identity rather than by content, so
/// these types always need visiting even when their storage holds nothing.
/// </summary>
public sealed class ReflectionMigrator : IValueMigrator
{
    public bool Handles(Type type)
        => typeof(MemberInfo).IsAssignableFrom(type)
           || typeof(Assembly).IsAssignableFrom(type)
           || typeof(ParameterInfo).IsAssignableFrom(type);

    public MigrationPlan Plan(Type type, PlanContext context) => new ReflectionPlan(context.Assemblies);

    private sealed class ReflectionPlan : MigrationPlan
    {
        private readonly AssemblyMap _assemblies;

        public ReflectionPlan(AssemblyMap assemblies) => _assemblies = assemblies;

        public override bool NeedsFill => false;

        public override Allocation Allocate(object source, MigrationContext context) => source switch
        {
            Assembly assembly => Result(source, _assemblies.Resolve(assembly).Target),
            Type type => Result(source, context.Types.Resolve(type).Target),
            MemberInfo member => Result(source, context.Members.Resolve(member)),
            ParameterInfo parameter => Result(source, context.Members.ResolveParameter(parameter)),
            _ => Allocation.Preserve(source),
        };

        private static Allocation Result(object source, object? resolved)
        {
            if (resolved == null) return Allocation.Drop;
            return ReferenceEquals(resolved, source) ? Allocation.Preserve(source) : Allocation.Replace(resolved);
        }

        public override string Describe() => "reflection handle remap";
    }
}
