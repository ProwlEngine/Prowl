using System;

using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class Module : ComponentType
{
    // Modules depend on a Session instance, but sessions dont depend on modules.
    // Keep a ref to the session to prevent its underlying native ptr from being disposed while this module still exists.
    internal Session _parentSession;

    internal IModule _module;


    internal Module(IModule module, Session parent) : base(module)
    {
        _parentSession = parent;
        _module = module;
    }

}