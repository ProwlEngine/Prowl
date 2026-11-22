// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Identifiers stay unique within a section: an object or component arriving with one the section already holds
/// gets a fresh one, and nothing else does.
/// </summary>
public class SectionIdentifierTests : RuntimeTestBase
{
    private sealed class Tag : Component
    {
        public static Action<Tag, string>? Hook;
        public override void OnAddedToScene() => Hook?.Invoke(this, "added");
    }

    public override void Dispose()
    {
        Tag.Hook = null;
        base.Dispose();
    }


    private readonly Scene _world;
    private readonly SceneSection _other;

    public SectionIdentifierTests()
    {
        _world = CreateScene();
        _other = _world.CreateSection("Other");
    }

    // An object in the active section wearing the identifier of one in the other section, which is allowed.
    private (GameObject Holder, GameObject Mover, Guid Shared) Twins()
    {
        GameObject holder = _world.Create(_other, "Holder");
        GameObject mover = _world.Create("Mover");
        mover.SetIdentifier(holder.Identifier);
        return (holder, mover, holder.Identifier);
    }


    [Fact]
    public void AMoveWithNoClash_KeepsEveryIdentifier_ThereAndBack()
    {
        GameObject mover = _world.Create("Mover");
        GameObject child = _world.Create("Child", mover);
        Tag tag = child.AddComponent<Tag>();
        _world.Create(_other, "Bystander").AddComponent<Tag>();
        (Guid, Guid, Guid) before = (mover.Identifier, child.Identifier, tag.Identifier);

        mover.MoveTo(_other);
        mover.MoveTo(_world.ActiveSection);
        mover.MoveTo(_other);

        Assert.Equal(before, (mover.Identifier, child.Identifier, tag.Identifier));
    }

    [Fact]
    public void AClashDeepInEitherTree_RenewsOnlyTheClashingObject()
    {
        GameObject holderChild = _world.Create("HolderChild", _world.Create("HolderParent", _world.Create(_other, "Holder")));
        GameObject mover = _world.Create("Mover");
        GameObject moverChild = _world.Create("MoverChild", mover);
        moverChild.SetIdentifier(holderChild.Identifier);
        Guid moverId = mover.Identifier, shared = holderChild.Identifier;

        mover.MoveTo(_other);

        Assert.Equal(moverId, mover.Identifier);
        Assert.Equal(shared, holderChild.Identifier);
        Assert.NotEqual(shared, moverChild.Identifier);
    }

    [Fact]
    public void Components_AreCheckedToo_AgainstObjectsAndComponents()
    {
        GameObject holder = _world.Create(_other, "Holder");
        Tag held = holder.AddComponent<Tag>();
        GameObject mover = _world.Create("Mover");
        Tag sameAsComponent = mover.AddComponent<Tag>();
        Tag sameAsObject = mover.AddComponent<Tag>();
        Tag free = mover.AddComponent<Tag>();
        sameAsComponent.Identifier = held.Identifier;
        sameAsObject.Identifier = holder.Identifier;
        GameObject child = _world.Create("Child", mover);
        child.SetIdentifier(held.Identifier);
        Guid freeId = free.Identifier;

        mover.MoveTo(_other);

        Assert.NotEqual(held.Identifier, sameAsComponent.Identifier);
        Assert.NotEqual(holder.Identifier, sameAsObject.Identifier);
        Assert.NotEqual(held.Identifier, child.Identifier);
        Assert.Equal(freeId, free.Identifier);
    }

    [Fact]
    public void AHolderThatIsGone_NoLongerCounts()
    {
        // Disposed, before the world has flushed it.
        (GameObject holder, GameObject mover, Guid shared) = Twins();
        holder.Dispose();
        mover.MoveTo(_other);
        Assert.Equal(shared, mover.Identifier);

        // Moved out of the section.
        (holder, mover, shared) = Twins();
        holder.MoveTo(_world.ActiveSection);
        Assert.NotEqual(shared, holder.Identifier);
        Guid moverId = mover.Identifier;
        mover.MoveTo(_other);
        Assert.Equal(moverId, mover.Identifier);

        // Parented under a root of another section.
        GameObject elsewhere = _world.Create(_world.CreateSection("Third"), "Elsewhere");
        (holder, mover, shared) = Twins();
        holder.SetParent(elsewhere);
        mover.MoveTo(_other);
        Assert.Equal(shared, mover.Identifier);

        // Taken out of the world.
        (holder, mover, shared) = Twins();
        _world.Remove(holder);
        mover.MoveTo(_other);
        Assert.Equal(shared, mover.Identifier);

        // Given another identifier.
        (holder, mover, shared) = Twins();
        holder.SetIdentifier(Guid.NewGuid());
        mover.MoveTo(_other);
        Assert.Equal(shared, mover.Identifier);
    }

    [Fact]
    public void AHolderQueuedForDestroy_StillCounts_UntilItIsDisposed()
    {
        (GameObject holder, GameObject mover, Guid shared) = Twins();
        holder.Destroy();
        Assert.False(holder.IsDisposed);

        mover.MoveTo(_other);

        Assert.NotEqual(shared, mover.Identifier);
    }

