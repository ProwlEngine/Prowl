// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Utilities;

public class AddItemAction<T> : AbstractAction
{
    private readonly Action<T> _add;
    private readonly Action<T> _remove;
    private readonly T _item;

    public AddItemAction(Action<T> add, Action<T> remove, T item) =>
        (_add, _remove, _item) = (add, remove, item);

    protected override void Do() => _add(_item);
    protected override void Undo() => _remove(_item);
}
