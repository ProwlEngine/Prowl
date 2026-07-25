// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using static Prowl.Echo.Test.RoundtripTestHelpers;

namespace Prowl.Echo.Test;

public class EnumSerialization_Tests
{
    // Two enums with the same simple name "State" to probe compact enum type identity.
    public static class NsA { public enum State { Off, On } }
    public static class NsB { public enum State { Idle, Busy = 5 } }

    [Flags]
    public enum Flags { None = 0, A = 1, B = 2, C = 4, All = A | B | C }
    public enum ByteEnum : byte { X = 1, Y = 200 }
    public enum LongEnum : long { Big = 9000000000L }
    public enum SignedEnum { Neg = -5, Pos = 5 }

    [Fact] public void Enum_Flags_Combined() => Assert.Equal(Flags.A | Flags.C, Roundtrip(Flags.A | Flags.C));
    [Fact] public void Enum_ByteBacked() => Assert.Equal(ByteEnum.Y, Roundtrip(ByteEnum.Y));
    [Fact] public void Enum_LongBacked() => Assert.Equal(LongEnum.Big, Roundtrip(LongEnum.Big));
    [Fact] public void Enum_Negative() => Assert.Equal(SignedEnum.Neg, Roundtrip(SignedEnum.Neg));
    [Fact] public void Enum_UndefinedValue() => Assert.Equal((Flags)999, Roundtrip((Flags)999));
    [Fact] public void Enum_LongBacked_Text() => Assert.Equal(LongEnum.Big, RoundtripText(LongEnum.Big));

    [Fact]
    public void Enum_InObjectField_KeepsIdentity()
    {
        object boxed = NsB.State.Busy;
        Assert.IsType<NsB.State>(Roundtrip(boxed));
    }
}
