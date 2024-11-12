// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Utilities;

public sealed class Transaction : IAction, IDisposable
{
    private readonly List<IAction> _actions = [];
    private bool _aborted;
    public bool IsDelayed { get; private set; }

    public Transaction(bool delayed)
    {
        IsDelayed = delayed;
    }

    public void Execute()
    {
        if (!IsDelayed) { IsDelayed = true; return; }
        foreach (IAction action in _actions) action.Execute();
    }

    public void UnExecute()
    {
        foreach (IAction? action in _actions.AsEnumerable().Reverse())
            action.UnExecute();
    }

    public bool CanExecute() => _actions.All(a => a.CanExecute());
    public bool CanUnExecute() => _actions.All(a => a.CanUnExecute());

    public void Add(IAction action) => _actions.Add(action ?? throw new ArgumentNullException(nameof(action)));
    public void Remove(IAction action) => _actions.Remove(action ?? throw new ArgumentNullException(nameof(action)));
    public bool HasActions() => _actions.Count != 0;

    public void Commit() => UndoRedoManager.CommitTransaction();
    public void Rollback() { UndoRedoManager.RollBackTransaction(); _aborted = true; }
    public void Dispose() { if (!_aborted) Commit(); }
}
