using Prowl.Editor;

using Veldrid;
using Veldrid.Sdl2;

namespace Prowl.Runtime;

public class GameViewInputHandler : DefaultInputHandler
{
    private GameWindow _window;
    public bool HasFocus => GameWindow.LastFocused.Target == _window;


    public GameViewInputHandler(GameWindow window) : base()
    {
        _window = window;
    }


    protected override Vector2Int GetActualMousePosition(InputSnapshot snapshot)
    {
        return base.GetActualMousePosition(snapshot) - new Vector2Int((int)GameWindow.FocusedPosition.x, (int)GameWindow.FocusedPosition.y);
    }

    protected override void SetActualMousePosition(Vector2Int pos)
    {
        base.SetActualMousePosition(pos + new Vector2Int((int)GameWindow.FocusedPosition.x, (int)GameWindow.FocusedPosition.y));
    }

    protected override bool WantsCursorRelease(Vector2Int mouse)
    {
        return base.WantsCursorRelease(mouse) || !HasFocus;
    }

    protected override bool CanUpdateState() => HasFocus;
}
