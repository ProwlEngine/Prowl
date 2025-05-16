using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


/** A result code for a Slang API operation.

This type is generally compatible with the Windows API `HRESULT` type. In particular, negative
values indicate failure results, while zero or positive results indicate success.

In general, Slang APIs always return a zero result on success, unless documented otherwise.
Strictly speaking a negative value indicates an error, a positive (or 0) value indicates
success. This can be tested for with the macros SLANG_SUCCEEDED(x) or SLANG_FAILED(x).

It can represent if the call was successful or not. It can also specify in an extensible manner
what facility produced the result (as the integral 'facility') as well as what caused it (as an
integral 'code'). Under the covers SlangResult is represented as a int32_t.

SlangResult is designed to be compatible with COM HRESULT.

It's layout in bits is as follows

Severity | Facility | Code
---------|----------|-----
31       |    30-16 | 15-0

Severity - 1 fail, 0 is success - as SlangResult is signed 32 bits, means negative number
indicates failure. Facility is where the error originated from. Code is the code specific to the
facility.

Result codes have the following styles,
1) SLANG_name
2) SLANG_s_f_name
3) SLANG_s_name

where s is S for success, E for error
f is the short version of the facility name

Style 1 is reserved for SLANG_OK and SLANG_FAIL as they are so commonly used.

It is acceptable to expand 'f' to a longer name to differentiate a name or drop if unique
without it. ie for a facility 'DRIVER' it might make sense to have an error of the form
SLANG_E_DRIVER_OUT_OF_MEMORY
*/
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SlangResult(uint value = 0x00000000)
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
        return this == Ok;
    }


    public readonly Exception? GetException()
    {
        if (this == InvalidHandle)
            return new InvalidHandleException("Invalid handle: " + SlangNative.slang_getLastInternalErrorMessage().String);
        if (this == InvalidArg)
            return new ArgumentException("Invalid argument: " + SlangNative.slang_getLastInternalErrorMessage().String);
        if (this == OutOfMemory)
            return new OutOfMemoryException("Out of memory: " + SlangNative.slang_getLastInternalErrorMessage().String);
        if (this == BufferTooSmall)
            return new ArgumentOutOfRangeException("Buffer is too small: " + SlangNative.slang_getLastInternalErrorMessage().String);
        if (this == Uninitialized)
            return new NullReferenceException("Value is uninitialized: " + SlangNative.slang_getLastInternalErrorMessage().String);
        if (this == Pending)
            return new PendingException();
        if (this == CannotOpen)
            return new UnauthorizedAccessException("Cannot open file: " + SlangNative.slang_getLastInternalErrorMessage().String);
        if (this == NotFound)
            return new FileNotFoundException("File not found: " + SlangNative.slang_getLastInternalErrorMessage().String);
        if (this == InternalFail)
            return new Exception("Internal Failure: " + SlangNative.slang_getLastInternalErrorMessage().String);
        if (this == NotAvailable)
            return new NotSupportedException("Not supported: " + SlangNative.slang_getLastInternalErrorMessage().String);
        if (this == TimeOut)
            return new TimeoutException();

        return Marshal.GetExceptionForHR((int)_value);
    }


    public readonly void Throw()
    {
        Exception? ex = GetException();

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


[Serializable]
public class InvalidHandleException : Exception
{
    public InvalidHandleException() { }
    public InvalidHandleException(string message) : base(message) { }
    public InvalidHandleException(string message, Exception inner) : base(message, inner) { }
}


[Serializable]
public class PendingException : Exception
{
    public PendingException() { }
    public PendingException(string message) : base(message) { }
    public PendingException(string message, Exception inner) : base(message, inner) { }
}