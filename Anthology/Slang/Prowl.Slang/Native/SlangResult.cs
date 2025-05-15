using System;
using System.Diagnostics.CodeAnalysis;
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
    public static readonly SlangResult Ok = new SlangResult(0x00000000);
    public static readonly SlangResult Fail = new SlangResult(0x80004005);
    public static readonly SlangResult NoInterface = new SlangResult(0x80004002);

    uint _value = value;


    public readonly bool IsOk()
    {
        return _value == 0x00000000;
    }


    public readonly void Throw()
    {
        Exception? ex = Marshal.GetExceptionForHR((int)_value);

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
