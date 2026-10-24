// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking.Enums;

/// <summary>Where transport I/O runs. Session callbacks always run on the caller.</summary>
public enum NetworkExecution
{
    Auto,
    Inline,
    DedicatedThread
}
