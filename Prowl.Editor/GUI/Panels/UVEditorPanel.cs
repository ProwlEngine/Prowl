// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.GUI.SceneView.Editors;
using Prowl.Editor.Modeling;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// The UV layout of the model faces in edit mode over a texture. UV points are selected with the points of the scene
/// view, so picking one here picks its point there. Drag selected points to move them, G, R and S to move, turn and
/// scale them with the mouse, Alt drag to pan, the wheel to zoom and F to frame the unit square.
/// </summary>
public class UVEditorPanel : DockPanel
{
    [MenuItem("Window/General/UV Editor", priority: 10)]
    static void Open() => EditorApplication.Instance?.OpenPanel<UVEditorPanel>();

    public override string Title => Loc.Get("uv.title");
    public override string Icon => EditorIcons.BorderAll;

    private enum Backdrop { Texture, Checker, None }
    private enum Stretch { Off, Angle, Area, Islands, Overlap }
    private enum Gesture { None, Box, Move, Pan, Grab, Rotate, Scale }

    private const float PickRadius = 7f;

    private Backdrop _backdrop;
    private Stretch _stretch;
    private Rect _rect;
    private Float2 _offset = new(40f, 40f);
    private float _zoom = 300f;

    private Gesture _gesture;
    private Float2 _from;
    private Float2 _pivot;
    private readonly List<(ModelObject Model, GeometryData.Loop Corner, Float2 UV)> _moving = new();

    // Corners picked in this view, so the two sides of a seam can be told apart. Dropped once the points selected in the scene change
    private readonly HashSet<GeometryData.Loop> _corners = new();

    // With a pinned point moving, every shown corner as it started and the faces unwrapped again around the pins
    private readonly List<(GeometryData.Loop Corner, Float2 UV)> _before = new();
    private readonly List<Shown> _unwrapped = new();

    private readonly struct Shown(ModelObject model, GeometryData.Face face)
    {
        public readonly ModelObject Model = model;
        public readonly GeometryData.Face Face = face;
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        ModelMeshTool? tool = SceneToolManager.Get<ModelMeshTool>();
        if (tool is not { Editing: true })
        {
            Origami.Label(paper, "uv_off", Loc.Get("uv.not_editing")).Show();
            return;
        }

        SyncCorners(tool);
        List<Shown> faces = ShownFaces(tool);
        DrawToolbar(paper, tool, faces);

        paper.Box("uv_view").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
            .BackgroundColor(Color32.FromArgb(255, 30, 30, 34))
            .OnScroll(e =>
            {
                e.StopPropagation();
                Float2 at = paper.PointerPos - new Float2((float)_rect.Min.X, (float)_rect.Min.Y);
                float before = _zoom;
                _zoom = Math.Clamp(_zoom * MathF.Exp(e.Delta * 0.15f), 20f, 20000f);
                _offset = at - (at - _offset) * (_zoom / before);
            })
            .OnPostLayout((handle, rect) =>
            {
                _rect = rect;
                paper.Draw(ref handle, (canvas, r) => Paint(canvas, tool, faces));
            });

        HandleInput(paper, tool, faces);
    }

    // ================================================================
    //  What is shown
    // ================================================================

    /// <summary>The selected faces of the edited objects, or all their shown faces when none is selected.</summary>
    private static List<Shown> ShownFaces(ModelMeshTool tool)
    {
        bool anySelected = tool.EditedObjects.Any(m => tool.SelectionOf(m).Faces.Count > 0);
        var shown = new List<Shown>();
        foreach (ModelObject model in tool.EditedObjects)
        {
            IEnumerable<GeometryData.Face> faces = anySelected ? tool.SelectionOf(model).Faces : model.Mesh.Faces;
            foreach (GeometryData.Face face in faces)
                if (!tool.IsHidden(model, face)) shown.Add(new Shown(model, face));
        }
        return shown;
    }

    private bool Picked(ModelMeshTool tool, ModelObject model, GeometryData.Loop corner)
        => _corners.Count > 0 ? _corners.Contains(corner) : tool.SelectionOf(model).Vertices.Contains(corner.Vert);

    private void SyncCorners(ModelMeshTool tool)
    {
        if (_corners.Count == 0) return;
        var selected = new HashSet<GeometryData.Vertex>(tool.EditedObjects.SelectMany(m => tool.SelectionOf(m).Vertices));
        bool stillThere = _corners.All(c => tool.EditedObjects.Any(m => m.Mesh.Contains(c)));
        if (!stillThere || !selected.SetEquals(_corners.Select(c => c.Vert))) _corners.Clear();
    }

