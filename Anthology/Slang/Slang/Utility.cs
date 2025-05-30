// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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


    internal static unsafe T Validate<T>(T* sourcePtr, ISlangBlob* diagnosticsPtr, out DiagnosticInfo diagnostics, bool trackRefs) where T : IUnknown
    {
        diagnostics = default;

        if (diagnosticsPtr != null)
            diagnostics = new(NativeComProxy.Create(diagnosticsPtr).GetString());

        if (sourcePtr == null)
            throw new NullReferenceException($"Source pointer is null. Diagnostics: {diagnostics.Message}");

        return NativeComProxy.Create(sourcePtr, trackRefs);
    }


    internal static unsafe T* ValidateRaw<T>(T* sourcePtr, ISlangBlob* diagnosticsPtr, out DiagnosticInfo diagnostics)
    {
        diagnostics = default;

        if (diagnosticsPtr != null)
            diagnostics = new(NativeComProxy.Create(diagnosticsPtr).GetString());

        if (sourcePtr == null)
            throw new NullReferenceException($"Source pointer is null. Diagnostics: {diagnostics.Message}");

        return sourcePtr;
    }
}
