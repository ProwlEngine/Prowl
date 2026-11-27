using System;
using System.Numerics;
using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Core.Tests;

public class Vector2Test
{
    [Fact]
    public void TestImplicitCasting()
    {
        for (int __r = 0; __r < 100000; __r++)
        {
            var v1 = new Vector2(Random.Shared.NextSingle(), Random.Shared.NextSingle());
            var v2 = new RcVec2f(Random.Shared.NextSingle(), Random.Shared.NextSingle());

            Assert.Equal(RcVec2f.Distance(v1, v2), Vector2.Distance(v1, v2));
        }
    }

}