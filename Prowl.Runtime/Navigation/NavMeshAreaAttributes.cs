// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

/// <summary>
/// Draws an int field as a dropdown of the navigation areas defined in project settings
/// (see <see cref="NavMeshAreas"/>), instead of a raw number.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class NavMeshAreaAttribute : Attribute { }

/// <summary>
/// Draws an int field as a multi-select of the navigation areas defined in project settings
/// (like a layer mask), instead of a raw bitmask number.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class NavMeshAreaMaskAttribute : Attribute { }
