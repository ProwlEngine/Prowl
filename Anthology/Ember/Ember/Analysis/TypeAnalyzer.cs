using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>What the engine knows about a type before it sees a single instance of it.</summary>
public readonly record struct TypeFacts(
    bool IsSubstituted,
    bool IsInert,
    bool HasReloadHooks);

/// <summary>
/// Decides which types cannot transitively hold anything worth migrating, so the walk never descends into
/// them.
/// </summary>
/// <remarks>
/// Inertness is the complement of reachability, so it is a greatest fixpoint: assume inert, then propagate
/// non-inertness along the edges that carry it. Solving it that way is what makes a reference cycle come out
/// right. A pessimistic recursion breaker, which is the obvious way to write this, reports
/// <c>class Node { Node? Next; }</c> as non-inert purely because computing it requires computing it, and then
/// every node of a long list gets walked on every reload for no reason.
/// </remarks>
internal sealed class TypeAnalyzer
{
    private const BindingFlags DeclaredInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly HashSet<Type> KnownLeaves = new()
    {
        typeof(string), typeof(decimal), typeof(Guid),
        typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(TimeOnly), typeof(DateOnly),
        typeof(Uri), typeof(Version), typeof(IntPtr), typeof(UIntPtr),
        typeof(System.Globalization.CultureInfo), typeof(System.Text.Encoding),
        typeof(System.Text.RegularExpressions.Regex),
    };

    private readonly TypeMap _types;
    private readonly ScopeRules _scope;
    private readonly MigratorRegistry _migrators;
    private readonly InertAnalysisMode _mode;

    private readonly Dictionary<Type, TypeFacts> _facts = new();
    private readonly Dictionary<Type, bool> _inert = new();

    public TypeAnalyzer(TypeMap types, ScopeRules scope, MigratorRegistry migrators, InertAnalysisMode mode)
    {
        _types = types;
        _scope = scope;
        _migrators = migrators;
        _mode = mode;
    }

    public int InertCount { get; private set; }

    public TypeFacts For(Type type)
    {
        if (_facts.TryGetValue(type, out var cached)) return cached;

        var facts = new TypeFacts(
            IsSubstituted: _types.Resolve(type).IsSubstituted,
            IsInert: IsInert(type),
            HasReloadHooks: HasHooks(type));

        _facts[type] = facts;
        return facts;
    }

    public bool IsInert(Type type)
    {
        if (_inert.TryGetValue(type, out var cached)) return cached;

        // Independent of the mode. A primitive's only field is itself, so field walking one never terminates.
        if (type.IsPrimitive || type.IsPointer) return _inert[type] = true;

        if (_mode == InertAnalysisMode.Off)
            return _inert[type] = _scope.IsExcluded(type.Assembly);

        if (_mode == InertAnalysisMode.Conservative)
            return _inert[type] = Trivially(type) ?? false;

        return Solve(type);
    }

    /// <summary>An answer that needs no graph walk, or null when the field closure has to be examined.</summary>
    private bool? Trivially(Type type)
    {
        // An enum is not listed here: a substituted one still has to be converted through its underlying type,
        // and an unchanged one comes out inert anyway from its single primitive field.
        if (type.IsPrimitive || type.IsPointer) return true;
        if (type.IsByRef) return true; // a byref cannot be stored in a field we would rewrite
        if (type.IsArray) return null;

        // Checked before anything that opts out: a type this reload replaces always has to be migrated, or an
        // instance of a type that no longer exists would be left in the graph.
        if (!_types.Resolve(type).IsUnchanged) return false;

        if (KnownLeaves.Contains(type)) return true;
        if (IgnoreRules.Applies(type)) return true;
        if (_scope.IsExcluded(type.Assembly)) return true;

        // A type with lifecycle hooks always has to be visited, or the hooks would never fire for an instance
        // whose own storage happens to hold nothing interesting.
        if (HasHooks(type)) return false;

        if (ForcesVisit(type)) return false;

        return null;
    }

    private static bool HasHooks(Type type)
        => typeof(IReloadAware).IsAssignableFrom(type) || typeof(IReloadObserver).IsAssignableFrom(type);

    /// <summary>
    /// Whether a slot declared as this type can be carried across without migrating what it holds. Stricter
    /// than inertness: an unsealed reference slot could hold a derived instance that is anything at all, so
    /// what the declared type itself holds proves nothing.
    /// </summary>
    public bool IsInertSlot(Type declaredType)
        => (declaredType.IsValueType || declaredType.IsSealed || declaredType.IsArray || declaredType.IsPointer)
           && IsInert(declaredType);

    private bool ForcesVisit(Type type)
    {
        foreach (var migrator in _migrators)
            if (migrator.Handles(type))
                return migrator.ForcesVisit(type);

        return false;
    }

    // Explore the storage closure reachable from the query, then run the fixpoint over just that region.
    private bool Solve(Type root)
    {
        var successors = new Dictionary<Type, List<Type>>();
        var predecessors = new Dictionary<Type, List<Type>>();
        var seeds = new Queue<Type>();

        var pending = new Stack<Type>();
        pending.Push(root);

        while (pending.TryPop(out var type))
        {
            if (successors.ContainsKey(type)) continue;

            if (_inert.TryGetValue(type, out var known))
            {
                // An earlier query already settled this one. If it lost, it still has to seed this region.
                if (!known) seeds.Enqueue(type);
                continue;
            }

            if (Trivially(type) is { } trivial)
            {
                Settle(type, trivial);
                if (!trivial) seeds.Enqueue(type);
                continue;
            }

            var reached = new List<Type>();
            if (!CollectReachable(type, reached))
            {
                // A local condition disqualifies it outright, so it seeds the propagation.
                Settle(type, false);
                seeds.Enqueue(type);
                continue;
            }

            successors[type] = reached;

            foreach (var next in reached)
            {
                if (!predecessors.TryGetValue(next, out var into))
                    predecessors[next] = into = new List<Type>();
                into.Add(type);

                pending.Push(next);
            }
        }

        // Optimistic start: everything still in the region is inert until a non-inert successor reaches it.
        while (seeds.TryDequeue(out var type))
        {
            if (!predecessors.TryGetValue(type, out var into)) continue;

            foreach (var predecessor in into)
            {
                if (!successors.Remove(predecessor)) continue;
                Settle(predecessor, false);
                seeds.Enqueue(predecessor);
            }
        }

        foreach (var survivor in successors.Keys)
            Settle(survivor, true);

        return _inert[root];
    }

    private void Settle(Type type, bool inert)
    {
        _inert[type] = inert;
        if (inert) InertCount++;
    }

    /// <summary>
    /// Adds the types this one can reach through storage. Returns false when it is disqualified outright, which
    /// happens when it can hold an unsealed reference: the runtime type of that slot is unknown, so nothing can
    /// be proven about what it holds.
    /// </summary>
    private static bool CollectReachable(Type type, List<Type> reached)
    {
        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            if (IsOpenReference(element)) return false;
            reached.Add(element);
            return true;
        }

        if (type.BaseType != null)
            reached.Add(type.BaseType);

        foreach (var field in type.GetFields(DeclaredInstance))
        {
            if (IgnoreRules.Applies(field)) continue;

            var fieldType = field.FieldType;
            if (fieldType.IsPointer) continue;
            if (IsOpenReference(fieldType)) return false;

            reached.Add(fieldType);
        }

        return true;
    }

    // An array is sealed in effect: its element type is what decides, and that is followed separately.
    private static bool IsOpenReference(Type type)
        => !type.IsValueType && !type.IsSealed && !type.IsArray && !type.IsPointer;
}
