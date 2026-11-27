using System;
using System.Linq;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// Migrates ordered containers, walking only their live elements. Covers anything that presents as a list or
/// as a thread safe producer consumer collection, including user types deriving from one.
/// </summary>
public sealed class SequenceMigrator : IValueMigrator
{
    public bool Handles(Type type)
        => CollectionShape.Probe(type).Kind is CollectionKind.Sequence or CollectionKind.ProducerConsumer;

    public MigrationPlan Plan(Type type, PlanContext context)
    {
        var shape = CollectionShape.Probe(type);
        var resolution = context.Types.Resolve(type);
        if (resolution.IsRemoved) return MigrationPlan.Dropped;

        var element = shape.ElementType;
        bool elementsNeedMapping = !context.IsInertSlot(element) || context.Types.Resolve(element).IsSubstituted;

        if (resolution.IsUnchanged && !elementsNeedMapping) return MigrationPlan.Preserved;

        var target = resolution.Target!;

        // Without a usable constructor there is no way to produce an empty container to refill. A plain field
        // copy migrates the backing array through the array migrator and gets the same answer.
        if (target.GetConstructor(new[] { typeof(int) }) == null && target.GetConstructor(Type.EmptyTypes) == null)
            return new ObjectPlan(type, target, context);

        return new SequencePlan(shape, type, target, resolution.IsSubstituted, elementsNeedMapping,
            SubclassState(type, target, context));
    }

    /// <summary>
    /// Fields a subclass adds on top of the container. Rebuilding the container replaces the instance, so
    /// without this the subclass's own state would be silently lost.
    /// </summary>
    internal static ObjectPlan? SubclassState(Type source, Type target, PlanContext context)
    {
        if (ObjectPlan.SubclassBoundary(target) is not { } boundary) return null;

        var plan = new ObjectPlan(source, target, context, boundary);
        return plan.HasSteps ? plan : null;
    }

    private sealed class SequencePlan : MigrationPlan
    {
        // The two sides have different element types once the container moved, so reading the source and
        // writing the target need separately typed accessors.
        private readonly ISequenceAccessor _from;
        private readonly ISequenceAccessor _to;
        private readonly Type _target;
        private readonly bool _moved;
        private readonly bool _elementsNeedMapping;
        private readonly ConstructorInfo? _capacity;
        private readonly ObjectPlan? _subclassState;

        public SequencePlan(CollectionShape shape, Type source, Type target, bool moved, bool elementsNeedMapping,
            ObjectPlan? subclassState)
        {
            _from = shape.CreateSequenceAccessor();
            _to = moved ? CollectionShape.Probe(target).CreateSequenceAccessor() : _from;
            _target = target;
            _moved = moved;
            _elementsNeedMapping = elementsNeedMapping;
            _capacity = target.GetConstructor(new[] { typeof(int) });
            _subclassState = subclassState;
        }

        public override Allocation Allocate(object source, MigrationContext context)
        {
            if (!_moved) return Allocation.Preserve(source);

            var created = _capacity != null
                ? _capacity.Invoke(new object[] { _from.Count(source) })
                : Activator.CreateInstance(_target);

            return created == null ? Allocation.Drop : Allocation.Replace(created);
        }

        public override void Fill(object source, object target, MigrationContext context)
        {
            _subclassState?.Fill(source, target, context);

            if (ReferenceEquals(source, target))
            {
                FillInPlace(target, context);
                return;
            }

            _to.Clear(target);

            foreach (var item in _from.Items(source))
                TryAdd(target, item, context);
        }

        private void FillInPlace(object collection, MigrationContext context)
        {
            if (!_elementsNeedMapping) return;

            if (_from.SupportsIndexer)
            {
                int index = 0;
                foreach (var item in _from.Items(collection).ToArray())
                {
                    try
                    {
                        _from.SetAt(collection, index, context.Map(item));
                    }
                    catch (Exception e)
                    {
                        context.Report(ReloadCode.CollectionElementFailed, ReloadSeverity.Error,
                            $"Element {index}: {e.Message}", collection.GetType().FullName);
                    }
                    index++;
                }
                return;
            }

            // No indexer, so the only way to rewrite it is to drain a snapshot and refill in order.
            var snapshot = _from.Items(collection).ToArray();
            _from.Clear(collection);

            foreach (var item in snapshot)
                TryAdd(collection, item, context);
        }

        private void TryAdd(object collection, object? item, MigrationContext context)
        {
            try
            {
                _to.Add(collection, _elementsNeedMapping ? context.Map(item) : item);
            }
            catch (Exception e)
            {
                context.Report(ReloadCode.CollectionElementFailed, ReloadSeverity.Error, e.Message,
                    collection.GetType().FullName);
            }
        }

        public override string Describe() => _moved ? $"sequence rebuilt as {_target.Name}" : "sequence, elements in place";
    }
}
