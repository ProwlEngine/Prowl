using Prowl.Editor.Inspector;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Editor.Theming;

using Prowl.Editor.GUI;
namespace Prowl.Editor.Projects.Settings;

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
        Origami.Header(paper, "gen_header", $"{EditorIcons.Gear}  General").Underline().Show();

        EditorGUI.SettingsTextField(paper, "gen_company", "Company Name", CompanyName, v => CompanyName = v);
        EditorGUI.SettingsTextField(paper, "gen_product", "Product Name", ProductName, v => ProductName = v);
        EditorGUI.SettingsTextField(paper, "gen_version", "Version", Version, v => Version = v);
    }
}
