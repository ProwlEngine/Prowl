using System;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// Repoints a weak reference at the migrated target without resurrecting one that has already been collected.
/// </summary>
public sealed class WeakReferenceMigrator : IValueMigrator
{
    public bool Handles(Type type)
        => type == typeof(WeakReference)
           || (type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(WeakReference<>));

    public MigrationPlan Plan(Type type, PlanContext context)
    {
        if (type == typeof(WeakReference)) return new UntypedPlan();

        var resolution = context.Types.Resolve(type);
        if (resolution.IsRemoved) return MigrationPlan.Dropped;

        return new TypedPlan(type, resolution.Target!, resolution.IsSubstituted);
    }

    private sealed class UntypedPlan : MigrationPlan
    {
        public override Allocation Allocate(object source, MigrationContext context) => Allocation.Preserve(source);

        public override void Fill(object source, object target, MigrationContext context)
        {
            var reference = (WeakReference)target;
            if (!reference.IsAlive) return;

            var current = reference.Target;
            if (current == null) return;

            reference.Target = context.Map(current);
        }

        public override string Describe() => "weak reference, target repointed";
    }

    private sealed class TypedPlan : MigrationPlan
    {
        private readonly Type _target;
        private readonly bool _moved;
        private readonly MethodInfo _tryGetTarget;
        private readonly MethodInfo _setTarget;

        public TypedPlan(Type source, Type target, bool moved)
        {
            _target = target;
            _moved = moved;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            _tryGetTarget = source.GetMethod(nameof(WeakReference<object>.TryGetTarget), flags)!;
            _setTarget = target.GetMethod(nameof(WeakReference<object>.SetTarget), flags)!;
        }

        public override Allocation Allocate(object source, MigrationContext context)
            => _moved
                ? Allocation.Replace(Activator.CreateInstance(_target, new object?[] { null })!)
                : Allocation.Preserve(source);

        public override void Fill(object source, object target, MigrationContext context)
        {
            var arguments = new object?[1];

            // A collected or absent target leaves the replacement empty, which is what it already is.
            if (!(bool)_tryGetTarget.Invoke(source, arguments)! || arguments[0] == null) return;

            arguments[0] = context.Map(arguments[0]);
            _setTarget.Invoke(target, arguments);
        }

        public override string Describe() => _moved ? $"weak reference rebuilt as {_target.Name}" : "weak reference, target repointed";
    }
}
