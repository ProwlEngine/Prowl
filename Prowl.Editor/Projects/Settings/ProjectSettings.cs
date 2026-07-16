using Prowl.PaperUI;

namespace Prowl.Editor.Projects.Settings;

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

public abstract class ProjectSettingsBase
{
    public virtual bool DrawInProjectSettingsPanel => true;
    public virtual void Apply() { }
    public virtual void ResetToDefaults() { }
    public abstract void OnGUI(Paper paper, float width);
}
