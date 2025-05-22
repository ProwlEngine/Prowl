using Prowl.Slang.Native;


namespace Prowl.Slang;


internal unsafe class SharedLibrary
{
    internal ISlangSharedLibrary _library;


    internal SharedLibrary(ISlangSharedLibrary library)
    {
        _library = library;
    }
}
