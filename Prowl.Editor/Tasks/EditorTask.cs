// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;

namespace Prowl.Editor.Tasks;

public class EditorTask
{
    public virtual async System.Threading.Tasks.Task IdleOnCondition(Func<bool> condition)
    {
        while(!condition())
        {
            await System.Threading.Tasks.Task.Delay(50);
        }
    }

}
