// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Events {

/// <summary>
/// Interface for typed, zero-cast invocation of an event handler.
/// Implemented by <see cref="EventDelegateContainer{T, TArgs}"/> so that
/// callers holding a typed reference can invoke without a runtime type check.
/// </summary>
public interface IInvocable<in TArgs>
{
    void Invoke(TArgs args);
}

}
