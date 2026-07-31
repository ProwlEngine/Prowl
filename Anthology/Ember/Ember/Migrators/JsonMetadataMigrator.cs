using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Prowl.Ember;

/// <summary>
/// Drops System.Text.Json's per type metadata and clears its caches around the reload. That metadata is keyed
/// on the previous types: it cannot be migrated, it pins the previous assembly, and it would serve stale
/// shapes to anything that serialized afterwards.
/// </summary>
public sealed class JsonMetadataMigrator : IValueMigrator, IReloadScopedMigrator
{
    private int _encountered;

    public bool Handles(Type type) => typeof(JsonTypeInfo).IsAssignableFrom(type);

    public MigrationPlan Plan(Type type, PlanContext context) => new DropMetadataPlan(this);

    public void OnReloadStarting(PlanContext context)
    {
        _encountered = 0;
        ClearCache(context);
    }

    public void OnReloadFinished(PlanContext context)
    {
        if (_encountered > 0)
            context.Report(ReloadCode.JsonCacheRepopulated, ReloadSeverity.Warning,
                $"{_encountered} JsonTypeInfo instances were reachable during the reload, which means serialization ran while it was in progress.",
                null);

        // Again, in case serialization refilled the cache while the reload was running.
        ClearCache(context);
    }

    private static void ClearCache(PlanContext context)
    {
        try
        {
            var handler = typeof(JsonSerializerOptions).Assembly
                .GetType("System.Text.Json.JsonSerializerOptionsUpdateHandler");

            handler?.GetMethod("ClearCache", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                   ?.Invoke(null, new object?[] { null });
        }
        catch (Exception e)
        {
            context.Report(ReloadCode.JsonCacheRepopulated, ReloadSeverity.Warning,
                $"Could not clear the System.Text.Json cache: {e.Message}. It may serve metadata for the previous types.",
                null);
        }
    }

    private sealed class DropMetadataPlan : MigrationPlan
    {
        private readonly JsonMetadataMigrator _owner;

        public DropMetadataPlan(JsonMetadataMigrator owner) => _owner = owner;

        public override bool NeedsFill => false;

        public override Allocation Allocate(object source, MigrationContext context)
        {
            _owner._encountered++;
            return Allocation.Drop;
        }

        public override string Describe() => "drop, Json metadata is rebuilt on demand";
    }
}
