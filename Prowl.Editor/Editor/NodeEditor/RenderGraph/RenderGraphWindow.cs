using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.RenderPipelines;

namespace Prowl.Editor
{
    public class RenderGraphWindow : EditorWindow
    {
        NodeEditor editor;
        NodeGraph graph;
        double saveTimer = 0;
        bool doSave = false;

        protected override double Width => 800;
        protected override double Height => 600;

        public RenderGraphWindow() : base()
        {
            Title = FontAwesome6.FolderTree + " Render Graph";
        }

        protected override void Draw()
        {
            using (gui.Node("Root").Expand().Margin(5).Enter())
            {
                //bool isFocused = gui.IsNodeHovered() && (gui.IsNodePressed() || gui.IsNodeFocused());
                bool isFocused = gui.IsNodeHovered();

                var innerRect = gui.CurrentNode.LayoutData.InnerRect;
                if (editor == null)
                {
                    gui.Draw2D.DrawRect(innerRect, Color.red, 2);
                    gui.Draw2D.DrawText(Font.DefaultFont, "No Graph Assigned", 40f, innerRect, Color.red);
                }
                else
                {
                    var tex = editor.Update(gui, isFocused, innerRect.Position, (uint)innerRect.width, (uint)innerRect.height, out var changed);

                    if (Graphics.Device.IsClipSpaceYInverted)
                    {
                        gui.Draw2D.DrawImage(tex, innerRect.Position, innerRect.Size, new(0, 0), new(1, 1), Color.white);
                    }
                    else
                    {
                        gui.Draw2D.DrawImage(tex, innerRect.Position, innerRect.Size, new(0, 1), new(1, 0), Color.white);
                    }

                    changed |= editor.DrawBlackBoard(gui);

                    if (!editor.IsDragging)
                    {
                        editor.DrawContextMenu(gui);
                    }

                    if (changed)
                    {
                        graph.Validate();
                        saveTimer = 0.25;
                        doSave = true;
                    }

                    if (doSave)
                    {
                        saveTimer -= Time.deltaTime;
                        if (saveTimer <= 0)
                        {
                            doSave = false;
                            if (AssetDatabase.TryGetFile(graph.AssetID, out var assetFile))
                            {
                                StringTagConverter.WriteToFile(Serializer.Serialize(graph), assetFile);
                                AssetDatabase.Reimport(assetFile, false);
                            }
                        }
                    }
                }
            }

            if (DragnDrop.Drop<RenderPipeline>(out var rp))
            {
                editor?.Release();
                editor = new(rp);
                graph = rp;
            }
        }

    }
}
