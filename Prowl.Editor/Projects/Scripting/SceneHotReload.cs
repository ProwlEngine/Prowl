using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Ember;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>
/// Migrates a live scene onto recompiled user types with the <see cref="ReloadEngine"/>: the engine, the editor
/// and the scene are all in scope, so the walk migrates the whole graph in place (component fields, identity, and
/// delegates to user methods all carry over), then <see cref="Scene.OnHotReload"/> re-derives per-frame membership
/// from the new types. Both assemblies stay loaded, no unload required.
/// </summary>
public static class SceneHotReload
{
    /// <summary>Migrate a scene across a single user-assembly swap.</summary>
    public static void Migrate(Scene scene, Assembly previousAssembly, Assembly currentAssembly)
        => Migrate(scene, new[] { (previousAssembly, currentAssembly) });

    /// <summary>Migrate the scene across every previous to current swap in <paramref name="assemblyPairs"/>.</summary>
    public static void Migrate(Scene scene, IReadOnlyCollection<(Assembly Previous, Assembly Current)> assemblyPairs)
    {
        if (assemblyPairs.Count == 0) return;

        var engine = ReloadEngine.Create(options =>
        {
            // Cecil reads the swapped IL for closure matching and new-field defaults.
            options.AssemblyBytes = ScriptAssemblyManager.GetAssemblyBytes;
            options.Diagnostics = new DelegateDiagnosticSink(Log);

            // A handler that cannot be rebuilt throws where it is invoked rather than turning into a null that
            // fails somewhere unrelated. Worth the noise in an editor.
            options.BrokenDelegates = BrokenDelegatePolicy.Throwing;

            options.Migrators.Add(new EchoCacheMigrator()); // the engine handles System.Text.Json itself

            // Keep the walk out of native and heavy third-party internals; they hold no user references.
            foreach (var prefix in s_excludedAssemblyPrefixes)
                options.Scope.ExcludePrefix(prefix);

            // The engine and the editor: their statics and delegates hold user references, including the editor's
            // own into the scene (selection, inspector target, hierarchy). Walking the editor headless is covered
            // by a test, so no separate repoint pass is needed.
            options.Scope.Include(typeof(GameObject).Assembly);     // Prowl.Runtime
            options.Scope.Include(typeof(SceneHotReload).Assembly); // Prowl.Editor

            foreach (var (previous, _) in assemblyPairs)
                options.Scope.Include(previous); // user statics live on the previous assembly
        });

        var request = ReloadRequest.Create().Root(scene);
        foreach (var (previous, current) in assemblyPairs)
            request.Replace(previous, current);

        var report = engine.Apply(request.Build());

        Summarize(report);

        scene.OnHotReload(); // re-derive per-frame membership and rebuild GameObject lookups from the new types
    }

    private static void Log(ReloadDiagnostic diagnostic)
    {
        string message = $"[Ember] {diagnostic}";

        switch (diagnostic.Severity)
        {
            case ReloadSeverity.Error: Debug.LogError(message); break;
            case ReloadSeverity.Warning: Debug.LogWarning(message); break;
            default: Debug.Log(message); break;
        }
    }

    private static void Summarize(ReloadReport report)
    {
        var stats = report.Statistics;

        string summary =
            $"[Ember] Migrated {stats.ObjectsReplaced} object(s), preserved {stats.ObjectsPreserved}, " +
            $"dropped {stats.ObjectsDropped}, rebuilt {stats.DelegatesRebuilt} handler(s).";

        if (stats.DelegatesBroken > 0)
            summary += $" {stats.DelegatesBroken} handler(s) could not be rebuilt and will throw if invoked.";

        if (report.Succeeded) Debug.Log(summary);
        else Debug.LogError(summary + " The reload reported errors; see above.");
    }

    // Native and heavy third-party assembly families the walk must not cascade into.
    private static readonly string[] s_excludedAssemblyPrefixes =
    {
        "Silk.NET", "Jitter2", "Magick.NET", "Microsoft.CodeAnalysis",
    };
}
