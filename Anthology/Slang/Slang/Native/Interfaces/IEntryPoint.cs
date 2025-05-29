// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Slang.Native;


[UUID(0x8f241361, 0xf5bd, 0x4ca0, 0xa3, 0xac, 0x2, 0xf7, 0xfa, 0x24, 0x2, 0xb8)]
internal unsafe interface IEntryPoint : IComponentType
{
    FunctionReflection* GetFunctionReflection();
}
