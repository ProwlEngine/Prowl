using System;
using System.Linq;

using Prowl.Editor.Docking;
using Prowl.Editor.Inspector;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Panels;

/// <summary>
/// A floating panel that displays a single component editor.
/// Tracks the component by GameObject Identifier + Component Identifier,
/// so it persists across scene changes, play mode, and editor restarts.
/// </summary>
public class ComponentPopoutPanel : DockPanel
{
    private readonly Guid _goIdentifier;
    private readonly Guid _compIdentifier;
    private readonly string _compTypeName;
    private string _displayTitle;

    public override string Title => _displayTitle;
    public override string Icon => EditorIcons.ArrowUpRightFromSquare;

    public ComponentPopoutPanel(Guid goIdentifier, Guid compIdentifier, string compTypeName)
    {
        _goIdentifier = goIdentifier;
        _compIdentifier = compIdentifier;
        _compTypeName = compTypeName;
        _displayTitle = compTypeName;
    }

    // Parameterless constructor for serialization fallback (won't be useful but prevents crashes)
    public ComponentPopoutPanel() : this(Guid.Empty, Guid.Empty, "Component") { }

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Find the component in the current scene
        var (go, comp) = FindComponent();

        if (comp == null)
        {
            using (paper.Column($"cpop_missing").Size(width, height).Enter())
            {
                paper.Box("cpop_missing_spacer");
                paper.Box("cpop_missing_text")
                    .Height(40)
                    .Text("Component not found in current scene", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleCenter);

                paper.Box("cpop_missing_info")
                    .Height(24)
                    .Text($"Looking for: {_compTypeName}", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 3)
                    .Alignment(TextAlignment.MiddleCenter);

                paper.Box("cpop_missing_spacer2");
            }
            return;
        }

        // Update title with GO name
        _displayTitle = $"{comp.GetType().Name} ({go!.Name})";

        Origami.ScrollView(paper, "cpop_scroll", width, height).Body(() =>
        {
            // Component header
            string compName = comp.GetType().Name;
            var attr = comp.GetType().GetCustomAttributes(typeof(AddComponentMenuAttribute), false)
                .FirstOrDefault() as AddComponentMenuAttribute;
            string icon = attr?.Icon ?? EditorIcons.Cube;

            using (paper.Row("cpop_header").Height(28).ChildLeft(8).RowBetween(6).Enter())
            {
                paper.Box("cpop_icon")
                    .Width(20).Height(28)
                    .Text(icon, font).TextColor(EditorTheme.Purple400)
                    .FontSize(14f).Alignment(TextAlignment.MiddleCenter);

                paper.Box("cpop_name")
                    .Height(28)
                    .Text(compName, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                paper.Box("cpop_go")
                    .Height(28).ChildRight(8)
                    .Text($"on {go.Name}", font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleRight);
            }

            EditorGUI.Separator(paper, "cpop_sep");

            // Draw the component editor
            string compId = $"cpop_{comp.Identifier}";
            var customEditor = CustomEditorRegistry.GetEditor(comp.GetType());
            if (customEditor != null)
            {
                customEditor.OnGUI(paper, compId, comp);
            }
            else
            {
                PropertyGrid.Draw(paper, compId, comp);
            }

            // Draw [Button] methods
            GameObjectInspector.DrawButtonMethods(paper, $"{compId}_btns", comp);
        });
    }

    private (GameObject? go, MonoBehaviour? comp) FindComponent()
    {
        var scene = Scene.Current;
        if (scene == null) return (null, null);

        foreach (var go in scene.AllObjects)
        {
            if (go.Identifier != _goIdentifier) continue;

            foreach (var comp in go.GetComponents<MonoBehaviour>())
            {
                if (comp.Identifier == _compIdentifier)
                    return (go, comp);
            }
            break; // Found the GO but not the component
        }

        return (null, null);
    }

    /// <summary>
    /// Create and open a popout panel for the given component as a floating window.
    /// </summary>
    public static void PopOut(GameObject go, MonoBehaviour comp)
    {
        var panel = new ComponentPopoutPanel(go.Identifier, comp.Identifier, comp.GetType().Name);
        EditorApplication.Instance?.OpenPanelInstance(panel, 350, 400);
    }
}
