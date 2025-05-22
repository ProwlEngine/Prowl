using System;
using System.Collections.Generic;

using Prowl.Slang.Native;


namespace Prowl.Slang;


internal static class Utility
{
    public static IEnumerable<T> For<T>(uint range, Func<uint, T> getter)
    {
        for (uint i = 0; i < range; i++)
            yield return getter.Invoke(i);
    }


    internal static unsafe string? GetDiagnostic(ISlangBlob* blob)
    {
        if (blob == null)
            return null;

        return NativeComProxy.Create(blob).GetString();
    }
}
