// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Events {

/// <summary>
/// Declares the canonical <c>TArgs</c> type for an event enum value.
/// When present, <see cref="EventManager{T}.AddNewDelegate{TArgs}"/> and
/// <see cref="EventManager{T}.InvokeEvent{TArgs}"/> will assert that the
/// supplied <c>TArgs</c> matches the declared type.
/// <para>
/// Omitting the attribute on a value means "any TArgs is accepted" (opt-in safety).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EventArgsAttribute : Attribute
{
    public Type ArgsType { get; }

    public EventArgsAttribute(Type argsType)
    {
        ArgsType = argsType ?? throw new ArgumentNullException(nameof(argsType));
    }
}

}
