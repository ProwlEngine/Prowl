// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

namespace Tests;

/// <summary>
/// Reference ids may appear before their definition (a "forward reference"). This happens whenever a
/// producer serializes an object graph in an order other than strict definition-first - e.g. a flat
/// object list plus nested child lists that reference the same instances. Echo must resolve a forward
/// reference to a placeholder and then back-patch that placeholder when the definition arrives, so every
/// reference ends up pointing at the one fully-populated instance.
/// </summary>
public class ForwardReference_Tests
{
    public class RefNode
    {
        public string Name = "";
        public int Value;
    }

    // A bare reference stub: { "$id": id }
    private static EchoObject Ref(int id)
    {
        var e = EchoObject.NewCompound();
        e["$id"] = new EchoObject(EchoType.Int, id);
        return e;
    }

    // A full definition: { "$id": id, "Name": ..., "Value": ... }
    private static EchoObject Def(int id, string name, int value)
    {
        var e = EchoObject.NewCompound();
        e["$id"] = new EchoObject(EchoType.Int, id);
        e["Name"] = new EchoObject(EchoType.String, name);
        e["Value"] = new EchoObject(EchoType.Int, value);
        return e;
    }

    [Fact]
    public void DefinitionAfterReference_BackPatchesThePlaceholder()
    {
        var ctx = new SerializationContext();

        // The reference is deserialized BEFORE its definition.
        var fromRef = Serializer.Deserialize(Ref(1), typeof(RefNode), ctx) as RefNode;
        var fromDef = Serializer.Deserialize(Def(1, "Hello", 42), typeof(RefNode), ctx) as RefNode;

        Assert.NotNull(fromRef);
        Assert.NotNull(fromDef);
        Assert.Same(fromRef, fromDef);           // the reference and the definition are one instance
        Assert.Equal("Hello", fromRef!.Name);    // and the definition's data reached it (pre-fix: "")
        Assert.Equal(42, fromRef.Value);
    }

    [Fact]
    public void MultipleReferencesBeforeDefinition_AllShareThePopulatedInstance()
    {
        var ctx = new SerializationContext();

        var a = Serializer.Deserialize(Ref(7), typeof(RefNode), ctx) as RefNode;
        var b = Serializer.Deserialize(Ref(7), typeof(RefNode), ctx) as RefNode;
        var def = Serializer.Deserialize(Def(7, "Shared", 9), typeof(RefNode), ctx) as RefNode;

        Assert.Same(a, b);
        Assert.Same(a, def);
        Assert.Equal("Shared", a!.Name);
        Assert.Equal(9, a.Value);
    }

    [Fact]
    public void ReferenceAfterDefinition_StillResolvesToTheSameInstance()
    {
        // Normal order (definition first) must keep working.
        var ctx = new SerializationContext();

        var def = Serializer.Deserialize(Def(3, "First", 1), typeof(RefNode), ctx) as RefNode;
        var reference = Serializer.Deserialize(Ref(3), typeof(RefNode), ctx) as RefNode;

        Assert.Same(def, reference);
        Assert.Equal("First", def!.Name);
    }

    [Fact]
    public void TwoDefinitionsForSameId_Throws()
    {
        var ctx = new SerializationContext();

        Serializer.Deserialize(Def(5, "First", 1), typeof(RefNode), ctx);

        Assert.Throws<InvalidOperationException>(
            () => Serializer.Deserialize(Def(5, "Second", 2), typeof(RefNode), ctx));
    }
}
