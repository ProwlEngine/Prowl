using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Prowl.Ember;

/// <summary>
/// Rebuilds keyed containers. They cannot be field copied, because a container's internal buckets encode
/// positions computed from the previous keys' hash codes, and a migrated key is a different object with a
/// different hash. So the keys are migrated first, and the entries are re-inserted once those keys are
/// complete.
/// </summary>
public sealed class HashContainerMigrator : IValueMigrator
{
    public bool Handles(Type type) => CollectionShape.Probe(type).IsKeyed;

    public MigrationPlan Plan(Type type, PlanContext context)
    {
        var resolution = context.Types.Resolve(type);
        if (resolution.IsRemoved) return MigrationPlan.Dropped;

        var shape = CollectionShape.Probe(type);

        bool keysMove = shape.ItemTypes.Any(x => context.Types.Resolve(x).IsSubstituted || !context.IsInertSlot(x));
        if (resolution.IsUnchanged && !keysMove) return MigrationPlan.Preserved;

        return new HashContainerPlan(shape, type, resolution.Target!, resolution.IsSubstituted,
            SequenceMigrator.SubclassState(type, resolution.Target!, context));
    }

    private sealed class HashContainerPlan : MigrationPlan
    {
        // The two sides have different key and value types once the container moved, so reading the source and
        // writing the target need separately typed accessors.
        private readonly IKeyedAccessor _from;
        private readonly IKeyedAccessor _to;
        private readonly Type _target;
        private readonly bool _moved;
        private readonly PropertyInfo? _comparerProperty;
        private readonly ConstructorInfo? _comparerConstructor;
        private readonly object?[] _defaultComparers;
        private readonly ObjectPlan? _subclassState;
        private readonly Type? _containerBase;

        public HashContainerPlan(CollectionShape shape, Type source, Type target, bool moved, ObjectPlan? subclassState)
        {
            _subclassState = subclassState;
            _containerBase = ObjectPlan.SubclassBoundary(target);
            _from = shape.CreateKeyedAccessor();
            _to = moved ? CollectionShape.Probe(target).CreateKeyedAccessor() : _from;
            _target = target;
            _moved = moved;

            _comparerProperty = source.GetProperty("Comparer", BindingFlags.Instance | BindingFlags.Public);
            _comparerConstructor = FindComparerConstructor(target, _comparerProperty?.PropertyType);
            _defaultComparers = DefaultComparersFor(shape.ElementType);
        }

        /// <summary>
        /// The framework's default comparers for a key type. Carrying one across would migrate it like any
        /// other object, fabricating an uninitialized stand-in for a singleton the current side already has.
        /// </summary>
        private static object?[] DefaultComparersFor(Type keyType)
        {
            return new[]
            {
                DefaultOf(typeof(EqualityComparer<>), keyType),
                DefaultOf(typeof(Comparer<>), keyType),
            };

            static object? DefaultOf(Type definition, Type argument)
            {
                try
                {
                    return definition.MakeGenericType(argument)
                        .GetProperty("Default", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                }
                catch (Exception e) when (e is ArgumentException or TypeLoadException or TargetInvocationException)
                {
                    return null;
                }
            }
        }

        private bool IsDefault(object comparer)
        {
            foreach (var known in _defaultComparers)
                if (ReferenceEquals(comparer, known)) return true;
            return false;
        }

        public override bool NeedsRebuild => true;

        public override Allocation Allocate(object source, MigrationContext context)
        {
            if (!_moved) return Allocation.Preserve(source);

            // Constructing the replacement is the one place a plan needs a migrated value up front: a container
            // built without its comparer would hash differently from the one it replaces.
            if (_comparerConstructor != null && _comparerProperty?.GetValue(source) is { } comparer && !IsDefault(comparer))
            {
                var migrated = context.Map(comparer);
                if (migrated != null)
                    return Allocation.Replace(_comparerConstructor.Invoke(new[] { migrated }));

                context.Report(ReloadCode.CollectionComparerDropped, ReloadSeverity.Warning,
                    "The custom comparer could not be carried across, so the default is used. Ordering and lookups may change.",
                    _target.FullName);
            }

            if (_target.GetConstructor(Type.EmptyTypes) == null)
                return Allocation.Replace(CreateShell());

            var created = Activator.CreateInstance(_target);
            return created == null ? Allocation.Drop : Allocation.Replace(created);
        }

