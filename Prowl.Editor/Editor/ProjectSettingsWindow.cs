// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Preferences;
using Prowl.Editor.ProjectSettings;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

using static Prowl.Editor.EditorGUI;

namespace Prowl.Editor;

public class ProjectSettingsWindow : SingletonEditorWindow
{
    public ProjectSettingsWindow() : base("Project Settings") { }

    public ProjectSettingsWindow(Type settingToOpen) : base(settingToOpen, "Project Settings") { }

    public override void RenderSideView()
    {
        RenderSideViewElement(PhysicsSetting.Instance);
        RenderSideViewElement(BuildProjectSetting.Instance);
    }
}

public class PreferencesWindow : SingletonEditorWindow
{
    public PreferencesWindow() : base("Preferences") { }

    public PreferencesWindow(Type settingToOpen) : base(settingToOpen, "Editor Preferences") { }

    public override void RenderSideView()
    {
        RenderSideViewElement(GeneralPreferences.Instance);
        RenderSideViewElement(EditorPreferences.Instance);
        RenderSideViewElement(EditorStylePrefs.Instance);
        RenderSideViewElement(AssetPipelinePreferences.Instance);
        RenderSideViewElement(SceneViewPreferences.Instance);
    }
}

public abstract class SingletonEditorWindow : EditorWindow
{
    protected override double Width { get; } = 512;
    protected override double Height { get; } = 512;

    private Type? currentType;
    private object? currentSingleton;

    public SingletonEditorWindow(string windowTitle) : base() { Title = FontAwesome6.Gear + " " + windowTitle; }

    public SingletonEditorWindow(Type settingToOpen, string windowTitle) : base() { currentType = settingToOpen; Title = FontAwesome6.Gear + " " + windowTitle; }

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        gui.CurrentNode.Layout(LayoutType.Row);
        gui.CurrentNode.ScaleChildren();

        elementCounter = 0;

        using (gui.Node("SidePanel").Padding(5, 10, 10, 10).MaxWidth(150).ExpandHeight().Layout(LayoutType.Column).Spacing(5).Clip().Enter())
        {
            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGTwo, (float)EditorStylePrefs.Instance.WindowRoundness);
            RenderSideView();
        }

        using (gui.Node("ContentPanel").PaddingRight(10).ExpandHeight().Scroll().Enter())
        {
            RenderBody();
        }
    }

    public abstract void RenderSideView();

    private int elementCounter;
    protected void RenderSideViewElement<T>(T elementInstance)
    {
        Type settingType = elementInstance.GetType();
        using (gui.Node("Element" + elementCounter++).ExpandWidth().Height(EditorStylePrefs.Instance.ItemSize).Enter())
        {

            if (currentType == settingType)
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.ButtonRoundness);
            else if (gui.IsNodeHovered())
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);

            // remove 'Preferences'
            string name = settingType.Name.Replace("Preferences", "");
            gui.Draw2D.DrawText(name, gui.CurrentNode.LayoutData.Rect, false);

            if (gui.IsNodePressed() || currentType == settingType)
            {
                currentType = settingType;
                currentSingleton = elementInstance;
            }

        }
    }

    private void RenderBody()
    {
        if (currentType == null) return;

        // Draw Settings
        object? setting = currentSingleton ?? throw new Exception();
        
        string name = currentType.Name.Replace("Preferences", "");
        if (PropertyGrid(name, ref setting, TargetFields.Serializable | TargetFields.Properties, PropertyGridConfig.NoBorder | PropertyGridConfig.NoBackground))
        {
            // Use reflection to find a method "protected void Save()" and OnValidate
            MethodInfo? validateMethod = setting.GetType().GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            validateMethod?.Invoke(setting, null);
            MethodInfo? saveMethod = setting.GetType().GetMethod("Save", BindingFlags.Instance | BindingFlags.Public);
            saveMethod?.Invoke(setting, null);
        }

        // Draw any Buttons
        //EditorGui.HandleAttributeButtons(setting);
    }
}
