using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Editor.Assets;
using Prowl.Editor.PropertyDrawers;
using Prowl.Icons;
using HexaEngine.ImGuiNET;
using System.IO;
using System.Reflection;

namespace Prowl.Editor.EditorWindows;

public class InspectorWindow : EditorWindow
{
    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoCollapse;

    private Stack<object> _BackStack = new();
    private Stack<object> _ForwardStack = new();

    private object Selected;

    (object, ScriptedEditor)? customEditor;

    public InspectorWindow() : base()
    {
        Title = "Inspector";
        Selection.OnSelectionChanged += Selection_OnSelectionChanged;
    }

    private void Selection_OnSelectionChanged(object o, object n)
    {
        _ForwardStack.Clear();
        _BackStack.Push(Selected);
        Selected = n;
    }

    ~InspectorWindow()
    {
        Selection.OnSelectionChanged -= Selection_OnSelectionChanged;
    }

    protected override void Draw()
    {

        if (ImGui.BeginMenuBar())
        {
            if (Selected == null)               ImGui.Text("No object selected");
            else if (Selected is EngineObject)  ImGui.Text((Selection.Current as EngineObject)?.Name);
            else                                ImGui.Text(Selected.GetType().ToString());
            ImGui.EndMenuBar();
        }

        if (Selected is null) return;

        // remove nulls or destroyed
        while (_BackStack.Count > 0)
        {
            var peek = _BackStack.Peek();
            if (peek == null || (peek is EngineObject eObj && eObj.IsDestroyed) || peek == Selected)
                _BackStack.Pop();
            else
                break;
        }

        if (_BackStack.Count == 0) ImGui.BeginDisabled();
        {
            if (ImGui.Button(FontAwesome6.ArrowLeft))
            {
                _ForwardStack.Push(Selected);
                Selected = _BackStack.Pop();
            }
        }
        if (_BackStack.Count == 0) ImGui.EndDisabled();

        ImGui.SameLine();

        // remove nulls or destroyed
        while (_ForwardStack.Count > 0)
        {
            var peek = _ForwardStack.Peek();
            if (peek == null || (peek is EngineObject eObj && eObj.IsDestroyed) || peek == Selected)
                _ForwardStack.Pop();
            else
                break;
        }

        if (_ForwardStack.Count == 0) ImGui.BeginDisabled();
        {
            if (ImGui.Button(FontAwesome6.ArrowRight))
            {
                _BackStack.Push(Selected);
                Selected = _ForwardStack.Pop();
            }
        }
        if (_ForwardStack.Count == 0) ImGui.EndDisabled();

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
                    customEditor.Value.Item2.target = engineObj;
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
        else if(Selected is string path)
        {
            if (customEditor == null) 
            {
                string? relativeAssetPath = AssetDatabase.GetRelativePath(Selected.ToString());
                if (relativeAssetPath != null)
                {
                    // The selected object is a path in our asset database, load its meta data and display a custom editor for the Importer if ones found
                    var id = AssetDatabase.GUIDFromAssetPath(new FileInfo(Selected.ToString()));
                    if (id != Guid.Empty)
                    {
                        var meta = AssetDatabase.LoadMeta(relativeAssetPath);
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
                                ImGui.Text("No Editor Found: " + Selected.ToString());
                            }
                        }
                        else
                        {
                            ImGui.Text("No Meta File: " + Selected.ToString());
                        }
                    }
                    else
                    {
                        ImGui.Text("File in Assets folder: " + Selected.ToString());
                    }
                }
                else
                {
                    ImGui.Text("String: " + Selected.ToString());
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
            ImGui.Text("Object: " + Selected != null ? Selected.ToString() : "Null");
        }

        if (destroyCustomEditor)
        {
            customEditor?.Item2.OnDisable();
            customEditor = null;
        }

    }

}
