using Prowl.Runtime;
using Veldrid;

namespace Prowl.Editor;

public class NodeEditorInputHandler : DefaultInputHandler
{
    public Vector2Int position;
    public bool IsFocused;


    public NodeEditorInputHandler() : base() { }


    protected override Vector2Int GetActualMousePosition(InputSnapshot snapshot)
    {   
        return base.GetActualMousePosition(snapshot) - position;
    }

    protected override void SetActualMousePosition(Vector2Int pos)
    {
        base.SetActualMousePosition(pos + position);
    }

    protected override bool WantsCursorRelease(Vector2Int mouse)
    {
        return base.WantsCursorRelease(mouse) || !IsFocused;
    }

    protected override bool CanUpdateState() => IsFocused;
}