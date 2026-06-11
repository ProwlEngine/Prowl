// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Events {

/// <summary>
/// Implement on event argument types to allow handlers to stop further propagation.
/// When a handler sets <see cref="Cancelled"/> to <c>true</c>, subsequent handlers
/// in the priority chain are skipped.
/// </summary>
public interface ICancellable
{
    bool Cancelled { get; set; }
}

}
