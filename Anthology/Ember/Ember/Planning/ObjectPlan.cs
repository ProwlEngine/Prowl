using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Prowl.Ember;

internal enum FieldAction
{
    /// <summary>Read from the source, migrate, write to the target.</summary>
    CopyMapped,

    /// <summary>Read from the source and write it through. The field's type cannot hold anything migratable.</summary>
    CopyDirect,

    /// <summary>Newly added. Evaluate its declared initializer.</summary>
    Default,

    /// <summary>Newly added, with an explicit initializer method to run once every field is populated.</summary>
    Initialize,

    /// <summary>Newly added with nothing to give it, so it stays at zero.</summary>
    Zero,
}

internal readonly record struct FieldStep(
    FieldInfo? Source,
    FieldInfo Target,
    FieldAction Action,
    Func<object?>? Factory,
    MethodInfo? Initializer);

internal enum FillMode
{
    /// <summary>The target is a fresh uninitialized instance. Every step applies.</summary>
    Replace,

    /// <summary>The target already exists and was constructed normally. Only carry the previous values over.</summary>
    CarryOnly,

    /// <summary>The target is the source. Only fields that could have changed are touched.</summary>
    SameInstance,
}

/// <summary>
/// The terminal plan: copy each field of the target from the equivalent field of the source, matching base
/// class hierarchies through the reload, and give newly added fields their declared value.
/// </summary>
/// <remarks>
/// The field set is classified once per type pair rather than branched per field per instance. That is what
/// the plan model buys: <see cref="FieldAction.CopyDirect"/> in particular removes a migration call and an
/// identity map probe for every primitive, string, and inert struct field in the graph, which on a real scene
/// is most of them.
/// </remarks>
internal sealed class ObjectPlan : MigrationPlan
{
    private const BindingFlags DeclaredInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Type _sourceType;
    private readonly Type _targetType;
    private readonly PlanContext _context;
    private readonly bool _substituted;
    private readonly Type? _below;

    private readonly FieldStep[] _replace;
    private readonly FieldStep[] _sameInstance;
    private Dictionary<(Type, FillMode), FieldStep[]>? _unexpected;

    /// <remarks>
    /// A non-null <c>below</c> handles only fields declared by types more derived than it. A container
    /// migrator uses that to carry the state a subclass adds, leaving the container's own internals to the
    /// migrator that understands them.
    /// </remarks>
    public ObjectPlan(Type sourceType, Type targetType, PlanContext context, Type? below = null)
    {
        _sourceType = sourceType;
        _targetType = targetType;
        _context = context;
        _below = below;
        _substituted = !ReferenceEquals(sourceType, targetType);

        _replace = Build(targetType, FillMode.Replace);
        _sameInstance = _substituted ? Array.Empty<FieldStep>() : Build(targetType, FillMode.SameInstance);
    }

    /// <summary>
    /// The point in a type's base chain where its own code stops and the framework's begins. Everything more
    /// derived than this is state the author added and expects to survive.
    /// </summary>
    public static Type? SubclassBoundary(Type type)
    {
        for (var level = type; level != null; level = level.BaseType)
            if (level.Assembly != type.Assembly)
                return level;

        return null;
    }

    public bool HasSteps => _replace.Length > 0 || _sameInstance.Length > 0;

    public override Allocation Allocate(object source, MigrationContext context)
        => _substituted
            ? Allocation.Replace(RuntimeHelpers.GetUninitializedObject(_targetType))
            : Allocation.Preserve(source);

    public override void Fill(object source, object target, MigrationContext context)
    {
        var steps = Select(source, target);
        List<MethodInfo>? deferred = null;

        foreach (var step in steps)
        {
            object? value;

            switch (step.Action)
            {
                case FieldAction.CopyMapped:
                    value = context.Map(step.Source!.GetValue(source));
                    break;

                case FieldAction.CopyDirect:
                    value = step.Source!.GetValue(source);
                    break;

                case FieldAction.Default:
                    value = step.Factory!();
                    break;

                case FieldAction.Initialize:
                    (deferred ??= new List<MethodInfo>()).Add(step.Initializer!);
                    continue;

                default:
                    continue;
            }

            try
            {
                step.Target.SetValue(target, value);
            }
            catch (Exception e)
            {
                context.Report(ReloadCode.FieldWriteFailed, ReloadSeverity.Error, e.Message,
                    $"{step.Target.DeclaringType?.Name}.{step.Target.Name}");
            }
        }

        if (deferred == null) return;

        // Initializer methods run only once every field is populated, so one may read any sibling field
        // regardless of declaration order.
        foreach (var initializer in deferred)
        {
            try
            {
                initializer.Invoke(target, null);
            }
            catch (Exception e)
            {
                context.Report(ReloadCode.InitializerMethodFailed, ReloadSeverity.Error,
                    (e.InnerException ?? e).Message, $"{_targetType.Name}.{initializer.Name}");
            }
        }
    }

