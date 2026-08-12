using System.Collections.Generic;

using Prowl.Editor.Profiling;

namespace Prowl.Editor.GUI.RenderProfiler.Data;

public interface ISnapshotResourceResolver
{
    SnapshotResource? Resolve(SnapshotResourceID id);
}

public sealed class SnapshotResourceResolver : ISnapshotResourceResolver
{
    private readonly Dictionary<uint, SnapshotResource> _byResourceId;

    public SnapshotResourceResolver(Snapshot snapshot)
    {
        _byResourceId = new Dictionary<uint, SnapshotResource>(snapshot.Resources.Count);
        foreach (SnapshotResource resource in snapshot.Resources)
            _byResourceId[resource.ResourceId] = resource;
    }

    public SnapshotResource? Resolve(SnapshotResourceID id)
    {
        if (!id.IsValid || !_byResourceId.TryGetValue(id.ResourceId, out SnapshotResource? resource))
            return null;
        return resource;
    }
}
