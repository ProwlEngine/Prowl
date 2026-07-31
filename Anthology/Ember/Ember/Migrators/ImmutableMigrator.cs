using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Prowl.Ember;

/// <summary>
/// Rebuilds immutable hash keyed collections through their builders. Their internal trees store positions
/// computed from the previous keys' hash codes, so a field copy would produce a structure whose entries can
/// never be found again.
/// </summary>
/// <remarks>
/// The rebuild cannot happen while the replacement is being allocated. A migrated key is allocated first and
/// populated later, so hashing it at allocation time would file it under the hash of an object whose fields
/// are all still zero, which is exactly the failure this migrator exists to prevent. Instead an empty shell is
/// allocated so references to the collection have something stable to point at, and the real collection is
/// built in the rebuild phase and copied onto that shell once every key is complete.
/// </remarks>
public sealed class ImmutableMigrator : IValueMigrator
{
    public bool Handles(Type type)
    {
        if (!type.IsConstructedGenericType) return false;

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(ImmutableDictionary<,>) || definition == typeof(ImmutableHashSet<>);
    }

    public MigrationPlan Plan(Type type, PlanContext context)
    {
        var resolution = context.Types.Resolve(type);
        if (resolution.IsRemoved) return MigrationPlan.Dropped;

        var arguments = type.GetGenericArguments();
        bool keysMove = false;

        foreach (var argument in arguments)
            keysMove |= context.Types.Resolve(argument).IsSubstituted || !context.IsInertSlot(argument);

        if (resolution.IsUnchanged && !keysMove) return MigrationPlan.Preserved;

        var target = resolution.Target!;
        var targetArguments = target.GetGenericArguments();

        var rebuild = typeof(ImmutableMigrator)
            .GetMethod(targetArguments.Length == 2 ? nameof(RebuildDictionary) : nameof(RebuildSet),
                BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(targetArguments);

        return new ImmutablePlan(rebuild, target);
    }

    private static object RebuildDictionary<TKey, TValue>(object source, MigrationContext context) where TKey : notnull
    {
        var builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();

        foreach (DictionaryEntry entry in (IDictionary)source)
            if (context.Map(entry.Key) is TKey key)
                builder[key] = (TValue)context.Map(entry.Value)!;

        return builder.ToImmutable();
    }

    private static object RebuildSet<T>(object source, MigrationContext context)
    {
        var builder = ImmutableHashSet.CreateBuilder<T>();

        foreach (var item in (IEnumerable)source)
            if (context.Map(item) is T migrated)
                builder.Add(migrated);

        return builder.ToImmutable();
    }

    private sealed class ImmutablePlan : MigrationPlan
    {
        private readonly MethodInfo _rebuild;
        private readonly Type _target;
        private readonly FieldInfo[] _fields;

        public ImmutablePlan(MethodInfo rebuild, Type target)
        {
            _rebuild = rebuild;
            _target = target;
            _fields = InstanceFields(target);
        }

        public override bool NeedsRebuild => true;

        public override Allocation Allocate(object source, MigrationContext context)
            => Allocation.Replace(RuntimeHelpers.GetUninitializedObject(_target));

        /// <summary>
        /// Forces the keys through migration so their replacements exist and are queued. Nothing is inserted
        /// yet: a key's fields are not populated until its own fill runs.
        /// </summary>
        public override void Fill(object source, object target, MigrationContext context)
        {
            foreach (var entry in Entries(source))
            {
                try
                {
                    context.Map(entry);
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
            var built = _rebuild.Invoke(null, new object[] { source, context });
            if (built == null) return;

            // An immutable collection cannot be filled after the fact, so the finished one is copied onto the
            // shell every reference already points at.
            foreach (var field in _fields)
                field.SetValue(target, field.GetValue(built));
        }

        private static IEnumerable<object?> Entries(object source)
        {
            if (source is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    yield return entry.Key;
                    yield return entry.Value;
                }
                yield break;
            }

            foreach (var item in (IEnumerable)source)
                yield return item;
        }

        private static FieldInfo[] InstanceFields(Type type)
        {
            var fields = new List<FieldInfo>();

            for (var level = type; level != null && level != typeof(object); level = level.BaseType)
                fields.AddRange(level.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));

            return fields.ToArray();
        }

        public override string Describe() => $"immutable container rebuilt as {_target.Name}";
    }
}
