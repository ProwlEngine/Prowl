using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
using Shader = Prowl.Runtime.Resources.Shader;
using Material = Prowl.Runtime.Resources.Material;
namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Material))]
public class MaterialAssetEditor : AssetImporterEditor
{
    // A material is edited live, so the scene shows every tweak as it happens. Edits are undo records owned by
    // the material, which put it in the dirty asset set that Ctrl+S writes.

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        // Include the GUID in element IDs so Paper UI state is unique per asset
        id = $"{id}_{entry.Guid:N}";

        var material = asset as Material;

        Origami.Header(paper, $"{id}_h_info", $"{EditorIcons.Palette}  Material").Show();
        Origami.Label(paper, $"{id}_path", $"Path: {entry.Path}").Show();

        if (material.IsNotValid()) return;

        // Shader reference
        Origami.Separator(paper, $"{id}_sep_shader").Show();
        DrawShaderPicker(paper, id, material, entry);

        Core.Undo.InspectorBaseline(material);
        MaterialPropertyDrawer.DrawMaterial(paper, id, material, () =>
        {
            Core.Undo.InspectorChanged(material);
            PreviewWidget.For(entry.Guid).Invalidate();
        });

        DrawSaveRevertBar(paper, id, material);

        // 3D Preview
        Origami.Header(paper, $"{id}_h_preview", "Preview").Underline().Show();

        PreviewWidget.For(entry.Guid).Get(material, p => p.SetupForMaterial(material)).DrawPreview(paper, $"{id}_preview", 256, 256);
    }

    // ============================================================
    // Shader picker
    // ============================================================
    // A shader's menu path is declared in its own source, so the list is swept fresh from the
    // embedded defaults plus the project's .shader files each time the popup opens rather than
    // being derived from the asset database. Only one picker is open at a time, so the state is
    // static and keyed by which material owns it.

    private static Guid _shaderPickerOwner;
    private static readonly MenuTreeState _shaderMenu = new();
    private static List<MenuTreeEntry> _shaderEntries = [];

    private void DrawShaderPicker(Paper paper, string id, Material material, AssetEntry entry)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        Guid ownerGuid = entry.Guid;

        EditorGUI.Row(paper, $"{id}_shader", "Shader", () =>
        {
            string none = Loc.Get("inspector.shader_none");
            Guid current = material.Shader is { } shader ? shader.AssetID : Guid.Empty;
            string label = EditorAssetBackend.Instance?.GetShaderMenuPath(current, none) ?? none;

            ElementBuilder trigger = paper.Row($"{id}_shader_btn")
                .Height(EditorTheme.RowHeight)
                .Rounded(EditorTheme.Roundness)
                .BackgroundColor(EditorTheme.Ink100)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                .PaddingLeft(8).PaddingRight(8).Gap(6)
                .OnClick(0, (_, _) => ToggleShaderPicker(ownerGuid));

            using (trigger.Enter())
            {
                ElementHandle trigHandle = paper.CurrentParent;

                if (font != null)
                {
                    paper.Box($"{id}_shader_lbl")
                        .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).IsNotInteractable()
                        .Text(label, font).TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                    paper.Box($"{id}_shader_arw")
                        .Width(12).Height(EditorTheme.RowHeight).IsNotInteractable()
                        .Text(EditorIcons.ChevronDown, font).TextColor(EditorTheme.Ink300)
                        .FontSize(10f).Alignment(TextAlignment.MiddleCenter);
                }

                if (_shaderPickerOwner == ownerGuid)
                {
                    if (paper.IsKeyPressed(PaperKey.Escape))
                    {
                        CloseShaderPicker();
                    }
                    else
                    {
                        MenuTreePopup.Backdrop(paper, $"{id}_shader_pick", CloseShaderPicker);
                        MenuTreePopup.Popover(paper, $"{id}_shader_pick", trigHandle, _shaderEntries, _shaderMenu,
                            picked =>
                            {
                                if (picked.Tag is Guid guid && guid != current && AssetDatabase.Load<Shader>(guid) is { IsLoaded: true } pickedShader)
                                {
                                    Core.Undo.RecordObject(material, "Shader");
                                    material.Shader = pickedShader;
                                    PreviewWidget.For(entry.Guid).Invalidate();
                                }
                                CloseShaderPicker();
                            },
                            Loc.Get("popup.search_shaders"),
                            Loc.Get("popup.no_shaders"));
                    }
                }
            }
        });
    }

    private static void ToggleShaderPicker(Guid owner)
    {
        if (_shaderPickerOwner == owner)
        {
            CloseShaderPicker();
            return;
        }

        _shaderPickerOwner = owner;
        _shaderMenu.Reset();
        _shaderEntries = [.. (EditorAssetBackend.Instance?.GetShaderCatalog() ?? []).Select(s => MenuTreeEntry.FromPath(s.MenuPath, s.IsBuiltIn ? EditorIcons.Cube : EditorIcons.FileCode, s.Guid))];
    }

    private static void CloseShaderPicker() => _shaderPickerOwner = Guid.Empty;
}
