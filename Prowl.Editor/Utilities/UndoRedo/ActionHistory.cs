// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Utilities;

public class ActionHistory : IEnumerable<IAction>
{
    public event EventHandler CollectionChanged;
    public SimpleHistoryNode CurrentState { get; private set; } = new();
    public bool CanMoveForward => CurrentState?.NextAction != null;
    public bool CanMoveBack => CurrentState?.PreviousAction != null;
    public int Length { get; private set; }

    public bool AppendAction(IAction action)
    {
        if (CurrentState.PreviousAction != null && CurrentState.PreviousAction.TryMerge(action))
            return false;

        CurrentState.NextAction = action;
        CurrentState.NextNode = new SimpleHistoryNode(action, CurrentState);
        return true;
    }

    public void MoveForward()
    {
        if (!CanMoveForward) throw new InvalidOperationException("Cannot move forward: at last state.");
        CurrentState.NextAction.Execute();
        CurrentState = CurrentState.NextNode;
        Length++;
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveBack()
    {
        if (!CanMoveBack) throw new InvalidOperationException("Cannot move back: at first state.");
        CurrentState.PreviousAction.UnExecute();
        CurrentState = CurrentState.PreviousNode;
        Length--;
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        CurrentState = new SimpleHistoryNode();
        Length = 0;
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public IEnumerable<IAction> EnumUndoableActions()
    {
        for (SimpleHistoryNode node = CurrentState; node?.PreviousAction != null; node = node.PreviousNode)
            yield return node.PreviousAction;
    }

    public IEnumerable<IAction> EnumRedoableActions()
    {
        for (SimpleHistoryNode node = CurrentState; node?.NextAction != null; node = node.NextNode)
            yield return node.NextAction;
    }

    public IEnumerator<IAction> GetEnumerator() => EnumUndoableActions().GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
