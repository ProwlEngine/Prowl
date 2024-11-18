// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

namespace Prowl.Editor.Utilities;

public abstract class AbstractAction : IAction
{
    protected int ExecuteCount { get; set; }

    public virtual void Execute()
    {
        try
        {
            if (CanExecute())
            {
                Do();
                ExecuteCount++;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Action.Do() Failed: " + e.Message);
        }
    }

    public virtual void UnExecute()
    {
        try
        {
            if (CanUnExecute())
            {
                Undo();
                ExecuteCount--;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Action.Undo() Failed: " + e.Message);
        }
    }

    protected abstract void Do();
    protected abstract void Undo();

    public virtual bool CanExecute() => ExecuteCount == 0;
    public virtual bool CanUnExecute() => !CanExecute();
}
