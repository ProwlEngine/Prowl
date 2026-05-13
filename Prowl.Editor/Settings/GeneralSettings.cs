using Prowl.Editor.Inspector;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
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

        InspectorRow.Draw(paper, "gen_company", "Company Name", () =>
            Origami.TextField(paper, "gen_company_v", CompanyName,
                v => { CompanyName = v; ProjectSettingsRegistry.SaveAll(); }).Show());

        InspectorRow.Draw(paper, "gen_product", "Product Name", () =>
            Origami.TextField(paper, "gen_product_v", ProductName,
                v => { ProductName = v; ProjectSettingsRegistry.SaveAll(); }).Show());

        InspectorRow.Draw(paper, "gen_version", "Version", () =>
            Origami.TextField(paper, "gen_version_v", Version,
                v => { Version = v; ProjectSettingsRegistry.SaveAll(); }).Show());
    }
}
