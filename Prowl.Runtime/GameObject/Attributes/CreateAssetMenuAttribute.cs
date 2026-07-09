// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

/// <summary>
/// Marks an <see cref="EngineObject"/> subclass so that it appears in the
/// Assets &gt; Create menu and the Project panel context menu.
/// The editor will create a new instance via <c>Activator.CreateInstance</c>,
/// serialize it with Echo, and write it to a file with the given extension.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class CreateAssetMenuAttribute : Attribute
{
    /// <summary>Display name shown in the menu.</summary>
    public string Name { get; }

    /// <summary>File extension including the leading dot (e.g. ".mat").</summary>
    public string Extension { get; set; } = ".asset";

    /// <summary>Optional icon string (e.g. an EditorIcons constant). Empty for default.</summary>
    public string Icon { get; set; } = "";

    /// <summary>Sort order within the menu. Lower values appear first.</summary>
    public int Order { get; set; } = 100;

    public CreateAssetMenuAttribute(string name)
    {
        Name = name;
    }
}
