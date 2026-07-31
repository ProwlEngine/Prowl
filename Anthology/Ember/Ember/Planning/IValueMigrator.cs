using System;

namespace Prowl.Ember;

/// <summary>
/// A strategy for migrating instances of some set of types. Asked once per type, not once per instance: the
/// answer is a <see cref="MigrationPlan"/> that then runs for every instance of that type.
/// </summary>
public interface IValueMigrator
{
    /// <summary>
    /// Whether this migrator owns the type. Must be cheap and must not consult <see cref="TypeFacts"/>, since
    /// the analyzer calls it while computing them.
    /// </summary>
    bool Handles(Type type);

    /// <summary>Builds the plan. Called at most once per type per reload, so it may precompute freely.</summary>
    MigrationPlan Plan(Type type, PlanContext context);

    /// <summary>
    /// Whether claiming a type forces instances of it to be visited even when its storage provably holds
    /// nothing migratable. True for anything whose meaning is not carried by its fields, such as a reflection
    /// handle, which denotes a type by identity rather than by content.
    /// </summary>
    bool ForcesVisit(Type type) => true;
}

/// <summary>
/// Implemented by the few migrators that need process wide setup around a reload, such as clearing a framework
/// cache keyed on the previous types.
/// </summary>
public interface IReloadScopedMigrator
{
    void OnReloadStarting(PlanContext context);
    void OnReloadFinished(PlanContext context);
}
