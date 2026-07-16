// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

namespace Tests;

/// <summary>
/// [FormerlySerializedAs] on a class/struct preserves references to a type across a rename: old
/// serialized data whose $type is the former type name must resolve to the renamed type, so renaming a
/// serialized type (e.g. a component script) doesn't orphan existing scenes/prefabs.
/// </summary>
public class FormerTypeName_Tests
{
    [FormerlySerializedAs("OldWidget")]
    public class Widget { public int Value; }

    [FormerlySerializedAs("Legacy.Namespaced.Gadget")]
    public class Gadget { public string Label = ""; }

    [FormerlySerializedAs("Ancient")]
    [FormerlySerializedAs("Old")]
    public class Renamed { public int N; }

    private static EchoObject WithType(string typeName, Action<EchoObject> fill)
    {
        var e = EchoObject.NewCompound();
        e["$type"] = new EchoObject(EchoType.String, typeName);
        fill(e);
        return e;
    }

    [Fact]
    public void FormerName_ShortName_ResolvesToRenamedType()
    {
        var echo = WithType("OldWidget", e => e["Value"] = new EchoObject(EchoType.Int, 42));

        var result = Serializer.Deserialize(echo, typeof(object));

        Assert.IsType<Widget>(result);
        Assert.Equal(42, ((Widget)result).Value);
    }

    [Fact]
    public void FormerName_NamespaceQualified_ResolvesToRenamedType()
    {
        var echo = WithType("Legacy.Namespaced.Gadget", e => e["Label"] = new EchoObject(EchoType.String, "hi"));

        var result = Serializer.Deserialize(echo, typeof(object));

        Assert.IsType<Gadget>(result);
        Assert.Equal("hi", ((Gadget)result).Label);
    }

    [Fact]
    public void MultipleFormerNames_AllResolve()
    {
        var fromAncient = Serializer.Deserialize(WithType("Ancient", e => e["N"] = new EchoObject(EchoType.Int, 1)), typeof(object));
        var fromOld = Serializer.Deserialize(WithType("Old", e => e["N"] = new EchoObject(EchoType.Int, 2)), typeof(object));

        Assert.IsType<Renamed>(fromAncient);
        Assert.IsType<Renamed>(fromOld);
        Assert.Equal(1, ((Renamed)fromAncient).N);
        Assert.Equal(2, ((Renamed)fromOld).N);
    }
}