    [Fact]
    public void ARemovedOrDisposedComponent_NoLongerCounts_AndAnAddedOneDoes()
    {
        GameObject holder = _world.Create(_other, "Holder");
        Tag removed = holder.AddComponent<Tag>();
        Tag disposed = holder.AddComponent<Tag>();
        Tag added = holder.AddComponent<Tag>();
        GameObject mover = _world.Create("Mover");
        Tag a = mover.AddComponent<Tag>(), b = mover.AddComponent<Tag>(), c = mover.AddComponent<Tag>();
        (a.Identifier, b.Identifier, c.Identifier) = (removed.Identifier, disposed.Identifier, added.Identifier);
        Guid removedId = removed.Identifier, disposedId = disposed.Identifier;
        holder.RemoveComponent(removed);
        disposed.Dispose();

        mover.MoveTo(_other);

        Assert.Equal(removedId, a.Identifier);
        Assert.Equal(disposedId, b.Identifier);
        Assert.NotEqual(added.Identifier, c.Identifier);
    }

    [Fact]
    public void AnIdentifierSetWhileInTheSection_Counts()
    {
        GameObject holder = _world.Create(_other, "Holder");
        Tag held = holder.AddComponent<Tag>();
        GameObject mover = _world.Create("Mover");
        Tag tag = mover.AddComponent<Tag>();
        Guid objectId = Guid.NewGuid(), componentId = Guid.NewGuid();
        mover.SetIdentifier(objectId);
        tag.Identifier = componentId;
        holder.SetIdentifier(objectId);
        held.Identifier = componentId;

        mover.MoveTo(_other);

        Assert.NotEqual(objectId, mover.Identifier);
        Assert.NotEqual(componentId, tag.Identifier);
    }

    [Fact]
    public void ThreeHoldersOfOneIdentifier_AreEachSeenInTheirOwnSection()
    {
        SceneSection third = _world.CreateSection("Third");
        GameObject first = _world.Create("First");
        GameObject second = _world.Create(_other, "Second");
        GameObject last = _world.Create(third, "Last");
        Guid shared = first.Identifier;
        second.SetIdentifier(shared);
        last.SetIdentifier(shared);
        GameObject mover = _world.Create(_world.CreateSection("Fourth"), "Mover");
        mover.SetIdentifier(shared);

        mover.MoveTo(third);

        Assert.NotEqual(shared, mover.Identifier);
        Assert.Equal((shared, shared, shared), (first.Identifier, second.Identifier, last.Identifier));

        // The one that joined first leaves, and the others still hold it where they are.
        first.Dispose();
        GameObject again = _world.Create("Again");
        again.SetIdentifier(shared);
        again.MoveTo(_other);
        Assert.NotEqual(shared, again.Identifier);
    }

    [Fact]
    public void ParentingIntoAWorld_OrAcrossSections_RenewsAClash()
    {
        GameObject holder = _world.Create(_other, "Holder");
        Guid shared = holder.Identifier;

        var detached = new GameObject("Detached");
        Tag tag = detached.AddComponent<Tag>();
        Guid tagId = tag.Identifier;
        detached.SetIdentifier(shared);
        detached.SetParent(holder);
        Assert.NotEqual(shared, detached.Identifier);
        Assert.Equal(tagId, tag.Identifier);
        Assert.Same(_world, detached.Scene);

        GameObject crossing = _world.Create("Crossing");
        crossing.SetIdentifier(shared);
        crossing.SetParent(holder);
        Assert.NotEqual(shared, crossing.Identifier);

        // Within one section nothing is checked, so a twin made in place stays as it is.
        GameObject sibling = _world.Create(_other, "Sibling");
        sibling.SetIdentifier(shared);
        sibling.SetParent(holder);
        Assert.Equal(shared, sibling.Identifier);
    }

    [Fact]
    public void AnOwnedRoot_FollowingItsOwner_IsCheckedToo()
    {
        GameObject holder = _world.Create(_other, "Holder");
        GameObject character = _world.Create("Character");
        GameObject helper = _world.Create("Helper", owner: character.AddComponent<Tag>());
        helper.SetIdentifier(holder.Identifier);

        character.MoveTo(_other);

        Assert.Same(_other, helper.Section);
        Assert.NotEqual(holder.Identifier, helper.Identifier);
    }

    [Fact]
    public void MovingToAnotherWorld_ChecksTheSectionItLandsIn()
    {
        (GameObject holder, GameObject mover, Guid shared) = Twins();
        Scene to = CreateScene();
        GameObject there = to.Create("There");
        there.SetIdentifier(shared);
        SceneSection side = to.CreateSection("Side");

        mover.MoveTo(side);
        Assert.Equal(shared, mover.Identifier);

        holder.MoveTo(to);
        Assert.NotEqual(shared, holder.Identifier);
        Assert.Equal(shared, there.Identifier);
    }

