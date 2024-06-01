using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;
using System.IO;

namespace Prowl.Editor
{
    public class InspectorWindow : EditorWindow
    {

        private Stack<object> _BackStack = new();
        private Stack<object> _ForwardStack = new();

        private object? Selected = null;
        private bool lockSelection = false;

        (object, ScriptedEditor)? customEditor;

        public InspectorWindow() : base()
        {
            Title = FontAwesome6.BookOpen + " Inspector";
            GlobalSelectHandler.OnGlobalSelectObject += Selection_OnSelectObject;
        }


        private void Selection_OnSelectObject(object n)
        {
            if (lockSelection) return;

            if (n is DirectoryInfo) return; // Dont care about directories

            if (n is IAssetRef asset)
                n = asset.GetInstance();

            if (n is WeakReference weak) n = weak.Target;

            if (n == null) return;

            _ForwardStack.Clear();
            if (Selected != null)
                _BackStack.Push(Selected);
            Selected = n;
        }

        protected override void Close()
        {
            GlobalSelectHandler.OnGlobalSelectObject -= Selection_OnSelectObject;
        }

        protected override void Draw()
        {
            g.CurrentNode.Layout(Runtime.GUI.LayoutType.Column);
            g.CurrentNode.ScaleChildren();

            using (g.Node("Header").ExpandWidth().MaxHeight(GuiStyle.ItemHeight).Layout(Runtime.GUI.LayoutType.Row).Padding(0, 10, 10, 10).Enter())
            {
                ForwardBackButtons();

                using (g.ButtonNode("LockBtn", out var lockPressed, out var lockHovered).Scale(GuiStyle.ItemHeight).IgnoreLayout().Left(Offset.Percentage(1f, -GuiStyle.ItemHeight)).Enter())
                {
                    g.DrawText(lockSelection ? FontAwesome6.Lock : FontAwesome6.LockOpen, g.CurrentNode.LayoutData.InnerRect, GuiStyle.Base4, false);

                    if (lockPressed)
                    {
                        lockSelection = !lockSelection;

                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo);
                    }
                    else if (lockHovered)
                    {
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo * 0.8f);
                    }
                }
            }

