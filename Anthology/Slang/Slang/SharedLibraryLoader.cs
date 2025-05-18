using Prowl.Slang.Native;


namespace Prowl.Slang;


internal unsafe abstract class SharedLibraryLoader : ManagedComProxy<ISlangSharedLibraryLoader>, ISlangSharedLibraryLoader
{
    public unsafe abstract SlangResult LoadSharedLibrary(ConstU8Str path, out ISlangSharedLibrary* sharedLibraryOut);
}
