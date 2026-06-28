// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for managed and native plugins. Edits the per-platform import settings stored in the
/// plugin's .meta companion (see <see cref="PluginInfo.Keys"/>). Resolved via the marker
/// <see cref="PluginAsset"/> the importer produces.
/// </summary>
[CustomAssetEditor(typeof(PluginAsset))]
public class PluginAssetEditor : AssetImporterEditor
{
    private enum PluginCpu { AnyCPU, x64, arm64 }

    private EchoObject? _settings;
    private Guid _forGuid;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        if (Project.Current == null) return;

        string absPath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        string metaPath = MetaFile.GetMetaPath(absPath);
        if (!File.Exists(metaPath) || !File.Exists(absPath)) return;

        bool isNative = !Path.GetExtension(absPath).Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || !PluginScanner.IsManagedAssembly(absPath);
        bool underEditor = entry.Path.Split('/', '\\').Any(s => s.Equals("Editor", StringComparison.OrdinalIgnoreCase));

        Origami.Header(paper, $"{id}_hdr", $"{EditorIcons.PuzzlePiece}  {(isNative ? "Native" : "Managed")} Plugin").Show();
        Origami.Label(paper, $"{id}_file", $"File: {Path.GetFileName(absPath)}").Show();

        if (_settings == null || _forGuid != entry.Guid)
        {
            var meta = MetaFile.Read(metaPath);
            _settings = meta.Settings ?? EchoObject.NewCompound();
            var defaults = new Importers.PluginImporter().DefaultSettings();
            if (defaults != null)
                foreach (var kvp in defaults.Tags)
                    if (!_settings.TryGet(kvp.Key, out _))
                        _settings[kvp.Key] = kvp.Value.Clone();
            _forGuid = entry.Guid;
        }
        var s = _settings;

        paper.Box($"{id}_sp1").Height(6);
        Origami.Header(paper, $"{id}_settings_hdr", $"{EditorIcons.Gear}  Plugin Settings").Underline().Show();

        if (underEditor)
            Origami.Label(paper, $"{id}_eonote", "In an Editor/ folder: always editor-only.").Show();

        Origami.Checkbox(paper, $"{id}_eo", Bool(s, PluginInfo.Keys.EditorOnly) || underEditor,
                v => s[PluginInfo.Keys.EditorOnly] = new EchoObject(v))
            .LabelRight("Editor Only").Show();

        if (!isNative)
        {
            Origami.Checkbox(paper, $"{id}_auto", Bool(s, PluginInfo.Keys.AutoReferenced),
                    v => s[PluginInfo.Keys.AutoReferenced] = new EchoObject(v))
                .LabelRight("Auto Referenced").Show();
        }

        paper.Box($"{id}_sp2").Height(6);
        Origami.Header(paper, $"{id}_plat_hdr", "Platforms").Show();

        bool anyPlatform = Bool(s, PluginInfo.Keys.AnyPlatform);
        Origami.Checkbox(paper, $"{id}_any", anyPlatform,
                v => s[PluginInfo.Keys.AnyPlatform] = new EchoObject(v))
            .LabelRight("Any Platform").Show();

        if (!anyPlatform)
        {
            PlatformToggle(paper, $"{id}_win", "Windows", s, PluginInfo.Keys.Windows);
            PlatformToggle(paper, $"{id}_lin", "Linux", s, PluginInfo.Keys.Linux);
            PlatformToggle(paper, $"{id}_mac", "macOS", s, PluginInfo.Keys.MacOS);
        }

        if (isNative)
        {
            var cpu = ParseCpu(s.TryGet(PluginInfo.Keys.Cpu, out var c) ? c.StringValue : "x64");
            InspectorRow.Draw(paper, $"{id}_cpu", "CPU", () =>
                Origami.EnumDropdown(paper, $"{id}_cpu_v", cpu,
                    v => s[PluginInfo.Keys.Cpu] = new EchoObject(v.ToString())).Show());
        }

        paper.Box($"{id}_sp3").Height(8);
        Origami.Button(paper, $"{id}_save", $"{EditorIcons.FloppyDisk}  Save & Reimport", () =>
        {
            var meta = MetaFile.Read(metaPath);
            meta.Settings = s;
            MetaFile.Write(metaPath, meta);
            _settings = null;
            EditorAssetDatabase.Instance?.Reimport(entry.Guid);
            ScriptAssemblyManager.RequestRecompile();
        }).Width(150).Show();
    }

    private static void PlatformToggle(Paper paper, string id, string label, EchoObject s, string key)
        => Origami.Checkbox(paper, id, Bool(s, key), v => s[key] = new EchoObject(v)).LabelRight(label).Show();

    private static bool Bool(EchoObject s, string key) => s.TryGet(key, out var t) && t.BoolValue;

    private static PluginCpu ParseCpu(string raw) => raw.ToLowerInvariant() switch
    {
        "arm64" => PluginCpu.arm64,
        "anycpu" => PluginCpu.AnyCPU,
        _ => PluginCpu.x64,
    };
}