    /// <summary>Picks corners, and selects their points in the scene so the rest of the editor follows.</summary>
    private void PickCorners(ModelMeshTool tool, List<Shown> faces, IEnumerable<(ModelObject Model, GeometryData.Loop Corner)> picked, bool add)
    {
        if (add && _corners.Count == 0)
            foreach ((_, GeometryData.Loop corner) in SelectedCorners(tool, faces)) _corners.Add(corner);
        if (!add) _corners.Clear();
        foreach ((_, GeometryData.Loop corner) in picked) _corners.Add(corner);

        var points = new Dictionary<ModelObject, List<GeometryData.Vertex>>();
        foreach (ModelObject model in tool.EditedObjects)
            points[model] = [.. _corners.Where(model.Mesh.Contains).Select(c => c.Vert).Distinct()];
        tool.SelectPoints(points, false);
    }

    /// <summary>The shown corners on one point of a face that sit at the same UV as it, so a seam's other side is left out.</summary>
    private static IEnumerable<(ModelObject, GeometryData.Loop)> SameUV(List<Shown> faces, ModelObject model, GeometryData.Loop corner)
    {
        Float2 at = UVs.Get(corner);
        foreach (Shown shown in faces)
        {
            if (!ReferenceEquals(shown.Model, model) || UVs.CornerAt(shown.Face, corner.Vert) is not { } other) continue;
            if (Float2.Distance(UVs.Get(other), at) < 1e-5f) yield return (model, other);
        }
    }

    /// <summary>Every shown corner of the UV island the corner is on.</summary>
    private static IEnumerable<(ModelObject, GeometryData.Loop)> IslandOf(List<Shown> faces, ModelObject model, GeometryData.Loop corner)
    {
        List<GeometryData.Face> mine = [.. faces.Where(f => ReferenceEquals(f.Model, model)).Select(f => f.Face)];
        foreach (List<GeometryData.Face> island in UVs.Islands(model.Mesh, mine))
            if (island.Contains(corner.Face))
                return island.SelectMany(UVs.Corners).Select(c => (model, c)).ToList();
        return [];
    }

    /// <summary>The corners of the shown faces that are picked, which is what UV edits move.</summary>
    private List<(ModelObject Model, GeometryData.Loop Corner)> SelectedCorners(ModelMeshTool tool, List<Shown> faces)
    {
        var corners = new List<(ModelObject, GeometryData.Loop)>();
        foreach (Shown shown in faces)
            foreach (GeometryData.Loop corner in UVs.Corners(shown.Face))
                if (Picked(tool, shown.Model, corner)) corners.Add((shown.Model, corner));
        return corners;
    }

    private Float2 ToScreen(Float2 uv) => new((float)_rect.Min.X + _offset.X + uv.X * _zoom, (float)_rect.Min.Y + _offset.Y + (1f - uv.Y) * _zoom);

    private Float2 ToUV(Float2 screen) => new((screen.X - (float)_rect.Min.X - _offset.X) / _zoom, 1f - (screen.Y - (float)_rect.Min.Y - _offset.Y) / _zoom);

    // ================================================================
    //  Toolbar
    // ================================================================

