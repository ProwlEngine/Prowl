// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Graphics casts Prowl's VSyncMode to Graphite's, so the two must keep the same members with the same values.</summary>
public class VSyncModeTests
{
    [Fact]
    public void VSyncMode_MatchesGraphiteMemberForMember()
    {
        Assert.Equal(Members<Graphite.VSyncMode>(), Members<VSyncMode>());
    }

    private static (string, int)[] Members<T>() where T : struct, Enum
        => Enum.GetValues<T>().Select(v => (v.ToString(), Convert.ToInt32(v))).ToArray();
}
