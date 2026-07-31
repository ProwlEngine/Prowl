using System;

namespace Prowl.Ember;

/// <summary>
/// Carries an enum value across a change to its underlying type, so widening from <c>byte</c> to <c>int</c>
/// keeps the value instead of discarding it as an incompatible field.
/// </summary>
public sealed class EnumMigrator : IValueMigrator
{
    public bool Handles(Type type) => type.IsEnum;

    /// <summary>An enum value is self contained, so an unchanged one never needs visiting.</summary>
    public bool ForcesVisit(Type type) => false;

    public MigrationPlan Plan(Type type, PlanContext context)
    {
        var resolution = context.Types.Resolve(type);

        if (resolution.IsUnchanged) return MigrationPlan.Preserved;
        if (resolution.IsRemoved) return MigrationPlan.Dropped;

        return new EnumPlan(type, resolution.Target!);
    }

    private sealed class EnumPlan : MigrationPlan
    {
        private readonly Type _source;
        private readonly Type _target;
        private readonly Type _underlying;

        public EnumPlan(Type source, Type target)
        {
            _source = source;
            _target = target;
            _underlying = Enum.GetUnderlyingType(target);
        }

        public override bool NeedsFill => false;

        public override Allocation Allocate(object source, MigrationContext context)
        {
            try
            {
                return Allocation.Replace(Enum.ToObject(_target, Convert.ChangeType(source, _underlying)));
            }
            catch (Exception e) when (e is OverflowException or InvalidCastException or FormatException)
            {
                context.Report(ReloadCode.EnumValueTruncated, ReloadSeverity.Warning,
                    $"Value {source} does not fit the new underlying type {_underlying.Name}, so it resets to zero.",
                    _source.FullName);

                return Allocation.Replace(Enum.ToObject(_target, 0));
            }
        }

        public override string Describe() => $"enum {_source.Name} to {_target.Name} via {_underlying.Name}";
    }
}