        /// <summary>
        /// A subclass whose only constructor takes arguments still has to be replaced rather than left behind.
        /// An uninitialized instance is not enough on its own, because a container's storage is set up by its
        /// constructor and inserting into one that never ran fails, so the container's own state is seeded from
        /// a freshly built instance of the base. The subclass's own fields are carried over separately.
        /// </summary>
        private object CreateShell()
        {
            var shell = RuntimeHelpers.GetUninitializedObject(_target);
            if (_containerBase == null) return shell;

            object template;
            try
            {
                if (Activator.CreateInstance(_containerBase) is not { } created) return shell;
                template = created;
            }
            catch (Exception e) when (e is MissingMethodException or MemberAccessException or TargetInvocationException)
            {
                return shell;
            }

            const BindingFlags declared =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (var level = _containerBase; level != null && level != typeof(object); level = level.BaseType)
                foreach (var field in level.GetFields(declared))
                    field.SetValue(shell, field.GetValue(template));

            return shell;
        }

        /// <summary>
        /// Forces the keys through migration so their replacements exist and are queued. Nothing is inserted
        /// yet: a key's fields are not populated until its own fill runs, and inserting before then would hash
        /// it against an empty object.
        /// </summary>
        public override void Fill(object source, object target, MigrationContext context)
        {
            _subclassState?.Fill(source, target, context);

            foreach (var entry in _from.Entries(source))
            {
                try
                {
                    context.Map(entry.Key);
                }
                catch (Exception e)
                {
                    context.Report(ReloadCode.CollectionElementFailed, ReloadSeverity.Error,
                        $"Key: {e.Message}", source.GetType().FullName);
                }
            }
        }

        public override void Rebuild(object source, object target, MigrationContext context)
        {
            // Only the instance knows: a type implementing IDictionary is free to reject every mutation, and
            // third party immutables do exactly that without sharing a namespace we could recognise.
            if (_to.IsReadOnly(target))
            {
                context.Report(ReloadCode.CollectionReadOnly, ReloadSeverity.Warning,
                    "The container rejects mutation, so its entries keep pointing at the previous types.",
                    target.GetType().FullName);
                return;
            }

            bool inPlace = ReferenceEquals(source, target);

            // Reading has to finish before clearing when both sides are the same container.
            var entries = inPlace
                ? _from.Entries(source).ToArray()
                : _from.Entries(source);

            _to.Clear(target);

            bool reportedCollision = false;

            foreach (var entry in entries)
            {
                try
                {
                    var key = context.Map(entry.Key);
                    if (key == null)
                    {
                        context.Report(ReloadCode.CollectionKeyNull, ReloadSeverity.Warning,
                            "A key migrated to null, so its entry was dropped.", target.GetType().FullName);
                        continue;
                    }

                    var value = ReferenceEquals(entry.Key, entry.Value) ? key : context.Map(entry.Value);

                    if (_to.Add(target, key, value)) continue;

                    if (reportedCollision) continue;
                    reportedCollision = true;

                    context.Report(ReloadCode.CollectionKeyCollision, ReloadSeverity.Error,
                        $"Keys that were distinct before the reload now collide, starting with {key}. Entries were lost.",
                        target.GetType().FullName);
                }
                catch (Exception e)
                {
                    context.Report(ReloadCode.CollectionRebuildFailed, ReloadSeverity.Error, e.Message,
                        target.GetType().FullName);
                }
            }
        }

        private static ConstructorInfo? FindComparerConstructor(Type type, Type? comparerType)
        {
            if (comparerType == null) return null;

            foreach (var constructor in type.GetConstructors())
            {
                var parameters = constructor.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == comparerType)
                    return constructor;
            }

            return null;
        }

        public override string Describe() => _moved ? $"keyed container rebuilt as {_target.Name}" : "keyed container rehashed in place";
    }
}
