using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Ember;

public enum AssemblyChangeKind
{
    Added,
    Replaced,
    Removed,
}

/// <summary>One assembly's part in a reload. Exactly one of the two may be null.</summary>
public readonly record struct AssemblyChange(Assembly? Previous, Assembly? Current)
{
    public AssemblyChangeKind Kind =>
        Previous == null ? AssemblyChangeKind.Added :
        Current == null ? AssemblyChangeKind.Removed :
        AssemblyChangeKind.Replaced;
}

/// <summary>
/// An immutable description of one reload: which assemblies changed and which objects to migrate as roots.
/// The engine holds no pending swap state, so two reloads cannot interleave and a failed one leaves no
/// residue.
/// </summary>
public sealed class ReloadRequest
{
    private ReloadRequest(IReadOnlyList<AssemblyChange> changes, IReadOnlyList<object> roots)
    {
        Changes = changes;
        Roots = roots;
    }

    public IReadOnlyList<AssemblyChange> Changes { get; }
    public IReadOnlyList<object> Roots { get; }

    public static Builder Create() => new();

    public sealed class Builder
    {
        private readonly List<AssemblyChange> _changes = new();
        private readonly List<object> _roots = new();

        public Builder Replace(Assembly previous, Assembly current)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (ReferenceEquals(previous, current))
                throw new ArgumentException($"Cannot replace {previous.GetName().Name} with itself.", nameof(current));

            _changes.Add(new AssemblyChange(previous, current));
            return this;
        }

        public Builder Add(Assembly current)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            _changes.Add(new AssemblyChange(null, current));
            return this;
        }

        public Builder Remove(Assembly previous)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            _changes.Add(new AssemblyChange(previous, null));
            return this;
        }

        public Builder Root(object instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            _roots.Add(instance);
            return this;
        }

        public Builder Roots(IEnumerable<object> instances)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            foreach (var instance in instances) Root(instance);
            return this;
        }

        public ReloadRequest Build()
        {
            Validate();
            return new ReloadRequest(_changes.ToArray(), _roots.ToArray());
        }

        private void Validate()
        {
            var seenPrevious = new HashSet<Assembly>();
            var seenCurrent = new HashSet<Assembly>();

            foreach (var change in _changes)
            {
                if (change.Previous != null && !seenPrevious.Add(change.Previous))
                    throw new ArgumentException($"Assembly {change.Previous.GetName().Name} is named as previous more than once.");

                if (change.Current != null && !seenCurrent.Add(change.Current))
                    throw new ArgumentException($"Assembly {change.Current.GetName().Name} is named as current more than once.");
            }

            foreach (var change in _changes)
                if (change.Kind == AssemblyChangeKind.Removed && seenCurrent.Contains(change.Previous!))
                    throw new ArgumentException($"Assembly {change.Previous!.GetName().Name} is both removed and introduced.");
        }
    }
}
