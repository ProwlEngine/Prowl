// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo.Cloning;

namespace Prowl.Echo.Test;

public class Cloning_Tests
{
    #region Types

    private class Node
    {
        public string Name = "";
        public int Value;
        public Node? Child;
        public Node? Peer;
    }

    private class Holder
    {
        public Node? A;
        public Node? B;
        public List<Node> Items = [];
        public Node[]? Array;
    }

    [CloneBehavior(CloneBehavior.Reference)]
    private class SharedThing
    {
        public int Value;
    }

    private class ReferenceHolder
    {
        public SharedThing? Shared;
        public Node? Owned;
    }

    private class FieldBehaviour
    {
        [CloneBehavior(CloneBehavior.Reference)]
        public Node? NotMine;
        public Node? Mine;
    }

    private class Identified
    {
        [CloneField(CloneFieldFlags.IdentityRelevant)]
        public Guid Id = Guid.NewGuid();
        public string Name = "";
    }

    private class Flagged
    {
        [CloneField(CloneFieldFlags.Skip)]
        public int Skipped;

        [SerializeIgnore]
        public int NotSerialized;

        [SerializeIgnore]
        [CloneField(CloneFieldFlags.DontSkip)]
        public int NotSerializedButCloned;

        private int _private;
        public readonly int ReadOnly;

        public void SetPrivate(int v) => _private = v;
        public int GetPrivate() => _private;

        public Flagged() { }
        public Flagged(int readOnlyValue) => ReadOnly = readOnlyValue;
    }

    private struct StructWithReference
    {
        public int Number;
        public Node? Node;
    }

    private class StructHolder
    {
        public StructWithReference Value;
        public Node? Direct;
    }

    private class Bag
    {
        public Dictionary<Node, int> ByReference = [];
        public Dictionary<string, Node?> ByString = [];
        public Dictionary<string, int>? CaseInsensitive;
        public HashSet<Node> Set = [];
    }

    private class Handler
    {
        public Action? OnThing;
        public int Received;
        public void Handle() => Received++;
    }

    private class ReadOnlyHolder
    {
        public readonly List<int> Numbers = [];
    }

    private class Counted : ICloneCallbackReceiver
    {
        public Counted? X;
        public Counted? Y;
        public static int Copies;

        public void OnBeforeClone(CloneContext context) => Copies++;
        public void OnAfterClone(CloneContext context) { }
    }

    private class MultiDimensional { public int[,]? Grid; }

    private class Jagged { public int[][]? Rows; public Node[][]? Refs; }

    private struct ItemWithReference { public int Number; public Node? Node; }

    private class StructArrayHolder { public ItemWithReference[]? Items; }

    private class Base { public int A; }

    private class Derived : Base { public int B; }

    private class NoDefaultConstructor
    {
        public int Value;
        public NoDefaultConstructor(int value) => Value = value;
    }

    private class CallbackObject : ICloneCallbackReceiver
    {
        public int Value;
        public static int BeforeCount;
        public static int AfterCount;

        public void OnBeforeClone(CloneContext context) => BeforeCount++;
        public void OnAfterClone(CloneContext context) => AfterCount++;
    }

    private class ExplicitObject : ICloneExplicit
    {
        [ManuallyCloned]
        public Node? Handled;
        public int Plain;
        public static int SetupCalls;

        public void SetupCloneTargets(object target, ICloneSetup setup)
        {
            SetupCalls++;
            var typed = (ExplicitObject)target;
            setup.HandleObject(Handled, typed.Handled, CloneBehavior.ChildObject);
        }

        public void CopyCloneTo(object target, ICloneOperation operation)
        {
            var typed = (ExplicitObject)target;
            typed.Plain = Plain + 100;
            typed.Handled = (Node?)operation.GetTarget(Handled);
            operation.HandleObject(Handled, typed.Handled);
        }
    }

    private class HybridExplicit : ICloneExplicit
    {
        public int Plain;
        public string Text = "";

        [ManuallyCloned]
        public Node? Special;

