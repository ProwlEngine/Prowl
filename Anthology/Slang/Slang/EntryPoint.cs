// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Slang.Native;


namespace Prowl.Slang;


/// <summary>
/// An entrypoint defined in a shader module.
/// </summary>
public unsafe class EntryPoint : ComponentType
{
    internal IEntryPoint _entryPoint;


    internal EntryPoint(IEntryPoint entryPoint, Session session) : base(entryPoint, session)
    {
        _entryPoint = entryPoint;
    }


    /// <summary>
    /// Gets the reflection information for the entrypoint function.
    /// </summary>
    public FunctionReflection GetFunctionReflection()
    {
        return new FunctionReflection(_entryPoint.GetFunctionReflection(), this);
    }
}
