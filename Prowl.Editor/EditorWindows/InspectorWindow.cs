using HexaEngine.ImGuiNET;
using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;

namespace Prowl.Editor.EditorWindows;

public class InspectorWindow : EditorWindow
{
    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoCollapse;

    private Stack<WeakReference> _BackStack = new();
    private Stack<WeakReference> _ForwardStack = new();

    private WeakReference? Selected = null;

    (object, ScriptedEditor)? customEditor;

    public InspectorWindow() : base()
    {
        Title = "Inspector";
        Selection.OnSelectObject += Selection_OnSelectObject;
    }

    private void Selection_OnSelectObject(object n)
    {
        _ForwardStack.Clear();
        if(Selected != null)
            _BackStack.Push(Selected);
        Selected = new WeakReference(n);
    }

    ~InspectorWindow()
    {
        Selection.OnSelectObject -= Selection_OnSelectObject;
    }

    protected override void Draw()
    {

        if (ImGui.BeginMenuBar())
        {
            if(Selection.Count > 1)
                ImGui.Text("Mult-Editing is not currently supported!");


            if (Selected == null) ImGui.Text("No object selected");
            if (Selected.IsAlive == false) ImGui.Text("Object Destroyed");
            else if (Selected.Target is EngineObject eo) ImGui.Text(eo.Name);
            else ImGui.Text(Selected.Target.GetType().ToString());
            ImGui.EndMenuBar();
        }

        if (Selected == null) return;
        if (Selected.IsAlive == false) return;

        // remove nulls or destroyed
        while (_BackStack.Count > 0)
        {
            var peek = _BackStack.Peek();
            if (peek == null || !peek.IsAlive || (peek.Target is EngineObject eObj && eObj.IsDestroyed) || ReferenceEquals(peek, Selected.Target))
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
            if (peek == null || !peek.IsAlive || (peek.Target is EngineObject eObj && eObj.IsDestroyed) || ReferenceEquals(peek, Selected.Target))
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

        if (Selected.Target is EngineObject engineObj)
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
        else if(Selected.Target is FileInfo path)
        {
            if (customEditor == null) 
            {
                string? relativeAssetPath = AssetDatabase.GetRelativePath(path.FullName);
                if (relativeAssetPath != null)
                {
                    // The selected object is a path in our asset database, load its meta data and display a custom editor for the Importer if ones found
                    var id = AssetDatabase.GUIDFromAssetPath(path);
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
                                ImGui.Text("No Editor Found: " + path.FullName);
                            }
                        }
                        else
                        {
                            ImGui.Text("No Meta File: " + path.FullName);
                        }
                    }
                    else
                    {
                        ImGui.Text("File in Assets folder: " + path.FullName);
                    }
                }
                else
                {
                    ImGui.Text("FileInfo: " + path.FullName);
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
