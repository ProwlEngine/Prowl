// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.NodeSystem;

namespace Prowl.Editor;


public class BlueprintWindow : EditorWindow
{
    NodeEditor _editor;
    NodeGraph _graph;
    double _saveTimer = 0;
    bool _doSave = false;

    protected override double Width => 800;
    protected override double Height => 600;

    public BlueprintWindow() : base()
    {
        Title = FontAwesome6.FolderTree + " Blueprint";
    }

    protected override void Draw()
    {
        using (gui.Node("Root").Expand().Margin(5).Enter())
        {
            //bool isFocused = gui.IsNodeHovered() && (gui.IsNodePressed() || gui.IsNodeFocused());
            bool isFocused = gui.IsNodeHovered();

            Rect innerRect = gui.CurrentNode.LayoutData.InnerRect;
            if (_editor == null)
            {
                gui.Draw2D.DrawRect(innerRect, Color.red, 2);
                gui.Draw2D.DrawText(Font.DefaultFont, "No Blueprint Assigned", 40f, innerRect, Color.red);
            }
            else
            {
                Runtime.Rendering.Texture2D tex = _editor.Update(gui, isFocused, innerRect.Position, (uint)innerRect.width, (uint)innerRect.height, out var changed);

                if (Graphics.Device.IsClipSpaceYInverted)
                {
                    gui.Draw2D.DrawImage(tex, innerRect.Position, innerRect.Size, new(0, 0), new(1, 1), Color.white);
                }
                else
                {
                    gui.Draw2D.DrawImage(tex, innerRect.Position, innerRect.Size, new(0, 1), new(1, 0), Color.white);
                }

                changed |= _editor.DrawBlackBoard(gui);

                if (!_editor.IsDragging)
                {
                    _editor.DrawContextMenu(gui);
                }

                if (changed)
                {
                    _graph.Validate();
                    _saveTimer = 0.25;
                    _doSave = true;
                }

                if (_doSave)
                {
                    _saveTimer -= Time.deltaTime;
                    if (_saveTimer <= 0)
                    {
                        _doSave = false;
                        if (AssetDatabase.TryGetFile(_graph.AssetID, out FileInfo? assetFile))
                        {
                            StringTagConverter.WriteToFile(Serializer.Serialize(_graph), assetFile!);
                            AssetDatabase.Reimport(assetFile!, false);
                        }
                    }
                }
            }
        }

        if (DragnDrop.Drop<Blueprint>(out Blueprint? rp))
        {
            _editor?.Release();
            _editor = new(rp!);
            _graph = rp!;
        }
    }

}
