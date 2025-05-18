using System.Runtime.InteropServices;


namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PreprocessorMacroDescription
{
    public ConstU8Str Name;
    public ConstU8Str Value;


    public PreprocessorMacroDescription Allocate(Slang.PreprocessorMacroDescription src)
    {
        if (src.Name != null)
            Name = U8Str.Alloc(src.Name);

        if (src.Value != null)
            Value = U8Str.Alloc(src.Value);

        return this;
    }


    public void Free()
    {
        if (Name.Data != null)
            NativeMemory.Free(Name.Data);

        if (Value.Data != null)
            NativeMemory.Free(Value.Data);

        Name.Data = null;
        Value.Data = null;
    }


    public Slang.PreprocessorMacroDescription Read()
    {
        return new()
        {
            Name = Name.String,
            Value = Value.String
        };
    }
}
