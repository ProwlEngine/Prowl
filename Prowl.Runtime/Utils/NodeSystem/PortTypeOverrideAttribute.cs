// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.NodeSystem;

/// <summary> Overrides the ValueType of the Port, to have a ValueType different from the type of its serializable field </summary>
/// <remarks> Especially useful in Dynamic Port Lists to create Value-Port Pairs with different type. </remarks>
[AttributeUsage(AttributeTargets.Field)]
public class PortTypeOverrideAttribute : Attribute
{
    public readonly Type type;
    /// <summary> Overrides the ValueType of the Port </summary>
    /// <param name="type">ValueType of the Port</param>
    public PortTypeOverrideAttribute(Type type)
    {
        this.type = type;
    }
}