    private FieldStep[] Select(object source, object target)
    {
        if (ReferenceEquals(source, target)) return _sameInstance;

        var actualType = target.GetType();
        if (ReferenceEquals(actualType, _targetType)) return _replace;

        // A pre-existing target, which happens when a readonly static is upgraded in place, or a slot holding a
        // more derived instance than the field declares.
        _unexpected ??= new Dictionary<(Type, FillMode), FieldStep[]>();
        var key = (actualType, FillMode.CarryOnly);

        if (!_unexpected.TryGetValue(key, out var steps))
            _unexpected[key] = steps = Build(actualType, FillMode.CarryOnly);

        return steps;
    }

    private FieldStep[] Build(Type targetType, FillMode mode)
    {
        var steps = new List<FieldStep>();
        var pairs = new List<(FieldInfo Target, FieldInfo? Source)>();
        var claimed = new HashSet<FieldInfo>();

        // Pair each target field with the source field at the hierarchy level it was matched to.
        foreach (var (sourceLevel, targetLevel) in MatchHierarchies(_sourceType, targetType).Reverse())
        {
            if (!IsHandledLevel(targetLevel)) continue;

            foreach (var targetField in targetLevel.GetFields(DeclaredInstance))
            {
                if (targetField.DeclaringType != targetLevel) continue;
                if (targetField.FieldType.IsPointer) continue;

                if (IgnoreRules.Applies(targetField))
                {
                    // Opting out means the previous value is not carried over. It does not mean the field is
                    // left unset: a replacement starts uninitialized, so skipping it entirely would hand back
                    // null for anything a fresh instance would have built in its initializer.
                    if (mode == FillMode.Replace && IgnoredFieldStep(targetField) is { } reset)
                        steps.Add(reset);

                    continue;
                }

                var sourceField = ReferenceEquals(sourceLevel, targetLevel)
                    ? targetField
                    : sourceLevel?.GetField(targetField.Name, DeclaredInstance);

                if (sourceField != null) claimed.Add(sourceField);
                pairs.Add((targetField, sourceField));
            }
        }

        // Relocation across the boundary into a container's internals would be meaningless.
        var relocated = _below == null ? UnclaimedSourceFields(claimed) : new Dictionary<string, FieldInfo>();

        foreach (var (targetField, matched) in pairs)
        {
            var sourceField = matched;

            // A field pushed down to a base class, or pulled up from one, has no counterpart at its own level.
            // Claiming an unambiguous one of the same name from elsewhere in the source carries it anyway.
            if (sourceField == null && relocated.Remove(targetField.Name, out var moved))
                sourceField = moved;

            if (BuildStep(sourceField, targetField, mode) is { } step)
                steps.Add(step);
        }

        return steps.ToArray();
    }

    private bool IsHandledLevel(Type targetLevel)
        => _below == null || (targetLevel != _below && _below.IsAssignableFrom(targetLevel));

    /// <summary>
    /// Source fields no level matched, keyed by name. A name declared more than once in the source hierarchy
    /// is dropped: with shadowing there is no way to tell which one a moved field came from, and guessing
    /// wrong is worse than leaving the field at its default.
    /// </summary>
    private Dictionary<string, FieldInfo> UnclaimedSourceFields(HashSet<FieldInfo> claimed)
    {
        var byName = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        var ambiguous = new List<string>();

        for (var level = _sourceType; level != null; level = level.BaseType)
            foreach (var field in level.GetFields(DeclaredInstance))
            {
                if (field.DeclaringType != level) continue;
                if (field.FieldType.IsPointer) continue;
                if (claimed.Contains(field)) continue;
                if (IgnoreRules.Applies(field)) continue;

                if (!byName.TryAdd(field.Name, field)) ambiguous.Add(field.Name);
            }

        foreach (var name in ambiguous)
            byName.Remove(name);

        return byName;
    }

