// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

/// <summary>
/// Sets the glyph shown next to a component header in the Inspector. Use a Font Awesome
/// icon string (e.g. the <c>EditorIcons.Camera</c> constant). Inherited, so base classes
/// provide a default icon for all subclasses.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class ComponentIconAttribute : Attribute
{
    public string Icon { get; }
    public ComponentIconAttribute(string icon) => Icon = icon;
}
