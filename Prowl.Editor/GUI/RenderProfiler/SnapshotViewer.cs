using System;
using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime.Rendering;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// The snapshot viewer half of the render profiler window. Driven by a frozen <see cref="RenderSnapshot"/>,
/// it presents four sub-tabs (Passes, Draw Calls, Object Hierarchy, Counters). Each list-driven tab uses
/// a local selection that populates a dedicated inspector pane on the right (a real Inspector-window drawer
/// is not used because the snapshot payload is plain serializable DTOs, not EngineObjects, and the detail
/// views - the CPU texture inspector and the projected wireframe - are bespoke to this window). The shared
/// <see cref="SnapshotTextureInspector"/> and <see cref="WireframeViewer"/> render the heavy detail.
/// </summary>
public sealed class SnapshotViewer
{
    private enum SnapTab { Passes, DrawCalls, Objects, Counters }

    private SnapTab _tab = SnapTab.Passes;
    private RenderSnapshot? _bound;

    private int _selPass = -1;
    private int _selDraw = -1;
    private int _selObject = -1;
    private string _selResource = "";

    private readonly SnapshotTextureInspector _textureInspector = new();
    private readonly WireframeViewer _wireframe = new();

    private const float TabHeight = 30f;

    public void Draw(Paper paper, RenderSnapshot? snapshot, FontFile font, float width, float height)
    {
        if (!ReferenceEquals(snapshot, _bound))
        {
            _bound = snapshot;
            _selPass = _selDraw = _selObject = -1;
            _selResource = "";
        }

        using (paper.Column("rp_snap_root").Size(width, height).Enter())
        {
            using (paper.Box("rp_snap_tabs_wrap").Width(width).Height(TabHeight).Padding(8, 8, 0, 0).Enter())
                Origami.Tabs(paper, "rp_snap_tabs", (int)_tab, i => _tab = (SnapTab)i)
                    .Height(TabHeight)
                    .Tab("Passes")
                    .Tab("Draw Calls")
                    .Tab("Objects")
                    .Tab("Counters")
                    .Show();

            float contentH = height - TabHeight;
            using (paper.Box("rp_snap_content").Width(width).Height(contentH).Clip().Enter())
            {
                if (snapshot == null)
                {
                    EditorGUI.EmptyState(paper, "rp_snap_empty", "No snapshot. Capture or Load one.", font);
                    return;
                }

                switch (_tab)
                {
                    case SnapTab.Passes: DrawPasses(paper, snapshot, font, width, contentH); break;
                    case SnapTab.DrawCalls: DrawDrawCalls(paper, snapshot, font, width, contentH); break;
                    case SnapTab.Objects: DrawObjects(paper, snapshot, font, width, contentH); break;
                    case SnapTab.Counters: SnapshotCountersView.Draw(paper, snapshot.Report, font, width, contentH); break;
                }
            }
        }
    }

    // ---- shared split layout -------------------------------------------------

    private static (float listW, float detailW) Split(float width)
    {
        float listW = MathF.Max(180f, width * 0.36f);
        return (listW, width - listW);
    }

    // ---- Passes --------------------------------------------------------------