        public void SetupCloneTargets(object target, ICloneSetup setup)
        {
            var typed = (HybridExplicit)target;
            setup.HandleObject(this, target);
            setup.HandleObject(Special, typed.Special);
        }

        public void CopyCloneTo(object target, ICloneOperation operation)
        {
            var typed = (HybridExplicit)target;
            operation.HandleObject(this, target);
            typed.Special = (Node?)operation.GetTarget(Special);
            operation.HandleObject(Special, typed.Special);
        }
    }

    #endregion

    #region Basics

    [Fact]
    public void Clone_ProducesADifferentInstanceWithTheSameValues()
    {
        var source = new Node { Name = "root", Value = 7 };
        Node clone = Cloner.Clone(source);

        Assert.NotSame(source, clone);
        Assert.Equal("root", clone.Name);
        Assert.Equal(7, clone.Value);
    }

    [Fact]
    public void Clone_OwnedChildrenAreCopied()
    {
        var source = new Node { Name = "root", Child = new Node { Name = "child" } };
        Node clone = Cloner.Clone(source);

        Assert.NotSame(source.Child, clone.Child);
        Assert.Equal("child", clone.Child!.Name);
    }

    [Fact]
    public void Clone_NullStaysNull()
    {
        var source = new Node { Name = "root" };
        Node clone = Cloner.Clone(source);
        Assert.Null(clone.Child);
    }

    #endregion

    #region References

    [Fact]
    public void Clone_ReferenceWithinTheGraphPointsAtTheCopy()
    {
        var child = new Node { Name = "child" };
        var source = new Holder { A = child, B = child };

        Holder clone = Cloner.Clone(source);

        Assert.NotSame(child, clone.A);
        Assert.Same(clone.A, clone.B);
    }

    [Fact]
    public void Clone_DeeperReferenceWithinTheGraphPointsAtTheCopy()
    {
        var child = new Node { Name = "child" };
        var root = new Node { Name = "root", Child = child };
        child.Peer = root;

        Node clone = Cloner.Clone(root);

        Assert.NotSame(root, clone);
        Assert.NotSame(child, clone.Child);
        Assert.Same(clone, clone.Child!.Peer);
    }

    [Fact]
    public void Clone_ReferenceBehaviourTypeIsShared()
    {
        var shared = new SharedThing { Value = 5 };
        var source = new ReferenceHolder { Shared = shared, Owned = new Node { Name = "owned" } };

        ReferenceHolder clone = Cloner.Clone(source);

        Assert.Same(shared, clone.Shared);
        Assert.NotSame(source.Owned, clone.Owned);
    }

    [Fact]
    public void Clone_ReferenceBehaviourOnAFieldIsShared()
    {
        var node = new Node { Name = "n" };
        var source = new FieldBehaviour { NotMine = node, Mine = new Node { Name = "m" } };

        FieldBehaviour clone = Cloner.Clone(source);

        Assert.Same(node, clone.NotMine);
        Assert.NotSame(source.Mine, clone.Mine);
    }

    [Fact]
    public void Clone_SelfReferenceDoesNotLoop()
    {
        var source = new Node { Name = "self" };
        source.Child = source;

        Node clone = Cloner.Clone(source);

        Assert.NotSame(source, clone);
        Assert.Same(clone, clone.Child);
    }

    [Fact]
    public void Clone_MutualReferenceDoesNotLoop()
    {
        var a = new Node { Name = "a" };
        var b = new Node { Name = "b" };
        a.Child = b;
        b.Child = a;

        Node clone = Cloner.Clone(a);

        Assert.Same(clone, clone.Child!.Child);
    }

    #endregion

    #region CopyTo

    [Fact]
    public void CopyTo_KeepsTheTargetInstance()
    {
        var source = new Node { Name = "source", Value = 3 };
        var target = new Node { Name = "target" };
        Node original = target;

        Cloner.CopyTo(source, target);

        Assert.Same(original, target);
        Assert.Equal("source", target.Name);
        Assert.Equal(3, target.Value);
    }

