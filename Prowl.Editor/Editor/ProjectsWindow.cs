// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor
{
    public class ProjectsWindow : EditorWindow
    {
        private static readonly string s_defaultPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.Create));


        public Project? SelectedProject = null;

        private string _searchText = "";
        private string _createName = "";

        private (string, Action)[] _tabs;
        private int _currentTab = 0;

        private Action? _sideTab = null;

        private FileDialog _dialog;
        private FileDialogContext _dialogContext;


        protected override bool Center { get; } = true;
        protected override double Width { get; } = 1024;
        protected override double Height { get; } = 640;
        protected override bool BackgroundFade { get; } = true;
        protected override bool TitleBar { get; } = false;
        protected override bool RoundCorners => false;
        protected override bool LockSize => true;
        protected override double Padding => 0;


        public ProjectsWindow() : base()
        {
            Title = FontAwesome6.Book + " Project Window";

            _tabs = [
                (FontAwesome6.RectangleList + "  Projects", DrawProjectsTab),
                (FontAwesome6.BookOpen + "  Learn", () => {})
            ];
        }


        protected override void Draw()
        {
            if (Project.HasProject)
                isOpened = false;

            using (gui.Node("TopBar").ExpandWidth().MaxHeight(50).Enter())
            {
                using (gui.Node("Name").Scale(150, 50).Enter())
                {
                    Rect rect = gui.CurrentNode.LayoutData.Rect;
                    gui.Draw2D.DrawText(Font.DefaultFont, "Prowl", 40, rect, Color.white);
                }

                using (gui.Node("Settings").Scale(40).Top(5).Enter())
                {
                    gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 5));

                    Interactable interact = gui.GetInteractable();

                    Rect rect = gui.CurrentNode.LayoutData.Rect;

                    if (interact.TakeFocus())
                    {
                        gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Highlighted, 5, CornerRounding.All);
                        Debug.Log("Opened editor settings");
                    }
                    else if (interact.IsHovered())
                        gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Hovering, 5, CornerRounding.All);

                    gui.Draw2D.DrawText(FontAwesome6.Gear, 30, gui.CurrentNode.LayoutData.Rect);
                }
            }

            using (gui.Node("Content").ExpandWidth().Layout(LayoutType.Row).Enter())
            {
                double width = gui.CurrentNode.LayoutData.Rect.width;

                using (gui.Node("Side").ExpandHeight().Width(150).Layout(LayoutType.Column).Spacing(5).Enter())
                {
                    DrawSidePanel();
                }

                const double sideTabWidth = 300;

                using (gui.Node("TabContent").Width(width - (150 + (_sideTab == null ? 0 : sideTabWidth))).ExpandHeight().Enter())
                {
                    _tabs[_currentTab].Item2.Invoke();
                }

                if (_sideTab != null)
                {
                    using (gui.Node("SideTabContent").Width(sideTabWidth).ExpandHeight().Enter())
                    {
                        gui.CurrentNode.Left(Offset.Percentage(1.0f));

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, Color.red);

                        _sideTab.Invoke();
                    }
                }
            }
        }


        private void DrawProjectsTab()
        {
            Rect rect = gui.CurrentNode.LayoutData.Rect;
            gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.WindowBGOne * 0.25f, (float)EditorStylePrefs.Instance.WindowRoundness);

            Vector2 shadowA = new(rect.x, rect.y + 30);
            Vector2 shadowB = new(rect.x + rect.width, rect.y + 30);

            gui.Draw2D.DrawHorizontalBlackGradient(shadowA, shadowB, -30, 0.25f);

            shadowA = new(rect.x, rect.y);
            shadowB = new(rect.x, rect.y + (rect.height - 60));

            gui.Draw2D.DrawVerticalBlackGradient(shadowA, shadowB, 30, 0.25f);

            Rect footer = new(shadowB.x, shadowB.y, rect.width, 60);
            gui.Draw2D.DrawRectFilled(footer, EditorStylePrefs.Instance.WindowBGOne, (float)EditorStylePrefs.Instance.WindowRoundness, 4);

            shadowA = shadowB;
            shadowB = new(rect.x, rect.y + rect.height);
            gui.Draw2D.DrawVerticalBlackGradient(shadowA, shadowB, 20, 0.25f);


            using (gui.Node("TopBar").ExpandWidth().Height(40).Enter())
            {
                using (gui.Node("Search").TopLeft(30, 10).Scale(150, 30).Enter())
                {
                    gui.InputField("SearchInput", ref _searchText, 0x100, Gui.InputFieldFlags.None, 0, 0, 150, null, EditorGUI.GetInputStyle());
                }

                if (_sideTab == null)
                {
                    using (gui.Node("Add").Top(7.5).Scale(75, 30).Enter())
                    {
                        gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 87.5));

                        Interactable interact = gui.GetInteractable();

                        Color openCol = Color.white * 0.4f;

                        if (interact.TakeFocus())
                        {
                            openCol = EditorStylePrefs.Instance.Highlighted;
                            OpenAddDialog();
                        }
                        else if (interact.IsHovered())
                            openCol = EditorStylePrefs.Instance.Hovering;

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, openCol, (float)EditorStylePrefs.Instance.WindowRoundness, CornerRounding.All);

                        gui.Draw2D.DrawText("Add", 20, gui.CurrentNode.LayoutData.Rect);
                    }


                    using (gui.Node("Create").Top(7.5).Scale(75, 30).Enter())
                    {
                        gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 7.5));

                        Interactable interact = gui.GetInteractable();

                        Color createCol = EditorStylePrefs.Instance.Highlighted;

                        if (interact.TakeFocus())
                            _sideTab = DrawCreateProject;
                        else if (interact.IsHovered())
                            createCol = EditorStylePrefs.Instance.Hovering;

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, createCol, (float)EditorStylePrefs.Instance.WindowRoundness, CornerRounding.All);

                        gui.Draw2D.DrawText("Create", 20, gui.CurrentNode.LayoutData.Rect);
                    }
                }
            }


            using (gui.Node("List").ExpandWidth(-25).ExpandHeight(-25).TopLeft(25, 45).Layout(LayoutType.Column).Spacing(5).Clip().Scroll().Enter())
            {
                for (int i = 0; i < ProjectCache.Instance.ProjectsCount; i++)
                {
                    Project? project = ProjectCache.Instance.GetProject(i);

                    if (project == null)
                        continue;

                    if (string.IsNullOrEmpty(_searchText) || project.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                        DisplayProject(project);
                }
            }


            using (gui.Node("OpenBtn").TopLeft(Offset.Percentage(1f, -162), Offset.Percentage(1f, -60)).Scale(162, 60).Enter())
            {
                if (SelectedProject != null)
                {
                    if (gui.IsNodePressed())
                    {
                        Project.Open(SelectedProject);
                        isOpened = false;
                    }

                    Color col = gui.IsNodeActive() ? EditorStylePrefs.Instance.Highlighted :
                                gui.IsNodeHovered() ? EditorStylePrefs.Instance.Highlighted * 0.8f : EditorStylePrefs.Instance.Highlighted;

                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, col, (float)EditorStylePrefs.Instance.WindowRoundness, 4);
                    gui.Draw2D.DrawText("Open", gui.CurrentNode.LayoutData.Rect);
                }
                else
                {
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, Color.white * 0.4f, (float)EditorStylePrefs.Instance.WindowRoundness, 4);
                    gui.Draw2D.DrawText("Open", gui.CurrentNode.LayoutData.Rect);
                }
            }
        }


        private void OpenAddDialog()
        {
            _dialogContext ??= new();

            _dialogContext.title = "Open Existing Project";
            _dialogContext.directoryPath = new DirectoryInfo(s_defaultPath);
            _dialogContext.OnComplete += (x) => ProjectCache.Instance.AddProject(new Project(new DirectoryInfo(x)));

            EditorGuiManager.Remove(_dialog);

            _dialog = new FileDialog(_dialogContext);

            EditorGuiManager.FocusWindow(_dialog);
        }


        private void DrawAddProject()
        {
            if (Input.GetKeyDown(Key.Escape))
                _sideTab = null;
        }


        private void DrawCreateProject()
        {
            if (Input.GetKeyDown(Key.Escape))
                _sideTab = null;
        }


        private void DisplayProject(Project project)
        {
            using (gui.Node(project.Name).Height(48).Width(Size.Percentage(1f, -17)).Margin(5).Enter())
            {
                Rect rect = gui.CurrentNode.LayoutData.Rect;
                gui.Draw2D.DrawText(Font.DefaultFont, project.Name, 20, rect.Position + new Vector2(8, 5), Color.white);
                string path = project.ProjectPath;

                // Cut off the path if it's too long
                if (path.Length > 48)
                    path = string.Concat("...", path.AsSpan(path.Length - 48));

                gui.Draw2D.DrawText(Font.DefaultFont, path, 20, rect.Position + new Vector2(8, 22), Color.white * 0.5f);

                gui.Draw2D.DrawText(Font.DefaultFont, GetFormattedLastModifiedTime(project.ProjectDirectory.LastAccessTime), 20, rect.Position + new Vector2(rect.width - 125, 14), Color.white * 0.5f);

                Interactable interact = gui.GetInteractable();

                if (interact.TakeFocus() || SelectedProject == project)
                {
                    SelectedProject = project;
                    gui.Draw2D.DrawRect(gui.CurrentNode.LayoutData.Rect, new(0.7f, 0.7f, 0.7f, 1f), 1, 2);
                }
                else if (interact.IsHovered())
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, new(0.1f, 0.1f, 0.1f, 0.4f), 2);
            }
        }


        private void CreateProjectTab()
        {
            Rect rect = gui.CurrentNode.LayoutData.Rect;
            gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.WindowBGOne * 0.25f, (float)EditorStylePrefs.Instance.WindowRoundness);

            Vector2 shadowA = new(rect.x, rect.y);
            Vector2 shadowB = new(rect.x, rect.y + (rect.height - 77));

            gui.Draw2D.DrawVerticalBlackGradient(shadowA, shadowB, 30, 0.25f);
            Rect footer = new(shadowB.x, shadowB.y, rect.width, 77);

            gui.Draw2D.DrawRectFilled(footer, EditorStylePrefs.Instance.WindowBGOne, (float)EditorStylePrefs.Instance.WindowRoundness, 4);
            Vector2 shadowC = new(rect.x, rect.y + rect.height);

            gui.Draw2D.DrawVerticalBlackGradient(shadowB, shadowC, 20, 0.25f);

            if (string.IsNullOrWhiteSpace(_createName))
                _createName = s_defaultPath;

            gui.InputField("CreateInput", ref _createName, 0x100, Gui.InputFieldFlags.None, 30, 450, 340, null, EditorGUI.GetInputStyle());

            gui.Draw2D.DrawText(Font.DefaultFont, _createName, 20, rect.Position + new Vector2(30, 480), Color.white * 0.5f);

            using (gui.Node("CreateBtn").TopLeft(Offset.Percentage(1f, -172), Offset.Percentage(1f, -77)).Scale(172, 77).Enter())
            {
                if (!string.IsNullOrEmpty(_createName) && Directory.Exists(Path.GetDirectoryName(_createName)))
                {
                    if (gui.IsNodePressed())
                    {
                        Project.CreateNew(new DirectoryInfo(_createName));
                        _currentTab = 0;
                    }

                    Color col = gui.IsNodeActive() ? EditorStylePrefs.Instance.Highlighted :
                              gui.IsNodeHovered() ? EditorStylePrefs.Instance.Highlighted * 0.8f : EditorStylePrefs.Instance.Highlighted;

                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, col, (float)EditorStylePrefs.Instance.WindowRoundness, 4);
                    gui.Draw2D.DrawText("Create", gui.CurrentNode.LayoutData.Rect);
                }
                else
                {
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, Color.white * 0.4f, (float)EditorStylePrefs.Instance.WindowRoundness, 4);
                    gui.Draw2D.DrawText("Create", gui.CurrentNode.LayoutData.Rect);
                }
            }
        }


        private void DrawSidePanel()
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                using (gui.Node(_tabs[i].Item1).Height(40).Width(Size.Percentage(1f)).Top(Offset.Percentage(0, 0)).Enter())
                {
                    Interactable interact = gui.GetInteractable();

                    if (interact.TakeFocus() || _currentTab == i)
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted);
                        _currentTab = i;
                    }
                    else if (interact.IsHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, 0);

                    Rect rect = gui.CurrentNode.LayoutData.Rect;
                    gui.Draw2D.DrawText(Font.DefaultFont, _tabs[i].Item1, 20, rect, Color.white);
                }
            }
        }


        private static string GetFormattedLastModifiedTime(DateTime lastModified)
        {
            TimeSpan timeSinceLastModified = DateTime.Now - lastModified;

            if (timeSinceLastModified.TotalMinutes < 1)
                return "Just now";
            else if (timeSinceLastModified.TotalMinutes < 60)
                return $"{(int)timeSinceLastModified.TotalMinutes} minutes ago";
            else if (timeSinceLastModified.TotalHours < 24)
                return $"{(int)timeSinceLastModified.TotalHours} hours ago";
            else
                return $"{(int)timeSinceLastModified.TotalDays} days ago";
        }
    }
}