    private void DrawToolbar(Paper paper, ModelMeshTool tool, List<Shown> faces)
    {
        using (paper.Row("uv_bar").Height(26).Gap(4).Padding(4, 4, 2, 2).Enter())
        {
            Origami.Dropdown(paper, "uv_backdrop", (int)_backdrop, i => _backdrop = (Backdrop)i,
                [Loc.Get("uv.backdrop_texture"), Loc.Get("uv.backdrop_checker"), Loc.Get("uv.backdrop_none")]).Width(110).Show();
            Origami.Dropdown(paper, "uv_stretch", (int)_stretch, i => _stretch = (Stretch)i,
                [Loc.Get("uv.stretch_off"), Loc.Get("uv.stretch_angle"), Loc.Get("uv.stretch_area"), Loc.Get("uv.overlay_islands"), Loc.Get("uv.overlay_overlap")]).Width(120).Show();
            Origami.Checkbox(paper, "uv_checker", ModelObject.ShowChecker, ModelEditingMode.SetChecker).Show();
            Origami.Label(paper, "uv_checker_label", Loc.Get("uv.checker_world")).Show();
            Origami.Button(paper, "uv_frame", Loc.Get("uv.frame"), Frame).Show();
            Origami.Button(paper, "uv_export", Loc.Get("uv.export_layout"), () => ExportLayout(tool, faces)).Show();
        }
        using (paper.Row("uv_ops").Height(26).Gap(4).Padding(4, 4, 2, 2).Enter())
        {
            void Corners(string id, string label, string undo, Action<GeometryData, List<GeometryData.Loop>> run)
                => Origami.Button(paper, id, Loc.Get(label), () => EditCorners(tool, faces, undo, run)).Show();
            void Faces(string id, string label, string undo, Action<GeometryData, List<GeometryData.Face>> run)
                => Origami.Button(paper, id, Loc.Get(label), () => EditFaces(tool, faces, undo, run)).Show();

            Corners("uv_pin", "uv.pin", "Pin UVs", (_, c) => UVs.SetPinned(c, true));
            Corners("uv_unpin", "uv.unpin", "Unpin UVs", (_, c) => UVs.SetPinned(c, false));
            Corners("uv_flip_u", "uv.flip_u", "Flip UVs", (m, c) => UVs.Flip(m, c, 0));
            Corners("uv_flip_v", "uv.flip_v", "Flip UVs", (m, c) => UVs.Flip(m, c, 1));
            Corners("uv_rotate", "uv.rotate_90", "Rotate UVs", (m, c) => UVs.Rotate(m, c, MathF.PI * 0.5f));
            Corners("uv_align_u", "uv.align_u", "Align UVs", (m, c) => UVs.Align(m, c, 0));
            Corners("uv_align_v", "uv.align_v", "Align UVs", (m, c) => UVs.Align(m, c, 1));
            Corners("uv_weld", "uv.weld", "Weld UVs", UVs.Weld);
            Corners("uv_straighten", "uv.straighten", "Straighten UVs", (m, c) => UVs.Straighten(m, c));
            Corners("uv_distribute", "uv.distribute", "Distribute UVs", (m, c) => UVs.Distribute(m, c));
            Origami.Button(paper, "uv_relax", Loc.Get("uv.relax"), () => EditCornersOnFaces(tool, faces, "Relax UVs", (m, c, f) => UVs.Relax(m, c, f, 0.5f))).Show();
            Origami.Button(paper, "uv_stitch", Loc.Get("uv.stitch"), () => EditCornersOnFaces(tool, faces, "Stitch UVs", (m, c, f) =>
            {
                var picked = new HashSet<GeometryData.Vertex>(c.Select(l => l.Vert));
                UVs.Stitch(m, f.SelectMany(UVs.Corners).Select(l => l.Edge).Distinct().Where(e => picked.Contains(e.Vert1) && picked.Contains(e.Vert2)).ToList(), f);
            })).Show();
            Corners("uv_pixels", "uv.snap_pixels", "Snap UVs To Pixels", (m, c) =>
            {
                (int w, int h) = TextureSize(tool);
                UVs.SnapToPixels(m, c, w, h);
            });
            Faces("uv_unwrap", "uv.unwrap", "Unwrap", UVs.Unwrap);
            // The faces are found again when run, as adjusting the step after runs it on the meshes undo brings back
            Origami.Button(paper, "uv_pack", Loc.Get("uv.pack"), () => tool.RunRemembered("Pack UVs", [new(Loc.Get("uv.margin"), 1f / 256f)],
                v => EditFaces(tool, ShownFaces(tool), "Pack UVs", (m, f) => UVs.Pack(m, f, MathF.Max(0f, v[0]))))).Show();
            Faces("uv_average", "uv.average_scale", "Average Island Scale", UVs.AverageIslandScale);
            Origami.Button(paper, "uv_by_angle", Loc.Get("uv.project_angle"), () => tool.RunRemembered("Project By Angle",
                [new(Loc.Get("model.field_degrees"), 66f), new(Loc.Get("uv.margin"), 1f / 256f)],
                v => EditFaces(tool, ShownFaces(tool), "Project By Angle", (m, f) => UVs.ByAngle(m, f, v[0], MathF.Max(0f, v[1]))))).Show();
            Faces("uv_follow", "uv.follow_quad", "Follow Quad", (m, f) =>
            {
                if (f.Where(x => x.VertCount == 4).OrderBy(x => x.Index).FirstOrDefault() is { } active) UVs.FollowQuad(m, active, f);
            });
        }
    }