    [Fact]
    public void CopyTo_KeepsNestedTargetInstances()
    {
        var source = new Node { Name = "s", Child = new Node { Name = "sc", Value = 9 } };
        var target = new Node { Name = "t", Child = new Node { Name = "tc" } };
        Node targetChild = target.Child!;

        Cloner.CopyTo(source, target);

        Assert.Same(targetChild, target.Child);
        Assert.Equal("sc", target.Child!.Name);
        Assert.Equal(9, target.Child.Value);
    }

    [Fact]
    public void CopyTo_ReferenceIntoTheTargetIsRewrittenToTheTarget()
    {
        var sourceChild = new Node { Name = "sc" };
        var source = new Holder { A = sourceChild, B = sourceChild };

        var targetChild = new Node { Name = "tc" };
        var target = new Holder { A = targetChild, B = targetChild };

        Cloner.CopyTo(source, target);

        Assert.Same(targetChild, target.A);
        Assert.Same(target.A, target.B);
        Assert.Equal("sc", target.A!.Name);
    }

    [Fact]
    public void CopyTo_CreatesWhatTheTargetLacks()
    {
        var source = new Node { Name = "s", Child = new Node { Name = "sc" } };
        var target = new Node { Name = "t" };

        Cloner.CopyTo(source, target);

        Assert.NotNull(target.Child);
        Assert.NotSame(source.Child, target.Child);
        Assert.Equal("sc", target.Child!.Name);
    }

    [Fact]
    public void CopyTo_ClearsWhatTheSourceLacks()
    {
        var source = new Node { Name = "s" };
        var target = new Node { Name = "t", Child = new Node { Name = "tc" } };

        Cloner.CopyTo(source, target);

        Assert.Null(target.Child);
    }

    [Fact]
    public void CopyTo_ReplacesATargetOfTheWrongType()
    {
        var source = new Holder { A = new Node { Name = "sa" } };
        var target = new Holder();

        Cloner.CopyTo(source, target);

        Assert.NotNull(target.A);
        Assert.Equal("sa", target.A!.Name);
    }

    #endregion

    #region Seeded targets

    [Fact]
    public void AddTarget_SendsReferencesToTheSeededObject()
    {
        var sourceChild = new Node { Name = "sc", Value = 4 };
        var source = new Holder { A = sourceChild, B = sourceChild };

        var targetChild = new Node { Name = "tc" };
        var target = new Holder();

        var context = new CloneContext();
        context.AddTarget(sourceChild, targetChild);

        Cloner.CopyTo(source, target, context);

        Assert.Same(targetChild, target.A);
        Assert.Same(targetChild, target.B);
        Assert.Equal("sc", targetChild.Name);
        Assert.Equal(4, targetChild.Value);
    }

    [Fact]
    public void AddTarget_MatchedByIdentityRatherThanPosition()
    {
        var first = new Node { Name = "first" };
        var second = new Node { Name = "second" };
        var source = new Holder { A = first, B = second };

        var targetForSecond = new Node { Name = "existing" };
        var target = new Holder();

        var context = new CloneContext();
        context.AddTarget(second, targetForSecond);

        Cloner.CopyTo(source, target, context);

        Assert.Same(targetForSecond, target.B);
        Assert.Equal("second", target.B!.Name);
        Assert.NotSame(targetForSecond, target.A);
    }

    [Fact]
    public void AddTarget_SeededObjectsOwnContentsAreStillWalked()
    {
        var sourceChild = new Node { Name = "sc", Child = new Node { Name = "grandchild" } };
        var source = new Holder { A = sourceChild };

        var targetChild = new Node { Name = "tc" };
        var target = new Holder();

        var context = new CloneContext();
        context.AddTarget(sourceChild, targetChild);

        Cloner.CopyTo(source, target, context);

        Assert.Same(targetChild, target.A);
        Assert.Equal("sc", targetChild.Name);
        Assert.NotNull(targetChild.Child);
        Assert.NotSame(sourceChild.Child, targetChild.Child);
        Assert.Equal("grandchild", targetChild.Child!.Name);
    }

