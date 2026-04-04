// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Rosetta;

public enum MissingKeyBehavior
{
    /// <summary>Try the fallback locale first, then return the key itself.</summary>
    ReturnFallback,

    /// <summary>Return the key string as-is.</summary>
    ReturnKey,

    /// <summary>Throw a KeyNotFoundException.</summary>
    Throw
}
