namespace Prowl.Ember;

/// <summary>The stage of a reload. Each runs to completion before the next begins.</summary>
public enum ReloadPhase
{
    /// <summary>Build the maps and the per type facts. Touches no live object.</summary>
    Plan,

    /// <summary>Allocate a replacement for each root value.</summary>
    Map,

    /// <summary>Copy state, allocating replacements for everything the roots reach.</summary>
    Fill,

    /// <summary>Re-insert content that had to wait for its keys to be complete.</summary>
    Rebuild,

    /// <summary>Write the mapped values back into their root slots.</summary>
    Commit,

    /// <summary>Run the lifecycle hooks, with the whole graph consistent.</summary>
    Notify,
}
