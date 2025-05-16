using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class ComponentType
{
    // Components depend on a Session instance, but sessions dont depend on components.
    // Keep a ref to the session to prevent its underlying native ptr from being disposed while this component still exists.
    internal Session _session;
    internal IComponentType _componentType;


    internal ComponentType(IComponentType componentType, Session session)
    {
        _session = session;
        _componentType = componentType;
    }

}