    private FieldStep? BuildStep(FieldInfo? sourceField, FieldInfo targetField, FillMode mode)
    {
        if (sourceField != null && !CarriesOver(sourceField, targetField))
            sourceField = null;

        if (sourceField != null)
        {
            if (CanCopyDirect(sourceField.FieldType))
                return mode == FillMode.SameInstance ? null : new FieldStep(sourceField, targetField, FieldAction.CopyDirect, null, null);

            return new FieldStep(sourceField, targetField, FieldAction.CopyMapped, null, null);
        }

        // Anything below only applies to a target that started uninitialized.
        if (mode != FillMode.Replace) return null;

        return NewFieldStep(targetField);
    }

    private bool CarriesOver(FieldInfo sourceField, FieldInfo targetField)
    {
        var resolved = _context.Types.Resolve(sourceField.FieldType);

        if (!resolved.IsRemoved && targetField.FieldType.IsAssignableFrom(resolved.Target))
            return true;

        _context.Report(ReloadCode.FieldTypeChanged, ReloadSeverity.Warning,
            $"Type changed from {sourceField.FieldType.Name} to {targetField.FieldType.Name}. The previous value is discarded.",
            $"{targetField.DeclaringType?.Name}.{targetField.Name}");
        return false;
    }

    /// <summary>
    /// What an opted-out field gets on a fresh replacement: whatever a newly constructed instance would have
    /// had. Silent, because opting out is deliberate and there is nothing to warn about.
    /// </summary>
    private FieldStep? IgnoredFieldStep(FieldInfo targetField)
        => _context.TryGetFieldDefault(targetField, out var factory)
            ? new FieldStep(null, targetField, FieldAction.Default, factory, null)
            : null;

    private FieldStep? NewFieldStep(FieldInfo targetField)
    {
        if (targetField.GetCustomAttribute<ReloadInitializerAttribute>() is { } attribute)
        {
            if (attribute.MethodName == null) return null; // deliberately left at its default

            var initializer = targetField.DeclaringType!.GetMethod(attribute.MethodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);

            if (initializer == null)
            {
                _context.Report(ReloadCode.InitializerMethodMissing, ReloadSeverity.Error,
                    $"No parameterless instance method named '{attribute.MethodName}' exists.",
                    $"{targetField.DeclaringType?.Name}.{targetField.Name}");
                return null;
            }

            return new FieldStep(null, targetField, FieldAction.Initialize, null, initializer);
        }

        if (_context.TryGetFieldDefault(targetField, out var factory))
            return new FieldStep(null, targetField, FieldAction.Default, factory, null);

        _context.Report(ReloadCode.NewFieldDefaulted, ReloadSeverity.Info,
            "Newly added with no initializer to replay, so it starts at its zero value.",
            $"{targetField.DeclaringType?.Name}.{targetField.Name}");
        return null;
    }

    private bool CanCopyDirect(Type fieldType) => _context.IsInertSlot(fieldType);

    // Walk the target's base chain, pairing each level with the source level that maps onto it.
    private IEnumerable<(Type? SourceLevel, Type TargetLevel)> MatchHierarchies(Type? sourceType, Type targetType)
    {
        var sourceLevel = sourceType;

        for (var targetLevel = targetType; targetLevel != null; targetLevel = targetLevel.BaseType)
        {
            var matched = FindMatchingLevel(sourceLevel, targetLevel);

            if (matched != null)
            {
                yield return (matched, targetLevel);
                sourceLevel = matched.BaseType;
            }
            else
            {
                yield return (null, targetLevel);
            }
        }
    }

    private Type? FindMatchingLevel(Type? sourceLevel, Type targetLevel)
    {
        for (; sourceLevel != null; sourceLevel = sourceLevel.BaseType)
        {
            if (_context.Types.Resolve(sourceLevel).Target is not { } mapped) continue;
            if (ReferenceEquals(mapped, targetLevel)) return sourceLevel;

            // Example<T1> pairs with Example<T2>.
            if (mapped.IsGenericType && targetLevel.IsGenericType
                && mapped.GetGenericTypeDefinition() == targetLevel.GetGenericTypeDefinition())
                return sourceLevel;
        }

        return null;
    }

    public override string Describe()
        => _substituted
            ? $"field copy {_sourceType.Name} to {_targetType.Name}, {_replace.Length} fields"
            : $"field copy in place, {_sameInstance.Length} of {_replace.Length} fields";
}
