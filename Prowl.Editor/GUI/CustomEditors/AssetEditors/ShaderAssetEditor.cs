using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.Graphite;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Rendering.Shaders;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Shader))]
public class ShaderAssetEditor : AssetImporterEditor
{
    private Guid _currentGuid;

    // Selected backend per permutation, keyed by "{passIndex}_{variantIndex}".
    private readonly Dictionary<string, GraphicsBackend> _selectedBackend = new();

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        if (_currentGuid != entry.Guid)
        {
            _currentGuid = entry.Guid;
            _selectedBackend.Clear();
        }

        id = $"{id}_{entry.Guid:N}";

        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var shader = asset as Shader;

        Origami.Header(paper, $"{id}_h_info", $"{EditorIcons.WandSparkles}  Shader").Show();
        Origami.Label(paper, $"{id}_path", $"Path: {entry.Path}").Show();

        if (shader == null) return;

        ShaderPass[] passes = shader.Passes.ToArray();
        int totalVariants = passes.Sum(p => p.Variants.Count());
        int variantSpaces = passes
            .SelectMany(p => p.Variants)
            .SelectMany(v => v.Keywords)
            .Select(k => k.Name)
            .Distinct()
            .Count();

        Origami.Separator(paper, $"{id}_sep_diag").Show();
        Origami.Header(paper, $"{id}_h_diag", "Diagnostics").Underline().Show();
        Origami.Label(paper, $"{id}_diag_passes", $"Passes: {passes.Length}").Show();
        Origami.Label(paper, $"{id}_diag_variants", $"Variants compiled: {totalVariants}").Show();
        Origami.Label(paper, $"{id}_diag_spaces", $"Variant spaces detected: {variantSpaces}").Show();

        Origami.Separator(paper, $"{id}_sep_perms").Show();
        Origami.Header(paper, $"{id}_h_perms", "Permutations").Underline().Show();

        for (int passIndex = 0; passIndex < passes.Length; passIndex++)
        {
            ShaderPass pass = passes[passIndex];
            ShaderVariant[] variants = pass.Variants.ToArray();
            string passId = $"{id}_pass_{passIndex}";

            Origami.Foldout(paper, passId, $"Pass: {pass.Name}")
                .Badge($"{variants.Length} variant{(variants.Length == 1 ? "" : "s")}")
                .Body(() =>
                {
                    for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
                        DrawVariant(paper, $"{passId}_v{variantIndex}", passIndex, variantIndex, variants[variantIndex]);
                });
        }
    }

    private void DrawVariant(Paper paper, string id, int passIndex, int variantIndex, ShaderVariant variant)
    {
        string key = $"{passIndex}_{variantIndex}";
        string label = variant.Keywords.Length == 0
            ? "Base (no keywords)"
            : string.Join(", ", variant.Keywords.Select(k => $"{k.Name}={k.Value}"));

        GraphicsBackend[] backends = [.. variant.Backends.Select(b => b.Item2)];
        if (!_selectedBackend.TryGetValue(key, out GraphicsBackend selected) || Array.IndexOf(backends, selected) < 0)
        {
            selected = backends.Length > 0 ? backends[0] : default;
            _selectedBackend[key] = selected;
        }

        Origami.Label(paper, $"{id}_lbl", label).Show();

        Origami.Dropdown(paper, $"{id}_backend", selected, v => _selectedBackend[key] = v, backends)
            .Show();

        Origami.Button(paper, $"{id}_open", $"{EditorIcons.FileCode}  View Shader Source", () =>
        {
            (ShaderDescription description, GraphicsBackend backend) = Array.Find(variant.Backends, b => b.Item2 == selected);
            OpenPermutation(passIndex, variantIndex, description, backend);
        }).Show();

        Origami.Separator(paper, $"{id}_sep").Show();
    }

    private static void OpenPermutation(int passIndex, int variantIndex, ShaderDescription description, GraphicsBackend backend)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ProwlShaderInspector");
            Directory.CreateDirectory(tempDir);

            string extension = backend switch
            {
                GraphicsBackend.Vulkan => "spv",
                GraphicsBackend.Direct3D11 => "hlsl",
                _ => "glsl",
            };

            string fileName = $"pass{passIndex}_variant{variantIndex}_{backend}.{extension}";
            string filePath = Path.Combine(tempDir, fileName);

            var builder = new StringBuilder();
            foreach (ShaderStageDescription stage in description.Stages)
            {
                builder.AppendLine($"// ==================== {stage.Stage} ({stage.EntryPoint}) ====================");
                builder.AppendLine(Encoding.UTF8.GetString(stage.ShaderBytes));
                builder.AppendLine();
            }

            File.WriteAllText(filePath, builder.ToString());
            EditorUtils.OpenUrl(filePath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to open shader permutation: {ex.Message}");
        }
    }
}
