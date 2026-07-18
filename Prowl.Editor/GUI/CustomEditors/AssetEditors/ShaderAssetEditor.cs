using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.Graphite;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Graphite.ShaderDef;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Shader))]
public class ShaderAssetEditor : AssetImporterEditor
{
    private Guid _currentGuid;

    // Selected backend per permutation, keyed by "{passIndex}_{variantIndex}".
    private readonly Dictionary<string, GraphicsBackend> _selectedBackend = new();

    // Cache settings across frames so changes stick until Save
    private EchoObject? _cachedSettings;
    private bool _dirty;
    private Guid _cachedForGuid;

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

        DrawImportSettings(paper, id, entry);

        if (shader == null) return;

        ShaderPass[] passes = shader.Passes.ToArray();
        IReadOnlyList<Variant>[] passVariants = [.. Enumerable.Range(0, passes.Length).Select(shader.GetCompiledVariants)];
        int totalVariants = passVariants.Sum(v => v.Count);
        int variantSpaces = passVariants
            .SelectMany(v => v)
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
            IReadOnlyList<Variant> variants = passVariants[passIndex];
            string passId = $"{id}_pass_{passIndex}";

            Origami.Foldout(paper, passId, $"Pass: {pass.Name}")
                .Badge($"{variants.Count} variant{(variants.Count == 1 ? "" : "s")}")
                .Body(() =>
                {
                    for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
                        DrawVariant(paper, $"{passId}_v{variantIndex}", passIndex, variantIndex, variants[variantIndex]);
                });
        }
    }

    private void DrawImportSettings(Paper paper, string id, AssetEntry entry)
    {
        if (Project.Current == null) return;
        string absPath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        string metaPath = MetaFile.GetMetaPath(absPath);
        if (!File.Exists(metaPath)) return;

        // Load and cache settings (only reload when asset changes)
        if (_cachedSettings == null || _cachedForGuid != entry.Guid)
        {
            var meta = MetaFile.Read(metaPath);
            _cachedSettings = meta.Settings ?? EchoObject.NewCompound();

            var defaults = new Importers.ShaderImporter().DefaultSettings();
            if (defaults != null)
                foreach (var kvp in defaults.Tags)
                    if (!_cachedSettings.TryGet(kvp.Key, out _))
                        _cachedSettings[kvp.Key] = kvp.Value.Clone();

            _dirty = false;
            _cachedForGuid = entry.Guid;
        }

        var settings = _cachedSettings;

        EditorGUI.SectionHeader(paper, $"{id}_settings_hdr", "Import Settings", first: false);

        bool onDemand = settings.TryGet("onDemandCompilation", out var onDemandTag) && onDemandTag.BoolValue;
        EditorGUI.SettingsToggle(paper, $"{id}_ondemand", "On-Demand Compilation", onDemand,
            v => { settings["onDemandCompilation"] = new EchoObject(v); _dirty = true; }, separator: false);

        bool dirty = !Origami.IsReadOnly && _dirty;
        var m = Origami.Current.Metrics;
        paper.Box($"{id}_save").Width(UnitValue.Auto).Height(30)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.SpacingLarge, m.SpacingLarge).Rounded(8).Padding(16, 16, 0, 0)
            .BackgroundColor(dirty ? EditorTheme.Accent : EditorTheme.Neutral300)
            .Hovered.BackgroundColor(dirty ? EditorTheme.AccentBright : EditorTheme.Neutral300).End()
            .Text($"{EditorIcons.FloppyDisk}  Save & Reimport", EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont)
            .TextColor(dirty ? System.Drawing.Color.White : EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) =>
            {
                if (!dirty) return;
                var meta = MetaFile.Read(metaPath);
                meta.Settings = settings;
                MetaFile.Write(metaPath, meta);
                _cachedSettings = null;
                _dirty = false;
                EditorAssetBackend.Instance?.Reimport(entry.Guid);
            });
    }

    private void DrawVariant(Paper paper, string id, int passIndex, int variantIndex, Variant variant)
    {
        string key = $"{passIndex}_{variantIndex}";
        string label = variant.Keywords.Length == 0
            ? "Base (no keywords)"
            : string.Join(", ", variant.Keywords.Select(k => $"{k.Name}={k.Value}"));

        GraphicsBackend[] backends = [.. variant.Compiled.Select(b => b.Backend)];
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
            (GraphicsBackend backend, ShaderDescription description) = Array.Find(variant.Compiled, b => b.Backend == selected);
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
