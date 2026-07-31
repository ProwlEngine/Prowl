using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Prowl.Ember;

/// <summary>
/// The migrators, in the order they are asked. A plain list with explicit insertion, rather than an ordering
/// derived from attributes: the order is the thing that matters, so it should be the thing you write.
/// </summary>
public sealed class MigratorRegistry : IReadOnlyList<IValueMigrator>
{
    private readonly List<IValueMigrator> _migrators = new();

    public static MigratorRegistry CreateDefault()
    {
        var registry = new MigratorRegistry();

        registry.Add(new EnumMigrator());
        registry.Add(new DelegateMigrator());
        registry.Add(new ReflectionMigrator());
        registry.Add(new ArrayMigrator());
        registry.Add(new WeakReferenceMigrator());
        registry.Add(new ImmutableMigrator());
        registry.Add(new HashContainerMigrator());
        registry.Add(new SequenceMigrator());
        registry.Add(new JsonMetadataMigrator());

        return registry;
    }

    public int Count => _migrators.Count;
    public IValueMigrator this[int index] => _migrators[index];

    public void Add(IValueMigrator migrator)
        => _migrators.Add(migrator ?? throw new ArgumentNullException(nameof(migrator)));

    public void InsertBefore<TExisting>(IValueMigrator migrator) where TExisting : IValueMigrator
        => Insert<TExisting>(migrator, offset: 0);

    public void InsertAfter<TExisting>(IValueMigrator migrator) where TExisting : IValueMigrator
        => Insert<TExisting>(migrator, offset: 1);

    public bool Replace<TExisting>(IValueMigrator migrator) where TExisting : IValueMigrator
    {
        int at = IndexOf<TExisting>();
        if (at < 0) return false;

        _migrators[at] = migrator ?? throw new ArgumentNullException(nameof(migrator));
        return true;
    }

    public bool Remove<TExisting>() where TExisting : IValueMigrator
    {
        int at = IndexOf<TExisting>();
        if (at < 0) return false;

        _migrators.RemoveAt(at);
        return true;
    }

    public bool Contains<TExisting>() where TExisting : IValueMigrator => IndexOf<TExisting>() >= 0;

    /// <summary>The resolution order, which is the answer to "what claimed my type".</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        for (int i = 0; i < _migrators.Count; i++)
            text.Append(i).Append(". ").AppendLine(_migrators[i].GetType().Name);
        text.Append(_migrators.Count).AppendLine(". (terminal) field copy, or preserve when inert");
        return text.ToString();
    }

    public IEnumerator<IValueMigrator> GetEnumerator() => _migrators.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Insert<TExisting>(IValueMigrator migrator, int offset) where TExisting : IValueMigrator
    {
        if (migrator == null) throw new ArgumentNullException(nameof(migrator));

        int at = IndexOf<TExisting>();
        if (at < 0)
            throw new InvalidOperationException($"No {typeof(TExisting).Name} is registered to position against.");

        _migrators.Insert(at + offset, migrator);
    }

    private int IndexOf<TExisting>()
    {
        for (int i = 0; i < _migrators.Count; i++)
            if (_migrators[i] is TExisting) return i;
        return -1;
    }
}
