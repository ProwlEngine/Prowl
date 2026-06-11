// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Threading.Tasks;

namespace Prowl.Runtime.Events {

/// <summary>
/// Interface for typed, async invocation of an event handler.
/// Implemented by <see cref="AsyncEventDelegateContainer{T, TArgs}"/> so that
/// <see cref="Event{T}.InvokeAsync{TArgs}"/> can await async handlers while still
/// calling synchronous handlers inline.
/// </summary>
public interface IAsyncInvocable<in TArgs>
{
    Task InvokeAsync(TArgs args);
}

}
