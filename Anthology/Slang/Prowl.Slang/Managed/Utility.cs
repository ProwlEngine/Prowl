using System;
using System.Collections.Generic;


namespace Prowl.Slang;


public static class Utility
{
    public static IEnumerable<T> For<T>(uint range, Func<uint, T> getter)
    {
        for (uint i = 0; i < range; i++)
            yield return getter.Invoke(i);
    }
}