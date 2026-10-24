// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking.Enums;

/// <summary>Options that disable parts of a network object's default replication behavior.</summary>
[Flags]
public enum NetworkFlags : byte
{
    None = 0,
    NoPositionSync = 1 << 0,
    NoRotationSync = 1 << 1,
    NoScaleSync = 1 << 2,
    NoTransformSync = NoPositionSync | NoRotationSync | NoScaleSync,
    NoInterpolation = 1 << 3
}
