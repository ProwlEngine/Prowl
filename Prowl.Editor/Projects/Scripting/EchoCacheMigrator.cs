// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Ember;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>Clears Echo's reflection caches around a reload so they stop holding the outgoing types.</summary>
internal sealed class EchoCacheMigrator : IValueMigrator, IReloadScopedMigrator
{
    // Registered for the lifecycle only. Echo's caches are private statics the walk never reaches.
    public bool Handles(Type type) => false;

    public MigrationPlan Plan(Type type, PlanContext context)
        => throw new NotSupportedException($"{nameof(EchoCacheMigrator)} claims no types.");

    public void OnReloadStarting(PlanContext context) => Echo.Serializer.ClearCache();

    // Again on the way out, in case something serialized mid-reload.
    public void OnReloadFinished(PlanContext context) => Echo.Serializer.ClearCache();
}
