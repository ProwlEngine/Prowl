// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using static Prowl.Echo.Test.RoundtripTestHelpers;

namespace Prowl.Echo.Test;

// Polymorphic fields keeping their derived runtime type, and fields shadowed by a `new` same-name field.
public class Inheritance_Tests
{
    public class Animal { public string? Name; }
    public class Dog : Animal { public bool Barks = true; }
    public class Holder { public Animal? Pet; }

    public class Base { public int Value = 1; }
    public class Derived : Base { public new int Value = 2; }

    [Fact]
    public void Polymorphic_Field_KeepsDerivedType()
    {
        var back = Roundtrip(new Holder { Pet = new Dog { Name = "Rex" } });
        Assert.IsType<Dog>(back.Pet);
        Assert.True(((Dog)back.Pet!).Barks);
    }

    [Fact]
    public void Polymorphic_ListOfBase_KeepsDerived()
    {
        var back = Roundtrip(new List<Animal> { new Dog { Name = "A" }, new Animal { Name = "B" } });
        Assert.IsType<Dog>(back[0]);
        Assert.IsType<Animal>(back[1]);
    }

    [Fact]
    public void FieldShadowing_BothValuesSurvive()
    {
        var d = new Derived();
        ((Base)d).Value = 10;
        d.Value = 20;
        var back = Roundtrip(d);
        Assert.Equal(20, back.Value);
        Assert.Equal(10, ((Base)back).Value);
    }
}
