using System;
using System.IO;
using System.Linq;

using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor;

/// <summary>
/// Full-screen project launcher shown at startup before the editor loads.
/// Shows recent projects, and allows creating/opening projects.
/// </summary>
public static class ProjectLauncher
{
    public static bool IsOpen { get; private set; } = true;

    private static string _newProjectName = "MyGame";
    private static string _newProjectPath = "";
    private static bool _showNewProject;
    private static float _animTime;

    public static void Initialize()
    {
        _newProjectPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prowl Projects");
        IsOpen = true;
        _showNewProject = false;
        _animTime = 0;
    }

    public static void Close()
    {
        IsOpen = false;
    }

    public static void Draw(Paper paper, float dt, bool forceDraw = false)
    {
        if (!IsOpen && !forceDraw) return;
        var font = EditorTheme.DefaultFont;
        var boldFont = EditorTheme.DefaultBoldFont ?? font;
        if (font == null) return;

        _animTime += dt;

        float w = paper.ScreenRect.Size.X;
        float h = paper.ScreenRect.Size.Y;

        // Full background
        paper.Box("pl_bg")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Size(w, h)
            .BackgroundColor(EditorTheme.Neutral100)
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
            {
                float cx = w / 2f;
                float cy = h / 2f;
                float radius = Math.Max(cx, cy) * 1.2f;
                float t = _animTime * 0.05f;
                var transparent = Prowl.Vector.Color32.FromArgb(0, 0, 0, 0);

                // Subtle background gradients (same as editor but dimmer)
                float px = cx + (float)Math.Sin(t) * cx * 0.6f;
                float py = cy + (float)Math.Sin(t * 2) * cy * 0.3f;
                var purple = Prowl.Vector.Color32.FromArgb(25, 140, 60, 200);
                canvas.SetRadialBrush(px, py, 0, radius, purple, transparent);
                canvas.BeginPath();
                canvas.Rect(0, 0, w, h);
                canvas.Fill();

                float bx = cx - (float)Math.Sin(t) * cx * 0.6f;
                float by = cy - (float)Math.Sin(t * 2) * cy * 0.3f;
                var blue = Prowl.Vector.Color32.FromArgb(25, 60, 140, 220);
                canvas.SetRadialBrush(bx, by, 0, radius, blue, transparent);
                canvas.BeginPath();
                canvas.Rect(0, 0, w, h);
                canvas.Fill();
            }));

        // Center card
        float cardW = 600f;
        float cardH = 500f;

        using (paper.Box("container").Size(w, h).Position(0, 0).PositionType(PositionType.SelfDirected).Enter())
        {
            using (paper.Column("pl_window")
                .Margin(UnitValue.StretchOne)
                .Size(cardW, cardH)
                .BorderColor(EditorTheme.Ink100)
                .BorderWidth(1)
                .Rounded(EditorTheme.Roundness)
                .BackgroundColor(EditorTheme.Neutral300)
                .Enter())
            {
                // Header
                using (paper.Row("header")
                    .Height(60)
                    .RowBetween(0)
                    .RoundedTop(EditorTheme.Roundness)
                    .BackgroundColor(EditorTheme.Neutral300)
                    .BorderColor(EditorTheme.Ink100)
                    .BorderWidth(1)
                    .Enter())
                {
                    paper.Box("pl_title")
                        .Height(60)
                        .Width(110)
                        .Margin(16, 0, 8, 0)
                        .Text("PROWL", boldFont)
                        .TextColor(EditorTheme.Ink500)
                        .FontSize(28f)
                        .Alignment(TextAlignment.MiddleLeft);

                    // Links
                    using (paper.Row("pl_header")
                        .Height(UnitValue.Auto)
                        .RowBetween(12)
                        .Margin(0, 0, 28, 0)
                        .Enter())
                    {
                        Origami.IconButton(paper, "www_link", EditorIcons.Globe, () =>
                        {
                            EditorUtils.OpenUrl("https://prowlengine.com");
                        }).Ghost().Show();

                        Origami.IconButton(paper, "ds_link", EditorIcons.Message, () =>
                        {
                            EditorUtils.OpenUrl("https://discord.gg/HgBsBqfSpa");
                        }).Ghost().Show();

                        Origami.IconButton(paper, "yt_link", EditorIcons.Video, () =>
                        {
                            EditorUtils.OpenUrl("https://youtube.com/@prowlengine");
                        }).Ghost().Show();

                        Origami.IconButton(paper, "gh_link", EditorIcons.Code, () =>
                        {
                            EditorUtils.OpenUrl("https://github.com/ProwlEngine/Prowl");
                        }).Ghost().Show();
                    }

                    paper.Box("spacer");


                    paper.Box("pl_version")
                        .Height(60)
                        .Width(80)
                        .Margin(16, 16, 12, 8)
                        .Text("v0.0.1", font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(12f)
                        .Alignment(TextAlignment.MiddleRight);
                }


                using (paper.Column("content")
                    .Size(cardW, cardH - 90)
                    .Enter())
                {
                    // New / Open buttons
                    using (paper.Row("toolbar")
                        .Height(30)
                        .Margin(10, 10, 16, 0)
                        .RowBetween(8)
                        .Enter())
                    {
                        // Spacer (search input is currently visual-only — no filter wired up)
                        Origami.SearchField(paper, "search", "", _ => { }, "Search Projects").Show();

                        Origami.Button(paper, "tl_btn_open", $"{EditorIcons.FolderOpen}  Open Project", () =>
                            {
                                EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder, path =>
                                {
                                    if (path == null) return;
                                    TryOpenProject(path);
                                });
                            }).Width(130).Show();

                        using (paper.Box("tl_btn_new")
                            .Height(EditorTheme.RowHeight)
                            .Width(120)
                            .BackgroundColor(EditorTheme.Blue300)
                            .Hovered.BackgroundColor(EditorTheme.Blue400).End()
                            .Rounded(3)
                            .BorderColor(EditorTheme.Blue400)
                            .BorderWidth(1)
                            .OnClick((_) => _showNewProject = !_showNewProject)
                            .Enter())
                        {
                            paper.Box($"label")
                                .Height(EditorTheme.RowHeight)
                                .Margin(EditorTheme.RowHeight / 4, 0)
                                .Alignment(PaperUI.TextAlignment.MiddleLeft)
                                .Text($" {EditorIcons.Plus}  New Project", EditorTheme.DefaultFont)
                                .TextColor(EditorTheme.Ink500)
                                .FontSize(EditorTheme.FontSize);
                        }
                    }

                    // New project panel (collapsible)
                    if (_showNewProject)
                    {
                        DrawNewProjectPanel(paper, font);
                    }

                    // Recent projects list
                    DrawRecentProjects(paper, font, cardW, cardH - 60 - 46 - (_showNewProject ? 80 : 0));
                }
            }
        }
    }

    private static void DrawNewProjectPanel(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Column("pl_newproj")
            .BackgroundColor(EditorTheme.Neutral400)
            .Rounded(6)
            .Height(UnitValue.Auto)
            .Margin(8, 8, 0, 8)
            .Enter())
        {
            using (paper.Row("pl_np_row1").Height(EditorTheme.RowHeight).Margin(8).RowBetween(8).Enter())
            {
                paper.Box("pl_np_lbl")
                    .Width(50)
                    .Height(EditorTheme.RowHeight)
                    .Text("Name:", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleRight);

                Origami.TextField(paper, "pl_np_name", _newProjectName, v => _newProjectName = v)
                    .Width(UnitValue.Stretch()).Show();
            }

            using (paper.Row("pl_np_row2").Height(EditorTheme.RowHeight).Margin(8, 8, 0, 8).RowBetween(8).Enter())
            {
                paper.Box("pl_np_lbl2")
                    .Width(50)
                    .Height(EditorTheme.RowHeight)
                    .Text("Path:", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleRight);

                using (paper.Box("prject_path_display")
                        .Height(EditorTheme.RowHeight)
                        .ChildLeft(4)
                        .ChildRight(4)
                        .BackgroundColor(EditorTheme.Neutral300)
                        .Rounded(4)
                        .Enter())
                {
                    paper.Box("pl_np_path")
                        .Text(" " + _newProjectPath, font)
                        .TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize - 3)
                        .Alignment(TextAlignment.MiddleLeft);
                }

                Origami.Button(paper, "pl_np_browse", EditorIcons.FolderOpen, () =>
                    {
                        EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder, path =>
                        {
                            if (path != null) _newProjectPath = path;
                        }, _newProjectPath);
                    }).Width(30).Show();

                Origami.Button(paper, "pl_np_create", "Create", () => { TryCreateProject(); }).Width(70).Show();
            }
        }
    }

    private static void DrawRecentProjects(Paper paper, Prowl.Scribe.FontFile font, float width, float height)
    {
        var entries = RecentProjects.Entries;

        Origami.ScrollView(paper, "pl_scroll", width, height).Padding(8, 8, 0, 0).ColSpacing(8).Body(() =>
        {
            if (entries.Count == 0)
            {
                paper.Box("pl_empty").Height(100)
                    .Text("No recent projects", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleCenter);

                paper.Box("pl_hint").Height(30)
                    .Text("Create a new project or open an existing one to get started.", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 3)
                    .Alignment(TextAlignment.MiddleCenter);
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                bool exists = Directory.Exists(entry.Path);
                int idx = i;

                using (paper.Row($"pl_proj_{i}")
                    .Height(52)
                    .BackgroundColor(exists ? EditorTheme.Neutral400 : EditorTheme.Red400)
                    .Hovered.BackgroundColor(exists ? EditorTheme.Neutral500 : EditorTheme.Red500).End()
                    .Rounded(6)
                    .ChildLeft(12)
                    .ChildRight(12)
                    .RowBetween(12)
                    .OnClick(entry, (e, _) =>
                    {
                        if (Directory.Exists(e.Path))
                            TryOpenProject(e.Path);
                    })
                    .Enter())
                {
                    // Project icon
                    paper.Box($"pl_pi_{i}").Width(32).Height(52)
                        .Text(EditorIcons.Cubes, font)
                        .TextColor(exists ? EditorTheme.Purple400 : EditorTheme.Ink300)
                        .FontSize(20f).Alignment(TextAlignment.MiddleCenter);

                    // Name + Path
                    using (paper.Column($"pl_info_{i}").Height(52).ColBetween(2).ChildTop(8).Enter())
                    {
                        paper.Box($"pl_pn_{i}").Height(20)
                            .Text(entry.Name, font)
                            .TextColor(exists ? EditorTheme.Ink500 : EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSize)
                            .Alignment(TextAlignment.MiddleLeft);

                        paper.Box($"pl_pp_{i}").Height(16)
                            .Text(entry.Path, font)
                            .TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSize - 4)
                            .Alignment(TextAlignment.MiddleLeft);
                    }

                    // Last opened time
                    string timeAgo = FormatTimeAgo(entry.LastOpened);
                    paper.Box($"pl_pt_{i}").Width(80).Height(52)
                        .Text(timeAgo, font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize - 3)
                        .Alignment(TextAlignment.MiddleRight);

                    // Remove button
                    paper.Box($"pl_pr_{i}").Width(24).Height(52)
                        .Text(EditorIcons.Xmark, font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(10f).Alignment(TextAlignment.MiddleCenter)
                        .Hovered.BackgroundColor(Color.FromArgb(255, 180, 60, 60)).End()
                        .Rounded(4)
                        .StopEventPropagation()
                        .OnClick(entry.Path, (p, _) => RecentProjects.Remove(p));

                    if (!exists)
                    {
                        paper.Box($"pl_pmissing_{i}").Width(60).Height(52)
                            .Text("Missing", font)
                            .TextColor(EditorTheme.Red400)
                            .FontSize(EditorTheme.FontSize - 3)
                            .Alignment(TextAlignment.MiddleCenter);
                    }
                }
            }

            paper.Box("spacer").Height(6);
        });
    }

    private static void TryOpenProject(string path)
    {
        try
        {
            var project = Project.Open(path);
            project.SetActive();
            Close();
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to open project: {ex.Message}");
        }
    }

    private static void TryCreateProject()
    {
        if (string.IsNullOrWhiteSpace(_newProjectName))
        {
            Toasts.Show("Invalid Name", "Project name cannot be empty.", ToastType.Warning, 3f);
            return;
        }

        string targetPath = Path.Combine(_newProjectPath, _newProjectName);
        if (Directory.Exists(targetPath) && Directory.GetFileSystemEntries(targetPath).Length > 0)
        {
            Toasts.Show("Folder Exists", $"'{_newProjectName}' already exists and is not empty. Choose a different name or location.", ToastType.Error, 5f);
            return;
        }

        try
        {
            var project = Project.Create(_newProjectPath, _newProjectName);
            project.SetActive();
            Close();
        }
        catch (Exception ex)
        {
            Toasts.Show("Create Failed", ex.Message, ToastType.Error, 5f);
        }
    }

    private static string FormatTimeAgo(DateTime utcTime)
    {
        var span = DateTime.UtcNow - utcTime;
        if (span.TotalMinutes < 1) return "Just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";
        return $"{(int)(span.TotalDays / 365)}y ago";
    }
}
