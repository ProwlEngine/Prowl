// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(Color))]
public class Color_PropertyDrawer : PropertyDrawer
{
    private static ColorPickerDialog? s_pickerDialog;

    private ColorPickerContext? _localContext;

    private bool _windowOpen;


    private void OpenPicker(Color color, Rect fitRect)
    {
        _localContext ??= new()
        {
            Title = "Select Color"
        };

        if (!_windowOpen)
            _localContext.Color = color;

        _localContext.HasAlpha = true;

        s_pickerDialog ??= new(_localContext);
        s_pickerDialog.Context = _localContext;

        s_pickerDialog.CenterOn(fitRect);

        EditorGuiManager.FocusWindow(s_pickerDialog);

        _windowOpen = true;
    }


    private bool GetChange()
    {
        if (_windowOpen && s_pickerDialog?.Context != _localContext)
        {
            _windowOpen = false;
            return true;
        }

        return false;
    }


    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
    {
        using (gui.Node("ColorField").Expand().Enter())
        {
            if (s_pickerDialog != null)
                gui.Draw2D.DrawCircle(new Vector2(s_pickerDialog.X, s_pickerDialog.Y), 5, Color.red);

            if (gui.IsNodeHovered() && gui.IsPointerClick(MouseButton.Left))
                OpenPicker((Color)value!, gui.CurrentNode.LayoutData.Rect);

            if (_windowOpen)
                s_pickerDialog.CenterOn(gui.CurrentNode.LayoutData.Rect);

            Color col = (Color)value!;
            Color pure = new Color(col.r, col.g, col.b, 1);
            Color transparent = new Color(1, 1, 1, col.a);

            Rect rect = gui.CurrentNode.LayoutData.Rect;

            gui.Draw2D.DrawRectFilled(rect, pure, (float)EditorStylePrefs.Instance.ButtonRoundness);

            Rect footer = rect;
            footer.y += footer.height - 3;
            footer.height = 3;

            gui.Draw2D.DrawRectFilled(footer, transparent, (float)EditorStylePrefs.Instance.ButtonRoundness, CornerRounding.Bottom);
        }

        if (GetChange())
        {
            value = _localContext!.Color;
            return true;
        }

        return false;
    }
}
