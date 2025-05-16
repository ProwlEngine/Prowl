using System;
using System.Collections.Generic;


namespace Prowl.Slang.Native;


public static class Utility
{
    public static IEnumerator<T> For<T>(uint range, Func<uint, T> getter)
    {
        for (uint i = 0; i < range; i++)
            yield return getter.Invoke(i);
    }
}