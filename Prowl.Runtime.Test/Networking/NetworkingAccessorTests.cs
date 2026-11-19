using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void ComponentsShareOneLazyObjectAccessorAndClonesOwnTheirSettings()
    {
        GameObject go = CreateGameObject();
        NetTestComponent first = go.AddComponent<NetTestComponent>();
        NetworkReadinessListener second = go.AddComponent<NetworkReadinessListener>();
        Assert.False(go.IsProxy);
        Assert.False(first.IsProxy);
        Assert.Null(go._network);
        NetworkAccessor network = go.Network;
        Assert.Same(network, go.Network);
        Assert.Same(network, first.Network);
        Assert.Same(network, second.Network);
        network.Networked = true;
        network.Orphaned = NetworkOrphaned.ClearOwner;
        network.Flags = NetworkFlags.NoScaleSync;

        using GameObject copy = Serializer.Deserialize<GameObject>(Serializer.Serialize(go))!;
        Assert.NotSame(network, copy.Network);
        Assert.Equal(network.Networked, copy.Network.Networked);
        Assert.Equal(network.Orphaned, copy.Network.Orphaned);
        Assert.Equal(network.Flags, copy.Network.Flags);
        Assert.Same(copy, copy.Network.Root);
        copy.Network.Flags = NetworkFlags.NoPositionSync;
        Assert.Equal(NetworkFlags.NoScaleSync, network.Flags);
    }

    [Fact]
    public void LoadingDefaultObjectSettingsResetsAnExistingAccessor()
    {
        GameObject defaults = CreateGameObject();
        GameObject go = CreateGameObject();
        NetworkAccessor accessor = go.Network;
        accessor.Networked = true;
        accessor.Orphaned = NetworkOrphaned.ClearOwner;
        accessor.Flags = NetworkFlags.NoTransformSync;
        go.Deserialize(Serializer.Serialize(defaults), new SerializationContext());
        Assert.Same(accessor, go.Network);
        Assert.False(accessor.Networked);
        Assert.Equal(NetworkOrphaned.Destroy, accessor.Orphaned);
        Assert.Equal(NetworkFlags.None, accessor.Flags);
        Assert.False(Serializer.Serialize(go).TryGet("Network", out _));
    }

}