    private void DrawPasses(Paper paper, RenderSnapshot snap, FontFile font, float width, float height)
    {
        var passes = snap.Report.Passes;
        (float listW, float detailW) = Split(width);

        using (paper.Row("rp_snap_passes").Size(width, height).Enter())
        {
            using (paper.Box("rp_snap_passes_list").Width(listW).Height(height).Padding(6, 6, 6, 6).Enter())
                Origami.ScrollView(paper, "rp_snap_passes_scroll", listW - 4, height - 4).Body(() =>
                {
                    if (passes.Count == 0) { EditorGUI.EmptyState(paper, "rp_snap_passes_none", "No passes", font); return; }
                    using (paper.Column("rp_snap_passes_col").Width(listW - 16).Height(UnitValue.Auto).ColBetween(2).Enter())
                        for (int i = 0; i < passes.Count; i++)
                        {
                            var p = passes[i];
                            string label = string.IsNullOrEmpty(p.Name) ? $"Pass {i}" : p.Name;
                            string sub = $"{p.CpuMs:0.00} ms  {p.DrawCalls.Count} dc";
                            int idx = i;
                            ListRow(paper, $"rp_snap_p_{i}", label, sub, _selPass == i, p.IsPresentationSource ? EditorTheme.Green400 : EditorTheme.Purple400, font, () =>
                            {
                                _selPass = idx;
                                _selResource = FirstResource(passes[idx]);
                            });
                        }
                });

            using (paper.Box("rp_snap_passes_detail").Width(detailW).Height(height).Padding(8, 8, 6, 6).Enter())
                Origami.ScrollView(paper, "rp_snap_passes_dscroll", detailW - 4, height - 4).Body(() =>
                    DrawPassDetail(paper, snap, font, detailW - 20));
        }
    }

    private void DrawPassDetail(Paper paper, RenderSnapshot snap, FontFile font, float width)
    {
        var passes = snap.Report.Passes;
        if (_selPass < 0 || _selPass >= passes.Count)
        {
            EditorGUI.EmptyState(paper, "rp_snap_pd_none", "Select a pass", font);
            return;
        }

        var pass = passes[_selPass];
        using (paper.Column("rp_snap_pd").Width(width).Height(UnitValue.Auto).ColBetween(6).Enter())
        {
            Header(paper, "rp_snap_pd_h", string.IsNullOrEmpty(pass.Name) ? $"Pass {_selPass}" : pass.Name, font);
            KeyVal(paper, "rp_snap_pd_idx", "Index", pass.Index.ToString(), font);
            KeyVal(paper, "rp_snap_pd_ms", "CPU", $"{pass.CpuMs:0.000} ms", font);
            KeyVal(paper, "rp_snap_pd_pres", "Presentation", pass.IsPresentationSource ? "yes" : "no", font);

            SectionLabel(paper, "rp_snap_pd_sc_l", "Sample breakdown", font);
            if (pass.Root != null && pass.Root.Children.Count > 0)
                DrawScopeTree(paper, "rp_snap_pd_sc", pass.Root, 0, font, width);
            else
                Muted(paper, "rp_snap_pd_sc_none", "(no sub-samples)", font);

            SectionLabel(paper, "rp_snap_pd_res_l", "Resources", font);
            DrawResourceRows(paper, snap, pass, font, width);

            SectionLabel(paper, "rp_snap_pd_tex_l", "Texture", font);
            var tex = FindTexture(snap, _selResource);
            _textureInspector.Draw(paper, "rp_snap_pd_tex", tex, snap.Camera.Projection, font, width, 320f);

            SectionLabel(paper, "rp_snap_pd_dc_l", $"Draw calls ({pass.DrawCalls.Count})", font);
            for (int i = 0; i < pass.DrawCalls.Count; i++)
            {
                var dc = pass.DrawCalls[i];
                string mesh = string.IsNullOrEmpty(dc.MeshName) ? "(mesh)" : dc.MeshName;
                Muted(paper, $"rp_snap_pd_dc_{i}", $"{mesh}  {dc.MaterialName}  {dc.IndexCount} idx x{dc.InstanceCount}", font);
            }
        }
    }

    private void DrawResourceRows(Paper paper, RenderSnapshot snap, PassReport pass, FontFile font, float width)
    {
        var seen = new HashSet<string>();
        foreach (var input in pass.Inputs)
            if (seen.Add("in:" + input))
                ResourceRow(paper, snap, "in", input, pass.Index, font, width, isOutput: false);
        foreach (var output in pass.Outputs)
            if (seen.Add("out:" + output))
                ResourceRow(paper, snap, "out", output, pass.Index, font, width, isOutput: true);

        if (pass.Inputs.Count == 0 && pass.Outputs.Count == 0)
            Muted(paper, "rp_snap_pd_res_none", "(no declared resources)", font);
    }