    private void EditCorners(ModelMeshTool tool, List<Shown> faces, string name, Action<GeometryData, List<GeometryData.Loop>> run)
    {
        List<(ModelObject Model, GeometryData.Loop Corner)> corners = SelectedCorners(tool, faces);
        tool.EditMeshes(name, (model, mesh, _) =>
        {
            List<GeometryData.Loop> mine = [.. corners.Where(c => c.Model == model).Select(c => c.Corner)];
            if (mine.Count > 0) run(mesh, mine);
        });
    }

    private void EditCornersOnFaces(ModelMeshTool tool, List<Shown> faces, string name, Action<GeometryData, List<GeometryData.Loop>, List<GeometryData.Face>> run)
    {
        List<(ModelObject Model, GeometryData.Loop Corner)> corners = SelectedCorners(tool, faces);
        tool.EditMeshes(name, (model, mesh, _) =>
        {
            List<GeometryData.Loop> mine = [.. corners.Where(c => c.Model == model).Select(c => c.Corner)];
            List<GeometryData.Face> shown = [.. faces.Where(f => f.Model == model).Select(f => f.Face)];
            if (mine.Count > 0) run(mesh, mine, shown);
        });
    }

    private static void EditFaces(ModelMeshTool tool, List<Shown> faces, string name, Action<GeometryData, List<GeometryData.Face>> run)
        => tool.EditMeshes(name, (model, mesh, _) =>
        {
            List<GeometryData.Face> mine = [.. faces.Where(f => f.Model == model).Select(f => f.Face)];
            if (mine.Count > 0) run(mesh, mine);
        });

