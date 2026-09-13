using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI.RenderProfiler.Inspectors;

// Snapshot-only: ProfiledCallingObject only exists on a captured frame's switch hierarchy.
public static class ProfilerObjectInspector
{
    public static void Draw(Paper paper, ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer,
        ProfiledPipeline? pipeline, ProfiledCallingObject? obj)
    {
        if (obj == null)
        {
            EditorGUI.EmptyState(paper, "rdp_obj_empty", "No object selected", EditorTheme.DefaultFont!);
            return;
        }

        using (paper.Column("rdp_obj_viewer").Height(UnitValue.Auto).Gap(InspectorKit.SectionGap).Enter())
        {
            using (paper.Row("rdp_obj_header").Height(InspectorKit.SelectionViewerHeaderHeight).Gap(8f).Enter())
            {
                Origami.Label(paper, "rdp_obj_title", obj.Label)
                    .Heading()
                    .AlignLeft()
                    .Show();

                if (view != null)
                {
                    Origami.Label(paper, "rdp_obj_view", view.Name)
                        .Muted()
                        .AlignLeft()
                        .Show();
                }

                if (pass != null)
                {
                    Origami.Label(paper, "rdp_obj_pass", pass.Name)
                        .Muted()
                        .AlignLeft()
                        .Show();
                }

                paper.Box("rdp_obj_header_spacer");

                Origami.Label(paper, "rdp_obj_status", obj.Culled ? "Culled" : "Rendered")
                    .Variant(obj.Culled ? OrigamiVariant.Subtle : OrigamiVariant.Success)
                    .AlignRight()
                    .Show();
            }

            InspectorKit.SectionCard(paper, "rdp_obj_properties", "Properties", () => DrawPropertiesCard(paper, obj));
            InspectorKit.SectionCard(paper, "rdp_obj_context", "Context", () => DrawContextCard(paper, view, pass, commandBuffer, pipeline));
        }
    }


    private static void DrawPropertiesCard(Paper paper, ProfiledCallingObject obj)
    {
        using (paper.Column("rdp_obj_properties_rows").Height(UnitValue.Auto).Gap(2f).Enter())
        {
            EditorGUI.StatRow(paper, "rdp_obj_label", "Label", obj.Label);
            EditorGUI.StatRow(paper, "rdp_obj_material", "Material", obj.MaterialName);
            EditorGUI.StatRow(paper, "rdp_obj_mesh", "Mesh", obj.MeshName);
            EditorGUI.StatRow(paper, "rdp_obj_layer", "Layer", obj.Layer.ToString());
            EditorGUI.StatRow(paper, "rdp_obj_position", "Position", FormatPosition(obj));
            EditorGUI.StatRow(paper, "rdp_obj_culled", "Culled", obj.Culled.ToString());
            EditorGUI.StatRow(paper, "rdp_obj_drawrange", "Draw Range", $"{obj.DrawStart} - {obj.DrawEnd}");
            EditorGUI.StatRow(paper, "rdp_obj_drawcount", "Draw Count", (obj.DrawEnd - obj.DrawStart).ToString());
        }
    }


    private static void DrawContextCard(Paper paper, ProfiledView? view, ProfiledPass? pass,
        ProfiledCommandBuffer? commandBuffer, ProfiledPipeline? pipeline)
    {
        using (paper.Column("rdp_obj_context_rows").Height(UnitValue.Auto).Gap(2f).Enter())
        {
            if (pipeline != null)
            {
                EditorGUI.StatRow(paper, "rdp_obj_ctx_shader", "Shader", pipeline.ShaderName);
                EditorGUI.StatRow(paper, "rdp_obj_ctx_pass", "Shader Pass", pipeline.ShaderPassName);
                EditorGUI.StatRow(paper, "rdp_obj_ctx_variant", "Variant", pipeline.Variant);
            }

            if (commandBuffer != null)
                EditorGUI.StatRow(paper, "rdp_obj_ctx_cmd", "Command Buffer", commandBuffer.Name);
            if (pass != null)
                EditorGUI.StatRow(paper, "rdp_obj_ctx_passname", "Pass", pass.Name);
            if (view != null)
                EditorGUI.StatRow(paper, "rdp_obj_ctx_view", "View", view.Name);
        }
    }


    private static string FormatPosition(ProfiledCallingObject obj)
        => $"({obj.Position.X:F2}, {obj.Position.Y:F2}, {obj.Position.Z:F2})";
}