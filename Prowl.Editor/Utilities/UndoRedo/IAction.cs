// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Utilities;

public interface IAction
{
    void Execute();
    void UnExecute();
    bool CanExecute();
    bool CanUnExecute();
    bool TryMerge(IAction action);
}
