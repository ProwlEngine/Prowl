// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Utilities;

public class SimpleHistoryNode
{
    public SimpleHistoryNode(IAction lastAction = null, SimpleHistoryNode lastNode = null)
    {
        PreviousAction = lastAction;
        PreviousNode = lastNode;
    }

    public IAction PreviousAction { get; set; }
    public IAction NextAction { get; set; }
    public SimpleHistoryNode PreviousNode { get; set; }
    public SimpleHistoryNode NextNode { get; set; }
}
