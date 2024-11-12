// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Runtime.Utils;

namespace Prowl.Editor.Utilities;

public static class UndoRedoManager
{
    public static event EventHandler CollectionChanged;
    public static IAction? CurrentAction { get; private set; }
    public static bool CanUndo => _history.CanMoveBack;
    public static bool CanRedo => _history.CanMoveForward;
    public static bool ExecuteImmediatelyWithoutRecording { get; set; }

    private static readonly Stack<Transaction> _transactions = new();
    private static ActionHistory _history = new();

    public static void RecordAction(IAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CurrentAction != null) throw new InvalidOperationException($"Action '{action}' cannot be recorded while '{CurrentAction}' is executing.");

        if (ExecuteImmediatelyWithoutRecording && action.CanExecute())
        {
            action.Execute();
            return;
        }

        if (_transactions.TryPeek(out Transaction? transaction))
        {
            transaction.Add(action);
            if (!transaction.IsDelayed) action.Execute();
            return;
        }

        CurrentAction = action;
        try
        {
            if (_history.AppendAction(action)) _history.MoveForward();
        }
        finally { CurrentAction = null; }
    }

    public static Transaction CreateTransaction(bool delayed = true)
    {
        var transaction = new Transaction(delayed);
        _transactions.Push(transaction);
        return transaction;
    }

    public static void CommitTransaction()
    {
        if (!_transactions.TryPop(out Transaction? transaction))
            throw new InvalidOperationException("No open transaction to commit.");
        if (transaction.HasActions()) RecordAction(transaction);
    }

    public static void RollBackTransaction()
    {
        if (_transactions.TryPeek(out Transaction? transaction))
        {
            transaction?.UnExecute();
            _transactions.Clear();
        }
    }

    public static void Undo()
    {
        if (!CanUndo || CurrentAction != null) return;
        CurrentAction = _history.CurrentState.PreviousAction;
        _history.MoveBack();
        CurrentAction = null;
    }

    public static void Redo()
    {
        if (!CanRedo || CurrentAction != null) return;
        CurrentAction = _history.CurrentState.NextAction;
        _history.MoveForward();
        CurrentAction = null;
    }

    [OnAssemblyLoad, OnAssemblyUnload, OnSceneLoad, OnSceneUnload, OnPlaymodeChanged]
    public static void Clear()
    {
        _history.Clear();
        CurrentAction = null;
    }

    public static void SetMember(object target, string field, object? fieldValue) => RecordAction(new SetMember(target, field.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new InvalidOperationException($"Field '{field}' not found on '{target}'"), fieldValue));
    public static void SetMember(object target, FieldInfo field, object? fieldValue) => RecordAction(new SetMember(target, field, fieldValue));
    public static void SetMember(object target, MemberInfo field, object? fieldValue) => RecordAction(new SetMember(target, field, fieldValue));
    public static void AddOrRemoveItem<T>(Action<T> perform, Action<T> undo, T item) => RecordAction(new AddItemAction<T>(perform, undo, item));
    public static void CallMethod(Action perform, Action undo) => RecordAction(new CallMethodAction(perform, undo));
}
