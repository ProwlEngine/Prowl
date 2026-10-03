// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.Motion;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// Builds a ragdoll of the humanoid character selected in the scene: a copy of it with its animators
/// removed and a body, collider and joints per body part.
/// </summary>
public class RagdollGeneratorWindow : DockPanel
{
    [MenuItem("Window/Tools/Ragdoll Generator", priority: 201)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(RagdollGeneratorWindow));

    private RagdollBuilder.Settings _settings = new();

    // The selected character's bones, found again only when the selection changes.
    private Animator? _checked;
    private Dictionary<HumanBodyBone, Transform>? _bones;
    private string _status = string.Empty;

    public override string Title => "Ragdoll Generator";
    public override string Icon => EditorIcons.PersonFalling;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        Animator? animator = SelectedCharacter();
        if (!ReferenceEquals(animator, _checked)) Check(animator);
        bool canCopy = _bones != null;
        string status = _status;

        using (paper.Column("rg_root").Width(width).Height(height).Padding(12, 12, 8, 12).Enter())
        {
            EditorGUI.SectionHeader(paper, "rg_char_h", "Character", first: true);
            paper.Box("rg_status").Height(24)
                .Text(status, font).TextColor(canCopy ? EditorTheme.Ink400 : EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            EditorGUI.SectionHeader(paper, "rg_set_h", "Settings");
            EditorGUI.SettingsRow(paper, "rg_mass", "Total Mass", () =>
                Origami.NumericField<float>(paper, "rg_mass_v", _settings.TotalMass, v => _settings = _settings with { TotalMass = v > 0f ? v : 0.01f }).Show());
            EditorGUI.SettingsToggle(paper, "rg_extra", "Hands and Feet", _settings.HandsAndFeet, v => _settings = _settings with { HandsAndFeet = v });

            using (paper.Row("rg_buttons").Height(28).Margin(0, 0, 12, 0).Enter())
                Origami.Button(paper, "rg_copy", "Generate Ragdoll", () => GenerateCopy(animator!))
                    .Primary().Disabled(!canCopy).Width(UnitValue.Stretch()).Show();

            paper.Box("rg_hint").Height(UnitValue.Auto).Margin(0, 0, 8, 0)
                .Text("Makes a limp copy of the character with its animators removed. For an animated character, add a Ragdoll node to its graph instead, which makes its own bodies.", font)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Wrap(Scribe.TextWrapMode.Wrap);
        }
    }

    private void Check(Animator? animator)
    {
        _checked = animator;
        _bones = null;
        if (animator.IsNotValid())
        {
            _status = "Select a character with an Animator in the scene.";
            return;
        }

        Dictionary<HumanBodyBone, Transform>? bones = RagdollBuilder.FindBones(animator!, out string problem);
        if (bones == null) _status = problem;
        else if (RagdollBuilder.HasRagdoll(bones)) _status = $"'{animator!.GameObject.Name}' already has a ragdoll.";
        else
        {
            _bones = bones;
            _status = $"'{animator!.GameObject.Name}' is ready.";
        }
    }

    private static Animator? SelectedCharacter()
    {
        GameObject? selected = Selection.GetSelected<GameObject>().FirstOrDefault();
        return selected.IsValid() ? selected!.GetComponentInParent<Animator>() : null;
    }

    private void GenerateCopy(Animator animator)
    {
        GameObject? copy = GameObjectClipboard.Duplicate([animator.GameObject]).FirstOrDefault();
        if (copy.IsNotValid()) return;

        copy!.Name = animator.GameObject.Name + " Ragdoll";
        // Beside the original, so the two do not overlap.
        copy.Transform.Position += animator.Transform.Right;

        Animator? copied = copy.GetComponent<Animator>();
        Dictionary<HumanBodyBone, Transform>? bones = copied.IsValid() ? RagdollBuilder.FindBones(copied!, out _) : null;
        if (bones != null)
            RagdollBuilder.Build(copy.Transform, bones, _settings with { Avatar = copied!.Avatar });

        foreach (Animator stripped in copy.GetComponentsInChildren<Animator>().ToList())
            stripped.GameObject.RemoveComponent(stripped);

        Undo.RegisterCreatedObject(copy, "Generate Ragdoll");
        Selection.Select(copy);
        EditorSceneManager.MarkDirty();
    }
}