    private void ResourceRow(Paper paper, RenderSnapshot snap, string kind, string resId, int passIndex, FontFile font, float width, bool isOutput)
    {
        string consumers = "";
        if (isOutput)
        {
            var report = FindResource(snap, resId);
            if (report != null && report.ConsumedByPassIndex.Count > 0)
            {
                var names = new List<string>();
                foreach (int ci in report.ConsumedByPassIndex)
                    names.Add(ci >= 0 && ci < snap.Report.Passes.Count ? snap.Report.Passes[ci].Name : ci.ToString());
                consumers = "  -> " + string.Join(", ", names);
            }
        }

        bool selected = _selResource == resId;
        string id = $"rp_snap_res_{kind}_{resId}";
        string local = resId;
        using (paper.Row(id).Width(width).Height(22).Padding(6, 6, 0, 0).Rounded(4)
            .BackgroundColor(selected ? EditorTheme.WithAlpha(EditorTheme.Accent, 55) : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .OnClick(_ => _selResource = local)
            .Enter())
        {
            paper.Box(id + "_k").Width(30).Height(22).Text(kind, font).FontSize(EditorTheme.FontSizeSmall - 1f)
                .TextColor(isOutput ? EditorTheme.Amber400 : EditorTheme.Blue400).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            paper.Box(id + "_n").Width(UnitValue.Stretch()).Height(22).Text(resId + consumers, font)
                .FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink400).Alignment(TextAlignment.MiddleLeft)
                .TextTruncate().IsNotInteractable();
        }
    }

