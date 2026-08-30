using System;
using System.Collections.Generic;

using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.Graphite;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI.RenderProfiler.Inspectors;

// Snapshot-only: shader identity, variant keywords, tags, and bound pipeline state all come from
// ProfiledPipeline, which only exists on a captured frame.
public sealed class ProfilerShaderInspector
{
    private const int TabRasterizer = 0;
    private const int TabBlend = 1;
    private const int TabDepthStencil = 2;

    private int _stateTab = TabRasterizer;


    public void Draw(Paper paper, ProfiledPipeline? pipeline)
    {
        if (pipeline == null)
        {
            EditorGUI.EmptyState(paper, "rdp_sh_empty", "No shader selected", EditorTheme.DefaultFont!);
            return;
        }

        using (paper.Column("rdp_sh_viewer").Height(UnitValue.Auto).ColBetween(InspectorKit.SectionGap).Enter())
        {
            DrawHeader(paper, pipeline);
            DrawShaderCard(paper, pipeline);
            DrawVariantCard(paper, pipeline);
            DrawTagsCard(paper, pipeline);
            DrawPipelineStateCard(paper, pipeline);
        }
    }


    private static void DrawHeader(Paper paper, ProfiledPipeline pipeline)
    {
        using (paper.Row("rdp_sh_header").Height(InspectorKit.SelectionViewerHeaderHeight).ColBetween(8f).Enter())
        {
            Origami.Label(paper, "rdp_sh_title", pipeline.ShaderName)
                .Heading()
                .AlignLeft()
                .Show();

            Origami.Label(paper, "rdp_sh_passname", pipeline.ShaderPassName)
                .Muted()
                .AlignLeft()
                .Show();

            paper.Box("rdp_sh_header_spacer");

            Origami.Label(paper, "rdp_sh_kind", pipeline.IsCompute ? "Compute" : "Graphics")
                .Variant(pipeline.IsCompute ? OrigamiVariant.Success : OrigamiVariant.Subtle)
                .AlignRight()
                .Show();

            Origami.Label(paper, "rdp_sh_stages", pipeline.Stages.ToString())
                .Muted()
                .AlignRight()
                .Show();
        }
    }


    private static void DrawShaderCard(Paper paper, ProfiledPipeline pipeline)
    {
        InspectorKit.SectionCard(paper, "rdp_sh_shader", "Shader", () =>
        {
            using (paper.Column("rdp_sh_shader_rows").Height(UnitValue.Auto).ColBetween(2f).Enter())
            {
                EditorGUI.StatRow(paper, "rdp_sh_name", "Shader", pipeline.ShaderName);
                EditorGUI.StatRow(paper, "rdp_sh_pass", "Pass", pipeline.ShaderPassName);
                EditorGUI.StatRow(paper, "rdp_sh_stage", "Stages", pipeline.Stages.ToString());

                if (!string.IsNullOrWhiteSpace(pipeline.MaterialName))
                    EditorGUI.StatRow(paper, "rdp_sh_material", "Source Material", pipeline.MaterialName);
            }
        });
    }


    private static void DrawVariantCard(Paper paper, ProfiledPipeline pipeline)
    {
        InspectorKit.SectionCard(paper, "rdp_sh_variant", "Variant", () =>
        {
            if (string.IsNullOrWhiteSpace(pipeline.Variant))
            {
                EditorGUI.EmptyState(paper, "rdp_sh_variant_empty", "No variant keywords", EditorTheme.DefaultFont!);
                return;
            }

            var keywords = SplitVariant(pipeline.Variant);
            if (keywords.Count == 0)
            {
                EditorGUI.EmptyState(paper, "rdp_sh_variant_empty", "No variant keywords", EditorTheme.DefaultFont!);
                return;
            }

            TableBuilder table = Origami.Table(paper, "rdp_sh_variant_table", -1, _ => { })
                .Bordered(true)
                .Width(UnitValue.Stretch())
                .RowHeight(24f)
                .Column("Keyword", 1f)
                .Column("Value", 1f, align: TextAlignment.MiddleRight);

            foreach ((string key, string value) in keywords)
                table.Row().Cell(key, EditorTheme.Ink500).CellRight(value, EditorTheme.Ink500);

            table.Show();
        });
    }


    private static void DrawTagsCard(Paper paper, ProfiledPipeline pipeline)
    {
        InspectorKit.SectionCard(paper, "rdp_sh_tags", "Tags", () =>
        {
            IReadOnlyDictionary<string, string>? tags = pipeline.Tags;
            if (tags == null || tags.Count == 0)
            {
                EditorGUI.EmptyState(paper, "rdp_sh_tags_empty", "No tags", EditorTheme.DefaultFont!);
                return;
            }

            TableBuilder table = Origami.Table(paper, "rdp_sh_tags_table", -1, _ => { })
                .Bordered(true)
                .Width(UnitValue.Stretch())
                .RowHeight(24f)
                .Column("Tag", 1f)
                .Column("Value", 1f, align: TextAlignment.MiddleRight);

            foreach ((string key, string value) in tags)
                table.Row().Cell(key, EditorTheme.Ink500).CellRight(value, EditorTheme.Ink500);

            table.Show();
        });
    }


