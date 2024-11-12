// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Utilities;

public abstract class AbstractAction : IAction
{
    protected int ExecuteCount { get; set; }

    public virtual void Execute()
    {
        if (CanExecute())
        {
            Do();
            ExecuteCount++;
        }
    }

    public virtual void UnExecute()
    {
        if (CanUnExecute())
        {
            Undo();
            ExecuteCount--;
        }
    }

    protected abstract void Do();
    protected abstract void Undo();

    public virtual bool CanExecute() => ExecuteCount == 0;
    public virtual bool CanUnExecute() => !CanExecute();
}
