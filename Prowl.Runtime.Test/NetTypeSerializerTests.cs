// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

using Prowl.Runtime.Networking;
using Prowl.Warp;

using Xunit;

namespace Prowl.Runtime.Test;

public abstract class TestShape
{
    public string Name = "";
}

public class TestCircle : TestShape
{
    public float Radius;
    public TestShape? Next;
}

public class TestSquare : TestShape
{
    public float Side;
}

/// <summary>Has no serializer, the way a type from outside game scripts wouldn't.</summary>
public class TestTriangle : TestShape;

/// <summary>Registers the test shapes the way the script compiler's generated file would.</summary>
internal static class TestShapeSerializers
{
    [ModuleInitializer]
    internal static void Register()
    {
        NetTypeSerializer.RegisterBase<TestShape>();
        NetTypeSerializer.RegisterClass<TestCircle>(0x7E570001u, static () => new TestCircle(),
            static (NetWriter w, ref TestCircle v) =>
            {
                NetSerializer<string>.Write(w, v.Name);
                NetSerializer<float>.Write(w, v.Radius);
                NetSerializer<TestShape>.Write(w, v.Next!);
            },
            static (ref NetReader r, ref TestCircle v) =>
            {
                v.Name = NetSerializer<string>.Read(ref r);
                v.Radius = NetSerializer<float>.Read(ref r);
                v.Next = NetSerializer<TestShape>.Read(ref r);
            });
        NetTypeSerializer.RegisterClass<TestSquare>(0x7E570002u, static () => new TestSquare(),
            static (NetWriter w, ref TestSquare v) =>
            {
                NetSerializer<string>.Write(w, v.Name);
                NetSerializer<float>.Write(w, v.Side);
            },
            static (ref NetReader r, ref TestSquare v) =>
            {
                v.Name = NetSerializer<string>.Read(ref r);
                v.Side = NetSerializer<float>.Read(ref r);
            });
    }
}

public class NetTypeSerializerTests
{
    private static T RoundTrip<T>(T value)
    {
        using NetWriter writer = new();
        NetSerializer<T>.Write(writer, value);
        NetReader reader = new(writer.WrittenSpan);
        T back = NetSerializer<T>.Read(ref reader);
        Assert.True(reader.IsAtEnd);
        return back;
    }

    /// <summary>The script compiler judges what scripts may send by [BuiltInTypes], so each type it lists must really be built in.</summary>
    [Fact]
    public void EveryTypeListedAsBuiltInIsSupported()
    {
        Type[] listed = typeof(NetSerializer<>).GetCustomAttributes(typeof(BuiltInTypesAttribute), false).Cast<BuiltInTypesAttribute>().Single().Types;
        Assert.NotEmpty(listed);
        foreach (Type type in listed)
            Assert.True((bool)typeof(NetSerializer<>).MakeGenericType(type).GetProperty("IsSupported")!.GetValue(null)!, type.Name);
    }

    [Fact]
    public void AValueArrivesAsWhatItReallyIs()
    {
        TestShape back = RoundTrip<TestShape>(new TestCircle { Name = "c", Radius = 2, Next = new TestSquare { Name = "s", Side = 3 } });

        TestCircle circle = Assert.IsType<TestCircle>(back);
        Assert.Equal(("c", 2f), (circle.Name, circle.Radius));
        TestSquare square = Assert.IsType<TestSquare>(circle.Next);
        Assert.Equal(("s", 3f), (square.Name, square.Side));
        Assert.Null(RoundTrip<TestShape>(null!));
    }

    [Fact]
    public void ReferencesAndCyclesArriveAsTheyWere()
    {
        TestCircle self = new() { Name = "self" };
        self.Next = self;
        TestCircle loop = Assert.IsType<TestCircle>(RoundTrip<TestShape>(self));
        Assert.Same(loop, loop.Next);

        TestCircle a = new() { Name = "a" };
        TestCircle b = new() { Name = "b", Next = a };
        a.Next = b;
        TestCircle a2 = RoundTrip(a);
        TestCircle b2 = Assert.IsType<TestCircle>(a2.Next);
        Assert.Equal("b", b2.Name);
        Assert.Same(a2, b2.Next);

        // Each value is its own: sending one again sends it whole, not as a reference to the last.
        TestCircle again = RoundTrip(a);
        Assert.NotSame(a2, again);
        Assert.Equal("b", ((TestCircle)again.Next!).Name);
    }

    [Fact]
    public void TypesWithoutASerializerAreRefusedClearly()
    {
        using NetWriter writer = new();
        NotSupportedException unsent = Assert.Throws<NotSupportedException>(() => NetSerializer<TestShape>.Write(writer, new TestTriangle()));
        Assert.Contains(nameof(TestTriangle), unsent.Message);

        // A square where a circle is expected is a lie, whatever the bytes say.
        writer.Reset();
        NetSerializer<TestShape>.Write(writer, new TestSquare { Name = "s" });
        Assert.Throws<InvalidDataException>(() =>
        {
            NetReader reader = new(writer.WrittenSpan);
            NetSerializer<TestCircle>.Read(ref reader);
        });

        // As are references to objects that weren't sent, and types this peer doesn't know.
        foreach (byte[] bad in new[] { new byte[] { 3, 5 }, new byte[] { 2, 0xEF, 0xBE, 0xAD, 0xDE } })
        {
            Assert.Throws<InvalidDataException>(() =>
            {
                NetReader reader = new(bad);
                NetSerializer<TestShape>.Read(ref reader);
            });
        }
    }

    [Fact]
    public void TooDeepIsRefusedRatherThanOverflowingTheStack()
    {
        TestCircle head = new();
        TestCircle tail = head;
        for (int i = 0; i < 300; i++)
            tail = (TestCircle)(tail.Next = new TestCircle());

        using NetWriter writer = new();
        Assert.Throws<InvalidOperationException>(() => NetSerializer<TestShape>.Write(writer, head));

        // And what failed leaves nothing behind for the next value.
        writer.Reset();
        TestCircle back = Assert.IsType<TestCircle>(RoundTrip<TestShape>(new TestCircle { Name = "fine" }));
        Assert.Equal("fine", back.Name);
    }
}
