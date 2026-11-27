namespace Prowl.Ember;

/// <summary>
/// Hot reload lifecycle hooks for a type whose instances are replaced. Tear down engine registrations
/// (physics bodies, event subscriptions, native handles) in <see cref="OnReloadDetach"/> on the outgoing
/// instance and re-establish them in <see cref="OnReloadAttach"/> on the incoming one.
/// </summary>
public interface IReloadAware
{
    /// <summary>
    /// Runs on the outgoing instance once its state has been read, while the previous graph is still intact.
    /// Values written to <paramref name="state"/> are migrated before they reach <see cref="OnReloadAttach"/>.
    /// </summary>
    void OnReloadDetach(ReloadState state) { }

    /// <summary>
    /// Runs on the incoming instance after every root has been committed, so the whole graph is consistent.
    /// </summary>
    void OnReloadAttach(ReloadState state) { }
}

/// <summary>
/// Hot reload notifications for the two outcomes that are not a replacement. Independent of
/// <see cref="IReloadAware"/>; a type may implement either, both, or neither.
/// </summary>
public interface IReloadObserver
{
    /// <summary>Runs on an instance that was visited but carried over unchanged.</summary>
    void OnReloadPreserved() { }

    /// <summary>Runs on an instance whose type was removed, so every reference to it became null.</summary>
    void OnReloadDropped() { }
}
