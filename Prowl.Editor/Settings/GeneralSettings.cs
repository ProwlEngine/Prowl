using Prowl.Editor.Widgets;
using Prowl.PaperUI;

namespace Prowl.Editor;

[ProjectSettings("General", EditorIcons.Gear, order: 0)]
public class GeneralSettings : ProjectSettingsBase
{
    public string CompanyName = "DefaultCompany";
    public string ProductName = "My Game";
    public string Version = "0.1.0";

    /// <summary>Relative path to the last loaded scene. Restored on project open.</summary>
    public string? LastScenePath;

    public override void OnGUI(Paper paper, float width)
    {
        EditorGUI.Header(paper, "gen_header", $"{EditorIcons.Gear}  General");
        EditorGUI.Separator(paper, "gen_sep");

        EditorGUI.TextField(paper, "gen_company", "Company Name", CompanyName)
            .OnValueChanged(v => { CompanyName = v; ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.TextField(paper, "gen_product", "Product Name", ProductName)
            .OnValueChanged(v => { ProductName = v; ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.TextField(paper, "gen_version", "Version", Version)
            .OnValueChanged(v => { Version = v; ProjectSettingsRegistry.SaveAll(); });
    }
}
