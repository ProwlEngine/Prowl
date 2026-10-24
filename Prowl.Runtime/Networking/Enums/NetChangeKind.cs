// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking.Enums;

/// <summary>What happened to a network collection.</summary>
public enum NetChangeKind : byte
{
    /// <summary>An item was added (to a list, at <c>Index</c>).</summary>
    Added,

    /// <summary>An item was removed. Its old value is given.</summary>
    Removed,

    /// <summary>An item was replaced: a list element or a dictionary value.</summary>
    Replaced,

    /// <summary>Everything was removed.</summary>
    Cleared
}
