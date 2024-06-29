using Prowl.Icons;
using Prowl.Runtime.GUI;
using System;

namespace Prowl.Runtime.Components.UI
{
    [AddComponentMenu($"{FontAwesome6.MoneyCheck}  GUI/{FontAwesome6.WindowMaximize}  GUI Canvas")]
    public class GUICanvas : MonoBehaviour
    {
        public enum Space { Screen}
        public Space space = Space.Screen;
        public Camera TargetCamera;
        public bool DoAntiAliasing = true;

        private Gui gui;

        public event Action<Gui> OnGUI;

        public override void Awake()
        {
            TargetCamera = GetComponent<Camera>();
            if (TargetCamera == null)
            {
                Debug.LogError("Target Camera is not set on GUICanvas.");
                return;
            }
        
            gui = new Gui(DoAntiAliasing);
        
            //if (space == Space.Screen)
            //{
                Input.OnKeyEvent += gui.SetKeyState;
                Input.OnMouseEvent += gui.SetPointerState;
                gui.OnPointerPosSet += (pos) => { Input.MousePosition = pos; };
                gui.OnCursorVisibilitySet += (visible) => { Input.CursorVisible = visible; };
            //}
            //else
            //{
            //    // TODO: World
            //}

            //TargetCamera.PostRender += DoGui;
        }

        /*
        public void DoGui(int width, int height)
        {
            gui.PointerWheel = Input.MouseWheelDelta;

            gui.ProcessFrame(new Rect(0, 0, width, height), 1f, Vector2.one, DoAntiAliasing, (g) => {
                OnGUI?.Invoke(g);
            });
        }
        */
    }
}
