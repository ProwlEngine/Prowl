// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using static Prowl.Echo.Test.RoundtripTestHelpers;

namespace Prowl.Echo.Test;

// A throwing user callback, a reused EchoObject or a partial populate must only cost the object it happened to.
public class FailureIsolation_Tests
{
    public class Payload { public string Text = ""; }

    public class ThrowsBeforeSerialize : ISerializationCallbackReceiver
    {
        public Payload? Payload;
        public void OnBeforeSerialize() => throw new InvalidOperationException("before");
        public void OnAfterDeserialize() { }
    }

    public class ThrowsAfterDeserialize : ISerializationCallbackReceiver
    {
        public int Value;
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() => throw new InvalidOperationException("after");
    }

    public class ThrowsInSerialize : ISerializable
    {
        public Payload? Payload;

        public void Serialize(ref EchoObject compound, SerializationContext ctx)
        {
            compound.Add("Payload", Serializer.Serialize(typeof(Payload), Payload, ctx));
            throw new InvalidOperationException("serialize");
        }

        public void Deserialize(EchoObject value, SerializationContext ctx)
            => Payload = Serializer.Deserialize<Payload>(value.Get("Payload"), ctx);
    }

    public class SharedHolder
    {
        public ThrowsBeforeSerialize? First;
        public Payload? Second;
    }

    public class SerializeHolder
    {
        public ThrowsInSerialize? First;
        public Payload? Second;
    }

    public class ArrayHolder { public object[]? Items; }

    public class EchoHolder { public EchoObject? Data; }

    public class Node { public Node? Child; public Node? Back; }

    public class DeferringChild : ISerializable
    {
        [NonSerialized] public bool DeferredRan;

        public void Serialize(ref EchoObject compound, SerializationContext ctx) => compound.Add("x", new EchoObject(1));

        public void Deserialize(EchoObject value, SerializationContext ctx) => ctx.Defer(() => DeferredRan = true);
    }

    public class DeferRoot : ISerializationCallbackReceiver
    {
        public DeferringChild? Child = new();
        [NonSerialized] public bool SawDeferred;
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() => SawDeferred = Child!.DeferredRan;
    }

    public class OldEnemy { public float Health; }
    public class NewEnemy : OldEnemy { public new float Health; }

    [Fact]
    public void ThrowingOnBeforeSerialize_StillWritesTheDefinition()
    {
        var shared = new Payload { Text = "kept" };
        var back = Roundtrip(new SharedHolder { First = new ThrowsBeforeSerialize { Payload = shared }, Second = shared });

        Assert.NotNull(back.First);
        Assert.Equal("kept", back.Second!.Text);
        Assert.Same(back.First!.Payload, back.Second);
    }

    [Fact]
    public void ThrowingISerializable_KeepsWhatItWrote()
    {
        var shared = new Payload { Text = "kept" };
        var back = Roundtrip(new SerializeHolder { First = new ThrowsInSerialize { Payload = shared }, Second = shared });

        Assert.Equal("kept", back.Second!.Text);
        Assert.Same(back.First!.Payload, back.Second);
    }

    [Fact]
    public void ThrowingOnAfterDeserialize_KeepsTheObjectAndItsSiblings()
    {
        var back = Roundtrip(new ArrayHolder { Items = [new Payload { Text = "a" }, new ThrowsAfterDeserialize { Value = 3 }, new Payload { Text = "b" }] });

        Assert.Equal(3, back.Items!.Length);
        Assert.Equal(3, Assert.IsType<ThrowsAfterDeserialize>(back.Items[1]).Value);
        Assert.Equal("b", Assert.IsType<Payload>(back.Items[2]).Text);
    }

    [Fact]
    public void EchoObjectStillAttachedToAnotherTree_IsSerialized()
    {
        var file = EchoObject.NewCompound();
        var inner = EchoObject.NewCompound();
        inner.Add("value", new EchoObject(7));
        file.Add("inner", inner);

        var saved = Serializer.Serialize(new EchoHolder { Data = inner });

        Assert.Equal(7, saved["Data"]["value"].IntValue);
        Assert.Same(file, inner.Parent);
    }

    [Fact]
    public void DeferredActions_RunBeforeTheRootsOnAfterDeserialize()
    {
        var back = Roundtrip(new DeferRoot());

        Assert.True(back.SawDeferred);
    }

    [Fact]
    public void DeserializeInto_ResolvesReferencesBackToTheTarget()
    {
        var source = new Node();
        source.Child = new Node { Back = source };
        var target = new Node();

        Serializer.DeserializeInto(Serializer.Serialize(source), target);

        Assert.Same(target, target.Child!.Back);
    }

    public class ObjectHolder { public object? Value; }

    [Fact]
    public void DefinitionWithAnUnresolvableType_IsRecordedByItsId()
    {
        var saved = Serializer.Serialize(new ObjectHolder { Value = new Payload { Text = "kept" } });
        string text = saved.WriteToString().Replace(typeof(Payload).FullName!, "Missing.Type");
        var ctx = new SerializationContext();

        Serializer.Deserialize<ObjectHolder>(EchoObject.ReadFromString(text), ctx);

        var definition = Assert.Single(ctx.unresolvedDefinitions).Value;
        Assert.Equal("kept", definition["Text"].StringValue);
    }

    [Fact]
    public void AddingAShadowingField_LeavesExistingDataWithTheBaseField()
    {
        var saved = Serializer.Serialize(new OldEnemy { Health = 50 });
        string text = saved.WriteToString().Replace(typeof(OldEnemy).FullName!, typeof(NewEnemy).FullName!);

        var back = (NewEnemy)Serializer.Deserialize<OldEnemy>(EchoObject.ReadFromString(text))!;

        Assert.Equal(50, ((OldEnemy)back).Health);
        Assert.Equal(0, back.Health);
    }
}
