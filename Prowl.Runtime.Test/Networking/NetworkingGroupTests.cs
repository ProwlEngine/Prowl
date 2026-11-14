using Prowl.Echo;
using Prowl.Runtime.Networking;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void NetworkGroupsCombineAndOverlap()
    {
        NetworkGroups red = NetworkGroups.Of(0), blue = NetworkGroups.Of(63);
        Assert.True(NetworkGroups.None.IsEmpty);
        Assert.False(red.IsEmpty);
        Assert.True(red.Contains(0));
        Assert.False(red.Contains(1));
        Assert.False(red.Overlaps(blue));
        Assert.False(NetworkGroups.None.Overlaps(NetworkGroups.None));
        Assert.True(red.With(63).Overlaps(blue));
        Assert.Equal(red.With(63), blue.With(0));
        Assert.True(red.Without(0).IsEmpty);
        Assert.Equal(red, red.Without(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkGroups.Of(64));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkGroups.Of(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => red.With(64));
    }

    [Fact]
    public void GroupsAreStoredOnlyWhenSet()
    {
        GameObject go = CreateGameObject();
        go.Network.Mode = NetworkMode.Object;
        EchoObject plain = Serializer.Serialize(go);
        Assert.False(plain["Network"].TryGet("Groups", out _));

        go.Network.Groups = NetworkGroups.Of(3).With(63);
        EchoObject stored = Serializer.Serialize(go);
        GameObject copy = Serializer.Deserialize<GameObject>(stored)!;
        Assert.Equal(NetworkGroups.Of(3).With(63), copy.Network.Groups);
        copy.Dispose();

        GameObject only = CreateGameObject();
        only.Network.Groups = NetworkGroups.Of(1);
        GameObject reset = Serializer.Deserialize<GameObject>(Serializer.Serialize(only))!;
        Assert.Equal(NetworkGroups.Of(1), reset.Network.Groups);
        NetworkSerialization.Read(reset, Serializer.Serialize(CreateGameObject()));
        Assert.True(reset.Network.Groups.IsEmpty);
        reset.Dispose();
    }

    [Fact]
    public void AGroupedObjectReachesOnlyClientsSharingOneOfItsGroups()
    {
        using VisibilitySession session = VisibilityHost();
        NetworkInterest interest = Networking.Networking.Interest!;
        interest.For(session.AliceHere).Groups = NetworkGroups.Of(1);
        GameObject red = Crate(session.World, NetworkVisibility.Default, "Red");
        red.Network.Groups = NetworkGroups.Of(1);
        GameObject blue = Crate(session.World, NetworkVisibility.Default, "Blue");
        blue.Network.Groups = NetworkGroups.Of(2);
        GameObject plain = Crate(session.World, NetworkVisibility.Default, "Plain");
        red.Network.Spawn();
        blue.Network.Spawn();
        plain.Network.Spawn();

        Assert.True(red.Network.IsVisibleTo(session.AliceHere));
        Assert.False(blue.Network.IsVisibleTo(session.AliceHere));
        Assert.True(plain.Network.IsVisibleTo(session.BobHere));
        Assert.False(red.Network.IsVisibleTo(session.BobHere));
        Assert.False(blue.Network.IsVisibleTo(session.BobHere));
        Pump(() => SpawnsOf(session.Alice, red) == 1 && SpawnsOf(session.Bob, plain) == 1, session.Alice, session.Bob);
        Assert.False(Mentions(session.Alice, blue.Identifier));
        Assert.False(Mentions(session.Bob, red.Identifier, blue.Identifier));

        interest.For(session.BobHere).Groups = NetworkGroups.Of(2);
        Assert.True(blue.Network.IsVisibleTo(session.BobHere));
        Pump(() => SpawnsOf(session.Bob, blue) == 1, session.Alice, session.Bob);

        red.Network.Groups = NetworkGroups.Of(2);
        Assert.False(red.Network.IsVisibleTo(session.AliceHere));
        Assert.True(red.Network.IsVisibleTo(session.BobHere));
        Pump(() => DespawnsOf(session.Alice, red) == 1 && SpawnsOf(session.Bob, red) == 1, session.Alice, session.Bob);

        interest.For(session.BobHere).Groups = NetworkGroups.None;
        Assert.False(red.Network.IsVisibleTo(session.BobHere));
        Assert.True(plain.Network.IsVisibleTo(session.BobHere));
        Pump(() => DespawnsOf(session.Bob, red) == 1 && DespawnsOf(session.Bob, blue) == 1, session.Alice, session.Bob);
    }

    [Fact]
    public void OwnersShownPeersAndSpectatorsIgnoreGroups()
    {
        using VisibilitySession session = VisibilityHost();
        NetworkInterest interest = Networking.Networking.Interest!;
        GameObject flag = Crate(session.World, NetworkVisibility.Default, "Flag");
        flag.Network.Groups = NetworkGroups.Of(4);
        flag.Network.Spawn(session.AliceHere);
        Assert.True(flag.Network.IsVisibleTo(session.AliceHere));
        Assert.False(flag.Network.IsVisibleTo(session.BobHere));

        interest.For(session.BobHere).SeesEverything = true;
        Assert.True(flag.Network.IsVisibleTo(session.BobHere));
        interest.For(session.BobHere).SeesEverything = false;
        Assert.False(flag.Network.IsVisibleTo(session.BobHere));

        flag.Network.ShowTo(session.BobHere);
        Assert.True(flag.Network.IsVisibleTo(session.BobHere));
        flag.Network.ClearVisibilityOverride(session.BobHere);
        Assert.False(flag.Network.IsVisibleTo(session.BobHere));

        GameObject secret = Crate(session.World, NetworkVisibility.Manual, "Secret");
        secret.Network.Groups = NetworkGroups.Of(4);
        secret.Network.Spawn();
        interest.For(session.BobHere).Groups = NetworkGroups.Of(4);
        Assert.False(secret.Network.IsVisibleTo(session.BobHere));
        Assert.True(flag.Network.IsVisibleTo(session.BobHere));
    }

    [Fact]
    public void GroupsFilterBeforeDistance()
    {
        using InterestSession session = InterestHost();
        session.AliceSees.SetViewpoints([Float3.Zero]);
        session.AliceSees.Groups = NetworkGroups.Of(1);
        GameObject near = CrateAt(session.World, 5, name: "Near");
        near.Network.Groups = NetworkGroups.Of(2);
        GameObject ours = CrateAt(session.World, 5, name: "Ours");
        ours.Network.Groups = NetworkGroups.Of(1);
        GameObject far = CrateAt(session.World, 500, name: "Far");
        far.Network.Groups = NetworkGroups.Of(1);
        near.Network.Spawn();
        ours.Network.Spawn();
        far.Network.Spawn();

        PumpFor(100, session.Alice, session.Bob);
        Assert.False(near.Network.IsVisibleTo(session.AliceHere));
        Assert.True(ours.Network.IsVisibleTo(session.AliceHere));
        Assert.False(far.Network.IsVisibleTo(session.AliceHere));
        Assert.False(Mentions(session.Alice, near.Identifier, far.Identifier));
    }

    [Fact]
    public void ALinkToAGroupedObjectNeverNamesIt()
    {
        GameObject go = CreateGameObject("Grouped");
        go.Network.Mode = NetworkMode.Object;
        Assert.False(NetworkSerialization.IsSecret(go));
        go.Network.Groups = NetworkGroups.Of(7);
        Assert.True(NetworkSerialization.IsSecret(go));
        go.Dispose();
    }

    [Fact]
    public void DistanceInterestMatchesEveryObjectsRangeWhileThingsMove()
    {
        using InterestSession session = InterestHost(c =>
        {
            c.InterestHysteresis = 0;
            c.InterestLingerSeconds = 0;
        });
        Random random = new(7);
        List<GameObject> crates = new();
        for (int i = 0; i < 120; i++)
        {
            GameObject crate = CrateAt(session.World, 0, name: $"Crate{i}");
            if (i % 10 == 0)
            {
                crate.Network.CullDistance = 40;
            }

            crates.Add(crate);
        }

        GameObject distant = CrateAt(session.World, 1e12f, name: "Distant");
        crates.Add(distant);
        foreach (GameObject crate in crates)
        {
            crate.Network.Spawn();
        }

        Float3 RandomPoint() => new((float)(random.NextDouble() * 120 - 60), (float)(random.NextDouble() * 40 - 20),
            (float)(random.NextDouble() * 120 - 60));

        bool Matches(Float3[] viewpoints)
        {
            foreach (GameObject crate in crates)
            {
                float range = crate.Network.CullDistance > 0 ? crate.Network.CullDistance : 10;
                bool expected = viewpoints.Any(v => Float3.LengthSquared(crate.Transform.Position - v) <= range * range);
                if (crate.Network.IsVisibleTo(session.AliceHere) != expected)
                {
                    return false;
                }
            }

            return true;
        }

        for (int round = 0; round < 6; round++)
        {
            Float3[] viewpoints = round == 5 ? [RandomPoint(), new Float3(1e12f, 0, 0)] : [RandomPoint(), RandomPoint()];
            session.AliceSees.SetViewpoints(viewpoints);
            foreach (GameObject crate in crates)
            {
                if (crate != distant)
                {
                    crate.Transform.Position = RandomPoint();
                }
            }

            Pump(() => Matches(viewpoints), session.Alice, session.Bob);
        }

        Assert.True(distant.Network.IsVisibleTo(session.AliceHere));
    }
}
