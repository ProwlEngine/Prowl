using Hexa.NET.ImGui;
using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;

namespace Prowl.Editor.EditorWindows;

public class InspectorWindow : EditorWindow
{
    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoCollapse;

    private Stack<object> _BackStack = new();
    private Stack<object> _ForwardStack = new();

    private object? Selected = null;

    (object, ScriptedEditor)? customEditor;

    public InspectorWindow() : base()
    {
        Title = FontAwesome6.BookOpen + " Inspector";
        GlobalSelectHandler.OnGlobalSelectObject += Selection_OnSelectObject;
    }

    private void Selection_OnSelectObject(object n)
    {
        if (n is DirectoryInfo) return; // Dont care about directories

        if(n is IAssetRef asset)
            n = asset.GetInstance();

        if (n is WeakReference weak) n = weak.Target;

        if (n == null) return;

        _ForwardStack.Clear();
        if(Selected != null)
            _BackStack.Push(Selected);
        Selected = n;
    }

    protected override void Close()
    {
        GlobalSelectHandler.OnGlobalSelectObject -= Selection_OnSelectObject;
    }

    protected override void Draw()
    {
        ForwardBackButtons();
        ImGui.Separator();
        ImGui.Spacing();

        if (Selected == null) return;
        if (Selected is EngineObject eo1 && eo1.IsDestroyed) return;

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
            if (peek == null || (peek is EngineObject eo3 && eo3.IsDestroyed) || ReferenceEquals(peek, Selected))
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
    }
}
