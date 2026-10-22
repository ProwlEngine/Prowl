// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking;

/// <summary>Describes how a <see cref="SyncAttribute" /> property is synchronized.</summary>
[Flags]
public enum SyncFlags : uint
{
    None = 0,

    /// <summary>Only the host may change the value, even on an object a client owns.</summary>
    FromHost = 1
}
