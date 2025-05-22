using Prowl.Slang.Native;


namespace Prowl.Slang;


internal abstract unsafe class SharedLibraryLoader : ManagedComProxy<ISlangSharedLibraryLoader>, ISlangSharedLibraryLoader
{
    public abstract unsafe SlangResult LoadSharedLibrary(ConstU8Str path, out ISlangSharedLibrary* sharedLibraryOut);
}
