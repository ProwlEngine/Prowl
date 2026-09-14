// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Editor.Core.Tasks;

/// <summary> Base class for editor tasks that provides a utility to asynchronously wait until a condition is met. </summary>
public class EditorTask
{
    /// <summary> Polls the condition every 50ms and returns once it evaluates to true. </summary>
    public virtual async System.Threading.Tasks.Task IdleOnCondition(Func<bool> condition)
    {
        while (!condition())
        {
            await System.Threading.Tasks.Task.Delay(50);
        }
    }

}
