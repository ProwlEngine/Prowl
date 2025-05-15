using System.Runtime.InteropServices;


namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
public struct CBool
{
    private byte _value;

    public static implicit operator bool(CBool cbool) => cbool._value == 1;

    public static implicit operator CBool(bool nbool) => new() { _value = (byte)(nbool ? 1 : 0) };
}
