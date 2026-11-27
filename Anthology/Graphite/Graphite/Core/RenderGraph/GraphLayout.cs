using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>One pass of a layout: its name, its group and every resource access it declared.</summary>
public readonly record struct PassLayout(string Name, int GroupIndex, PassResourceAccess[] Accesses);

/// <summary>One resource of a layout.</summary>
public readonly record struct ResourceLayout(RenderResourceID Id, GraphResourceKind Kind, Lifetime Lifetime, bool Imported, int Scope);

/// <summary>The groups, passes and resources of a frame. The same object is kept while the declarations do not change.</summary>
public sealed class GraphLayout
{
    internal GraphLayout(ulong hash, GroupInfo[] groups, PassLayout[] passes, ResourceLayout[] resources)
    {
        Hash = hash;
        Groups = groups;
        Passes = passes;
        Resources = resources;
        PassInfos = new PassInfo[passes.Length];
        for (int i = 0; i < passes.Length; i++)
            PassInfos[i] = new PassInfo(passes[i].Name, i, passes[i].GroupIndex, passes[i].Accesses);
    }

    public ulong Hash { get; }

    public IReadOnlyList<GroupInfo> Groups { get; }

    public IReadOnlyList<PassLayout> Passes { get; }

    public IReadOnlyList<ResourceLayout> Resources { get; }

    internal PassInfo[] PassInfos { get; }
}
