using System;

namespace Prowl.Ember;

/// <summary>
/// Allocates a new array of the migrated element type, or keeps the existing one when the element type did not
/// move, and migrates each element.
/// </summary>
public sealed class ArrayMigrator : IValueMigrator
{
    public bool Handles(Type type) => type.IsArray;

    /// <summary>An array is exactly as interesting as its element type, which the analyzer follows already.</summary>
    public bool ForcesVisit(Type type) => false;

    public MigrationPlan Plan(Type type, PlanContext context)
    {
        var element = type.GetElementType()!;
        var resolution = context.Types.Resolve(element);

        if (resolution.IsRemoved) return MigrationPlan.Dropped;

        bool moved = resolution.IsSubstituted;
        bool elementsNeedMapping = moved || !context.IsInertSlot(element);

        if (!elementsNeedMapping) return MigrationPlan.Preserved;

        return new ArrayPlan(resolution.Target!, moved);
    }

    private sealed class ArrayPlan : MigrationPlan
    {
        private readonly Type _element;
        private readonly bool _moved;

        public ArrayPlan(Type element, bool moved)
        {
            _element = element;
            _moved = moved;
        }

        public override Allocation Allocate(object source, MigrationContext context)
        {
            var array = (Array)source;
            if (!_moved) return Allocation.Preserve(array);

            if (array.Rank == 1) return Allocation.Replace(Array.CreateInstance(_element, array.Length));

            var lengths = new int[array.Rank];
            for (int i = 0; i < lengths.Length; i++)
                lengths[i] = array.GetLength(i);

            return Allocation.Replace(Array.CreateInstance(_element, lengths));
        }

        public override void Fill(object source, object target, MigrationContext context)
        {
            var from = (Array)source;
            var to = (Array)target;

            if (from.Rank == 1)
            {
                int length = Math.Min(from.Length, to.Length);
                for (int i = 0; i < length; i++)
                    SetElement(to, from, i, context);
                return;
            }

            if (from.Length == 0) return;

            var indices = new int[from.Rank];
            do
            {
                try
                {
                    to.SetValue(context.Map(from.GetValue(indices)), indices);
                }
                catch (Exception e)
                {
                    context.Report(ReloadCode.CollectionElementFailed, ReloadSeverity.Error, e.Message, from.GetType().FullName);
                }
            }
            while (Advance(from, indices));
        }

        private static void SetElement(Array to, Array from, int index, MigrationContext context)
        {
            try
            {
                to.SetValue(context.Map(from.GetValue(index)), index);
            }
            catch (Exception e)
            {
                context.Report(ReloadCode.CollectionElementFailed, ReloadSeverity.Error,
                    $"Element {index}: {e.Message}", from.GetType().FullName);
            }
        }

        private static bool Advance(Array array, int[] indices)
        {
            for (int i = 0; i < array.Rank; i++)
            {
                if (++indices[i] < array.GetLength(i)) return true;
                indices[i] = 0;
            }
            return false;
        }

        public override string Describe() => _moved ? $"array of {_element.Name}, reallocated" : "array, elements in place";
    }
}
