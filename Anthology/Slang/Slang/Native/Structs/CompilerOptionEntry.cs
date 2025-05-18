using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

using System;
using System.Linq;


namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CompilerOptionEntry
{
    public CompilerOptionName Name;
    public CompilerOptionValueKind Kind;

    public int IntValue0;
    public int IntValue1;

    public ConstU8Str StringValue0;
    public ConstU8Str StringValue1;


    public CompilerOptionEntry Allocate(Slang.CompilerOptionEntry src)
    {
        Name = src.Name;
        Kind = src.Value.Kind;
        IntValue0 = src.Value.IntValue0;
        IntValue1 = src.Value.IntValue1;

        if (src.Value.StringValue0 != null)
            StringValue0 = U8Str.Alloc(src.Value.StringValue0);

        if (src.Value.StringValue1 != null)
            StringValue1 = U8Str.Alloc(src.Value.StringValue1);

        return this;
    }


    public void Free()
    {
        if (StringValue0.Data != null)
            NativeMemory.Free(StringValue0.Data);

        if (StringValue1.Data != null)
            NativeMemory.Free(StringValue1.Data);

        StringValue0.Data = null;
        StringValue1.Data = null;
    }


    public Slang.CompilerOptionEntry Read()
    {
        return new()
        {
            Name = Name,
            Value = new()
            {
                Kind = Kind,
                IntValue0 = IntValue0,
                IntValue1 = IntValue1,
                StringValue0 = StringValue0.String,
                StringValue1 = StringValue1.String
            }
        };
    }
}