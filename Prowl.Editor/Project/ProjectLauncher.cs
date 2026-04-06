using System;
using System.IO;
using System.Linq;

using Prowl.Editor.Widgets;
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

    private static float _scrollY;
    private static int _hoveredIndex = -1;
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

        int w = Window.InternalWindow.Size.X;
        int h = Window.InternalWindow.Size.Y;

        // Full background
        // paper.Box("pl_bg")
        //     .PositionType(PositionType.SelfDirected)
        //     .Position(0, 0)
        //     .Size(w, h)
        //     .BackgroundColor(EditorTheme.Neutral100)
        //     .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
        //     {
        //         float cx = w / 2f;
        //         float cy = h / 2f;
        //         float radius = Math.Max(cx, cy) * 1.2f;
        //         float t = _animTime * 0.05f;
        //         var transparent = Prowl.Vector.Color32.FromArgb(0, 0, 0, 0);

        //         // Subtle background gradients (same as editor but dimmer)
        //         float px = cx + (float)Math.Sin(t) * cx * 0.6f;
        //         float py = cy + (float)Math.Sin(t * 2) * cy * 0.3f;
        //         var purple = Prowl.Vector.Color32.FromArgb(25, 140, 60, 200);
        //         canvas.SetRadialBrush(px, py, 0, radius, purple, transparent);
        //         canvas.BeginPath();
        //         canvas.Rect(0, 0, w, h);
        //         canvas.Fill();

        //         float bx = cx - (float)Math.Sin(t) * cx * 0.6f;
        //         float by = cy - (float)Math.Sin(t * 2) * cy * 0.3f;
        //         var blue = Prowl.Vector.Color32.FromArgb(25, 60, 140, 220);
        //         canvas.SetRadialBrush(bx, by, 0, radius, blue, transparent);
        //         canvas.BeginPath();
        //         canvas.Rect(0, 0, w, h);
        //         canvas.Fill();
        //     }));

        // Center card
        // float cardW = 600f;
        // float cardH = 500f;

        float sidebarW = 200f;

        using (paper.Row("root")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Size(w, h)
            .BorderColor(EditorTheme.Ink100)
            .BorderWidth(1)
            .BackgroundColor(EditorTheme.Neutral300)
            .Enter())
        {
            using (paper.Column("sidebar")
                .BackgroundColor(EditorTheme.Neutral400)
                .Size(sidebarW, h)
                .BorderColor(EditorTheme.Ink100)
                .BorderWidth(1)
                .Enter())
            {

                // Prowl Emblem
                using (paper.Row("pl_header")
                    .Height(60)
                    .RowBetween(12)
                    .BackgroundColor(EditorTheme.Neutral300)
                    .BorderColor(EditorTheme.Ink100)
                    .BorderWidth(1)
                    .Enter())
                {
                    paper.Box("pl_title")
                        .Height(60)
                        .Margin(16, 16, 8, 8)
                        .Text("PROWL", boldFont)
                        .TextColor(EditorTheme.Ink500)
                        .FontSize(28f)
                        .Alignment(TextAlignment.MiddleLeft);

                    // Spacer
                    paper.Box("pl_spacer");

                    paper.Box("pl_version")
                        .Height(60)
                        .Width(80)
                        .Margin(16, 16, 12, 8)
                        .Text("v0.0.1", font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(12f)
                        .Alignment(TextAlignment.MiddleRight);
                }

                paper.Box("spacer").Height(UnitValue.Stretch());

                // Links
                using (paper.Column("pl_header")
                    .Height(60)
                    .ColBetween(12)
                    .Margin(24, 12)
                    .Enter())
                {
                    EditorGUI.Button(paper, "yt_link", "YouTube").OnValueChanged((_) =>
                    {
                      WebService.OpenUrl("https://youtube.com/@prowlengine");
                    });

                    EditorGUI.Button(paper, "gh_link", "Github").OnValueChanged((_) =>
                    {
                      WebService.OpenUrl("https://github.com/ProwlEngine/Prowl");
                    });
                }
            }

            using (paper.Column("content")
                .Size(w - sidebarW, h)
                .Enter())
            {
                // Toolbar: New / Open buttons
                using (paper.Row("pl_toolbar")
                    .Height(40)
                    .Margin(10, 10, 16, 0)
                    .RowBetween(8)
                    .Enter())
                {
                    paper.Box("pl_recent_label")
                        .Width(UnitValue.Auto)
                        .Height(EditorTheme.RowHeight)
                        .Text("Projects", boldFont)
                        .TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize + 8)
                        .Alignment(TextAlignment.MiddleLeft);

                    // Spacer
                    paper.Box("pl_tb_spacer");

                    // Spacer
                    EditorGUI.SearchBar(paper, "search", "", "Search");
                    
                    EditorGUI.Button(paper, "pl_btn_open", $"{EditorIcons.FolderOpen}  Open Project")
                        .OnValueChanged(_ =>
                        {
                            FileDialog.Open(FileDialogMode.SelectFolder, path =>
                            {
                                        if (path == null) return;
                                        TryOpenProject(path);
                                    });
                        });

                    EditorGUI.Button(paper, "pl_btn_new", $"{EditorIcons.Plus}  New Project")
                        .OnValueChanged(_ => _showNewProject = !_showNewProject);

                }

                // New project panel (collapsible)
                if (_showNewProject)
                {
                    DrawNewProjectPanel(paper, font);
                }

                // Recent projects list
                DrawRecentProjects(paper, font, w - sidebarW, h - 60 - 40 - (_showNewProject ? 80 : 0));
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

                EditorGUI.TextField(paper, "pl_np_name", "", _newProjectName)
                    .OnValueChanged(v => _newProjectName = v);
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

                EditorGUI.Button(paper, "pl_np_browse", EditorIcons.FolderOpen, width: 30)
                    .OnValueChanged(_ =>
                    {
                        FileDialog.Open(FileDialogMode.SelectFolder, path =>
                        {
                                  if (path != null) _newProjectPath = path;
                              }, _newProjectPath);
                    });

                EditorGUI.Button(paper, "pl_np_create", "Create", width: 70)
                    .OnValueChanged(_ => TryCreateProject());
            }
        }
    }

    private static void DrawRecentProjects(Paper paper, Prowl.Scribe.FontFile font, float width, float height)
    {
        var entries = RecentProjects.Entries;

        using (ScrollView.Begin(paper, "pl_scroll", width, height, colSpacing: 8, paddingLeft: 8))
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
        }
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
            Runtime.Debug.LogWarning("Project name cannot be empty.");
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
            Runtime.Debug.LogError($"Failed to create project: {ex.Message}");
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
