// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>A section the host unloaded.</summary>
internal readonly record struct SectionRemoveMessage(ulong Generation, uint SectionId)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteUInt64(Generation);
        writer.WriteVarUInt32(SectionId);
    }

    internal static SectionRemoveMessage Read(ref NetReader reader) => new(reader.ReadUInt64(), reader.ReadVarUInt32());
}

/// <summary>A networked root the host moved into another section.</summary>
internal readonly record struct SectionMoveMessage(Guid Identifier, uint SectionId)
{
    internal void Write(NetWriter writer)
    {
        writer.WriteGuid(Identifier);
        writer.WriteVarUInt32(SectionId);
    }

    internal static SectionMoveMessage Read(ref NetReader reader) => new(reader.ReadGuid(), reader.ReadVarUInt32());
}

/// <summary>SceneLoaded, SceneReady and SceneActivate acknowledgements carry only this generation.</summary>
internal readonly record struct SceneGenerationMessage(ulong Generation)
{
    internal void Write(NetWriter writer) => writer.WriteUInt64(Generation);

    internal static SceneGenerationMessage Read(ref NetReader reader) => new(
        reader.ReadUInt64());
}
