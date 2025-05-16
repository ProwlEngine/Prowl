using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class Module : ComponentType
{
    // Modules depend on a Session instance, but sessions dont depend on modules.
    // Keep a ref to the session to prevent its underlying native ptr from being disposed while this module still exists.

    internal IModule _module;


    internal Module(IModule module, Session session) : base(module, session)
    {
        _module = module;
    }

}
