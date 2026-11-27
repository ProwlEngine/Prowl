# Prowl.Ember

**In-place assembly state migration for hot reload**, for .NET.

When you recompile code at runtime, the new assembly is loaded alongside the previous one. Ember walks the live object graph from watched static fields and caller supplied roots, and for every object whose type was replaced it produces a replacement, carries its state across, and repoints every reference to it - **preserving reference identity and cycles**. Both assemblies stay loaded, correctness never depends on the previous one unloading.

This idea, and some of its implementation, is based on the work by s&box engine.

## What it migrates

- **Objects and cycles** - fields carried across the swap, references repointed, identity and cycles preserved. The walk is a worklist rather than a recursion, so graph depth cannot exhaust the stack.
- **Delegates** - single and multicast, including compiler generated **lambdas, closures, local functions, and state machines**, matched across the swap by reading the replaced IL with [Mono.Cecil](https://github.com/jbevain/cecil).
- **Generic and anonymous types** - reconstructed against the current assembly; anonymous types re-matched by property name.
- **Collections** - lists, arrays (multidimensional and jagged), the dictionary and set families, concurrent and immutable containers, `ConditionalWeakTable`, `WeakReference`, and **your own subclasses of any of them**, rebuilt so comparers and hash codes stay valid and the subclass's own state survives.
- **Reflection handles** - `Assembly`, `Type`, `MemberInfo`, `ParameterInfo`.
- **New fields** - get their declared initializer value, replayed from the field initializer IL with no constructor side effects.
- **System.Text.Json** metadata caches are cleared around the reload.

## Usage

```csharp
using Prowl.Ember;

// Long lived: scope, migrators, and root providers.
var engine = ReloadEngine.Create(options =>
{
    // Ember reads the replaced IL with Cecil, so hand it the bytes each assembly was loaded from.
    options.AssemblyBytes = assembly => LoadedBytesFor(assembly);
    options.Diagnostics = new DelegateDiagnosticSink(d => Console.WriteLine($"{d.Id} {d.Message}"));

    options.Scope.Include(gameAssembly);   // walk its statics to find live instances
    options.Scope.ExcludePrefix("Silk.NET"); // never descend into native or third party internals
});

// Per reload: an immutable description of what changed and where to start.
var report = engine.Apply(ReloadRequest.Create()
    .Replace(previousAssembly, currentAssembly)
    .Roots(scene.AllComponents)
    .Build());

foreach (var diagnostic in report.Errors)
    Console.Error.WriteLine(diagnostic);

scene.Rebind(report.Replaced, report.Dropped);
```

`ReloadReport` carries every diagnostic with a stable code, the old to new map for every replaced object (not just the roots), the objects whose type was removed, and per phase counts and timings.

### Opting out and lifecycle hooks

Everything user code touches lives in `Prowl.Ember.Contracts`, which has no dependencies and is AOT safe.

- `[ReloadIgnore]` on a field, auto property, or type leaves it out of the migration. On a replacement the field gets what a freshly constructed instance would have, not null.
- `[ReloadInitializer("Method")]` on a newly added field runs the named method to initialize it, once every other field is populated.
- `IReloadAware` gives you `OnReloadDetach` on the outgoing instance and `OnReloadAttach` on the incoming one, with a typed `ReloadState` carried between them, for tearing down and re-establishing registrations, native handles, or event subscriptions.
- `IReloadObserver` gives you `OnReloadPreserved` and `OnReloadDropped`, the two outcomes that are not a replacement.
- `ReloadCache<TKey, TValue>` is a cache that empties itself across a reload, for the type keyed maps that would otherwise pin what they were derived from.

## License

MIT - part of the [Prowl Anthology](https://github.com/ProwlEngine/Anthology).
