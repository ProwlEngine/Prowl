// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Icons;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.SceneManagement;

using Veldrid;

namespace Prowl.Runtime;

[RequireComponent(typeof(Camera))]
[AddComponentMenu($"{FontAwesome6.MoneyCheck}  GUI/{FontAwesome6.WindowMaximize}  GUI Layer")]
public class GuiLayer : MonoBehaviour
{
    public bool DoAntiAliasing = true;

    private Gui gui;
    private Camera targetCamera;

    public void ExecuteGUI(Framebuffer target)
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
            if (targetCamera == null)
            {
                Debug.LogError("Target Camera is not set on GUI Layer.");
                return;
            }
        }

        if(gui == null)
        {
            gui = new Gui(DoAntiAliasing);

            Input.OnKeyEvent += gui.SetKeyState;
            Input.OnMouseEvent += gui.SetPointerState;
            gui.OnPointerPosSet += (pos) => { Input.MousePosition = pos; };
            gui.OnCursorVisibilitySet += (visible) => { Input.CursorVisible = visible; };
        }

        gui.PointerWheel = Input.MouseWheelDelta;

        CommandList list = Graphics.GetCommandList();
        list.SetFramebuffer(target);

        gui.ProcessFrame(list, new Rect(0, 0, target.Width, target.Height), 1f, Vector2.one, DoAntiAliasing, (g) => {
            foreach(GameObject obj in Scene.ActiveObjects)
            {
                foreach (MonoBehaviour comp in obj._components)
                    if (comp.EnabledInHierarchy)
                        comp.OnGUI(gui);
            }
        });

        Graphics.SubmitCommandList(list);

        list.Dispose();
    }
}