            using (g.Node("Content").ExpandWidth().Padding(5, 10, 10, 10).Enter())
            {
                if (Selected == null)
                {
                    g.DrawRect(g.CurrentNode.LayoutData.InnerRect, GuiStyle.Red, 2, 6);
                    DrawInspectorLabel("Nothing Selecting.");
                    return;
                }
                if (Selected is EngineObject eo1 && eo1.IsDestroyed)
                {
                    g.DrawRect(g.CurrentNode.LayoutData.InnerRect, GuiStyle.Red, 2, 6);
                    DrawInspectorLabel("Object Destroyed.");
                    return;
                }

                bool destroyCustomEditor = true;

                if (Selected is EngineObject engineObj)
                {
                    if (customEditor == null)
                    {
                        // Just selected a new object create the editor
                        Type? editorType = CustomEditorAttribute.GetEditor(engineObj.GetType());
                        if (editorType != null)
                        {
                            customEditor = (engineObj, (ScriptedEditor)Activator.CreateInstance(editorType));
                            customEditor.Value.Item2.target = Selected;
                            customEditor.Value.Item2.OnEnable();
                            destroyCustomEditor = false;
                        }
                    }
                    else if (customEditor.Value.Item1 == engineObj)
                    {
                        // We are still editing the same object
                        customEditor.Value.Item2.OnInspectorGUI();
                        destroyCustomEditor = false;
                    }
                }
                else if (Selected is FileInfo path)
                {
                    if (customEditor == null)
                    {
                        string? relativeAssetPath = AssetDatabase.GetRelativePath(path.FullName);
                        if (relativeAssetPath != null)
                        {
                            // The selected object is a path in our asset database, load its meta data and display a custom editor for the Importer if ones found
                            if (AssetDatabase.TryGetGuid(path, out var id))
                            {
                                var meta = MetaFile.Load(path);
                                if (meta != null)
                                {
                                    Type? editorType = CustomEditorAttribute.GetEditor(meta.importer.GetType());
                                    if (editorType != null)
                                    {
                                        customEditor = (path, (ScriptedEditor)Activator.CreateInstance(editorType));
                                        customEditor.Value.Item2.target = meta;
                                        customEditor.Value.Item2.OnEnable();
                                        destroyCustomEditor = false;
                                    }
                                    else
                                    {
                                        // Dummy Node
                                        DrawInspectorLabel("No Editor Found: " + path.FullName);
                                    }
                                }
                                else
                                {
                                    DrawInspectorLabel("No Meta File: " + path.FullName);
                                }
                            }
                            else
                            {
                                DrawInspectorLabel("File in Assets folder: " + path.FullName);
                            }
                        }
                        else
                        {
                            DrawInspectorLabel("FileInfo: " + path.FullName);
                        }
                    }
                    else if (customEditor.Value.Item1.Equals(path))
                    {
                        // We are still editing the same asset path
                        customEditor.Value.Item2.OnInspectorGUI();
                        destroyCustomEditor = false;
                    }
                }
                else
                {
                    DrawInspectorLabel("Object: " + Selected != null ? Selected.ToString() : "Null");
                }

                if (destroyCustomEditor)
                {
                    customEditor?.Item2.OnDisable();
                    customEditor = null;
                }

                g.ScrollV();
            }
        }

        private void DrawInspectorLabel(string message)
        {
            g.Node("DummyForText").ExpandWidth().Height(GuiStyle.ItemHeight * 10);
            g.DrawText(message, g.CurrentNode.LayoutData.Rect);
        }

        private void ForwardBackButtons()
        {
            // remove nulls or destroyed
            while (_BackStack.Count > 0)
            {
                var peek = _BackStack.Peek();
                if (peek == null || (peek is EngineObject eo2 && eo2.IsDestroyed) || ReferenceEquals(peek, Selected))
                    _BackStack.Pop();
                else
                    break;
            }

            LayoutNode backNode;
            bool backNodePressed = false;
            bool backNodeHovered = false;
            if (_BackStack.Count == 0)
                backNode = g.Node("BackBtn");
            else 
                backNode = g.ButtonNode("BackBtn", out backNodePressed, out backNodeHovered);

            using (backNode.Scale(GuiStyle.ItemHeight).Enter())
            {
                Color backCol = _BackStack.Count == 0 ? Color.white * 0.7f : Color.white;
                g.DrawText(FontAwesome6.ArrowLeft, g.CurrentNode.LayoutData.InnerRect, backCol, false);

                if (backNodePressed)
                {
                    _ForwardStack.Push(Selected);
                    Selected = _BackStack.Pop();

                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo);
                }
                else if (backNodeHovered)
                {
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo * 0.8f);
                }
            }


            // remove nulls or destroyed
            while (_ForwardStack.Count > 0)
            {
                var peek = _ForwardStack.Peek();
                if (peek == null || (peek is EngineObject eo3 && eo3.IsDestroyed) || ReferenceEquals(peek, Selected))
                    _ForwardStack.Pop();
                else
                    break;
            }

            LayoutNode forwardNode;
            bool forwardNodePressed = false;
            bool forwardNodeHovered = false;
            if (_ForwardStack.Count == 0)
                forwardNode = g.Node("ForwardBtn");
            else
                forwardNode = g.ButtonNode("ForwardBtn", out forwardNodePressed, out forwardNodeHovered);

            using (forwardNode.Scale(GuiStyle.ItemHeight).Enter())
            {
                Color forwardCol = _ForwardStack.Count == 0 ? Color.white * 0.7f : Color.white;
                g.DrawText(FontAwesome6.ArrowRight, g.CurrentNode.LayoutData.InnerRect, forwardCol, false);

                if (forwardNodePressed)
                {
                    _BackStack.Push(Selected);
                    Selected = _ForwardStack.Pop();

                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo);
                }
                else if (forwardNodeHovered)
                {
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo * 0.8f);
                }
            }
        }

    }
}