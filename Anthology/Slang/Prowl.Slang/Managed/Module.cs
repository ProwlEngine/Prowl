using System;

using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class Module : ComponentType
{
    internal IModule _module;


    internal Module(IModule module) : base(module)
    {
        _module = module;
    }

}