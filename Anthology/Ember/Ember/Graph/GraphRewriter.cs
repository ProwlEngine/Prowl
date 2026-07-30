using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// Walks the live graph and repoints it. The traversal is a queue rather than a recursion, so a deep object
/// graph cannot exhaust the stack, and identity is preserved by recording a replacement before anything it
/// references is touched.
/// </summary>
internal sealed class GraphRewriter
{
    private const int MaxRebuildGenerations = 64;

    private readonly Planner _planner;
    private readonly ReportBuilder _report;
    private readonly MigrationContext _context;

    private readonly Dictionary<object, object?> _map = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<Task> _fill = new();
    private readonly Queue<Task> _rebuild = new();
    private readonly List<AttachTask> _attach = new();
    private readonly Dictionary<Type, bool> _finalizable = new();

    private readonly record struct Task(MigrationPlan Plan, object Source, object Target);
    private readonly record struct AttachTask(object Target, ReloadState State);

    public GraphRewriter(Planner planner, TypeMap types, MemberMap members, ReportBuilder report)
    {
        _planner = planner;
        _report = report;
        _context = new MigrationContext(this, types, members, report);
    }

    public void Run(IEnumerable<Root> roots, IReadOnlyList<object> explicitRoots)
    {
        var commits = new List<(ValueSlot Destination, object? Value)>();

        using (_report.Time(ReloadPhase.Map))
        {
            _context.Phase = ReloadPhase.Map;

            foreach (var root in roots)
                MapRoot(root, commits);

            foreach (var instance in explicitRoots)
                Map(instance);
        }

        using (_report.Time(ReloadPhase.Fill))
        {
            _context.Phase = ReloadPhase.Fill;
            Drain();

            // Anything a detach hook stashed is migrated before the matching attach hook sees it.
            foreach (var task in _attach)
                task.State.Remap(Map);

            Drain();
        }

        using (_report.Time(ReloadPhase.Rebuild))
        {
            _context.Phase = ReloadPhase.Rebuild;
            DrainRebuild();
        }

        _context.Phase = ReloadPhase.Commit;
        foreach (var (destination, value) in commits)
            TryCommit(destination, value);

        using (_report.Time(ReloadPhase.Notify))
        {
            _context.Phase = ReloadPhase.Notify;
            Notify();
        }
    }

    private void MapRoot(in Root root, List<(ValueSlot, object?)> commits)
    {
        if (!root.HasSlot)
        {
            if (root.Instance != null) Map(root.Instance);
            return;
        }

        object? value;
        try
        {
            value = root.Source.Read();
        }
        catch (Exception e)
        {
            _report.Report(ReloadCode.FieldReadFailed, e, root.Source.ToString());
            return;
        }

        if (value == null) return;

        if (root.Destination.CanWrite)
        {
            commits.Add((root.Destination, Map(value)));
            return;
        }

        // A readonly static cannot be assigned, so whatever is already in it is upgraded in place. Reading it
        // runs the current side type initializer for the first time, so it can fail where the source did not.
        object? existing;
        try
        {
            existing = root.Destination.Read();
        }
        catch (Exception e)
        {
            _report.Report(ReloadCode.FieldReadFailed, e, root.Destination.ToString());
            return;
        }

        if (existing == null)
        {
            _report.Report(ReloadCode.ReadOnlyStaticUnset, ReloadSeverity.Info,
                "Holds null, so there is nothing to upgrade in place.", root.Destination.ToString());
            return;
        }

        MapInto(value, existing);
    }

    private void TryCommit(in ValueSlot destination, object? value)
    {
        try
        {
            destination.TryWrite(value);
        }
        catch (Exception e)
        {
            _report.Report(ReloadCode.FieldWriteFailed, e, destination.ToString());
        }
    }

    public object? Map(object? value)
    {
        if (value == null) return null;

        var type = value.GetType();
        var plan = _planner.For(type);

        // A boxed struct has no identity, so it is never recorded, and it is filled now rather than queued:
        // the box is written back by value and has to be complete when it is stored.
        if (type.IsValueType) return MapBoxed(plan, value);

        if (_map.TryGetValue(value, out var existing)) return existing;

        var allocation = SafeAllocate(plan, value);

        _report.ObjectsVisited++;

        if (allocation.IsDrop || allocation.Instance == null)
        {
            _map[value] = null;
            return null;
        }

        var target = allocation.Instance;
        _map[value] = target;

        if (allocation.IsPreserved)
        {
            _report.ObjectsPreserved++;
        }
        else
        {
            _report.RecordReplacement(value, target);
            HandOverOwnership(value, target);
        }

        if (plan.NeedsFill)
            _fill.Enqueue(new Task(plan, value, target));

        return target;
    }

    /// <summary>
    /// The replacement takes over whatever the previous instance held, including any native handle copied into
    /// it, so the previous finalizer must not run and free that handle out from under it. A handle the
    /// replacement did not take is leaked instead, which is the safer of the two failures.
    /// </summary>
    private void HandOverOwnership(object source, object target)
    {
        if (ReferenceEquals(source, target)) return;
        if (!HasFinalizer(source.GetType())) return;

        GC.SuppressFinalize(source);
    }

    // Most types have nothing to suppress, and a reload can replace a very large number of objects, so the
    // answer is worked out once per type rather than paid per instance.
    private bool HasFinalizer(Type type)
    {
        if (_finalizable.TryGetValue(type, out var cached)) return cached;

        var finalizer = type.GetMethod("Finalize", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes);
        return _finalizable[type] = finalizer != null && finalizer.DeclaringType != typeof(object);
    }

