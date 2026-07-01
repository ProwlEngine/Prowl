// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using SlangUInt = nuint;

namespace Prowl.Slang.Native;


[UUID(0x8044a8a3, 0xddc0, 0x4b7f, 0xaf, 0x8e, 0x2, 0x6e, 0x90, 0x5d, 0x73, 0x32)]
internal interface IMetadata : ISlangCastable
{
    SlangResult IsParameterLocationUsed(
        ParameterCategory category,
        SlangUInt spaceIndex,
        SlangUInt registerIndex,
        out CBool outUsed);
}
