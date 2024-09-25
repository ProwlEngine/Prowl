// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

/// <summary>
/// Indicates that only one instance of the component can be added to a GameObject in the Prowl Game Engine.
/// </summary>
/// <remarks>
/// This attribute can only be applied to classes. When used, it prevents multiple instances of the marked component
/// from being added to the same GameObject in the engine.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public class DisallowMultipleComponentAttribute : Attribute
{
    // This attribute doesn't have any properties or methods.
    // Its presence alone is sufficient to indicate the restriction.
}
