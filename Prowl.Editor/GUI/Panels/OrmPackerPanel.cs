// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;

using ImageMagick;

using Prowl.OrigamiUI;
using Prowl.Editor.Inspector;
using Prowl.Editor.GUI;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;
using Prowl.Editor.GUI.PropertyEditors;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// Production ORM map packer.
/// Packs Occlusion (R), Roughness (G), Metallic (B), and an optional alpha into a single
/// PNG matching the channel order Prowl's Standard shader expects (see <c>StandardSurface.glsl</c>).
/// Each input slot can sample any source channel (or luminance), invert it, or be replaced
/// with a constant value when no texture is assigned.
/// </summary>
[EditorWindow("Tools/ORM Packer")]
public class OrmPackerPanel : DockPanel
{
    public override string Title => Loc.Get("panel.orm_packer");
    public override string Icon => EditorIcons.LayerGroup;

    public enum SourceChannel { R, G, B, A, Luminance }
    public enum SizeMode { Auto, Custom }
    public enum Resample { Lanczos, Bilinear, Nearest, Mitchell }

    private sealed class ChannelSlot
    {
        public string Label = "";
        public string Hint = "";
        public string Id = "";
        public IAssetRef Ref = new AssetRef<Texture2D>();
        public SourceChannel SourceChannel = SourceChannel.R;
        public bool Invert;
        public float DefaultValue = 1f;
    }

    private readonly ChannelSlot[] _slots =
    {
        new() { Id = "ao",   Label = "R: Occlusion", Hint = "0 = unoccluded, 1 = fully occluded (matches Standard shader)", DefaultValue = 0f, SourceChannel = SourceChannel.R },
        new() { Id = "rgh",  Label = "G: Roughness", Hint = "0 = mirror, 1 = fully rough",                                  DefaultValue = 0.5f, SourceChannel = SourceChannel.R },
        new() { Id = "mtl",  Label = "B: Metallic",  Hint = "0 = dielectric, 1 = metal",                                    DefaultValue = 0f,  SourceChannel = SourceChannel.R },
        new() { Id = "alp",  Label = "A: Alpha (optional)", Hint = "Leave empty + 1.0 for ORM with no alpha payload",       DefaultValue = 1f,  SourceChannel = SourceChannel.R },
    };

    private SizeMode _sizeMode = SizeMode.Auto;
    private int _customW = 1024;
    private int _customH = 1024;
    private Resample _filter = Resample.Lanczos;
    private string _outputPath = "";