    /// <summary>Writes the shown faces' UV edges to a PNG at the texture's size.</summary>
    private static void ExportLayout(ModelMeshTool tool, List<Shown> faces)
    {
        (int width, int height) = TextureSize(tool);
        byte[] pixels = UVs.LayoutImage(faces.Select(f => f.Face), width, height);
        EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
        {
            if (path == null) return;
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
            try
            {
                using Aperture.Image image = Aperture.Image.FromPixels(pixels, width, height, Aperture.PixelFormat.Rgba8);
                image.Save(path, new Aperture.EncodeOptions { Format = Aperture.ImageFormat.Png });
            }
            catch (Exception e)
            {
                Debug.LogException("[UV] Writing the layout failed", e);
            }
        }, Project.Current?.AssetsPath, ["*.png"], [Loc.Get("uv.filter_png")]);
    }

    private void Frame()
    {
        float size = MathF.Max(40f, MathF.Min((float)_rect.Size.X, (float)_rect.Size.Y) - 40f);
        _zoom = size;
        _offset = new Float2(((float)_rect.Size.X - size) * 0.5f, ((float)_rect.Size.Y - size) * 0.5f);
    }

    // ================================================================
    //  Texture
    // ================================================================

    /// <summary>The base colour map of the material of the selected faces' slot, of the first edited object.</summary>
    private static Texture2D? ActiveTexture(ModelMeshTool tool)
    {
        if (tool.EditedObjects.Count == 0) return null;
        ModelObject model = tool.EditedObjects[0];
        int slot = Math.Max(0, tool.SelectedFaceSlot ?? 0);
        if (slot >= model.Materials.Count || model.Materials[slot] == Guid.Empty) return null;
        return AssetDatabase.Get<Material>(model.Materials[slot]) is { } material ? material.GetTexture("base_color_map") : null;
    }

    private static (int Width, int Height) TextureSize(ModelMeshTool tool)
        => ActiveTexture(tool) is { } texture ? ((int)texture.Width, (int)texture.Height) : (1024, 1024);

    // ================================================================
    //  Drawing
    // ================================================================

    private void Paint(Canvas canvas, ModelMeshTool tool, List<Shown> faces)
    {
        float x = (float)_rect.Min.X, y = (float)_rect.Min.Y, w = (float)_rect.Size.X, h = (float)_rect.Size.Y;
        canvas.SaveState();
        canvas.IntersectScissor(x, y, w, h);

        Float2 low = ToScreen(new Float2(0f, 1f)), high = ToScreen(new Float2(1f, 0f));
        float side = high.X - low.X;
        if (_backdrop == Backdrop.Texture && ActiveTexture(tool) is { } texture)
            canvas.DrawImage(texture, low.X, low.Y, side, side);
        else if (_backdrop != Backdrop.None)
        {
            const int Cells = 8;
            float cell = side / Cells;
            for (int i = 0; i < Cells; i++)
                for (int j = 0; j < Cells; j++)
                    canvas.RectFilled(low.X + i * cell, low.Y + j * cell, cell, cell,
                        (i + j) % 2 == 0 ? Color32.FromArgb(255, 70, 70, 76) : Color32.FromArgb(255, 50, 50, 56));
        }

        canvas.SetStrokeColor(Color32.FromArgb(120, 255, 255, 255));
        canvas.SetStrokeWidth(1f);
        canvas.BeginPath();
        canvas.MoveTo(low.X, low.Y);
        canvas.LineTo(high.X, low.Y);
        canvas.LineTo(high.X, high.Y);
        canvas.LineTo(low.X, high.Y);
        canvas.ClosePath();
        canvas.Stroke();

        float density = _stretch == Stretch.Area ? Density(faces) : 0f;
        HashSet<GeometryData.Face> overlapping = _stretch == Stretch.Overlap ? UVs.OverlappingFaces([.. faces.Select(f => f.Face)]) : [];
        var island = new Dictionary<GeometryData.Face, int>();
        if (_stretch == Stretch.Islands)
        {
            int offset = 0;
            foreach (IGrouping<ModelObject, Shown> group in faces.GroupBy(f => f.Model))
            {
                List<List<GeometryData.Face>> islands = UVs.Islands(group.Key.Mesh, [.. group.Select(f => f.Face)]);
                for (int i = 0; i < islands.Count; i++)
                    foreach (GeometryData.Face face in islands[i]) island[face] = offset + i;
                offset += islands.Count;
            }
        }
        foreach (Shown shown in faces)
        {
            List<GeometryData.Loop> corners = [.. UVs.Corners(shown.Face)];
            canvas.BeginPath();
            for (int i = 0; i < corners.Count; i++)
            {
                Float2 at = ToScreen(UVs.Get(corners[i]));
                if (i == 0) canvas.MoveTo(at.X, at.Y);
                else canvas.LineTo(at.X, at.Y);
            }
            canvas.ClosePath();
            if (_stretch is Stretch.Angle or Stretch.Area)
            {
                float amount = _stretch == Stretch.Angle ? AngleStretch(corners) : AreaStretch(shown.Face, density);
                canvas.SetFillColor(StretchColor(amount));
                canvas.Fill();
            }
            else if (_stretch == Stretch.Islands && island.TryGetValue(shown.Face, out int index))
            {
                canvas.SetFillColor(IslandColor(index));
                canvas.Fill();
            }
            else if (_stretch == Stretch.Overlap && overlapping.Contains(shown.Face))
            {
                canvas.SetFillColor(Color32.FromArgb(140, 235, 60, 50));
                canvas.Fill();
            }
            else if (shown.Face.NeighborVertices().TrueForAll(v => tool.SelectionOf(shown.Model).Vertices.Contains(v)))
            {
                canvas.SetFillColor(EditorTheme.WithAlpha(EditorTheme.Accent, 50));
                canvas.Fill();
            }
            canvas.SetStrokeColor(Color32.FromArgb(220, 190, 210, 240));
            canvas.SetStrokeWidth(1f);
            canvas.Stroke();
        }

        foreach (Shown shown in faces)
            foreach (GeometryData.Loop corner in UVs.Corners(shown.Face))
            {
                Float2 at = ToScreen(UVs.Get(corner));
                bool picked = Picked(tool, shown.Model, corner);
                Color32 colour = UVs.IsPinned(corner) ? Color32.FromArgb(255, 235, 70, 55) : picked ? EditorTheme.Accent : Color32.FromArgb(255, 20, 20, 20);
                canvas.CircleFilled(at.X, at.Y, picked ? 3.5f : 2.5f, colour);
            }

        if (_gesture == Gesture.Box)
        {
            Float2 now = Pointer;
            canvas.RectFilled(MathF.Min(_from.X, now.X), MathF.Min(_from.Y, now.Y), MathF.Abs(now.X - _from.X), MathF.Abs(now.Y - _from.Y), EditorTheme.WithAlpha(EditorTheme.Accent, 40));
        }
        canvas.RestoreState();
    }

    private static Color32 IslandColor(int index)
    {
        float hue = index * 0.618034f % 1f;
        float r = MathF.Abs(hue * 6f - 3f) - 1f, g = 2f - MathF.Abs(hue * 6f - 2f), b = 2f - MathF.Abs(hue * 6f - 4f);
        static byte Channel(float c) => (byte)(Math.Clamp(c, 0f, 1f) * 160f + 60f);
        return Color32.FromArgb(110, Channel(r), Channel(g), Channel(b));
    }

    private static Color32 StretchColor(float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color32.FromArgb(150, (byte)(60 + 195 * amount), (byte)(110 * (1f - amount) + 40), (byte)(230 * (1f - amount) + 20));
    }

    /// <summary>How far a face's UV corner angles are from its corner angles on the surface, 0 for none and 1 for a lot.</summary>
    private static float AngleStretch(List<GeometryData.Loop> corners)
    {
        float total = 0f;
        for (int i = 0; i < corners.Count; i++)
        {
            GeometryData.Loop prev = corners[(i + corners.Count - 1) % corners.Count], at = corners[i], next = corners[(i + 1) % corners.Count];
            float surface = Angle(prev.Vert.Point - at.Vert.Point, next.Vert.Point - at.Vert.Point);
            Float2 a = UVs.Get(prev) - UVs.Get(at), b = UVs.Get(next) - UVs.Get(at);
            float layout = Angle(new Float3(a.X, a.Y, 0f), new Float3(b.X, b.Y, 0f));
            total += MathF.Abs(surface - layout);
        }
        return total / corners.Count / (MathF.PI * 0.25f);
    }

    private static float Angle(Float3 a, Float3 b)
    {
        float length = Float3.Length(a) * Float3.Length(b);
        return length > 1e-12f ? MathF.Acos(Math.Clamp(Float3.Dot(a, b) / length, -1f, 1f)) : 0f;
    }

    private static (float Surface, float Texture) Areas(GeometryData.Face face)
    {
        float surface = 0f, texture = 0f;
        foreach ((GeometryData.Loop a, GeometryData.Loop b, GeometryData.Loop c) in UVs.Triangles(face))
        {
            surface += 0.5f * Float3.Length(Float3.Cross(b.Vert.Point - a.Vert.Point, c.Vert.Point - a.Vert.Point));
            Float2 ua = UVs.Get(a), ub = UVs.Get(b), uc = UVs.Get(c);
            texture += 0.5f * MathF.Abs((ub.X - ua.X) * (uc.Y - ua.Y) - (uc.X - ua.X) * (ub.Y - ua.Y));
        }
        return (surface, texture);
    }

    private static float Density(List<Shown> faces)
    {
        float surface = 0f, texture = 0f;
        foreach (Shown shown in faces)
        {
            (float s, float t) = Areas(shown.Face);
            surface += s;
            texture += t;
        }
        return surface > 0f ? texture / surface : 0f;
    }

    /// <summary>How far a face's share of texture is from the average, 0 for none and 1 for twice or half as much.</summary>
    private static float AreaStretch(GeometryData.Face face, float density)
    {
        (float surface, float texture) = Areas(face);
        if (surface <= 0f || density <= 0f) return 0f;
        float ratio = texture / surface / density;
        return ratio > 0f ? MathF.Abs(MathF.Log2(ratio)) : 1f;
    }

    // ================================================================
    //  Input
    // ================================================================

    private Float2 Pointer { get; set; }

    private void HandleInput(Paper paper, ModelMeshTool tool, List<Shown> faces)
    {
        Pointer = paper.PointerPos;
        bool over = paper.IsPointerOverRect((float)_rect.Min.X, (float)_rect.Min.Y, (float)_rect.Size.X, (float)_rect.Size.Y);

        if (_gesture is Gesture.Grab or Gesture.Rotate or Gesture.Scale)
        {
            Preview(tool, Modal(Pointer));
            if (paper.IsKeyPressed(PaperKey.Escape) || paper.IsPointerPressed(PaperMouseBtn.Right)) Cancel(tool);
            else if (paper.IsKeyPressed(PaperKey.Enter) || paper.IsPointerPressed(PaperMouseBtn.Left)) Commit(tool, Modal(Pointer));
            return;
        }

        if (_gesture == Gesture.Pan)
        {
            _offset += paper.PointerDelta;
            if (!paper.IsPointerDown(PaperMouseBtn.Left)) _gesture = Gesture.None;
            return;
        }
        if (_gesture == Gesture.Move)
        {
            Float2 delta = ToUV(Pointer) - ToUV(_from);
            Preview(tool, uv => uv + delta);
            if (paper.IsPointerDown(PaperMouseBtn.Left)) return;
            if (Float2.Distance(Pointer, _from) < 2f) Cancel(tool);
            else Commit(tool, uv => uv + delta);
            return;
        }
        if (_gesture == Gesture.Box)
        {
            if (!paper.IsPointerDown(PaperMouseBtn.Left)) SelectBox(paper, tool, faces);
            return;
        }

        if (!over) return;
        bool shift = paper.IsKeyDown(PaperKey.LeftShift) || paper.IsKeyDown(PaperKey.RightShift);
        bool alt = paper.IsKeyDown(PaperKey.LeftAlt) || paper.IsKeyDown(PaperKey.RightAlt);

        if (paper.IsKeyPressed(PaperKey.F)) Frame();
        else if (paper.IsKeyPressed(PaperKey.L) && Nearest(faces, float.MaxValue) is { } under) PickCorners(tool, faces, IslandOf(faces, under.Item1, under.Item2), shift);
        else if (paper.IsKeyPressed(PaperKey.G)) Begin(tool, faces, Gesture.Grab);
        else if (paper.IsKeyPressed(PaperKey.R)) Begin(tool, faces, Gesture.Rotate);
        else if (paper.IsKeyPressed(PaperKey.S)) Begin(tool, faces, Gesture.Scale);

        if (!paper.IsPointerPressed(PaperMouseBtn.Left)) return;
        _from = Pointer;
        if (alt)
        {
            _gesture = Gesture.Pan;
            return;
        }

        (ModelObject Model, GeometryData.Loop Corner)? hit = Nearest(faces);
        if (hit is { } point && Picked(tool, point.Model, point.Corner) && !shift)
        {
            Begin(tool, faces, Gesture.Move);
            return;
        }
        if (hit is { } clicked)
        {
            PickCorners(tool, faces, SameUV(faces, clicked.Model, clicked.Corner), shift);
            return;
        }
        _gesture = Gesture.Box;
    }

    private (ModelObject, GeometryData.Loop)? Nearest(List<Shown> faces, float reach = PickRadius)
    {
        (ModelObject, GeometryData.Loop)? best = null;
        float nearest = reach;
        foreach (Shown shown in faces)
            foreach (GeometryData.Loop corner in UVs.Corners(shown.Face))
            {
                float distance = Float2.Distance(ToScreen(UVs.Get(corner)), Pointer);
                if (distance >= nearest) continue;
                nearest = distance;
                best = (shown.Model, corner);
            }
        return best;
    }

    private void SelectBox(Paper paper, ModelMeshTool tool, List<Shown> faces)
    {
        _gesture = Gesture.None;
        Float2 min = new(MathF.Min(_from.X, Pointer.X), MathF.Min(_from.Y, Pointer.Y)), max = new(MathF.Max(_from.X, Pointer.X), MathF.Max(_from.Y, Pointer.Y));
        bool shift = paper.IsKeyDown(PaperKey.LeftShift) || paper.IsKeyDown(PaperKey.RightShift);
        var inside = new List<(ModelObject, GeometryData.Loop)>();
        foreach (Shown shown in faces)
            foreach (GeometryData.Loop corner in UVs.Corners(shown.Face))
            {
                Float2 at = ToScreen(UVs.Get(corner));
                if (at.X >= min.X && at.X <= max.X && at.Y >= min.Y && at.Y <= max.Y) inside.Add((shown.Model, corner));
            }
        PickCorners(tool, faces, inside, shift);
    }

    /// <summary>Starts moving the selected UV points, keeping where each started.</summary>
    private void Begin(ModelMeshTool tool, List<Shown> faces, Gesture gesture)
    {
        _moving.Clear();
        foreach ((ModelObject model, GeometryData.Loop corner) in SelectedCorners(tool, faces))
            _moving.Add((model, corner, UVs.Get(corner)));
        if (_moving.Count == 0) return;
        _before.Clear();
        _unwrapped.Clear();
        var pinnedFaces = new HashSet<GeometryData.Face>(_moving.Where(m => UVs.IsPinned(m.Corner)).Select(m => m.Corner.Face));
        if (pinnedFaces.Count > 0)
            foreach (IGrouping<ModelObject, Shown> group in faces.GroupBy(f => f.Model))
                foreach (List<GeometryData.Face> island in UVs.Islands(group.Key.Mesh, [.. group.Select(f => f.Face)]))
                {
                    if (!island.Exists(pinnedFaces.Contains)) continue;
                    foreach (GeometryData.Face face in island)
                    {
                        _unwrapped.Add(new Shown(group.Key, face));
                        foreach (GeometryData.Loop corner in UVs.Corners(face)) _before.Add((corner, UVs.Get(corner)));
                    }
                }
        Float2 sum = Float2.Zero;
        foreach ((_, _, Float2 uv) in _moving) sum += uv;
        _pivot = sum / _moving.Count;
        _from = Pointer;
        _gesture = gesture;
    }

    /// <summary>What the G, R or S gesture does to a UV with the pointer at a point.</summary>
    private Func<Float2, Float2> Modal(Float2 pointer)
    {
        Float2 pivot = ToScreen(_pivot);
        switch (_gesture)
        {
            case Gesture.Rotate:
            {
                Float2 a = _from - pivot, b = pointer - pivot;
                float angle = -(MathF.Atan2(b.Y, b.X) - MathF.Atan2(a.Y, a.X));
                float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
                Float2 centre = _pivot;
                return uv =>
                {
                    Float2 d = uv - centre;
                    return centre + new Float2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
                };
            }
            case Gesture.Scale:
            {
                float start = Float2.Distance(_from, pivot);
                float scale = start > 1f ? Float2.Distance(pointer, pivot) / start : 1f;
                Float2 centre = _pivot;
                return uv => centre + (uv - centre) * scale;
            }
            default:
            {
                Float2 delta = ToUV(pointer) - ToUV(_from);
                return uv => uv + delta;
            }
        }
    }

    /// <summary>Shows the moved points live, without recording anything.</summary>
    private void Preview(ModelMeshTool tool, Func<Float2, Float2> change)
    {
        Move(_moving, _unwrapped, change);
        foreach (ModelObject model in _moving.Select(m => m.Model).Distinct()) model.RefreshPreview();
    }

    /// <summary>Sets the moved points, and unwraps the faces again around the pins when a pinned point is among them.</summary>
    private static void Move(List<(ModelObject Model, GeometryData.Loop Corner, Float2 UV)> moving, List<Shown> unwrapped, Func<Float2, Float2> change, ModelObject? only = null)
    {
        foreach ((ModelObject model, GeometryData.Loop corner, Float2 uv) in moving)
            if (ReferenceEquals(only, null) || ReferenceEquals(model, only)) UVs.Set(corner, change(uv));
        foreach (IGrouping<ModelObject, Shown> group in unwrapped.GroupBy(f => f.Model))
            if (ReferenceEquals(only, null) || ReferenceEquals(group.Key, only)) UVs.Unwrap(group.Key.Mesh, [.. group.Select(f => f.Face)]);
    }

    private void Restore()
    {
        foreach ((GeometryData.Loop corner, Float2 uv) in _before) UVs.Set(corner, uv);
        foreach ((_, GeometryData.Loop corner, Float2 uv) in _moving) UVs.Set(corner, uv);
    }

    private void Cancel(ModelMeshTool tool)
    {
        Restore();
        foreach (ModelObject model in _moving.Select(m => m.Model).Distinct()) model.RefreshPreview();
        _moving.Clear();
        _before.Clear();
        _unwrapped.Clear();
        _gesture = Gesture.None;
    }

    /// <summary>Puts the points back, then moves them once more as one undo step.</summary>
    private void Commit(ModelMeshTool tool, Func<Float2, Float2> change)
    {
        Restore();
        var moving = _moving.ToList();
        var unwrapped = _unwrapped.ToList();
        _moving.Clear();
        _before.Clear();
        _unwrapped.Clear();
        _gesture = Gesture.None;
        tool.EditMeshes("Move UVs", (model, mesh, _) =>
        {
            UVs.EnsureUV(mesh);
            Move(moving, unwrapped, change, model);
            mesh.MarkChanged();
        });
    }
}
