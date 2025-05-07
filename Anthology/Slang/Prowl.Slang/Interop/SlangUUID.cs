using System;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
public unsafe struct SlangUUID
{
    public uint data1;
    public ushort data2;
    public ushort data3;

    public fixed byte data4[8];


    public SlangUUID(
        uint data1, ushort data2, ushort data3,
        byte d1, byte d2, byte d3, byte d4,
        byte d5, byte d6, byte d7, byte d8)
    {
        this.data1 = data1;
        this.data2 = data2;
        this.data3 = data3;
        data4[0] = d1;
        data4[1] = d2;
        data4[2] = d3;
        data4[3] = d4;
        data4[4] = d5;
        data4[5] = d6;
        data4[6] = d7;
        data4[7] = d8;
    }
}
