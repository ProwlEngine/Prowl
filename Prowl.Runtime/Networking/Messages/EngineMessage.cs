// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

internal enum EngineMessage : ushort
{
    Scene = Networking.EngineMessageIds,
    Spawn,
    Despawn,
    Owner,
    Sync,
    Transform,
    RpcToHost,
    RpcFromHost,
    Ready,
    SpawnStarted,
    SpawnFinished,
    Orphaned,
    OwnershipState,
    TransformAck,
    SectionAdd,
    SectionRemove,
    SectionMove,
    Time,
    Player
}