    private Texture2D? _previewTex;
    private string? _lastError;
    private string? _lastSuccess;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        Origami.ScrollView(paper, "orm_scroll", width, height)
            .Padding(12, 12, 12, 12)
            .ColSpacing(10)
            .Body(() =>
            {
                Origami.Header(paper, "orm_h_root", "ORM Packer").Show();

                Section_Inputs(paper);
                Section_Output(paper);
                Section_Preview(paper);
                Section_Actions(paper);
                Section_Status(paper);
            });
    }

    // ── Sections ───────────────────────────────────────────────

    private void Section_Inputs(Paper paper)
    {
        Origami.Foldout(paper, "orm_fo_inputs", "Inputs").DefaultExpanded().Body(() =>
        {
            using (paper.Column("orm_in_col").Height(UnitValue.Auto).RowBetween(10).Enter())
            {
                foreach (var slot in _slots)
                    DrawSlot(paper, slot);
            }
        });
    }

    private void Section_Output(Paper paper)
    {
        Origami.Foldout(paper, "orm_fo_output", "Output").DefaultExpanded().Body(() =>
        {
            using (paper.Column("orm_out_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "orm_out_path", "PNG Output Path", () =>
                {
                    using (paper.Row("orm_out_path_row").Height(EditorTheme.RowHeight).RowBetween(6).Enter())
                    {
                        Origami.TextField(paper, "orm_out_path_tf", _outputPath,
                                v => _outputPath = v ?? "")
                            .Placeholder("Pick a file…").Width(UnitValue.Stretch()).Show();

                        Origami.Button(paper, "orm_out_browse", "Browse", () => OpenSaveDialog()).Width(80).Show();
                    }
                });

                LabelRow(paper, "orm_size_mode", "Size", () =>
                    Origami.EnumDropdown(paper, "orm_size_mode_dd", _sizeMode, v => _sizeMode = v).Show());

                if (_sizeMode == SizeMode.Custom)
                {
                    LabelRow(paper, "orm_size_w", "Width", () =>
                        Origami.NumericField<int>(paper, "orm_size_w_f", _customW,
                            v => _customW = Math.Clamp(v, 1, 16384)).Min(1).Max(16384).Show());
                    LabelRow(paper, "orm_size_h", "Height", () =>
                        Origami.NumericField<int>(paper, "orm_size_h_f", _customH,
                            v => _customH = Math.Clamp(v, 1, 16384)).Min(1).Max(16384).Show());
                }

                LabelRow(paper, "orm_filter", "Resample Filter", () =>
                    Origami.EnumDropdown(paper, "orm_filter_dd", _filter, v => _filter = v).Show());
            }
        });
    }

    private void Section_Preview(Paper paper)
    {
        Origami.Foldout(paper, "orm_fo_prev", "Preview").Body(() =>
        {
            using (paper.Column("orm_prev_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                using (paper.Box("orm_prev_box")
                    .Width(UnitValue.Stretch())
                    .Height(220)
                    .BackgroundColor(EditorTheme.Neutral200)
                    .Rounded(4)
                    .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                    .Enter())
                {
                    if (_previewTex != null)
                    {
                        // Center a square texture preview.
                        var capturedTex = _previewTex;
                        paper.Box("orm_prev_img")
                            .Size(200, 200)
                            .Margin(UnitValue.StretchOne, UnitValue.StretchOne, 10, 10)
                            .OnPostLayout((h, r) =>
                            {
                                paper.Draw(ref h, (canvas, rr) =>
                                {
                                    canvas.DrawImage(capturedTex,
                                        (float)rr.Min.X, (float)rr.Min.Y,
                                        (float)rr.Size.X, (float)rr.Size.Y);
                                });
                            });
                    }
                    else
                    {
                        paper.Box("orm_prev_empty")
                            .Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                            .Alignment(TextAlignment.MiddleCenter)
                            .IsNotInteractable()
                            .Text("Click 'Generate Preview' to render a 256×256 thumbnail.", EditorTheme.DefaultFont)
                            .TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSize - 1);
                    }
                }
            }
        });
    }

    private void Section_Actions(Paper paper)
    {
        using (paper.Row("orm_actions").Height(32).RowBetween(8).Enter())
        {
            paper.Box("orm_actions_spacer").Width(UnitValue.StretchOne);

            Origami.Button(paper, "orm_preview_btn", $"{EditorIcons.Eye}  Generate Preview", () => RunPreview()).Width(180).Show();

            Origami.Button(paper, "orm_pack_btn", $"{EditorIcons.FloppyDisk}  Pack & Save", () => RunPack()).Width(160).Show();
        }
    }

    private void Section_Status(Paper paper)
    {
        if (_lastError == null && _lastSuccess == null) return;

        var bg   = _lastError != null ? EditorTheme.Red400 : Color.FromArgb(255, 80, 160, 95);
        var text = _lastError ?? _lastSuccess ?? "";

        paper.Box("orm_status").Height(28)
            .BackgroundColor(bg)
            .Rounded(4)
            .Alignment(TextAlignment.MiddleLeft)
            .Margin(0, 0, 4, 0)
            .Text("  " + text, EditorTheme.DefaultFont)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize);
    }

    // ── Slot drawing ───────────────────────────────────────────

    private void DrawSlot(Paper paper, ChannelSlot slot)
    {
        using (paper.Column($"orm_slot_{slot.Id}").Height(UnitValue.Auto).RowBetween(4)
            .BackgroundColor(EditorTheme.Neutral200)
            .Rounded(4)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1)
            .Margin(0, 0, 0, 0)
            .Padding(new UnitValue(8))
            .Enter())
        {
            paper.Box($"orm_slot_{slot.Id}_lbl")
                .Height(20)
                .Alignment(TextAlignment.MiddleLeft).IsNotInteractable()
                .Text(slot.Label, EditorTheme.DefaultFont)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);

            paper.Box($"orm_slot_{slot.Id}_hint")
                .Height(16)
                .Alignment(TextAlignment.MiddleLeft).IsNotInteractable()
                .Text(slot.Hint, EditorTheme.DefaultFont)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSize - 2);

            // Texture asset reference
            new AssetRefPropertyEditor().OnGUI(paper, $"orm_slot_{slot.Id}_ref", "Texture",
                slot.Ref, v =>
                {
                    if (v is IAssetRef ar) slot.Ref = ar;
                }, 0);

            bool hasTex = slot.Ref?.GetInstance() is Texture2D;

            // Source channel + invert (only relevant when a texture is assigned)
            using (paper.Row($"orm_slot_{slot.Id}_chRow").Height(EditorTheme.RowHeight).RowBetween(8).Enter())
            {
                paper.Box($"orm_slot_{slot.Id}_chLbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight)
                    .Alignment(TextAlignment.MiddleLeft)
                    .IsNotInteractable()
                    .Text("Source", EditorTheme.DefaultFont)
                    .TextColor(hasTex ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize);

                using (paper.Box($"orm_slot_{slot.Id}_chBox").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).Enter())
                {
                    Origami.EnumDropdown(paper, $"orm_slot_{slot.Id}_chDD",
                        slot.SourceChannel, v => slot.SourceChannel = v).Show();
                }

                Origami.Checkbox(paper, $"orm_slot_{slot.Id}_inv", slot.Invert, v => slot.Invert = v)
                    .LabelRight("Invert").Show();
            }

            // Default value slider used when no texture is assigned
            LabelRow(paper, $"orm_slot_{slot.Id}_def_row", hasTex ? "Default (unused)" : "Default", () =>
                Origami.Slider(paper, $"orm_slot_{slot.Id}_def", slot.DefaultValue,
                    v => slot.DefaultValue = v, 0f, 1f).Format("F2").Show());
        }
    }

    private static void LabelRow(Paper paper, string id, string label, Action draw)
    {
        using (paper.Row($"{id}_row").Height(EditorTheme.RowHeight).RowBetween(8).Enter())
        {
            paper.Box($"{id}_lbl")
                .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight)
                .Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable()
                .Text(label, EditorTheme.DefaultFont)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize);

            using (paper.Box($"{id}_ctl").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).Enter())
            {
                draw();
            }
        }
    }

    // ── File dialog ────────────────────────────────────────────

    private void OpenSaveDialog()
    {
        string startPath = Project.Current?.AssetsPath ?? Environment.CurrentDirectory;
        EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
            _outputPath = path;
        }, startPath, new[] { "*.png" }, new[] { "PNG (*.png)" });
    }

    // ── Packing ────────────────────────────────────────────────

    private void RunPreview()
    {
        _lastError = null;
        _lastSuccess = null;
        try
        {
            using var packed = BuildPacked(previewSize: 256);

            // Convert packed MagickImage into a Texture2D for on-screen preview.
            _previewTex?.Dispose();
            _previewTex = Texture2D.FromImage((MagickImage)packed.Clone());

            _lastSuccess = $"Preview generated ({packed.Width}×{packed.Height}).";
        }
        catch (Exception ex)
        {
            _lastError = $"Preview failed: {ex.Message}";
        }
    }

    private void RunPack()
    {
        _lastError = null;
        _lastSuccess = null;

        if (string.IsNullOrEmpty(_outputPath))
        {
            _lastError = "Pick an output path first.";
            return;
        }

        try
        {
            using var packed = BuildPacked(previewSize: 0);
            Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
            packed.Write(_outputPath, MagickFormat.Png);

            // The asset database's filesystem watcher picks up new files automatically;
            // no explicit refresh needed.

            _lastSuccess = $"Packed to {_outputPath} ({packed.Width}×{packed.Height}).";
        }
        catch (Exception ex)
        {
            _lastError = $"Pack failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Builds the packed RGBA image. <paramref name="previewSize"/>=0 packs at full resolution;
    /// any other value caps the output to that dimension for a cheap thumbnail.
    /// </summary>
    private MagickImage BuildPacked(int previewSize)
    {
        int targetW = ResolveTargetSize(true);
        int targetH = ResolveTargetSize(false);
        if (previewSize > 0)
        {
            float scale = Math.Min(1f, (float)previewSize / Math.Max(targetW, targetH));
            targetW = Math.Max(1, (int)(targetW * scale));
            targetH = Math.Max(1, (int)(targetH * scale));
        }

        var dest = new MagickImage(MagickColors.Black, (uint)targetW, (uint)targetH);
        dest.HasAlpha = true;

        var ops = new (Channels DestChannel, ChannelSlot Slot)[]
        {
            (Channels.Red,   _slots[0]),
            (Channels.Green, _slots[1]),
            (Channels.Blue,  _slots[2]),
            (Channels.Alpha, _slots[3]),
        };

        foreach (var (destChannel, slot) in ops)
        {
            using var grayscale = LoadSourceGrayscale(slot, targetW, targetH);
            CompositeOperator op = destChannel switch
            {
                Channels.Red   => CompositeOperator.CopyRed,
                Channels.Green => CompositeOperator.CopyGreen,
                Channels.Blue  => CompositeOperator.CopyBlue,
                Channels.Alpha => CompositeOperator.CopyAlpha,
                _              => CompositeOperator.CopyRed,
            };
            dest.Composite(grayscale, op);
        }

        return dest;
    }

    private IMagickImage<ushort> LoadSourceGrayscale(ChannelSlot slot, int targetW, int targetH)
    {
        var tex = (slot.Ref?.GetInstance() as Texture2D);
        if (tex == null)
        {
            // Constant fill at default value.
            byte v = (byte)Math.Clamp(slot.DefaultValue * 255f, 0f, 255f);
            return new MagickImage(MagickColor.FromRgba(v, v, v, 255), (uint)targetW, (uint)targetH);
        }

        string srcPath = ResolveSourcePath(tex);
        var src = new MagickImage(srcPath);
        if ((int)src.Width != targetW || (int)src.Height != targetH)
        {
            src.FilterType = ToFilterType(_filter);
            src.Resize((uint)targetW, (uint)targetH);
        }

        IMagickImage<ushort> grayscale;
        if (slot.SourceChannel == SourceChannel.Luminance)
        {
            var clone = (MagickImage)src.Clone();
            clone.Grayscale(PixelIntensityMethod.Rec709Luminance);
            grayscale = clone;
        }
        else
        {
            Channels c = slot.SourceChannel switch
            {
                SourceChannel.R => Channels.Red,
                SourceChannel.G => Channels.Green,
                SourceChannel.B => Channels.Blue,
                SourceChannel.A => Channels.Alpha,
                _               => Channels.Red,
            };
            // Separate returns a single-channel image where R=G=B=channel data.
            grayscale = src.Separate(c).First();
        }

        src.Dispose();

        if (slot.Invert)
            grayscale.Negate(Channels.RGB);

        return grayscale;
    }

    private string ResolveSourcePath(Texture2D tex)
    {
        if (string.IsNullOrEmpty(tex.AssetPath))
            throw new InvalidOperationException($"Texture '{tex.Name}' has no source path. Import it as a project asset first.");
        if (tex.AssetPath.Contains('#'))
            throw new InvalidOperationException("Sub-assets aren't supported as ORM packer inputs. Use a top-level texture file.");
        if (Project.Current == null)
            throw new InvalidOperationException("No active project.");
        return Path.Combine(Project.Current.AssetsPath, tex.AssetPath);
    }

    private int ResolveTargetSize(bool isWidth)
    {
        if (_sizeMode == SizeMode.Custom)
            return isWidth ? _customW : _customH;

        // Auto: max of inputs, or 1024 if nothing assigned.
        int max = 0;
        foreach (var slot in _slots)
        {
            var tex = slot.Ref?.GetInstance() as Texture2D;
            if (tex == null) continue;
            int dim = isWidth ? (int)tex.Width : (int)tex.Height;
            if (dim > max) max = dim;
        }
        return max > 0 ? max : 1024;
    }

    private static FilterType ToFilterType(Resample r) => r switch
    {
        Resample.Lanczos  => FilterType.Lanczos,
        Resample.Bilinear => FilterType.Triangle,
        Resample.Nearest  => FilterType.Point,
        Resample.Mitchell => FilterType.Mitchell,
        _                 => FilterType.Lanczos,
    };
}
