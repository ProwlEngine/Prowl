using System;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>How a field added since the previous build gets its starting value.</summary>
public enum NewFieldPolicy
{
    /// <summary>Leave every added field at its zero or null value.</summary>
    Zero,

    /// <summary>Replay the field initializer expression from IL, falling back to zero when it cannot be decoded.</summary>
    DeclaredInitializer,
}

/// <summary>What happens to a delegate that cannot be rebuilt against the current assembly.</summary>
public enum BrokenDelegatePolicy
{
    /// <summary>The reference becomes null. Reported either way.</summary>
    Drop,

    /// <summary>Substitute a delegate that throws <see cref="ReloadedDelegateException"/> when invoked.</summary>
    Throwing,

    /// <summary>Keep the previous delegate. It still works, at the cost of keeping the previous assembly alive.</summary>
    Preserve,
}

/// <summary>How hard the engine works to prove a type holds nothing worth visiting.</summary>
public enum InertAnalysisMode
{
    /// <summary>Nothing is inert except excluded assemblies.</summary>
    Off,

    /// <summary>Excluded assemblies, [ReloadIgnore] types, and the known leaf table. No fixpoint.</summary>
    Conservative,

    /// <summary>The full reachability fixpoint.</summary>
    Full,
}

/// <summary>Long lived engine configuration. Everything derived from a specific reload lives on the request.</summary>
public sealed class ReloadOptions
{
    public ScopeRules Scope { get; } = new();
    public MigratorRegistry Migrators { get; } = MigratorRegistry.CreateDefault();
    public RootProviderCollection Roots { get; } = RootProviderCollection.CreateDefault();

    /// <summary>
    /// Supplies the raw IL of a loaded assembly so Cecil can read it, for field initializer replay and closure
    /// matching. Prowl loads game assemblies from bytes to keep the file unlocked, so the loader's byte cache
    /// feeds this. A null resolver leaves both features unavailable, reported per assembly.
    /// </summary>
    public Func<Assembly, byte[]?>? AssemblyBytes { get; set; }

    public IDiagnosticSink? Diagnostics { get; set; }

    public NewFieldPolicy NewFields { get; set; } = NewFieldPolicy.DeclaredInitializer;
    public BrokenDelegatePolicy BrokenDelegates { get; set; } = BrokenDelegatePolicy.Drop;
    public InertAnalysisMode InertAnalysis { get; set; } = InertAnalysisMode.Full;
    public bool CollectStatistics { get; set; } = true;
}
