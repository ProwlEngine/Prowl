using Prowl.PaperUI;

namespace Prowl.Editor.Projects.Settings;

/// <summary> Attribute to mark a class as a project settings page. The Name, Icon, Order and ExportToBuild properties control how the page appears in the Project Settings panel. </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false)]
public class ProjectSettingsAttribute : System.Attribute
{
    public string Name { get; }
    public string Icon { get; }
    public int Order { get; }
    public bool ExportToBuild { get; }

    public ProjectSettingsAttribute(string name, string icon = "", int order = 100, bool exportToBuild = true)
    {
        Name = name; Icon = icon; Order = order; ExportToBuild = exportToBuild;
    }
}

public enum SerializerType { Standard, Echo }

/// <summary> Abstract base class for project settings pages. Subclasses are discovered via ProjectSettingsAttribute and shown in the Project Settings panel. </summary>
public abstract class ProjectSettingsBase
{
    public virtual bool DrawInProjectSettingsPanel => true;
    public virtual void Apply() { }
    public virtual void ResetToDefaults() { }
    /// <summary> Draws the settings UI inside the Project Settings panel. paper is the Origami Paper to draw into, width is the available content width. </summary>
    public abstract void OnGUI(Paper paper, float width);
}
