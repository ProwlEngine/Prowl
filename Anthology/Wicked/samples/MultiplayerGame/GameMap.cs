using Prowl.Wicked;

namespace MultiplayerGame;

/// <summary>
/// The single game map where all players exist.
/// </summary>
public class GameMap : Map
{
    public override void OnCreated()
    {
        Console.WriteLine($"[Map] Created: {MapId}");
    }

    public override void OnEntityEnter(NetworkEntity entity)
    {
        Console.WriteLine($"[Map] Entity entered: {entity.GetType().Name} (NetId={entity.NetworkId})");
    }

    public override void OnEntityLeave(NetworkEntity entity)
    {
        Console.WriteLine($"[Map] Entity left: {entity.GetType().Name} (NetId={entity.NetworkId})");
    }

    public override void OnObserverEnter(RemoteClient client)
    {
        Console.WriteLine($"[Map] Observer entered: ClientId={client.ClientId}");
    }

    public override void OnObserverLeave(RemoteClient client)
    {
        Console.WriteLine($"[Map] Observer left: ClientId={client.ClientId}");
    }
}
