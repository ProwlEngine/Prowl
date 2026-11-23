// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>Who receives a networked object. Its owner always does.</summary>
public enum NetworkVisibility : byte
{
    /// <summary>The <see cref="Networking.Interest" /> decides, or every client when there is none.</summary>
    Default,

    /// <summary>Every client receives it, whatever the interest decides.</summary>
    ForceShown,

    /// <summary>Only its owner receives it.</summary>
    ForceHidden
}
