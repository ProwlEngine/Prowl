// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Utilities;

public class CallMethodAction : AbstractAction
{
    private readonly Action _execute;
    private readonly Action _unexecute;

    public CallMethodAction(Action execute, Action unexecute) =>
        (_execute, _unexecute) = (execute, unexecute);

    protected override void Do() => _execute?.Invoke();
    protected override void Undo() => _unexecute?.Invoke();
}
