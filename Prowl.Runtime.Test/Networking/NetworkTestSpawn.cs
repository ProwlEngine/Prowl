using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Courier;
using Prowl.Echo;
using Prowl.Runtime.Networking;

namespace Prowl.Runtime.Test;

/// <summary>Spawns objects a test built by hand, as ones from a factory every peer has.</summary>
internal static class NetworkTestSpawn
{
    private static int s_next;

    // Scenes here are built in code, so their networked objects are ones a factory builds a copy of.
    [ModuleInitializer]
    internal static void Install() => NetworkSystem.UnstoredSceneObjects = TemplateOf;

    /// <summary>Spawns <paramref name="go" /> as an object a factory builds a copy of, as it is now.</summary>
    internal static void SpawnBuilt(this GameObject go, NetPeer? owner = null, NetworkOrphaned? orphaned = null)
    {
        if (Networking.Networking.System is not { } system)
        {
            go.Network.MarkSpawned(orphaned);
            return;
        }

        system.Spawn(go, owner, orphaned ?? go.Network.Orphaned, TemplateOf(go));
    }

    /// <summary>
    ///     A spawn message for <paramref name="go" /> as a host would send it, for a test that plays the host. One
    ///     spawned from a prefab or a factory of the test's own goes that way. Any other gets a factory that builds a
    ///     copy of the object as it is now, which is what the receiving client has.
    /// </summary>
    internal static void WriteSpawn(this NetworkSystem system, GameObject go, int owner, NetworkOrphaned orphaned,
        NetWriter output, uint generation = 0, uint netId = 0, bool replace = false)
    {
        NetworkOrphaned kept = go.Network.Orphaned;
        go.Network.ApplyOrphaned(orphaned);
        try
        {
            system.WriteSpawn(new NetworkObject(system, go)
            {
                Owner = owner, Generation = generation, NetId = netId == 0 ? system.NextNetId() : netId,
                Template = system.FindEntry(go) is { Template: { } built } && !IsCopy(built) ? built : TemplateOf(go)
            }, output, replace);
        }
        finally
        {
            go.Network.ApplyOrphaned(kept);
        }
    }

    private static bool IsCopy(SpawnTemplate template) => template.Factory?.StartsWith("test:") == true;

    /// <summary>A template naming a factory registered here, which builds a copy of the object as it is now.</summary>
    private static SpawnTemplate TemplateOf(GameObject go)
    {
        string factory = $"test:{++s_next}";
        EchoObject tree = Serializer.Serialize(typeof(GameObject), go,
            new SerializationContext { ExternalReferences = SceneReferenceResolver.ForTree(go) });
        Networking.Networking.RegisterFactory(factory, (in NetworkSpawn _) =>
            Serializer.Deserialize<GameObject>(tree, SceneReferenceResolver.ContextForLinking())!);
        List<Guid> identifiers = new();
        NetworkSystem.CollectIdentifiers(go, identifiers);
        return new SpawnTemplate(Guid.Empty, factory, [], [.. identifiers], NetworkComponents.Gather(go));
    }
}