    private void DrawPipelineStateCard(Paper paper, ProfiledPipeline pipeline)
    {
        InspectorKit.SectionCard(paper, "rdp_sh_state", "Pipeline State", () =>
        {
            ProfiledPipelineState? state = pipeline.State;
            if (state == null)
            {
                EditorGUI.EmptyState(paper, "rdp_sh_state_empty", "No pipeline state captured", EditorTheme.DefaultFont!);
                return;
            }

            ProfiledPipelineState s = state.Value;

            bool hasCompute = s.ThreadGroupSizeX != null;
            bool hasGraphics = s.BlendState != null || s.DepthStencilState != null || s.RasterizerState != null;

            if (hasCompute)
            {
                InspectorKit.StatGroup(paper, "rdp_sh_state_threadgroup", "Thread Group Size",
                    ("X", s.ThreadGroupSizeX.ToString() ?? "0"),
                    ("Y", s.ThreadGroupSizeY.ToString() ?? "0"),
                    ("Z", s.ThreadGroupSizeZ.ToString() ?? "0"));

                if (!hasGraphics)
                    return;
            }

            using (paper.Column("rdp_sh_state_tabcol").Height(UnitValue.Auto).ColBetween(6f).Enter())
            {
                Origami.Tabs(paper, "rdp_sh_state_tabs", _stateTab, i => _stateTab = i)
                    .Tab("Rasterizer")
                    .Tab("Blend")
                    .Tab("Depth-Stencil")
                    .Show();

                switch (_stateTab)
                {
                    case TabRasterizer:
                        if (s.RasterizerState is { } raster)
                            DrawRasterizerState(paper, "rdp_sh_rast", raster);
                        else
                            EditorGUI.EmptyState(paper, "rdp_sh_rast_empty", "No rasterizer state", EditorTheme.DefaultFont!);
                        break;

                    case TabBlend:
                        if (s.BlendState is { } blend)
                            DrawBlendState(paper, "rdp_sh_blend", blend);
                        else
                            EditorGUI.EmptyState(paper, "rdp_sh_blend_empty", "No blend state", EditorTheme.DefaultFont!);
                        break;

                    case TabDepthStencil:
                        if (s.DepthStencilState is { } depth)
                            DrawDepthStencilState(paper, "rdp_sh_ds", depth);
                        else
                            EditorGUI.EmptyState(paper, "rdp_sh_ds_empty", "No depth-stencil state", EditorTheme.DefaultFont!);
                        break;
                }
            }
        });
    }


    // ── Per-tab state readouts ────────────────────────────────────

    private static void DrawRasterizerState(Paper paper, string id, RasterizerStateDescription raster)
    {
        using (paper.Column(id).Height(UnitValue.Auto).ColBetween(2f).Enter())
        {
            EditorGUI.StatRow(paper, $"{id}_cull", "Cull Face", raster.CullMode.ToString());
            EditorGUI.StatRow(paper, $"{id}_front", "Front Face", raster.FrontFace.ToString());
            EditorGUI.StatRow(paper, $"{id}_clip", "Depth Clip", Enabled(raster.DepthClipEnabled));
            EditorGUI.StatRow(paper, $"{id}_scissor", "Scissor Test", Enabled(raster.ScissorTestEnabled));
        }
    }


    private static void DrawBlendState(Paper paper, string id, BlendStateDescription blend)
    {
        using (paper.Column(id).Height(UnitValue.Auto).ColBetween(2f).Enter())
        {
            EditorGUI.StatRow(paper, $"{id}_factor", "Blend Factor", FormatColor(blend.BlendFactor));
            EditorGUI.StatRow(paper, $"{id}_atoc", "Alpha To Coverage", Enabled(blend.AlphaToCoverageEnabled));
            EditorGUI.StatRow(paper, $"{id}_count", "Attachments", blend.AttachmentStates.Length.ToString());

            for (int i = 0; i < blend.AttachmentStates.Length; i++)
                DrawBlendAttachment(paper, $"{id}_a{i}", i, blend.AttachmentStates[i]);
        }
    }