    [Fact]
    public void AddTarget_SeededObjectKeepsItsExistingChildInstance()
    {
        var sourceChild = new Node { Name = "sc", Child = new Node { Name = "sg" } };
        var source = new Holder { A = sourceChild };

        var targetGrandchild = new Node { Name = "tg" };
        var targetChild = new Node { Name = "tc", Child = targetGrandchild };
        var target = new Holder { A = targetChild };

        var context = new CloneContext();
        context.AddTarget(sourceChild, targetChild);

        Cloner.CopyTo(source, target, context);

        Assert.Same(targetGrandchild, targetChild.Child);
        Assert.Equal("sg", targetGrandchild.Name);
    }

    #endregion

    #region Multiple roots

    [Fact]
    public void CloneAll_ReferenceBetweenRootsPointsAtTheOtherCopy()
    {
        var a = new Node { Name = "a" };
        var b = new Node { Name = "b" };
        a.Peer = b;
        b.Peer = a;

        List<Node> clones = Cloner.CloneAll([a, b]);

        Assert.Equal(2, clones.Count);
        Assert.NotSame(a, clones[0]);
        Assert.NotSame(b, clones[1]);
        Assert.Same(clones[1], clones[0].Peer);
        Assert.Same(clones[0], clones[1].Peer);
    }

    [Fact]
    public void Clone_SeparateOperationsDoNotShareAMap()
    {
        var a = new Node { Name = "a" };
        var b = new Node { Name = "b" };
        a.Peer = b;

        Node cloneA = Cloner.Clone(a);
        Node cloneB = Cloner.Clone(b);

        Assert.NotSame(cloneB, cloneA.Peer);
    }

    [Fact]
    public void Clone_RejectsCloningItsOwnResult()
    {
        var context = new CloneContext();
        var source = new Node { Name = "a" };
        Node clone = Cloner.Clone(source, context);

        Assert.Throws<InvalidOperationException>(() => Cloner.Clone(clone, context));
    }

    #endregion

    #region Identity

    [Fact]
    public void CopyTo_IdentityRelevantFieldIsLeftAlone()
    {
        var source = new Identified { Name = "source" };
        var target = new Identified { Name = "target" };
        Guid targetId = target.Id;

        Cloner.CopyTo(source, target);

        Assert.Equal(targetId, target.Id);
        Assert.Equal("source", target.Name);
    }

    [Fact]
    public void Clone_IdentityRelevantFieldIsCopiedWhenIdentityIsNotPreserved()
    {
        var source = new Identified { Name = "source" };
        Identified clone = Cloner.Clone(source, new CloneContext { PreserveIdentity = false });

        Assert.Equal(source.Id, clone.Id);
    }

    [Fact]
    public void Clone_IdentityRelevantFieldIsNotCopiedByDefault()
    {
        var source = new Identified { Name = "source" };
        Identified clone = Cloner.Clone(source);

        Assert.NotEqual(source.Id, clone.Id);
    }

    #endregion

    #region Field rules

    [Fact]
    public void Clone_HonoursTheFieldFlags()
    {
        var source = new Flagged
        {
            Skipped = 1,
            NotSerialized = 2,
            NotSerializedButCloned = 3
        };
        source.SetPrivate(4);

        Flagged clone = Cloner.Clone(source);

        Assert.Equal(0, clone.Skipped);
        Assert.Equal(0, clone.NotSerialized);
        Assert.Equal(3, clone.NotSerializedButCloned);
        Assert.Equal(4, clone.GetPrivate());
    }

    [Fact]
    public void Clone_ReadOnlyFieldIsCopied()
    {
        var source = new Flagged(42);
        Flagged clone = Cloner.Clone(source);
        Assert.Equal(42, clone.ReadOnly);
    }

    [Fact]
    public void Clone_ReadOnlyCollectionContentsAreCopied()
    {
        var source = new ReadOnlyHolder();
        source.Numbers.Add(5);

        ReadOnlyHolder clone = Cloner.Clone(source);

        Assert.Single(clone.Numbers);
        Assert.Equal(5, clone.Numbers[0]);
        Assert.NotSame(source.Numbers, clone.Numbers);
    }

