using System;

using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class ComponentType
{
    internal IComponentType _componentType;


    internal ComponentType(IComponentType componentType)
    {
        _componentType = componentType;
    }

}