    private object? MapBoxed(MigrationPlan plan, object value)
    {
        var allocation = SafeAllocate(plan, value);
        if (allocation.IsDrop || allocation.Instance is not { } target) return null;

        _report.ObjectsVisited++;

        if (!plan.NeedsFill) return target;

        SafeFill(plan, value, target);
        if (plan.NeedsRebuild) SafeRebuild(plan, value, target);

        RunBoxedHooks(value, target);
        return target;
    }

    public void MapInto(object source, object existingTarget)
    {
        if (source == null || existingTarget == null) return;
        if (_map.ContainsKey(source)) return;

        _map[source] = existingTarget;
        _report.ObjectsVisited++;

        if (ReferenceEquals(source, existingTarget))
        {
            _report.ObjectsPreserved++;
        }
        else
        {
            _report.RecordReplacement(source, existingTarget);
            HandOverOwnership(source, existingTarget);
        }

        var plan = _planner.For(source.GetType());
        if (plan.NeedsFill)
            _fill.Enqueue(new Task(plan, source, existingTarget));
    }

    public void ScheduleRebuild(object source, object target)
        => _rebuild.Enqueue(new Task(_planner.For(source.GetType()), source, target));

    public void RecordDrop(object source) => _report.RecordDrop(source);

    private void Drain()
    {
        while (_fill.TryDequeue(out var task))
        {
            SafeFill(task.Plan, task.Source, task.Target);

            if (task.Plan.NeedsRebuild)
                _rebuild.Enqueue(task);

            AfterFill(task.Source, task.Target);
        }
    }

    private void DrainRebuild()
    {
        for (int generation = 0; _rebuild.Count > 0; generation++)
        {
            if (generation >= MaxRebuildGenerations)
            {
                _report.Report(ReloadCode.RehashCycle, ReloadSeverity.Warning,
                    $"Containers still needed rebuilding after {MaxRebuildGenerations} passes, so the remaining {_rebuild.Count} were abandoned. Their keys depend on each other.",
                    null);
                _rebuild.Clear();
                return;
            }

            int pending = _rebuild.Count;
            for (int i = 0; i < pending; i++)
            {
                var task = _rebuild.Dequeue();
                SafeRebuild(task.Plan, task.Source, task.Target);
            }

            // A rebuild can discover new objects, and those still need filling.
            Drain();
        }
    }

    // The detach hook runs with the previous graph still intact, so a hook reading its own fields sees the
    // pre-reload values.
    private void AfterFill(object source, object target)
    {
        if (ReferenceEquals(source, target))
        {
            if (source is IReloadObserver observer)
                Guard(observer.OnReloadPreserved, ReloadCode.ObserverHookThrew, source);
            return;
        }

        ReloadState? state = null;

        if (source is IReloadAware detaching)
        {
            state = new ReloadState();
            var captured = state;
            Guard(() => detaching.OnReloadDetach(captured), ReloadCode.DetachHookThrew, source);
        }

        if (target is IReloadAware)
            _attach.Add(new AttachTask(target, state ?? new ReloadState()));
    }

    private void RunBoxedHooks(object source, object target)
    {
        ReloadState? state = null;

        if (source is IReloadAware detaching)
        {
            state = new ReloadState();
            var captured = state;
            Guard(() => detaching.OnReloadDetach(captured), ReloadCode.DetachHookThrew, source);
            state.Remap(Map);
        }

        if (target is IReloadAware attaching)
        {
            var captured = state ?? new ReloadState();
            Guard(() => attaching.OnReloadAttach(captured), ReloadCode.AttachHookThrew, target);
        }
    }

    // Attach runs after every root is committed, so a hook reading a static sees the post-reload world. In
    // reverse fill order, which is breadth first from the roots, so an object's dependencies attach first.
    private void Notify()
    {
        for (int i = _attach.Count - 1; i >= 0; i--)
        {
            var task = _attach[i];
            var target = (IReloadAware)task.Target;
            Guard(() => target.OnReloadAttach(task.State), ReloadCode.AttachHookThrew, task.Target);
        }

        foreach (var dropped in _report.Dropped)
            if (dropped is IReloadObserver observer)
                Guard(observer.OnReloadDropped, ReloadCode.ObserverHookThrew, dropped);
    }

    private Allocation SafeAllocate(MigrationPlan plan, object source)
    {
        try
        {
            return plan.Allocate(source, _context);
        }
        catch (Exception e)
        {
            _report.Report(ReloadCode.MigratorThrew, e, $"{plan.GetType().Name} allocating {source.GetType().FullName}");
            return Allocation.Preserve(source);
        }
    }

    private void SafeFill(MigrationPlan plan, object source, object target)
    {
        try
        {
            plan.Fill(source, target, _context);
        }
        catch (Exception e)
        {
            _report.Report(ReloadCode.MigratorThrew, e, $"{plan.GetType().Name} filling {source.GetType().FullName}");
        }
    }

    private void SafeRebuild(MigrationPlan plan, object source, object target)
    {
        try
        {
            plan.Rebuild(source, target, _context);
        }
        catch (Exception e)
        {
            _report.Report(ReloadCode.CollectionRebuildFailed, e, source.GetType().FullName);
        }
    }

    // One bad component must not abort a phase that has already mutated the graph.
    private void Guard(Action hook, ReloadCode code, object subject)
    {
        try
        {
            hook();
        }
        catch (Exception e)
        {
            _report.Report(code, e, subject.GetType().FullName);
        }
    }
}