    [Fact]
    public void Clone_TypeWithoutADefaultConstructor()
    {
        var source = new NoDefaultConstructor(11);
        NoDefaultConstructor clone = Cloner.Clone(source);

        Assert.NotSame(source, clone);
        Assert.Equal(11, clone.Value);
    }

    #endregion

    #region Collections

    [Fact]
    public void Clone_ArrayElementsAreCopied()
    {
        var shared = new Node { Name = "shared" };
        var source = new Holder { Array = [shared, new Node { Name = "other" }, shared] };

        Holder clone = Cloner.Clone(source);

        Assert.Equal(3, clone.Array!.Length);
        Assert.NotSame(shared, clone.Array[0]);
        Assert.Same(clone.Array[0], clone.Array[2]);
        Assert.Equal("other", clone.Array[1].Name);
    }

    [Fact]
    public void CopyTo_ArrayElementsAreReused()
    {
        var source = new Holder { Array = [new Node { Name = "sa" }, new Node { Name = "sb" }] };
        var target = new Holder { Array = [new Node { Name = "ta" }, new Node { Name = "tb" }] };
        Node firstTargetElement = target.Array![0];

        Cloner.CopyTo(source, target);

        Assert.Same(firstTargetElement, target.Array![0]);
        Assert.Equal("sa", target.Array[0].Name);
    }

    [Fact]
    public void CopyTo_ArrayLengthFollowsTheSource()
    {
        var source = new Holder { Array = [new Node { Name = "a" }] };
        var target = new Holder { Array = [new Node { Name = "x" }, new Node { Name = "y" }] };

        Cloner.CopyTo(source, target);

        Assert.Single(target.Array!);
        Assert.Equal("a", target.Array![0].Name);
    }

    [Fact]
    public void Clone_ListElementsAreCopied()
    {
        var shared = new Node { Name = "shared" };
        var source = new Holder { Items = { shared, new Node { Name = "other" }, shared } };

        Holder clone = Cloner.Clone(source);

        Assert.Equal(3, clone.Items.Count);
        Assert.NotSame(shared, clone.Items[0]);
        Assert.Same(clone.Items[0], clone.Items[2]);
        Assert.Equal("other", clone.Items[1].Name);
    }

    [Fact]
    public void Clone_ListIsNotSharedWithTheSource()
    {
        var source = new Holder { Items = { new Node { Name = "a" } } };
        Holder clone = Cloner.Clone(source);

        clone.Items.Add(new Node { Name = "b" });

        Assert.Single(source.Items);
        Assert.Equal(2, clone.Items.Count);
    }

    [Fact]
    public void Clone_DictionaryWithReferenceKeysStaysLookupable()
    {
        var key = new Node { Name = "k" };
        var source = new Bag();
        source.ByReference[key] = 42;

        Bag clone = Cloner.Clone(source);

        Assert.Single(clone.ByReference);
        Node clonedKey = clone.ByReference.Keys.First();
        Assert.NotSame(key, clonedKey);
        Assert.True(clone.ByReference.TryGetValue(clonedKey, out int found));
        Assert.Equal(42, found);
    }

    [Fact]
    public void Clone_DictionaryValuesAreCopiedAndRemapped()
    {
        var shared = new Node { Name = "shared" };
        var source = new Bag();
        source.ByString["a"] = shared;
        source.ByString["b"] = shared;

        Bag clone = Cloner.Clone(source);

        Assert.NotSame(shared, clone.ByString["a"]);
        Assert.Same(clone.ByString["a"], clone.ByString["b"]);
        Assert.Equal("shared", clone.ByString["a"]!.Name);
    }

