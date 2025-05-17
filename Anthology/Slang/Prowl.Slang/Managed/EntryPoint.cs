using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class EntryPoint : ComponentType
{
    internal IEntryPoint _entryPoint;


    internal EntryPoint(IEntryPoint entryPoint, Session session) : base(entryPoint, session)
    {
        _entryPoint = entryPoint;
    }


    public FunctionReflection GetFunctionReflection()
    {
        return new FunctionReflection(_entryPoint.GetFunctionReflection(), _session);
    }
}
