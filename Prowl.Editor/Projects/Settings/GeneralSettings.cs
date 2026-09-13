using Prowl.Editor.GUI;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
namespace Prowl.Editor.Projects.Settings;

/// <summary> Project settings for general project information such as company name, product name, version, and the last opened scene. </summary>
[ProjectSettings("General", EditorIcons.Gear, order: 0)]
public class GeneralSettings : ProjectSettingsBase
{
    public string CompanyName = "DefaultCompany";
    public string ProductName = "My Game";
    public string Version = "0.1.0";

    /// <summary>Relative path to the last loaded scene. Restored on project open.</summary>
    public string? LastScenePath;

    /// <summary> Draws the General settings panel in the Project Settings window. </summary>
    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "gen_header", $"{EditorIcons.Gear}  General").Underline().Show();

        EditorGUI.SettingsTextField(paper, "gen_company", "Company Name", CompanyName, v => CompanyName = v);
        EditorGUI.SettingsTextField(paper, "gen_product", "Product Name", ProductName, v => ProductName = v);
        EditorGUI.SettingsTextField(paper, "gen_version", "Version", Version, v => Version = v);
    }
}