    [Fact]
    public void AMoveMadeWhileATreeIsStillJoining_SeesThePartsThatHaveNotJoinedYet()
    {
        var root = new GameObject("Root");
        Tag first = root.AddComponent<Tag>();
        PlainComponent later = root.AddComponent<PlainComponent>();
        var child = new GameObject("Child");
        child.SetParent(root);
        GameObject likeTheChild = _world.Create("LikeTheChild"), likeTheComponent = _world.Create("LikeTheComponent");
        likeTheChild.SetIdentifier(child.Identifier);
        likeTheComponent.SetIdentifier(later.Identifier);
        Tag.Hook = (tag, step) =>
        {
            if (step != "added" || !ReferenceEquals(tag, first)) return;
            likeTheChild.MoveTo(_other);
            likeTheComponent.MoveTo(_other);
        };

        _world.Add(root, _other);

        Assert.NotEqual(child.Identifier, likeTheChild.Identifier);
        Assert.NotEqual(later.Identifier, likeTheComponent.Identifier);
    }

    [Fact]
    public void AChildAFillInPlaceAdded_Counts_BeforeItHasJoinedTheWorld()
    {
        GameObject copied = _world.Create(_other, "Copied"), filled = _world.Create(_other, "Filled");
        var source = new GameObject("Source");
        new GameObject("Extra").SetParent(source);

        ObjectCopy.CopyTo(source, copied);
        GameObject.PreservingIdentifiersWhile(() => { Serializer.DeserializeInto(Serializer.Serialize(source, new SerializationContext()), filled, new SerializationContext()); return 0; });
        GameObject added = Assert.Single(copied.Children), read = Assert.Single(filled.Children);
        Assert.False(added.IsInWorld);

        foreach (GameObject unjoined in new[] { added, read })
        {
            GameObject mover = _world.Create("Mover");
            mover.SetIdentifier(unjoined.Identifier);
            mover.MoveTo(_other);
            Assert.NotEqual(unjoined.Identifier, mover.Identifier);
        }
    }

    [Fact]
    public void AfterAHotReload_TheSectionsIdentifiersAreStillSeen()
    {
        (GameObject holder, GameObject mover, Guid shared) = Twins();
        Tag held = holder.AddComponent<Tag>();
        Tag tag = mover.AddComponent<Tag>();
        tag.Identifier = held.Identifier;

        _world.OnHotReload();
        mover.MoveTo(_other);

        Assert.NotEqual(shared, mover.Identifier);
        Assert.NotEqual(held.Identifier, tag.Identifier);
    }

    [Fact]
    public void AnIdentifierReadIntoALiveObjectOrComponent_Counts()
    {
        GameObject source = _world.Create("Source");
        Tag sourceTag = source.AddComponent<Tag>();
        GameObject filled = _world.Create(_other, "Filled");
        Tag filledTag = filled.AddComponent<Tag>();
        var context = new SerializationContext();
        EchoObject data = Serializer.Serialize(source, context);
        EchoObject tagData = Serializer.Serialize(sourceTag, new SerializationContext());

        GameObject.PreservingIdentifiersWhile(() => { Serializer.DeserializeInto(tagData, filledTag, new SerializationContext()); return 0; });
        Assert.Equal(sourceTag.Identifier, filledTag.Identifier);
        GameObject first = _world.Create("First");
        Tag firstTag = first.AddComponent<Tag>();
        firstTag.Identifier = sourceTag.Identifier;
        first.MoveTo(_other);
        Assert.NotEqual(sourceTag.Identifier, firstTag.Identifier);

        GameObject.PreservingIdentifiersWhile(() => { Serializer.DeserializeInto(data, filled, new SerializationContext()); return 0; });
        Assert.Equal(source.Identifier, filled.Identifier);
        Guid shared = source.Identifier;
        source.MoveTo(_other);
        Assert.NotEqual(shared, source.Identifier);
    }

    [Fact]
    public void AnUnloadedSectionsObjects_NoLongerCount()
    {
        Scene world = CreateScene(enable: true);
        Bind(world);
        SceneSection level = world.CreateSection("Level");
        GameObject holder = world.Create(level, "Holder");
        Guid shared = holder.Identifier;
        level.Unload();
        Scene.ProcessPendingLoad();
        Assert.False(level.IsLoaded);

        SceneSection next = world.CreateSection("Next");
        GameObject mover = world.Create("Mover");
        mover.SetIdentifier(shared);
        mover.MoveTo(next);

        Assert.Equal(shared, mover.Identifier);
    }

    [Fact]
    public void JoiningASection_AllocatesNothingForWhatTheSectionHolds()
    {
        GameObject parent = _world.Create(_other, "Parent");
        for (int i = 0; i < 2000; i++) _world.Create("Filler", parent).AddComponent<Tag>();
        GameObject mover = _world.Create("Mover");
        SceneSection home = _world.ActiveSection;

        mover.MoveTo(_other);
        mover.MoveTo(home);
        long before = GC.GetAllocatedBytesForCurrentThread();
        mover.MoveTo(_other);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        // A set of the section's 4,001 identifiers is over 100,000 bytes.
        Assert.True(bytes < 4096, $"Moving one object into a section of 2,001 objects allocated {bytes} bytes.");
    }
}
