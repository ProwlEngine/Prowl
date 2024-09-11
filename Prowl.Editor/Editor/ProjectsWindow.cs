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
        private static readonly string s_defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);


        public Project? SelectedProject = null;

        private string _searchText = "";

        private string _createFolder = s_defaultPath;
        private string _createName = "";

        private (string, Action)[] _tabs;
        private int _currentTab = 0;
        private bool _createTabOpen = false;

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

                using (gui.Node("Settings").Scale(30).Top(10).Enter())
                {
                    gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 10));

                    Interactable interact = gui.GetInteractable();

                    Rect rect = gui.CurrentNode.LayoutData.Rect;

                    if (interact.TakeFocus())
                    {
                        gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Highlighted, 5, CornerRounding.All);
                        Debug.Log("Opened editor settings");
                    }
                    else if (interact.IsHovered())
                        gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Hovering, 5, CornerRounding.All);

                    rect.y += 1; // Gear icon is offset upwards by a single pixel in the font, so we apply a teeny tiny offset to align it.
                    gui.Draw2D.DrawText(FontAwesome6.Gear, 30, rect);
                }
            }

            using (gui.Node("Content").ExpandWidth().Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                using (gui.Node("Side").ExpandHeight().MaxWidth(150).Layout(LayoutType.Column).Spacing(5).Enter())
                {
                    DrawSidePanel();
                }

                gui.PushID((ulong)_currentTab);

                using (gui.Node("TabContent").ExpandHeight().Enter())
                {
                    _tabs[_currentTab].Item2.Invoke();
                }

                gui.PopID();
            }
        }


        private void DrawProjectsTab()
        {
            Rect rect = gui.CurrentNode.LayoutData.Rect;
            gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.WindowBGTwo, (float)EditorStylePrefs.Instance.WindowRoundness);

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


            using (gui.Node("TopBar").ExpandWidth().MaxHeight(40).Enter())
            {
                using (gui.Node("Search").TopLeft(30, 10).Scale(150, 30).Enter())
                {
                    gui.InputField("SearchInput", ref _searchText, 0x100, Gui.InputFieldFlags.None, 0, 0, 150, null, EditorGUI.GetInputStyle());
                }

                if (!_createTabOpen)
                {
                    using (gui.Node("Add").Top(7.5).Scale(75, 30).Enter())
                    {
                        gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 87.5));

                        Interactable interact = gui.GetInteractable();

                        Color openCol = Color.white * 0.4f;

                        if (interact.TakeFocus())
                        {
                            openCol = EditorStylePrefs.Instance.Highlighted;
                            OpenDialog("Add Existing Project", (x) => ProjectCache.Instance.AddProject(new Project(new DirectoryInfo(x))));
                        }
                        else if (interact.IsHovered())
                            openCol = EditorStylePrefs.Instance.Hovering;

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, openCol, (float)EditorStylePrefs.Instance.ButtonRoundness, CornerRounding.All);

                        gui.Draw2D.DrawText("Add", 20, gui.CurrentNode.LayoutData.Rect);
                    }


                    using (gui.Node("Create").Top(7.5).Scale(75, 30).Enter())
                    {
                        gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 7.5));

                        Interactable interact = gui.GetInteractable();

                        Color createCol = EditorStylePrefs.Instance.Highlighted;

                        if (interact.TakeFocus())
                            _createTabOpen = true;
                        else if (interact.IsHovered())
                            createCol = EditorStylePrefs.Instance.Hovering;

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, createCol, (float)EditorStylePrefs.Instance.ButtonRoundness, CornerRounding.All);

                        gui.Draw2D.DrawText("Create", 20, gui.CurrentNode.LayoutData.Rect);
                    }
                }
            }

            double height = gui.CurrentNode.LayoutData.Rect.height;

            using (gui.Node("Projects").ExpandWidth().Height(height - 60).Enter())
            {
                double width = gui.CurrentNode.LayoutData.Rect.width;

                if (_createTabOpen)
                {
                    width -= 250;

                    shadowA = new(rect.x + width, rect.y);
                    shadowB = new(rect.x + width, rect.y + (rect.height - 60));

                    gui.Draw2D.DrawVerticalBlackGradient(shadowA, shadowB, -30, 0.25f);
                }

                using (gui.Node("List").TopLeft(25, 45).Width(width - 25).ExpandHeight(-45).Layout(LayoutType.Column).Spacing(5).Clip().Scroll().Enter())
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

                if (_createTabOpen)
                {
                    using (gui.Node("Sidebar").Left(width).Width(250).ExpandHeight().Enter())
                    {
                        rect = gui.CurrentNode.LayoutData.Rect;

                        shadowA = new(rect.x, rect.y + 20);
                        shadowB = new(rect.x + rect.width, rect.y + 20);

                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne);
                        gui.Draw2D.DrawHorizontalBlackGradient(shadowA, shadowB, -20, 0.25f);

                        shadowA = new(rect.x, rect.y + rect.height - 20);
                        shadowB = new(rect.x + rect.width, rect.y + rect.height - 20);
                        gui.Draw2D.DrawHorizontalBlackGradient(shadowA, shadowB, 20, 0.25f);

                        DrawCreateProject();
                    }
                }
            }

            using (gui.Node("Footer").TopLeft(Offset.Percentage(1f, -162), Offset.Percentage(1f, -60)).Scale(162, 60).Enter())
            {
                Color col = Color.white * 0.4f;

                bool isSelectable = _createTabOpen ? !string.IsNullOrEmpty(_createName) && Directory.Exists(_createFolder) && !Path.Exists(_createName) :
                    SelectedProject != null;

                string text = _createTabOpen ? "Create" : "Open";

                if (isSelectable)
                {
                    if (gui.IsNodePressed())
                    {
                        if (_createTabOpen)
                        {
                            Project.CreateNew(new DirectoryInfo(Path.Join(_createFolder, _createName)));
                            _createTabOpen = false;
                        }
                        else if (Project.Open(SelectedProject))
                        {
                            isOpened = false;
                        }
                    }

                    col = gui.IsNodeActive() ? EditorStylePrefs.Instance.Highlighted :
                        gui.IsNodeHovered() ? EditorStylePrefs.Instance.Highlighted * 0.8f : EditorStylePrefs.Instance.Highlighted;
                }

                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, col, (float)EditorStylePrefs.Instance.WindowRoundness, 4);
                gui.Draw2D.DrawText(text, gui.CurrentNode.LayoutData.Rect);
            }
        }


        private void OpenDialog(string title, Action<string> onComplete)
        {
            _dialogContext ??= new();

            _dialogContext.title = "Open Existing Project";
            _dialogContext.parentDirectory = new DirectoryInfo(s_defaultPath);

            _dialogContext.OnComplete = onComplete;
            _dialogContext.OnCancel += () => _dialogContext.OnComplete = (x) => { };

            EditorGuiManager.Remove(_dialog);

            _dialog = new FileDialog(_dialogContext);

            EditorGuiManager.FocusWindow(_dialog);
        }


        private void DrawCreateProject()
        {
            gui.CurrentNode.Layout(LayoutType.Column);
            gui.CurrentNode.ScaleChildren();
            gui.CurrentNode.Padding(5);
            gui.CurrentNode.Spacing(15);

            using (gui.Node("TopBar").ExpandWidth().MaxHeight(20).Enter())
            {
                using (gui.Node("Close").Scale(20).Enter())
                {
                    gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x));

                    Interactable closeInteract = gui.GetInteractable();

                    Rect rect = gui.CurrentNode.LayoutData.Rect;

                    if (closeInteract.TakeFocus())
                    {
                        gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Highlighted, 5, CornerRounding.All);
                        _createTabOpen = false;
                    }
                    else if (closeInteract.IsHovered())
                        gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Hovering, 5, CornerRounding.All);

                    rect.x += 1;
                    rect.y += 1; // Xmark icon is offset upwards by a single pixel in the font, so we apply a teeny tiny offset to align it.
                    gui.Draw2D.DrawText(FontAwesome6.Xmark, 30, rect);
                }
            }

            using (gui.Node("Banner").ExpandWidth().MaxHeight(200).Enter())
            {
                using (gui.Node("CreateProjectLabel").Expand().Enter())
                {
                    Rect rect = gui.CurrentNode.LayoutData.Rect;

                    gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.WindowRoundness);

                    Vector2 gradLeft = rect.Position;
                    gradLeft.y += rect.height * 0.05;
                    Vector2 gradRight = rect.Position;
                    gradRight.x += rect.width;
                    gradRight.y += rect.height * 0.05;

                    Color gradBottom = EditorStylePrefs.Instance.Highlighted * 0.6f;
                    gradBottom.a = 1.0f;

                    gui.Draw2D.DrawHorizontalGradient(gradLeft, gradRight, (float)rect.height * 0.9f, gradBottom, EditorStylePrefs.Instance.Highlighted);

                    gui.Draw2D.DrawRectFilled(new(rect.Position.x, rect.Position.y + rect.height * 0.94f, rect.width, rect.height * 0.06f), gradBottom, (float)EditorStylePrefs.Instance.WindowRoundness, CornerRounding.Bottom);

                    gui.Draw2D.DrawText(FontAwesome6.PuzzlePiece, 50, rect);
                }
            }

            using (gui.Node("ProjectFolder").ExpandWidth().MaxHeight(40).Enter())
            {
                gui.Draw2D.DrawText("Location", 20, gui.CurrentNode.LayoutData.Rect.Position, color: Color.white * 0.65f);

                Vector2 pos = gui.CurrentNode.LayoutData.Rect.Position;
                pos.y += 20;

                string path = _createFolder;

                if (path.Length > 48)
                    path = string.Concat("...", path.AsSpan(path.Length - 48));

                gui.Draw2D.DrawText(path, pos);

                using (gui.Node("SelectProject").Top(20).Scale(10, 20).Enter())
                {
                    gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x));

                    Interactable closeInteract = gui.GetInteractable();

                    Rect rect = gui.CurrentNode.LayoutData.Rect;

                    if (closeInteract.TakeFocus())
                    {
                        gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Highlighted, 5, CornerRounding.All);
                        OpenDialog("Select Folder", (x) => _createFolder = x);
                    }
                    else if (closeInteract.IsHovered())
                        gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Hovering, 5, CornerRounding.All);

                    gui.Draw2D.DrawText(FontAwesome6.EllipsisVertical, 20, rect);
                }
            }

            using (gui.Node("NameInput").ExpandWidth().MaxHeight(50).Enter())
            {
                gui.Draw2D.DrawText("Project Name", 20, gui.CurrentNode.LayoutData.Rect.Position, color: Color.white * 0.65f);

                gui.InputField("SearchInput", ref _createName, 0x100, Gui.InputFieldFlags.None, 0, 20, gui.CurrentNode.LayoutData.Rect.width, null, EditorGUI.GetInputStyle());
            }

            if (Input.GetKeyDown(Key.Escape))
                _createTabOpen = false;
        }


        private void DisplayProject(Project project)
        {
            using (gui.Node(project.Name).Height(48).Width(Size.Percentage(1f, -17)).Margin(5).Layout(LayoutType.Row).Enter())
            {
                Interactable interact = gui.GetInteractable();

                if (interact.TakeFocus() || SelectedProject == project)
                {
                    SelectedProject = project;
                    gui.Draw2D.DrawRect(gui.CurrentNode.LayoutData.Rect, new(0.7f, 0.7f, 0.7f, 1f), 1, 2);
                }

                if (interact.IsHovered())
                {
                    if (gui.IsPointerDoubleClick(MouseButton.Left))
                    {
                        Project.Open(project);
                        isOpened = false;
                    }

                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, new(0.1f, 0.1f, 0.1f, 0.4f), 2);
                }

                if (!project.IsValid())
                {
                    using (gui.Node("WarningLabel").Width(35).ExpandHeight().Enter())
                    {
                        Interactable warnInteract = gui.GetInteractable();

                        gui.Draw2D.DrawText(Font.DefaultFont, FontAwesome6.TriangleExclamation, 35, gui.CurrentNode.LayoutData.Rect, Color.yellow);

                        Vector2 warningPos = gui.CurrentNode.LayoutData.Rect.Position;
                        warningPos.y += gui.CurrentNode.LayoutData.Rect.height / 2;
                        gui.Tooltip("Project is invalid and may not open correctly.", warningPos, align: Gui.TooltipAlign.BottomRight);
                    }
                }

                using (gui.Node("ProjectText").ExpandWidth(-35).ExpandHeight().Enter())
                {
                    Rect rect = gui.CurrentNode.LayoutData.Rect;

                    gui.Draw2D.DrawText(Font.DefaultFont, project.Name, 20, rect.Position + new Vector2(8, 5), Color.white);

                    string path = project.ProjectPath;

                    // Cut off the path if it's too long
                    if (path.Length > 48)
                        path = string.Concat("...", path.AsSpan(path.Length - 48));

                    gui.Draw2D.DrawText(Font.DefaultFont, path, 20, rect.Position + new Vector2(8, 22), Color.white * 0.5f);

                    gui.Draw2D.DrawText(Font.DefaultFont, GetFormattedLastModifiedTime(project.ProjectDirectory.LastAccessTime), 20, rect.Position + new Vector2(rect.width - 125, 14), Color.white * 0.5f);
                }

                using (gui.Node("RemoveProject").Scale(20).Top(9).Enter())
                {
                    gui.CurrentNode.IgnoreLayout();
                    gui.CurrentNode.Left(Offset.Percentage(1.0f, -gui.CurrentNode.LayoutData.Scale.x - 7.5));

                    Interactable removeInteract = gui.GetInteractable();

                    bool focused = removeInteract.TakeFocus();

                    if (removeInteract.TakeFocus())
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.WindowRoundness, CornerRounding.All);
                        ProjectCache.Instance.RemoveProject(project);
                    }
                    else if (removeInteract.IsHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, Color.white * 0.4f, (float)EditorStylePrefs.Instance.WindowRoundness, CornerRounding.All);

                    gui.Draw2D.DrawText(Font.DefaultFont, FontAwesome6.Xmark, 25, gui.CurrentNode.LayoutData.Rect, Color.white);
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