    private static void DrawBlendAttachment(Paper paper, string id, int index, BlendAttachmentDescription attachment)
    {
        using (paper.Column(id).Height(UnitValue.Auto).ColBetween(2f).Enter())
        {
            Origami.Label(paper, $"{id}_title", $"Attachment {index}")
                .Subheading()
                .AlignLeft()
                .Show();

            EditorGUI.StatRow(paper, $"{id}_enabled", "Blend Enabled", Enabled(attachment.BlendEnabled));

            string writeMask = attachment.ColorWriteMask?.ToString() ?? "All";
            EditorGUI.StatRow(paper, $"{id}_writemask", "Color Write Mask", writeMask);
            EditorGUI.StatRow(paper, $"{id}_srccol", "Source Color Factor", attachment.SourceColorFactor.ToString());
            EditorGUI.StatRow(paper, $"{id}_dstcol", "Destination Color Factor", attachment.DestinationColorFactor.ToString());
            EditorGUI.StatRow(paper, $"{id}_colfn", "Color Function", attachment.ColorFunction.ToString());
            EditorGUI.StatRow(paper, $"{id}_srcalpha", "Source Alpha Factor", attachment.SourceAlphaFactor.ToString());
            EditorGUI.StatRow(paper, $"{id}_dstalpha", "Destination Alpha Factor", attachment.DestinationAlphaFactor.ToString());
            EditorGUI.StatRow(paper, $"{id}_alphafn", "Alpha Function", attachment.AlphaFunction.ToString());
        }
    }


    private static void DrawDepthStencilState(Paper paper, string id, DepthStencilStateDescription ds)
    {
        using (paper.Column(id).Height(UnitValue.Auto).ColBetween(2f).Enter())
        {
            EditorGUI.StatRow(paper, $"{id}_dptest", "Depth Test", Enabled(ds.DepthTestEnabled));
            EditorGUI.StatRow(paper, $"{id}_dpwrite", "Depth Write", Enabled(ds.DepthWriteEnabled));
            EditorGUI.StatRow(paper, $"{id}_dpcompare", "Depth Comparison", ds.DepthComparison.ToString());
            EditorGUI.StatRow(paper, $"{id}_sttest", "Stencil Test", Enabled(ds.StencilTestEnabled));
            EditorGUI.StatRow(paper, $"{id}_streadmask", "Stencil Read Mask", FormatMask(ds.StencilReadMask));
            EditorGUI.StatRow(paper, $"{id}_stwritemask", "Stencil Write Mask", FormatMask(ds.StencilWriteMask));
            EditorGUI.StatRow(paper, $"{id}_stref", "Stencil Reference", ds.StencilReference.ToString());

            DrawStencilFace(paper, $"{id}_front", "Stencil Front", ds.StencilFront);
            DrawStencilFace(paper, $"{id}_back", "Stencil Back", ds.StencilBack);
        }
    }


    private static void DrawStencilFace(Paper paper, string id, string title, StencilBehaviorDescription behavior)
    {
        using (paper.Column(id).Height(UnitValue.Auto).ColBetween(2f).Enter())
        {
            Origami.Label(paper, $"{id}_title", title)
                .Subheading()
                .AlignLeft()
                .Padding(0f, 4f)
                .Show();

            EditorGUI.StatRow(paper, $"{id}_fail", "Stencil Fail", behavior.Fail.ToString());
            EditorGUI.StatRow(paper, $"{id}_pass", "Stencil Pass", behavior.Pass.ToString());
            EditorGUI.StatRow(paper, $"{id}_depthfail", "Depth Fail", behavior.DepthFail.ToString());
            EditorGUI.StatRow(paper, $"{id}_compare", "Comparison", behavior.Comparison.ToString());
        }
    }


    // ── Value formatting ──────────────────────────────────────────

    private static string Enabled(bool value) => value ? "Enabled" : "Disabled";

    private static string FormatMask(byte mask) => "0x" + mask.ToString("X2");

    private static string FormatColor(Prowl.Vector.Color color)
    {
        byte r = (byte)Math.Clamp(MathF.Round(color.R * 255f), 0f, 255f);
        byte g = (byte)Math.Clamp(MathF.Round(color.G * 255f), 0f, 255f);
        byte b = (byte)Math.Clamp(MathF.Round(color.B * 255f), 0f, 255f);
        byte a = (byte)Math.Clamp(MathF.Round(color.A * 255f), 0f, 255f);
        return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }


    // ── Variant parsing ───────────────────────────────────────────

    // A variant is a ";"-separated list of "Keyword=Value" pairs (see ShaderBindMetadata producer);
    // an empty value (bare keyword) is still a valid row.
    private static List<(string Keyword, string Value)> SplitVariant(string variant)
    {
        var keywords = new List<(string, string)>();
        var parts = variant.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (trimmed.Length == 0)
                continue;

            int eq = trimmed.IndexOf('=');
            if (eq < 0)
            {
                keywords.Add((trimmed, ""));
                continue;
            }

            string key = trimmed[..eq].Trim();
            string value = trimmed[(eq + 1)..].Trim();
            if (key.Length == 0)
                continue;

            keywords.Add((key, value));
        }
        return keywords;
    }
}