using Prowl.Runtime;
using Prowl.Editor.PropertyDrawers;
using HexaEngine.ImGuiNET;
using System.Numerics;
using System.Reflection;

namespace Prowl.Editor.EditorWindows;

public class ProjectSettingsWindow : EditorWindow {

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

    protected override int Width { get; } = 512;
    protected override int Height { get; } = 512;

    private Type? currentType;

    public ProjectSettingsWindow() : base() { Title = "Project Settings"; }

    public ProjectSettingsWindow(IProjectSetting settingToOpen) : base() { currentType = settingToOpen.GetType(); }

    protected override void Draw()
    {
        const ImGuiTableFlags tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.ContextMenuInBody | ImGuiTableFlags.Resizable;
        Vector2 availableRegion = ImGui.GetContentRegionAvail();
        if (ImGui.BeginTable("MainViewTable", 2, tableFlags, availableRegion))
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None);

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);

            ImGui.BeginChild("SettingTypes");
            if (Project.HasProject) RenderSideView();
            ImGui.EndChild();
            ImGui.TableSetColumnIndex(1);
            ImGui.BeginChild("Settings");
            if (Project.HasProject) RenderBody();
            ImGui.EndChild();

            ImGui.EndTable();
        }
    }

    private void RenderSideView()
    {
        foreach (var settingType in Project.ProjectSettings.GetRegisteredSettingTypes())
            if (ImGui.Selectable(settingType.Name, currentType == settingType))
                currentType = settingType;
    }

    private void RenderBody()
    {
        if (currentType == null) return;

        // Draw Settings
        var setting = Project.ProjectSettings.GetSetting(currentType);

        FieldInfo[] fields = setting.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
        foreach (var field in fields)
        {
            // Dont render if the field has the Hide attribute
            if (!Attribute.IsDefined(field, typeof(HideInInspectorAttribute)))
            {
                var attributes = field.GetCustomAttributes(true);
                var imGuiAttributes = attributes.Where(attr => attr is IImGUIAttri).Cast<IImGUIAttri>();

                foreach (var imGuiAttribute in imGuiAttributes)
                    imGuiAttribute.Draw();

                // Draw the field using PropertyDrawer.Draw
                if (PropertyDrawer.Draw(setting, field))
                    Project.ProjectSettings.Save();

                foreach (var imGuiAttribute in imGuiAttributes)
                    imGuiAttribute.End();
            }
        }

        // Draw any Buttons
        ImGUIButtonAttribute.DrawButtons(setting);
    }
}
