// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SlangResult(uint value = 0x00000000)
{
    private static SlangResult MakeError(ushort fac, ushort code)
    {
        return new SlangResult((((uint)fac) << 16) | (code) | 0x80000000);
    }

    private static SlangResult MakeWinError(ushort code)
    {
        return MakeError(0, code);
    }

    public static readonly SlangResult Ok = new SlangResult(0x00000000);
    public static readonly SlangResult Fail = MakeWinError(0x4005);

    public static readonly SlangResult NotImplemented = MakeWinError(0x4001);
    public static readonly SlangResult NoInterface = MakeWinError(0x4002);
    public static readonly SlangResult Abort = MakeWinError(0x4004);

    public static readonly SlangResult InvalidHandle = MakeError(7, 6);
    public static readonly SlangResult InvalidArg = MakeError(7, 0x57);
    public static readonly SlangResult OutOfMemory = MakeError(7, 0xe);

    public static readonly SlangResult BufferTooSmall = MakeError(0x200, 1);
    public static readonly SlangResult Uninitialized = MakeError(0x200, 2);
    public static readonly SlangResult Pending = MakeError(0x200, 3);
    public static readonly SlangResult CannotOpen = MakeError(0x200, 4);
    public static readonly SlangResult NotFound = MakeError(0x200, 5);
    public static readonly SlangResult InternalFail = MakeError(0x200, 6);
    public static readonly SlangResult NotAvailable = MakeError(0x200, 7);
    public static readonly SlangResult TimeOut = MakeError(0x200, 8);


    uint _value = value;


    public readonly bool IsOk()
    {
        return (_value & 0x80000000) == 0;
    }


    public readonly Exception? GetException(string? exceptionMsg = null)
    {
        if (this == InvalidHandle)
            return new InvalidOperationException(exceptionMsg ?? "Invalid handle");
        if (this == InvalidArg)
            return new ArgumentException(exceptionMsg ?? "Invalid argument");
        if (this == OutOfMemory)
            return new OutOfMemoryException(exceptionMsg ?? "Out of memory");
        if (this == BufferTooSmall)
            return new ArgumentOutOfRangeException(exceptionMsg ?? "Buffer too small");
        if (this == Uninitialized)
            return new NullReferenceException(exceptionMsg ?? "Value uninitialized");
        if (this == Pending)
            return new Exception(exceptionMsg ?? "Pending");
        if (this == CannotOpen)
            return new UnauthorizedAccessException(exceptionMsg ?? "Cannot open file");
        if (this == NotFound)
            return new FileNotFoundException(exceptionMsg ?? "File not found");
        if (this == InternalFail)
            return new Exception(exceptionMsg ?? SlangNative.slang_getLastInternalErrorMessage().String);
        if (this == NotAvailable)
            return new NotSupportedException(exceptionMsg ?? "Feature not available");
        if (this == TimeOut)
            return new TimeoutException(exceptionMsg ?? "Request timed out");
        if (this == Fail)
            return new Exception(exceptionMsg ?? "Failure");

        return Marshal.GetExceptionForHR((int)_value);
    }


    public readonly void Throw()
    {
        Exception? ex = GetException();

        if (ex != null)
            throw ex;
    }


    internal readonly void ThrowOrDiagnose(ISlangBlob* diagPtr, out DiagnosticInfo diagnostics)
    {
        diagnostics = default;

        if (diagPtr != null)
            diagnostics = new(NativeComProxy.Create(diagPtr).GetString());

        Exception? ex = GetException(diagnostics.Message);

        if (ex != null)
            throw ex;
    }


    public static bool operator !=(SlangResult a, SlangResult b)
    {
        return a._value != b._value;
    }


    public static bool operator ==(SlangResult a, SlangResult b)
    {
        return a._value == b._value;
    }


    public override readonly bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is not SlangResult other)
            return false;

        return other._value == _value;
    }


    public override readonly int GetHashCode() => (int)_value;
}
