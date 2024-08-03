using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
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
            double ItemSize = EditorStylePrefs.Instance.ItemSize;

            gui.CurrentNode.Layout(Runtime.GUI.LayoutType.Column);
            gui.CurrentNode.ScaleChildren();

            using (gui.Node("Header").ExpandWidth().MaxHeight(ItemSize).Layout(Runtime.GUI.LayoutType.Row).Padding(0, 10, 10, 10).Enter())
            {
                ForwardBackButtons();

                using (gui.Node("LockBtn").Scale(ItemSize).IgnoreLayout().Left(Offset.Percentage(1f, -ItemSize)).Enter())
                {
                    gui.Draw2D.DrawText(lockSelection ? FontAwesome6.Lock : FontAwesome6.LockOpen, gui.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.LesserText, false);

                    if (gui.IsNodePressed())
                    {
                        lockSelection = !lockSelection;

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted);
                    }
                    else if (gui.IsNodeHovered())
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted * 0.8f);
                    }
                }
            }

            using (gui.Node("Content").ExpandWidth().Padding(5, 10, 10, 10).Clip().Scroll().Enter())
            {
                if (Selected == null)
                {
                    gui.Draw2D.DrawRect(gui.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.Warning, 2, (float)EditorStylePrefs.Instance.ButtonRoundness);
                    DrawInspectorLabel("Nothing Selecting.");
                    return;
                }
                if (Selected is EngineObject eo1 && eo1.IsDestroyed)
                {
                    gui.Draw2D.DrawRect(gui.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.Warning, 2, (float)EditorStylePrefs.Instance.ButtonRoundness);
                    DrawInspectorLabel("Object Destroyed.");
                    return;
                }

                bool destroyCustomEditor = true;

                if (Selected is FileInfo path)
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
                    if (customEditor == null)
                    {
                        // Just selected a new object create the editor
                        Type? editorType = CustomEditorAttribute.GetEditor(Selected.GetType());
                        if (editorType != null)
                        {
                            customEditor = (Selected, (ScriptedEditor)Activator.CreateInstance(editorType));
                            customEditor.Value.Item2.target = Selected;
                            customEditor.Value.Item2.OnEnable();
                            destroyCustomEditor = false;
                        }
                        else
                        {
                            // No Editor, Just display Property Grid
                            EditorGUI.PropertyGrid("Default Drawer", ref Selected, EditorGUI.TargetFields.Serializable, EditorGUI.PropertyGridConfig.NoHeader);
                        }
                    }
                    else if (customEditor.Value.Item1 == Selected)
                    {
                        // We are still editing the same object
                        customEditor.Value.Item2.OnInspectorGUI();
                        destroyCustomEditor = false;
                    }
                }

                if (destroyCustomEditor)
                {
                    customEditor?.Item2.OnDisable();
                    customEditor = null;
                }
            }
        }

        private void DrawInspectorLabel(string message)
        {
            double ItemSize = EditorStylePrefs.Instance.ItemSize;

            gui.Node("DummyForText").ExpandWidth().Height(ItemSize * 10);
            gui.Draw2D.DrawText(message, gui.CurrentNode.LayoutData.Rect);
        }

        private void ForwardBackButtons()
        {
            double ItemSize = EditorStylePrefs.Instance.ItemSize;

            // remove nulls or destroyed
            while (_BackStack.Count > 0)
            {
                var peek = _BackStack.Peek();
                if (peek == null || (peek is EngineObject eo2 && eo2.IsDestroyed) || ReferenceEquals(peek, Selected))
                    _BackStack.Pop();
                else
                    break;
            }

            using (gui.Node("BackBtn").Scale(ItemSize).Enter())
            {
                Color backCol = _BackStack.Count == 0 ? Color.white * 0.7f : Color.white;
                gui.Draw2D.DrawText(FontAwesome6.ArrowLeft, gui.CurrentNode.LayoutData.InnerRect, backCol, false);
                if (_BackStack.Count != 0)
                {
                    if (gui.IsNodePressed())
                    {
                        _ForwardStack.Push(Selected);
                        Selected = _BackStack.Pop();

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted);
                    }
                    else if (gui.IsNodeHovered())
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted * 0.8f);
                    }
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

            using (gui.Node("ForwardBtn").Scale(ItemSize).Enter())
            {
                Color forwardCol = _ForwardStack.Count == 0 ? Color.white * 0.7f : Color.white;
                gui.Draw2D.DrawText(FontAwesome6.ArrowRight, gui.CurrentNode.LayoutData.InnerRect, forwardCol, false);

                if (gui.IsNodePressed())
                {
                    if (gui.IsNodePressed())
                    {
                        _BackStack.Push(Selected);
                        Selected = _ForwardStack.Pop();

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted);
                    }
                    else if (gui.IsNodeHovered())
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted * 0.8f);
                    }
                }
            }
        }

    }
}