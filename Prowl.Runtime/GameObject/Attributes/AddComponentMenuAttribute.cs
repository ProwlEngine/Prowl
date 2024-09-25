// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

/// <summary>
/// Specifies the menu path for adding a component in the Prowl Game Engine's editor interface.
/// This attribute is used to organize components in the "Add Component" menu.
/// </summary>
/// <remarks>
/// This attribute can only be applied to classes and cannot be used multiple times on the same class.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class AddComponentMenuAttribute : Attribute
{
    /// <summary>
    /// Gets the menu path where the component should appear in the "Add Component" menu.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Initializes a new instance of the AddComponentMenuAttribute class.
    /// </summary>
    /// <param name="path">The menu path where the component should appear in the "Add Component" menu.</param>
    public AddComponentMenuAttribute(string path)
    {
        Path = path;
    }
}
