using System.Collections.Generic;

using Prowl.Editor.GUI;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
namespace Prowl.Editor.Projects.Settings;

/// <summary> Project settings for general project information such as company name, product name, version, and the last opened scene. </summary>
[ProjectSettings("General", EditorIcons.Gear, order: 0)]
public class GeneralSettings : ProjectSettingsBase
{
    public string CompanyName = "DefaultCompany";
    public string ProductName = "My Game";
    public string Version = "0.1.0";

    /// <summary>The icon of the built game, or the Prowl logo when empty.</summary>
    public AssetRef<Texture2D> Icon;

    /// <summary>The scenes open when the project was last used, restored on project open.</summary>
    public List<GUI.SceneView.SceneSetup> LastSetup = [];

    /// <summary>The scenes hidden in the scene view, restored with the setup.</summary>
    public List<System.Guid> HiddenScenes = [];

    /// <summary> Draws the General settings panel in the Project Settings window. </summary>
    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "gen_header", $"{EditorIcons.Gear}  General").Underline().Show();

        EditorGUI.SettingsTextField(paper, "gen_company", "Company Name", CompanyName, v => CompanyName = v);
        EditorGUI.SettingsTextField(paper, "gen_product", "Product Name", ProductName, v => ProductName = v);
        EditorGUI.SettingsTextField(paper, "gen_version", "Version", Version, v => Version = v);

        using (paper.Column("gen_icon_tip").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Tooltip("The icon of the built game, on its program file and in its window. It has to be square and at least 16 pixels wide. When empty, the game gets the Prowl logo.").Enter())
        {
            PropertyGridUtils.DrawField(paper, "gen_icon", "Icon", typeof(AssetRef<Texture2D>), Icon, v =>
            {
                Icon = (AssetRef<Texture2D>)v!;
                EditorRegistries.SaveSettings();
            });
        }
    }
}
