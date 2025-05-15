namespace Prowl.Slang.NativeAPI;


[UUID(0x197772c7, 0x0155, 0x4b91, 0x84, 0xe8, 0x66, 0x68, 0xba, 0xff, 0x06, 0x19)]
public unsafe interface ISlangProfiler : IUnknown
{
    nuint GetEntryCount();
    ConstU8Str GetEntryName(uint index);
    long GetEntryTimeMS(uint index);
    uint GetEntryInvocationTimes(uint index);
}
