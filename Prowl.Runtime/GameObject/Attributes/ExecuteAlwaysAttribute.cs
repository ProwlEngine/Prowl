// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

/// <summary>
/// Indicates that the marked component should execute its update methods even when the game is not in play mode in the Prowl Game Engine.
/// </summary>
/// <remarks>
/// This attribute can only be applied to classes and cannot be used multiple times on the same class.
/// When applied to a MonoBehaviour or a similar component, it allows the component to update and function
/// in the editor, enabling real-time behavior and feedback even when the game is not running.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ExecuteAlwaysAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the ExecuteAlwaysAttribute class.
    /// </summary>
    public ExecuteAlwaysAttribute()
    {
        // This constructor doesn't need to do anything as the attribute's presence alone is sufficient.
    }
}
