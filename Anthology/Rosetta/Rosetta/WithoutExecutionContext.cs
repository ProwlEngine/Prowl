// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Rosetta;

internal static class WithoutExecutionContext
{
    /// <summary>
    /// Runs <paramref name="start"/> so that a timer or watcher it starts does not copy the caller's execution
    /// context, which would keep every AsyncLocal value of the caller alive for as long as it runs.
    /// </summary>
    public static void Run(Action start)
    {
        // SuppressFlow throws when flow is already suppressed.
        if (ExecutionContext.IsFlowSuppressed())
        {
            start();
            return;
        }

        using (ExecutionContext.SuppressFlow())
            start();
    }
}
