// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using static Prowl.Echo.Test.RoundtripTestHelpers;

namespace Prowl.Echo.Test;

public class Tuple_Tests
{
    [Fact]
    public void ValueTuple_Roundtrips()
    {
        var back = Roundtrip((1, "two", 3.0));
        Assert.Equal(1, back.Item1);
        Assert.Equal("two", back.Item2);
        Assert.Equal(3.0, back.Item3);
    }

    [Fact]
    public void LargeTuple_WithTRest_Roundtrips()
    {
        var back = Roundtrip((1, 2, 3, 4, 5, 6, 7, 8, 9));
        Assert.Equal(8, back.Item8);
        Assert.Equal(9, back.Item9);
    }
}