    private void DrawScopeTree(Paper paper, string id, SampleScope scope, int depth, FontFile font, float width)
    {
        if (scope.Children == null) return;
        for (int i = 0; i < scope.Children.Count; i++)
        {
            var child = scope.Children[i];
            string childId = $"{id}_{depth}_{i}";
            using (paper.Row(childId).Width(width).Height(18).Padding(6 + depth * 14, 6, 0, 0).Enter())
            {
                paper.Box(childId + "_n").Width(UnitValue.Stretch()).Height(18).Text(child.Name, font)
                    .FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink400).Alignment(TextAlignment.MiddleLeft)
                    .TextTruncate().IsNotInteractable();
                paper.Box(childId + "_v").Width(70).Height(18).Text($"{child.CpuMs:0.000} ms", font)
                    .FontSize(EditorTheme.FontSizeSmall - 1f).TextColor(EditorTheme.InkDim).Alignment(TextAlignment.MiddleRight).IsNotInteractable();
            }
            DrawScopeTree(paper, id, child, depth + 1, font, width);
        }
    }

    // ---- Draw Calls ----------------------------------------------------------

    private void DrawDrawCalls(Paper paper, RenderSnapshot snap, FontFile font, float width, float height)
    {
        var flat = new List<(int passIndex, DrawCallReport dc)>();
        for (int pi = 0; pi < snap.Report.Passes.Count; pi++)
            foreach (var dc in snap.Report.Passes[pi].DrawCalls)
                flat.Add((pi, dc));

        (float listW, float detailW) = Split(width);

        using (paper.Row("rp_snap_dc").Size(width, height).Enter())
        {
            using (paper.Box("rp_snap_dc_list").Width(listW).Height(height).Padding(6, 6, 6, 6).Enter())
                Origami.ScrollView(paper, "rp_snap_dc_scroll", listW - 4, height - 4).Body(() =>
                {
                    if (flat.Count == 0) { EditorGUI.EmptyState(paper, "rp_snap_dc_none", "No draw calls", font); return; }
                    using (paper.Column("rp_snap_dc_col").Width(listW - 16).Height(UnitValue.Auto).ColBetween(2).Enter())
                        for (int i = 0; i < flat.Count; i++)
                        {
                            var (pi, dc) = flat[i];
                            string mesh = string.IsNullOrEmpty(dc.MeshName) ? "(mesh)" : dc.MeshName;
                            if (dc.SubMeshIndex >= 0) mesh += $" [{dc.SubMeshIndex}]";
                            string sub = $"{snap.Report.Passes[pi].Name}  {dc.IndexCount} idx";
                            int idx = i;
                            ListRow(paper, $"rp_snap_dc_{i}", mesh, sub, _selDraw == i, EditorTheme.Blue400, font, () => _selDraw = idx);
                        }
                });

            using (paper.Box("rp_snap_dc_detail").Width(detailW).Height(height).Padding(8, 8, 6, 6).Enter())
                Origami.ScrollView(paper, "rp_snap_dc_dscroll", detailW - 4, height - 4).Body(() =>
                {
                    if (_selDraw < 0 || _selDraw >= flat.Count) { EditorGUI.EmptyState(paper, "rp_snap_dc_pick", "Select a draw call", font); return; }
                    DrawDrawCallDetail(paper, snap, flat[_selDraw].passIndex, flat[_selDraw].dc, font, detailW - 20);
                });
        }
    }

    private void DrawDrawCallDetail(Paper paper, RenderSnapshot snap, int passIndex, DrawCallReport dc, FontFile font, float width)
    {
        using (paper.Column("rp_snap_dcd").Width(width).Height(UnitValue.Auto).ColBetween(5).Enter())
        {
            string mesh = string.IsNullOrEmpty(dc.MeshName) ? "(mesh)" : dc.MeshName;
            Header(paper, "rp_snap_dcd_h", mesh, font);
            KeyVal(paper, "rp_snap_dcd_sub", "SubMesh", dc.SubMeshIndex.ToString(), font);
            KeyVal(paper, "rp_snap_dcd_mguid", "Mesh guid", ShortGuid(dc.MeshGuid), font);
            KeyVal(paper, "rp_snap_dcd_mat", "Material", NameOr(dc.MaterialName, "(material)"), font);
            KeyVal(paper, "rp_snap_dcd_matg", "Material guid", ShortGuid(dc.MaterialGuid), font);
            KeyVal(paper, "rp_snap_dcd_sh", "Shader", NameOr(dc.ShaderName, "(shader)"), font);
            KeyVal(paper, "rp_snap_dcd_shg", "Shader guid", ShortGuid(dc.ShaderGuid), font);
            KeyVal(paper, "rp_snap_dcd_var", "Variant", dc.VariantKeywords.Count > 0 ? string.Join(" ", dc.VariantKeywords) : "(none)", font);
            KeyVal(paper, "rp_snap_dcd_pass", "Pass", passIndex >= 0 && passIndex < snap.Report.Passes.Count ? snap.Report.Passes[passIndex].Name : passIndex.ToString(), font);
            KeyVal(paper, "rp_snap_dcd_src", "Renderable id", dc.SourceRenderableId.ToString(), font);
            KeyVal(paper, "rp_snap_dcd_cnt", "Counts", $"{dc.IndexCount} idx  x{dc.InstanceCount}", font);

            SectionLabel(paper, "rp_snap_dcd_wf_l", "Wireframe", font);
            var geo = FindGeometry(snap, dc.MeshGuid, dc.SubMeshIndex);
            _wireframe.Draw(paper, "rp_snap_dcd_wf", geo, snap.Camera, font, width, 300f);
        }
    }

    // ---- Objects -------------------------------------------------------------

    private void DrawObjects(Paper paper, RenderSnapshot snap, FontFile font, float width, float height)
    {
        var groups = BuildObjectGroups(snap);
        (float listW, float detailW) = Split(width);

        using (paper.Row("rp_snap_obj").Size(width, height).Enter())
        {
            using (paper.Box("rp_snap_obj_list").Width(listW).Height(height).Padding(6, 6, 6, 6).Enter())
                Origami.ScrollView(paper, "rp_snap_obj_scroll", listW - 4, height - 4).Body(() =>
                {
                    if (groups.Count == 0) { EditorGUI.EmptyState(paper, "rp_snap_obj_none", "No objects", font); return; }
                    using (paper.Column("rp_snap_obj_col").Width(listW - 16).Height(UnitValue.Auto).ColBetween(2).Enter())
                        for (int i = 0; i < groups.Count; i++)
                        {
                            var grp = groups[i];
                            string label = grp.SourceId >= 0 ? $"Renderable {grp.SourceId}" : "(unattributed)";
                            string sub = $"{grp.Calls.Count} dc  {grp.Passes.Count} passes";
                            int idx = i;
                            ListRow(paper, $"rp_snap_obj_{i}", label, sub, _selObject == i, EditorTheme.Green400, font, () => _selObject = idx);
                        }
                });

            using (paper.Box("rp_snap_obj_detail").Width(detailW).Height(height).Padding(8, 8, 6, 6).Enter())
                Origami.ScrollView(paper, "rp_snap_obj_dscroll", detailW - 4, height - 4).Body(() =>
                {
                    if (_selObject < 0 || _selObject >= groups.Count) { EditorGUI.EmptyState(paper, "rp_snap_obj_pick", "Select an object", font); return; }
                    DrawObjectDetail(paper, snap, groups[_selObject], font, detailW - 20);
                });
        }
    }

    private sealed class ObjectGroup
    {
        public int SourceId = -1;
        public List<(int passIndex, DrawCallReport dc)> Calls = new();
        public HashSet<int> Passes = new();
    }

    private static List<ObjectGroup> BuildObjectGroups(RenderSnapshot snap)
    {
        var map = new Dictionary<int, ObjectGroup>();
        var order = new List<int>();
        for (int pi = 0; pi < snap.Report.Passes.Count; pi++)
            foreach (var dc in snap.Report.Passes[pi].DrawCalls)
            {
                if (!map.TryGetValue(dc.SourceRenderableId, out var grp))
                {
                    grp = new ObjectGroup { SourceId = dc.SourceRenderableId };
                    map[dc.SourceRenderableId] = grp;
                    order.Add(dc.SourceRenderableId);
                }
                grp.Calls.Add((pi, dc));
                grp.Passes.Add(pi);
            }

        var list = new List<ObjectGroup>(order.Count);
        foreach (int id in order) list.Add(map[id]);
        return list;
    }

    private void DrawObjectDetail(Paper paper, RenderSnapshot snap, ObjectGroup grp, FontFile font, float width)
    {
        using (paper.Column("rp_snap_od").Width(width).Height(UnitValue.Auto).ColBetween(5).Enter())
        {
            Header(paper, "rp_snap_od_h", grp.SourceId >= 0 ? $"Renderable {grp.SourceId}" : "Unattributed draws", font);
            KeyVal(paper, "rp_snap_od_src", "Source id", grp.SourceId.ToString(), font);
            bool visible = grp.Calls.Count > 0;
            KeyVal(paper, "rp_snap_od_vis", "Status", visible ? "visible (drew)" : "culled / no draws", font);

            SectionLabel(paper, "rp_snap_od_p_l", $"Passes ({grp.Passes.Count})", font);
            foreach (int pi in grp.Passes)
                Muted(paper, $"rp_snap_od_p_{pi}", pi >= 0 && pi < snap.Report.Passes.Count ? snap.Report.Passes[pi].Name : pi.ToString(), font);

            SectionLabel(paper, "rp_snap_od_dc_l", $"Draw calls ({grp.Calls.Count})", font);
            for (int i = 0; i < grp.Calls.Count; i++)
            {
                var (pi, dc) = grp.Calls[i];
                string mesh = string.IsNullOrEmpty(dc.MeshName) ? "(mesh)" : dc.MeshName;
                Muted(paper, $"rp_snap_od_dc_{i}", $"{mesh}  in {snap.Report.Passes[pi].Name}  {dc.IndexCount} idx", font);
            }
        }
    }

    // ---- lookups -------------------------------------------------------------

    private static string FirstResource(PassReport pass)
    {
        if (pass.Inputs.Count > 0) return pass.Inputs[0];
        if (pass.Outputs.Count > 0) return pass.Outputs[0];
        return "";
    }

    private static SnapshotTexture? FindTexture(RenderSnapshot snap, string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId)) return null;
        foreach (var t in snap.Textures)
            if (t.ResourceId == resourceId) return t;
        return null;
    }

    private static ResourceReport? FindResource(RenderSnapshot snap, string resourceId)
    {
        foreach (var r in snap.Report.Resources)
            if (r.Id == resourceId) return r;
        return null;
    }

    private static SnapshotGeometry? FindGeometry(RenderSnapshot snap, Guid meshGuid, int subMesh)
    {
        SnapshotGeometry? fallback = null;
        foreach (var g in snap.Geometry)
        {
            if (g.MeshGuid != meshGuid) continue;
            if (g.SubMeshIndex == subMesh) return g;
            fallback ??= g;
        }
        return fallback;
    }

    // ---- small ui helpers ----------------------------------------------------

    private static void ListRow(Paper paper, string id, string label, string sub, bool selected, Color accent, FontFile font, Action onClick)
    {
        using (paper.Column(id).Width(UnitValue.Percentage(100)).Height(38).Padding(8, 8, 4, 4).Rounded(4)
            .BackgroundColor(selected ? EditorTheme.WithAlpha(EditorTheme.Accent, 55) : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .OnClick(_ => onClick())
            .Enter())
        {
            paper.Box(id + "_l").Width(UnitValue.Percentage(100)).Height(18).Text(label, font)
                .FontSize(EditorTheme.FontSizeSmall).TextColor(selected ? accent : EditorTheme.Ink500)
                .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();
            paper.Box(id + "_s").Width(UnitValue.Percentage(100)).Height(14).Text(sub, font)
                .FontSize(EditorTheme.FontSizeSmall - 2f).TextColor(EditorTheme.InkDim)
                .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();
        }
    }

    private static void Header(Paper paper, string id, string text, FontFile font)
        => paper.Box(id).Width(UnitValue.Percentage(100)).Height(22).Text(text, font)
            .FontSize(EditorTheme.FontSize).TextColor(EditorTheme.Ink500)
            .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();

    private static void SectionLabel(Paper paper, string id, string text, FontFile font)
        => paper.Box(id).Width(UnitValue.Percentage(100)).Height(18).Text(text, font)
            .FontSize(EditorTheme.FontSizeSmall - 1f).TextColor(EditorTheme.Purple400)
            .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

    private static void Muted(Paper paper, string id, string text, FontFile font)
        => paper.Box(id).Width(UnitValue.Percentage(100)).Height(16).Text(text, font)
            .FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink300)
            .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();

    private static void KeyVal(Paper paper, string id, string key, string value, FontFile font)
    {
        using (paper.Row(id).Width(UnitValue.Percentage(100)).Height(18).Enter())
        {
            paper.Box(id + "_k").Width(110).Height(18).Text(key, font).FontSize(EditorTheme.FontSizeSmall - 1f)
                .TextColor(EditorTheme.InkDim).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            paper.Box(id + "_v").Width(UnitValue.Stretch()).Height(18).Text(value, font).FontSize(EditorTheme.FontSizeSmall)
                .TextColor(EditorTheme.Ink400).Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();
        }
    }

    private static string NameOr(string name, string fallback) => string.IsNullOrEmpty(name) ? fallback : name;
    private static string ShortGuid(Guid g) => g == Guid.Empty ? "(none)" : g.ToString("N").Substring(0, 12);
}