    [Fact]
    public void Clone_DictionaryKeepsItsComparer()
    {
        var source = new Bag { CaseInsensitive = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Key"] = 1 } };

        Bag clone = Cloner.Clone(source);

        Assert.True(clone.CaseInsensitive!.TryGetValue("KEY", out int found));
        Assert.Equal(1, found);
    }

    [Fact]
    public void CopyTo_DictionaryValuesReuseTheTargetObjects()
    {
        var source = new Bag();
        source.ByString["a"] = new Node { Name = "sa" };

        var target = new Bag();
        var targetValue = new Node { Name = "ta" };
        target.ByString["a"] = targetValue;

        Cloner.CopyTo(source, target);

        Assert.Same(targetValue, target.ByString["a"]);
        Assert.Equal("sa", targetValue.Name);
    }

    [Fact]
    public void Clone_HashSetWithReferenceItemsStaysContainable()
    {
        var item = new Node { Name = "i" };
        var source = new Bag { Set = { item } };

        Bag clone = Cloner.Clone(source);

        Assert.Single(clone.Set);
        Node cloned = clone.Set.First();
        Assert.NotSame(item, cloned);
        Assert.True(clone.Set.Contains(cloned));
    }

    #endregion

    #region Delegates

    [Fact]
    public void Clone_DelegateIsReboundToTheCopy()
    {
        var source = new Handler();
        source.OnThing = source.Handle;

        Handler clone = Cloner.Clone(source);
        clone.OnThing!.Invoke();

        Assert.Equal(0, source.Received);
        Assert.Equal(1, clone.Received);
    }

    [Fact]
    public void Clone_DelegateBoundOutsideTheCopyIsDropped()
    {
        var outsider = new Handler();
        var source = new Handler { OnThing = outsider.Handle };

        Handler clone = Cloner.Clone(source);

        Assert.Null(clone.OnThing);
        Assert.NotNull(source.OnThing);
    }

    [Fact]
    public void CopyTo_DelegateKeepsTheTargetsOwnOutsideSubscribers()
    {
        var source = new Handler();
        source.OnThing = source.Handle;

        var outsider = new Handler();
        var target = new Handler { OnThing = outsider.Handle };

        Cloner.CopyTo(source, target);

        target.OnThing!.Invoke();

        Assert.Equal(1, target.Received);
        Assert.Equal(1, outsider.Received);
        Assert.Equal(0, source.Received);
    }

    #endregion

    #region Structs

    [Fact]
    public void Clone_StructFieldHoldingAReferenceIsRemapped()
    {
        var node = new Node { Name = "n" };
        var source = new StructHolder
        {
            Direct = node,
            Value = new StructWithReference { Number = 5, Node = node }
        };

        StructHolder clone = Cloner.Clone(source);

        Assert.Equal(5, clone.Value.Number);
        Assert.NotSame(node, clone.Direct);
        Assert.Same(clone.Direct, clone.Value.Node);
    }

    #endregion

    #region Extension points

    [Fact]
    public void Clone_CallbacksFire()
    {
        CallbackObject.BeforeCount = 0;
        CallbackObject.AfterCount = 0;

        Cloner.Clone(new CallbackObject { Value = 1 });

        Assert.Equal(1, CallbackObject.BeforeCount);
        Assert.Equal(1, CallbackObject.AfterCount);
    }

    [Fact]
    public void Clone_ExplicitTypeCanAskForTheDefaultFieldWalk()
    {
        var source = new HybridExplicit { Plain = 3, Text = "t", Special = new Node { Name = "s" } };

        HybridExplicit clone = Cloner.Clone(source);

        Assert.Equal(3, clone.Plain);
        Assert.Equal("t", clone.Text);
        Assert.NotNull(clone.Special);
        Assert.NotSame(source.Special, clone.Special);
        Assert.Equal("s", clone.Special!.Name);
    }

    [Fact]
    public void Clone_ExplicitTypeControlsItsOwnCopy()
    {
        ExplicitObject.SetupCalls = 0;
        var source = new ExplicitObject { Plain = 1, Handled = new Node { Name = "handled" } };

        ExplicitObject clone = Cloner.Clone(source);

        Assert.True(ExplicitObject.SetupCalls > 0);
        Assert.Equal(101, clone.Plain);
        Assert.NotNull(clone.Handled);
        Assert.NotSame(source.Handled, clone.Handled);
        Assert.Equal("handled", clone.Handled!.Name);
    }

    #endregion

    #region Guards and awkward shapes

    [Fact]
    public void CopyTo_MismatchedRootTypesThrows()
    {
        var source = new Derived { A = 1, B = 2 };
        Base target = new Base();

        Assert.Throws<ArgumentException>(() => Cloner.CopyTo<Base>(source, target));
    }

    [Fact]
    public void Clone_SharedObjectIsCopiedOnce()
    {
        var shared = new Counted();
        var root = new Counted { X = shared, Y = shared };
        Counted.Copies = 0;

        Cloner.Clone(root);

        Assert.Equal(2, Counted.Copies);
    }

    [Fact]
    public void Clone_MultidimensionalArrayThrowsEveryTime()
    {
        var source = new MultiDimensional { Grid = new int[2, 2] };

        Assert.Throws<NotSupportedException>(() => Cloner.Clone(source));
        Assert.Throws<NotSupportedException>(() => Cloner.Clone(source));
    }

    [Fact]
    public void CopyTo_NullSourceDelegateUnsubscribesTheCopysOwnObjects()
    {
        var source = new Handler();
        var target = new Handler();
        var outsider = new Handler();

        target.OnThing = target.Handle;
        target.OnThing += outsider.Handle;

        Cloner.CopyTo(source, target);
        target.OnThing?.Invoke();

        Assert.Equal(0, target.Received);
        Assert.Equal(1, outsider.Received);
    }

    [Fact]
    public void Clone_JaggedArrays()
    {
        var shared = new Node { Name = "s" };
        var source = new Jagged { Rows = [[1, 2], [3]], Refs = [[shared], [shared]] };

        Jagged clone = Cloner.Clone(source);

        Assert.Equal(2, clone.Rows![0][1]);
        Assert.Equal(3, clone.Rows[1][0]);
        Assert.NotSame(source.Rows![0], clone.Rows[0]);
        Assert.NotSame(shared, clone.Refs![0][0]);
        Assert.Same(clone.Refs[0][0], clone.Refs[1][0]);
    }

    [Fact]
    public void Clone_ArrayOfStructsHoldingReferences()
    {
        var shared = new Node { Name = "s" };
        var source = new StructArrayHolder
        {
            Items = [new ItemWithReference { Number = 1, Node = shared }, new ItemWithReference { Number = 2, Node = shared }]
        };

        StructArrayHolder clone = Cloner.Clone(source);

        Assert.Equal(1, clone.Items![0].Number);
        Assert.Equal(2, clone.Items[1].Number);
        Assert.NotSame(shared, clone.Items[0].Node);
        Assert.Same(clone.Items[0].Node, clone.Items[1].Node);
    }

    [Fact]
    public void CopyTo_OntoItselfIsHarmless()
    {
        var node = new Node { Name = "n", Child = new Node { Name = "c" } };
        Node child = node.Child!;

        Cloner.CopyTo(node, node);

        Assert.Equal("n", node.Name);
        Assert.Same(child, node.Child);
    }

    [Fact]
    public void Clone_ImmutableRootsComeBackAsThemselves()
    {
        Assert.Equal("hello", Cloner.Clone("hello"));
        Assert.Equal(5, Cloner.Clone(5));
        Assert.Equal(3, Cloner.Clone(new[] { 1, 2, 3 }).Length);
    }

    #endregion

    #region Extension methods

    [Fact]
    public void DeepClone_MatchesCloner()
    {
        var source = new Node { Name = "x", Child = new Node { Name = "y" } };
        Node clone = source.DeepClone();

        Assert.NotSame(source, clone);
        Assert.Equal("y", clone.Child!.Name);
    }

    [Fact]
    public void DeepCopyTo_MatchesCloner()
    {
        var source = new Node { Name = "x" };
        var target = new Node { Name = "y" };

        source.DeepCopyTo(target);

        Assert.Equal("x", target.Name);
    }

    #endregion
}
