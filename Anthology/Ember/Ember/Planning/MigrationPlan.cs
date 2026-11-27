using System;

namespace Prowl.Ember;

/// <summary>What a plan decided to do with one instance.</summary>
public readonly struct Allocation
{
    private readonly bool _isDrop;

    private Allocation(object? instance, bool preserved, bool isDrop)
    {
        Instance = instance;
        IsPreserved = preserved;
        _isDrop = isDrop;
    }

    public object? Instance { get; }

    /// <summary>The target is the source itself. It may still be filled in place.</summary>
    public bool IsPreserved { get; }

    /// <summary>Every reference to the source becomes null.</summary>
    public bool IsDrop => _isDrop;

    public static Allocation Preserve(object instance) => new(instance, preserved: true, isDrop: false);
    public static Allocation Replace(object instance) => new(instance, preserved: false, isDrop: false);
    public static Allocation Drop => new(null, preserved: false, isDrop: true);
}

/// <summary>
/// How instances of one type are migrated. Built once per type by an <see cref="IValueMigrator"/>, then run
/// for every instance.
/// </summary>
public abstract class MigrationPlan
{
    /// <summary>
    /// Produces the target for one instance. Shallow by contract: it may read the source's shape, such as an
    /// array's length or a container's comparer, but must not migrate any value the source references. That
    /// restriction is what lets the walk run as a queue rather than a recursion.
    /// </summary>
    public abstract Allocation Allocate(object source, MigrationContext context);

    /// <summary>Carries state across, calling <see cref="MigrationContext.Map"/> for each referenced value.</summary>
    public virtual void Fill(object source, object target, MigrationContext context) { }

    /// <summary>Runs after every fill has drained, for content that could not be written until its keys were complete.</summary>
    public virtual void Rebuild(object source, object target, MigrationContext context) { }

    /// <summary>Whether <see cref="Fill"/> has anything to do. False skips queueing the instance at all.</summary>
    public virtual bool NeedsFill => true;

    /// <summary>Whether <see cref="Rebuild"/> should run for instances of this type.</summary>
    public virtual bool NeedsRebuild => false;

    public virtual string Describe() => GetType().Name;

    /// <summary>The instance carries over untouched, and nothing it holds needs visiting.</summary>
    public static MigrationPlan Preserved => PreservePlan.Instance;

    /// <summary>Every reference to the instance becomes null.</summary>
    public static MigrationPlan Dropped => DropPlan.Instance;
}

/// <summary>The instance carries over untouched, and nothing it holds needs visiting.</summary>
internal sealed class PreservePlan : MigrationPlan
{
    public static readonly PreservePlan Instance = new();

    private PreservePlan() { }

    public override Allocation Allocate(object source, MigrationContext context) => Allocation.Preserve(source);
    public override bool NeedsFill => false;
    public override string Describe() => "preserve";
}

/// <summary>The type is gone, so every reference to the instance becomes null.</summary>
internal sealed class DropPlan : MigrationPlan
{
    public static readonly DropPlan Instance = new();

    private DropPlan() { }

    public override Allocation Allocate(object source, MigrationContext context)
    {
        context.RecordDrop(source);
        return Allocation.Drop;
    }

    public override bool NeedsFill => false;
    public override string Describe() => "drop, type removed";
}
